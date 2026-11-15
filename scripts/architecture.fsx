// Architecture map read from the compiled assemblies, not from the source text: every method's IL is decoded
// and each member or type it touches is traced, through the portable PDB, to the source file that declares it.
// The same build always yields the same graph, so the map can be diffed between commits.
//
//   dotnet build FootballEngine.slnx -c Release
//   dotnet fsi scripts/architecture.fsx            -> artifacts/architecture/architecture.json
//
// Edges count IL uses (calls, construction, field access, type tests). A type named only in a signature and never
// used in a body is not an edge.

open System
open System.IO
open System.Collections.Generic
open System.Reflection
open System.Reflection.Emit
open System.Reflection.Metadata
open System.Reflection.Metadata.Ecma335
open System.Reflection.PortableExecutable
open System.Text.Json

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

// One- and two-byte opcode tables, built from the runtime's own definitions.
let opcodes1 = Array.create 256 (None: OpCode option)
let opcodes2 = Array.create 256 (None: OpCode option)

for f in typeof<OpCodes>.GetFields(BindingFlags.Public ||| BindingFlags.Static) do
  match f.GetValue null with
  | :? OpCode as op ->
    let v = uint16 op.Value

    if op.Size = 1 then
      opcodes1[int v] <- Some op
    else
      opcodes2[int (v &&& 0xFFus)] <- Some op
  | _ -> ()

let operandSize (op: OpCode) (il: byte[]) (pos: int) =
  match op.OperandType with
  | OperandType.InlineNone -> 0
  | OperandType.ShortInlineBrTarget
  | OperandType.ShortInlineI
  | OperandType.ShortInlineVar -> 1
  | OperandType.InlineVar -> 2
  | OperandType.InlineI8
  | OperandType.InlineR -> 8
  | OperandType.InlineSwitch -> 4 + 4 * BitConverter.ToInt32(il, pos)
  | _ -> 4

type Assembly =
  {
    Name: string
    Reader: MetadataReader
    Pe: PEReader
    Pdb: MetadataReader
    /// Source file of every type definition (row number -> relative path).
    TypeFile: Dictionary<int, string>
    /// Full name ("Ns.Outer/Nested") -> type definition row.
    ByName: Dictionary<string, int>
  }

let relative (path: string) =
  let full = Path.GetFullPath path

  if full.StartsWith root then
    full.Substring(root.Length + 1).Replace('\\', '/')
  else
    full

let load (dll: string) =
  let pe = new PEReader(File.OpenRead dll)
  let md = pe.GetMetadataReader()

  let pdbProvider =
    MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(Path.ChangeExtension(dll, ".pdb")))

  let pdb = pdbProvider.GetMetadataReader()
  let methodFile = Dictionary<int, string>()

  for h in md.MethodDefinitions do
    let row = MetadataTokens.GetRowNumber h

    let info =
      pdb.GetMethodDebugInformation(MetadataTokens.MethodDebugInformationHandle row)

    let docHandle =
      if not info.Document.IsNil then
        info.Document
      else
        info.GetSequencePoints()
        |> Seq.tryHead
        |> Option.map _.Document
        |> Option.defaultValue (DocumentHandle())

    if not docHandle.IsNil then
      let doc = pdb.GetDocument docHandle
      methodFile[row] <- relative (pdb.GetString doc.Name)

  let typeFile = Dictionary<int, string>()
  let byName = Dictionary<string, int>()

  let rec fullName (h: TypeDefinitionHandle) =
    let t = md.GetTypeDefinition h
    let name = md.GetString t.Name
    let declaring = t.GetDeclaringType()

    if declaring.IsNil then
      let ns = md.GetString t.Namespace
      if ns = "" then name else ns + "." + name
    else
      fullName declaring + "/" + name

  // A type's file is where most of its methods (and its nested closures') were written.
  let votes = Dictionary<int, Dictionary<string, int>>()

  let rec outermost (h: TypeDefinitionHandle) =
    let d = (md.GetTypeDefinition h).GetDeclaringType()
    if d.IsNil then h else outermost d

  for h in md.TypeDefinitions do
    byName[fullName h] <- MetadataTokens.GetRowNumber h
    let t = md.GetTypeDefinition h

    for m in t.GetMethods() do
      match methodFile.TryGetValue(MetadataTokens.GetRowNumber m) with
      | true, file ->
        for target in [ h; outermost h ] |> List.distinct do
          let row = MetadataTokens.GetRowNumber target

          let v =
            match votes.TryGetValue row with
            | true, v -> v
            | _ ->
              (let v = Dictionary() in
               votes[row] <- v
               v)

          v[file] <-
            (match v.TryGetValue file with
             | true, n -> n + 1
             | _ -> 1)
      | _ -> ()

  for KeyValue(row, v) in votes do
    typeFile[row] <- v |> Seq.sortBy (fun kv -> -kv.Value, kv.Key) |> Seq.head |> _.Key

  // A type without methods of its own takes its enclosing type's file.
  for h in md.TypeDefinitions do
    let row = MetadataTokens.GetRowNumber h

    if not (typeFile.ContainsKey row) then
      match typeFile.TryGetValue(MetadataTokens.GetRowNumber(outermost h)) with
      | true, f -> typeFile[row] <- f
      | _ -> ()

  { Name = Path.GetFileNameWithoutExtension dll
    Reader = md
    Pe = pe
    Pdb = pdb
    TypeFile = typeFile
    ByName = byName },
  methodFile

