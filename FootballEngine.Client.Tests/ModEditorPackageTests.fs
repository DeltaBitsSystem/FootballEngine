module FootballEngine.Client.Tests.ModEditorPackageTests

open System
open System.IO
open Expecto
open FootballEngine
open FootballEngine.AppMsgs
open FootballEngine.Data
open FootballEngine.Domain
open FootballEngine.Generation
open FootballEngine.ModEditorTypes

let private argentinaPath =
    Path.Combine(ModPaths.builtinsDir, "builtin.argentina-1.0.0.femod")

let private startDate = DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc)

let private withTemp (body: string -> unit) =
    let dir = Directory.CreateTempSubdirectory("fe-modeditor-").FullName

    try
        body dir
    finally
        if Directory.Exists dir then
            Directory.Delete(dir, true)

let private ok label result =
    match result with
    | Ok value -> value
    | Error error -> failtestf "%s failed: %A" label error

/// Same write path as `publish` (build input, workspace, packWorkspace) but into an isolated directory.
let private writePackage (document: EditorDocument) (modsDir: string) =
    let input = ModEditorPackage.buildPackageInput document |> ok "buildPackageInput"
    let workspace = Path.Combine(modsDir, "ws-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory workspace |> ignore
    File.WriteAllText(Path.Combine(workspace, "manifest.json"), input.ManifestJson)

    for KeyValue(relative, bytes) in input.Files do
        let file = Path.Combine(workspace, relative.Replace('/', Path.DirectorySeparatorChar))
        Directory.CreateDirectory(Path.GetDirectoryName file) |> ignore
        File.WriteAllBytes(file, bytes)

    let output = Path.Combine(modsDir, ModEditorPackage.fileNameFor document.Manifest)
    ModPackageFile.packWorkspace workspace output |> ok "packWorkspace"
    Directory.Delete(workspace, true)
    output

let private generate (modsDir: string) (activeIds: string list) =
    let resolved =
        ModPackageFile.loadInstalledPackages ModPaths.builtinsDir modsDir activeIds []
        |> ok "loadInstalledPackages"

    let materialized = ResolvedWorldData.materialize resolved |> ok "materialize"

    let state =
        WorldGen.generateNewGame resolved 0xC02UL startDate "ARG" "Test Manager" []
        |> ok "generateNewGame"

    materialized, state

let private keeper: PlayerEntryDto =
    { Name = "Zeta Testkeeper"
      Position = "GK"
      Age = 31
      Rating = 78
      Potential = 80
      PreferredFoot = None
      Nationality = None }

let private striker: PlayerEntryDto =
    { Name = "Omega Teststriker"
      Position = "ST"
      Age = 20
      Rating = 66
      Potential = 88
      PreferredFoot = None
      Nationality = Some "URU" }

let private withClub0Players (players: PlayerEntryDto list) (document: EditorDocument) =
    let country = document.Countries["ARG"]

    let clubs =
        country.Clubs
        |> List.mapi (fun i club -> if i = 0 then { club with Players = players } else club)

    { document with
        Countries = document.Countries |> Map.add "ARG" { country with Clubs = clubs } }

let private openArgentina () =
    ModEditorPackage.openPackage argentinaPath true |> ok "openPackage"

let private mentionsPlayers (diagnostics: ModDiagnostic list) =
    diagnostics
    |> List.exists (fun d ->
        let text = (defaultArg d.JsonPath "") + " " + (defaultArg d.EntityId "")
        text.Contains "Clubs/0/Players")

let private disableDrafts () =
    // update/saveDraft would otherwise write the author's real draft under LocalApplicationData.
    ModEditorPackage.draftsEnabled <- false

let private apply message state =
    UpdateModEditor.update message state |> fst

let private rename name =
    fun (d: CountryDataDto) ->
        { d with
            Country = { d.Country with Name = name } }

let private editState () =
    disableDrafts ()

    { initModEditorState with
        HasDocument = false
        Document =
            { emptyDocument with
                Countries = Map.ofList [ "TST", UpdateModEditor.createEmptyCountry "TST" "Testland" ] } }

let tests =
    testList
        "Mod editor package"
        [ test "override round trip: patched players reach the generated world and ARG is replaced, not duplicated" {
              disableDrafts ()

              withTemp (fun temp ->
                  let argentina = openArgentina ()

                  Expect.equal
                      argentina.Sources["ARG"]
                      { ModId = "builtin.argentina"
                        ContentId = "builtin.argentina:country/arg" }
                      "source points at the builtin entity"

                  Expect.notEqual argentina.Manifest.Id "builtin.argentina" "override gets its own mod id"

                  let doc = argentina |> withClub0Players [ keeper; striker ]
                  Expect.equal (ModEditorPackage.validate doc []) [] "valid edited document has no diagnostics"

                  let originalClub0 = argentina.Countries["ARG"].Clubs.Head.Name

                  // Sensitivity: with the patch ignored (builtins only) the authored names do not exist.
                  let emptyMods = Path.Combine(temp, "empty")
                  Directory.CreateDirectory emptyMods |> ignore
                  let _, baseState = generate emptyMods []

                  Expect.isFalse
                      (baseState.Players |> Map.exists (fun _ p -> p.Name = keeper.Name || p.Name = striker.Name))
                      "builtin-only world does not contain the authored players"

                  let mods = Path.Combine(temp, "mods")
                  Directory.CreateDirectory mods |> ignore
                  writePackage doc mods |> ignore
                  let materialized, state = generate mods [ doc.Manifest.Id ]

                  Expect.equal
                      (materialized.Countries |> List.filter (fun c -> c.Country.Code = "ARG") |> List.length)
                      1
                      "override replaced the builtin country"

                  Expect.equal
                      (state.Countries |> Map.toList |> List.filter (fun (c, _) -> c = "ARG") |> List.length)
                      1
                      "one ARG in world"

                  let club =
                      state.Clubs
                      |> Map.toList
                      |> List.map snd
                      |> List.find (fun c -> c.Name = originalClub0)

                  let squad = club.PlayerIds |> List.map (fun id -> state.Players[id])
                  let k = squad |> List.find (fun p -> p.Name = keeper.Name)
                  let s = squad |> List.find (fun p -> p.Name = striker.Name)
                  Expect.equal (k.CurrentSkill, k.PotentialSkill) (156, 160) "keeper skills double the rating"
                  Expect.equal (s.CurrentSkill, s.PotentialSkill) (132, 176) "striker skills double the rating"
                  Expect.equal s.Nationality "URU" "explicit nationality kept")
          }

          test "negative control: rating 0 is rejected by validate at the player path" {
              disableDrafts ()

              let doc = openArgentina () |> withClub0Players [ { keeper with Rating = 0 } ]
              let diagnostics = ModEditorPackage.validate doc []
              Expect.isNonEmpty diagnostics "invalid player is reported"
              Expect.isTrue (mentionsPlayers diagnostics) $"diagnostic names Clubs/0/Players: %A{diagnostics}"
          }

          test "re-opening the written package keeps countries, players and override sources" {
              disableDrafts ()

              withTemp (fun temp ->
                  let doc = openArgentina () |> withClub0Players [ keeper; striker ]
                  let path = writePackage doc temp
                  let reopened = ModEditorPackage.openPackage path false |> ok "reopen"
                  Expect.equal reopened.Countries doc.Countries "countries equal, players included"
                  Expect.equal reopened.Sources doc.Sources "patch read back as override"
                  Expect.equal reopened.Manifest.Id doc.Manifest.Id "same mod id")
          }

          test "a new country publishes as a plain country entity and validates clean" {
              disableDrafts ()

              let country =
                  { UpdateModEditor.createEmptyCountry "TST" "Testland" with
                      Names =
                          { FirstNames = [ "Alex"; "Sam" ]
                            LastNames = [ "Stone"; "River" ] }
                      LeagueNames = [ "Top"; "Second" ]
                      LeagueRules = [ UpdateModEditor.createEmptyLeague "Top"; UpdateModEditor.createEmptyLeague "Second" ]
                      Clubs =
                          [ for i in 1..4 -> UpdateModEditor.createEmptyClub $"Top FC {i}" 0 ]
                          @ [ for i in 1..4 -> UpdateModEditor.createEmptyClub $"Second FC {i}" 1 ] }

              let doc =
                  { emptyDocument with
                      Countries = Map.ofList [ "TST", country ] }

              let input = ModEditorPackage.buildPackageInput doc |> ok "build"
              Expect.isTrue (input.Files.ContainsKey "content/countries/tst.json") "plain country file"
              Expect.isFalse (input.Files.Keys |> Seq.exists (fun k -> k.StartsWith "content/patches")) "no patch"
              Expect.equal (ModEditorPackage.validate doc []) [] "clean"

              let broken =
                  { country with
                      LeagueNames = [ "Top" ]
                      LeagueRules = [ UpdateModEditor.createEmptyLeague "Top" ] }

              let diagnostics =
                  ModEditorPackage.validate
                      { doc with
                          Countries = Map.ofList [ "TST", broken ] }
                      []

              Expect.isNonEmpty diagnostics "clubs at level 1 without a league name are reported"
          }

          test "edits: same field coalesces, different field adds a step, equal value is a no-op, undo restores" {
              let initial = editState ()
              let a1 = apply (EditCountry("TST", "name", rename "One")) initial
              let a2 = apply (EditCountry("TST", "name", rename "Two")) a1
              Expect.equal a2.Past.Length 1 "same key coalesces into one step"

              let b =
                  apply
                      (EditCountry(
                          "TST",
                          "confederation",
                          fun d ->
                              { d with
                                  Country = { d.Country with Confederation = "CAF" } }
                      ))
                      a2

              Expect.equal b.Past.Length 2 "different field is a second step"

              let noop = apply (EditCountry("TST", "confederation", id)) b
              Expect.equal noop.Past b.Past "no-op leaves Past"
              Expect.equal noop.IsDirty b.IsDirty "no-op leaves IsDirty"
              Expect.equal noop.LastEditKey b.LastEditKey "no-op leaves edit key"

              let undone = apply UndoMod (apply UndoMod b)
              Expect.equal undone.Document initial.Document "two undos restore the pre-edit document"
          } ]