/// Full name of a type reference, in the same form as `ByName`.
let rec refName (md: MetadataReader) (h: TypeReferenceHandle) =
  let r = md.GetTypeReference h
  let name = md.GetString r.Name

  if r.ResolutionScope.Kind = HandleKind.TypeReference then
    refName md (TypeReferenceHandle.op_Explicit r.ResolutionScope) + "/" + name
  else
    let ns = md.GetString r.Namespace
    if ns = "" then name else ns + "." + name

/// The generic type definition or reference behind a type specification, from its signature blob.
let specTarget (md: MetadataReader) (h: TypeSpecificationHandle) : EntityHandle option =
  let blob = md.GetBlobReader((md.GetTypeSpecification h).Signature)
  let header = blob.ReadSignatureTypeCode()

  if header = SignatureTypeCode.GenericTypeInstance then
    blob.ReadSignatureTypeCode() |> ignore
    Some(blob.ReadTypeHandle())
  else
    None

/// The files a token names: its declaring type's file in this assembly, or in another loaded assembly.
let fileOfType (asm: Assembly) (others: Assembly list) (h: EntityHandle) : (string * string) option =
  let md = asm.Reader

  let rec go (h: EntityHandle) =
    match h.Kind with
    | HandleKind.TypeDefinition ->
      match asm.TypeFile.TryGetValue(MetadataTokens.GetRowNumber h) with
      | true, f -> Some(asm.Name, f)
      | _ -> None
    | HandleKind.TypeReference ->
      let name = refName md (TypeReferenceHandle.op_Explicit h)

      others
      |> List.tryPick (fun o ->
        match o.ByName.TryGetValue name with
        | true, row ->
          match o.TypeFile.TryGetValue row with
          | true, f -> Some(o.Name, f)
          | _ -> None
        | _ -> None)
    | HandleKind.TypeSpecification -> specTarget md (TypeSpecificationHandle.op_Explicit h) |> Option.bind go
    | _ -> None

  go h

let fileOfToken (asm: Assembly) others (token: int) =
  let md = asm.Reader
  let h = MetadataTokens.EntityHandle token

  match h.Kind with
  | HandleKind.MethodDefinition ->
    fileOfType
      asm
      others
      (TypeDefinitionHandle.op_Implicit (
        (md.GetMethodDefinition(MethodDefinitionHandle.op_Explicit h)).GetDeclaringType()
      ))
  | HandleKind.FieldDefinition ->
    fileOfType
      asm
      others
      (TypeDefinitionHandle.op_Implicit (
        (md.GetFieldDefinition(FieldDefinitionHandle.op_Explicit h)).GetDeclaringType()
      ))
  | HandleKind.MemberReference ->
    fileOfType asm others (md.GetMemberReference(MemberReferenceHandle.op_Explicit h)).Parent
  | HandleKind.MethodSpecification ->
    let spec = md.GetMethodSpecification(MethodSpecificationHandle.op_Explicit h)

    match spec.Method.Kind with
    | HandleKind.MethodDefinition ->
      fileOfType
        asm
        others
        (TypeDefinitionHandle.op_Implicit (
          (md.GetMethodDefinition(MethodDefinitionHandle.op_Explicit spec.Method)).GetDeclaringType()
        ))
    | _ -> fileOfType asm others (md.GetMemberReference(MemberReferenceHandle.op_Explicit spec.Method)).Parent
  | HandleKind.TypeDefinition
  | HandleKind.TypeReference
  | HandleKind.TypeSpecification -> fileOfType asm others h
  | _ -> None

let edges = Dictionary<struct (string * string), int>()
/// Which members an edge uses, so a reader can tell a shared helper from one system calling another.
let edgeMembers = Dictionary<struct (string * string), SortedSet<string>>()

let memberName (asm: Assembly) (token: int) =
  let md = asm.Reader
  let h = MetadataTokens.EntityHandle token

  let typeName (t: EntityHandle) =
    match t.Kind with
    | HandleKind.TypeDefinition -> md.GetString((md.GetTypeDefinition(TypeDefinitionHandle.op_Explicit t)).Name)
    | HandleKind.TypeReference -> md.GetString((md.GetTypeReference(TypeReferenceHandle.op_Explicit t)).Name)
    | _ -> "?"

  match h.Kind with
  | HandleKind.MethodDefinition ->
    let m = md.GetMethodDefinition(MethodDefinitionHandle.op_Explicit h)

    typeName (TypeDefinitionHandle.op_Implicit (m.GetDeclaringType()))
    + "."
    + md.GetString m.Name
  | HandleKind.FieldDefinition ->
    let f = md.GetFieldDefinition(FieldDefinitionHandle.op_Explicit h)

    typeName (TypeDefinitionHandle.op_Implicit (f.GetDeclaringType()))
    + "."
    + md.GetString f.Name
  | HandleKind.MemberReference ->
    let m = md.GetMemberReference(MemberReferenceHandle.op_Explicit h)
    typeName m.Parent + "." + md.GetString m.Name
  | HandleKind.TypeDefinition
  | HandleKind.TypeReference -> typeName h
  | _ -> "?"

let scan (asm: Assembly) (methodFile: Dictionary<int, string>) others =
  let md = asm.Reader

  for h in md.MethodDefinitions do
    let m = md.GetMethodDefinition h
    let row = MetadataTokens.GetRowNumber h
    // A closure the compiler emits without sequence points (`Step = Actions.step`) still belongs to its type's file.
    let source =
      match methodFile.TryGetValue row with
      | true, f -> Some f
      | _ ->
        match asm.TypeFile.TryGetValue(MetadataTokens.GetRowNumber(m.GetDeclaringType())) with
        | true, f -> Some f
        | _ -> None

    match source with
    | Some source when m.RelativeVirtualAddress <> 0 ->
      let il = asm.Pe.GetMethodBody(m.RelativeVirtualAddress).GetILBytes()
      let mutable pos = 0

      while pos < il.Length do
        let op =
          if il[pos] = 0xFEuy && pos + 1 < il.Length then
            (pos <- pos + 2
             opcodes2[int il[pos - 1]])
          else
            (pos <- pos + 1
             opcodes1[int il[pos - 1]])

        match op with
        | None -> pos <- il.Length
        | Some op ->
          match op.OperandType with
          | OperandType.InlineMethod
          | OperandType.InlineField
          | OperandType.InlineType
          | OperandType.InlineTok ->
            match fileOfToken asm others (BitConverter.ToInt32(il, pos)) with
            | Some(_, target) when target <> source ->
              let key = struct (source, target)

              edges[key] <-
                (match edges.TryGetValue key with
                 | true, n -> n + 1
                 | _ -> 1)

              let set =
                match edgeMembers.TryGetValue key with
                | true, v -> v
                | _ ->
                  (let v = SortedSet() in
                   edgeMembers[key] <- v
                   v)

              set.Add(memberName asm (BitConverter.ToInt32(il, pos))) |> ignore
            | _ -> ()
          | _ -> ()

          pos <- pos + operandSize op il pos
    | _ -> ()

let core, coreMethods =
  load (Path.Combine(root, "FootballEngine.Core/bin/Release/net10.0/FootballEngine.Core.dll"))

let client, clientMethods =
  load (Path.Combine(root, "FootballEngine.Client/bin/Release/net10.0/FootballEngine.Client.dll"))

scan core coreMethods []
scan client clientMethods [ core ]

/// Compile order and length of every source file, from the project files themselves.
let compiled (project: string) =
  let dir = Path.GetDirectoryName(Path.Combine(root, project))
  let doc = System.Xml.Linq.XDocument.Load(Path.Combine(root, project))

  doc.Descendants()
  |> Seq.filter (fun e -> e.Name.LocalName = "Compile")
  |> Seq.map (fun e ->
    relative (Path.Combine(dir, e.Attribute(System.Xml.Linq.XName.Get "Include").Value.Replace('\\', '/'))))
  |> Seq.mapi (fun i f ->
    {| File = f
       Order = i
       Lines =
        (if File.Exists(Path.Combine(root, f)) then
           File.ReadAllLines(Path.Combine(root, f)).Length
         else
           0) |})
  |> Array.ofSeq

let files =
  Array.append
    (compiled "FootballEngine.Core/FootballEngine.Core.fsproj"
     |> Array.map (fun f -> {| f with Assembly = "Core" |}))
    (compiled "FootballEngine.Client/FootballEngine.Client.fsproj"
     |> Array.map (fun f -> {| f with Assembly = "Client" |}))

let known = files |> Array.map _.File |> Set.ofArray

let fileEdges =
  edges
  |> Seq.map (fun kv ->
    let struct (s, t) = kv.Key in

    {| Source = s
       Target = t
       Uses = kv.Value
       Members = Array.ofSeq edgeMembers[kv.Key] |})
  // Compiler-generated documents carry no source path; they belong to no file a reader can open.
  |> Seq.filter (fun e -> known.Contains e.Source && known.Contains e.Target)
  |> Seq.sortBy (fun e -> e.Source, e.Target)
  |> Array.ofSeq

/// The folder a file is drawn in: one box per engine layer and per system family, one per client area.
let folderOf (file: string) =
  let parts = file.Split '/'
  let core = parts[0] = "FootballEngine.Core"
  let p = parts[1..]
  let prefix = if core then "Core/" else "Client/"

  if p.Length = 1 then
    prefix + "(root)"
  elif core && p[0] = "Match" && p.Length > 3 && p[1] = "Systems" then
    prefix + "Match/Systems/" + p[2]
  elif core && (p[0] = "Match" || p[0] = "World") && p.Length > 2 then
    prefix + p[0] + "/" + p[1]
  else
    prefix + p[0]

let folderEdges =
  fileEdges
  |> Array.filter (fun e -> folderOf e.Source <> folderOf e.Target)
  |> Array.groupBy (fun e -> folderOf e.Source, folderOf e.Target)
  |> Array.map (fun ((s, t), es) ->
    {| Source = s
       Target = t
       Uses = es |> Array.sumBy _.Uses
       Files = es.Length |})
  |> Array.sortBy (fun e -> e.Source, e.Target)

/// Systems are the modules SystemsTable registers (`Step = Module.step`), found by their `module` declaration.
let systemFiles =
  let table =
    File.ReadAllText(Path.Combine(root, "FootballEngine.Core/Match/Stepper/SystemsTable.fs"))

  let modules =
    Text.RegularExpressions.Regex.Matches(table, @"Step = ([\w]+)\.")
    |> Seq.map (fun m -> m.Groups[1].Value)
    |> Set.ofSeq

  files
  |> Array.filter (fun f -> f.Assembly = "Core")
  |> Array.choose (fun f ->
    let text = File.ReadAllText(Path.Combine(root, f.File))

    Text.RegularExpressions.Regex.Matches(
      text,
      @"^\s*module (?:private )?(\w+)\s*=",
      Text.RegularExpressions.RegexOptions.Multiline
    )
    |> Seq.map (fun m -> m.Groups[1].Value)
    |> Seq.tryFind modules.Contains
    |> Option.map (fun m -> f.File, m))
  |> Map.ofArray

/// Each rule is one sentence of AGENTS.md read as a property of the graph; an empty list means it holds.
let rules =
  let inFolder (prefix: string) (f: string) = (folderOf f).StartsWith prefix

  let check
    name
    (violates:
      {| Source: string
         Target: string
         Uses: int
         Members: string[] |}
        -> bool)
    =
    {| Rule = name
       Violations = fileEdges |> Array.filter violates |}

  [| check "Systems never call each other (a registered system module uses another one)" (fun e ->
       systemFiles.ContainsKey e.Source && systemFiles.ContainsKey e.Target)
     check "Types live in Match/Contracts (a system reaches the Stepper)" (fun e ->
       inFolder "Core/Match/Systems" e.Source && inFolder "Core/Match/Stepper" e.Target)
     check "Contracts depend only on contracts" (fun e ->
       inFolder "Core/Match/Contracts" e.Source
       && not (inFolder "Core/Match/Contracts" e.Target))
     check "The match engine knows nothing of the world" (fun e ->
       inFolder "Core/Match" e.Source && inFolder "Core/World" e.Target)
     check "Rendering never decides a sporting outcome (client reaches engine systems)" (fun e ->
       inFolder "Client" e.Source && inFolder "Core/Match/Systems" e.Target)
     check "Application state does not depend on views" (fun e ->
       inFolder "Client/AppState" e.Source
       && (inFolder "Client/Views" e.Target || inFolder "Client/Match3D" e.Target)) |]

/// Folders that reach each other in both directions (Tarjan); F# forbids file cycles, not folder cycles.
let folderCycles =
  let graph =
    folderEdges
    |> Array.groupBy _.Source
    |> Array.map (fun (s, es) -> s, es |> Array.map _.Target)
    |> Map.ofArray

  let nodes =
    folderEdges
    |> Array.collect (fun e -> [| e.Source; e.Target |])
    |> Array.distinct
    |> Array.sort

  let index = Dictionary<string, int>()
  let low = Dictionary<string, int>()
  let stack = Stack<string>()
  let onStack = HashSet<string>()
  let result = ResizeArray<string[]>()
  let mutable next = 0

  let rec visit v =
    index[v] <- next
    low[v] <- next
    next <- next + 1
    stack.Push v
    onStack.Add v |> ignore

    for w in graph |> Map.tryFind v |> Option.defaultValue [||] do
      if not (index.ContainsKey w) then
        visit w
        low[v] <- min low[v] low[w]
      elif onStack.Contains w then
        low[v] <- min low[v] index[w]

    if low[v] = index[v] then
      let comp = ResizeArray()
      let mutable fin = false

      while not fin do
        let w = stack.Pop()
        onStack.Remove w |> ignore
        comp.Add w
        fin <- w = v

      if comp.Count > 1 then
        result.Add(comp.ToArray() |> Array.sort)

  for v in nodes do
    if not (index.ContainsKey v) then
      visit v

  result.ToArray() |> Array.sortBy (fun c -> c[0])

/// Project references as MSBuild evaluates them, for every project in the repository.
let projects =
  Directory.EnumerateFiles(root, "*.fsproj", SearchOption.AllDirectories)
  |> Seq.map relative
  |> Seq.filter (fun p -> not (p.Contains "/obj/" || p.Contains "/bin/" || p.StartsWith "artifacts/"))
  |> Seq.sort
  |> Seq.map (fun p ->
    let doc = System.Xml.Linq.XDocument.Load(Path.Combine(root, p))

    let items name =
      doc.Descendants()
      |> Seq.filter (fun e -> e.Name.LocalName = name)
      |> Seq.map (fun e -> e.Attribute(System.Xml.Linq.XName.Get "Include").Value.Replace('\\', '/'))
      |> Seq.map (fun r -> Path.GetFileNameWithoutExtension r)
      |> Array.ofSeq

    {| Project = Path.GetFileNameWithoutExtension p
       Path = p
       References = items "ProjectReference"
       Packages = items "PackageReference" |})
  |> Array.ofSeq

let output =
  {| Projects = projects
     Files = files
     Systems =
      systemFiles
      |> Map.toArray
      |> Array.map (fun (f, m) -> {| File = f; Module = m |})
     FolderEdges = folderEdges
     FolderCycles = folderCycles
     Rules = rules
     Edges = fileEdges |}

let outDir = Path.Combine(root, "artifacts/architecture")
Directory.CreateDirectory outDir |> ignore

File.WriteAllText(
  Path.Combine(outDir, "architecture.json"),
  JsonSerializer.Serialize(output, JsonSerializerOptions(WriteIndented = true))
)

printfn
  "%d projects, %d files, %d file edges, %d folder edges -> artifacts/architecture/architecture.json"
  projects.Length
  files.Length
  fileEdges.Length
  folderEdges.Length

for r in rules do
  printfn "  %-4d %s" r.Violations.Length r.Rule

printfn "  %d folder cycles" folderCycles.Length
