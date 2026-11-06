open System
open System.Diagnostics
open System.IO
open System.Xml.Linq

type Options =
    { Project: string option
      Filter: string option
      List: bool
      SelfTest: bool }

let usage =
    """Repository tests (Expecto, built in Release)
dotnet fsi scripts/test.fsx [--project Core|Client|<project-name>] [--filter <full-name-prefix>]
dotnet fsi scripts/test.fsx [--project <name>] --list
dotnet fsi scripts/test.fsx --self-test

All test projects run by default. --filter uses Expecto's dot-separated full-name prefix.
Listing and self-test modes do not execute game tests. Reports: artifacts/tests/<run-id>/.
No arbitrary Expecto arguments: the runner owns selection, strictness and reporting.
"""

let parseArgs arguments =
    let initial =
        { Project = None
          Filter = None
          List = false
          SelfTest = false }

    let rec parse options remaining =
        match remaining with
        | [] when options.SelfTest && options <> { initial with SelfTest = true } ->
            Error "--self-test cannot be combined with test selection."
        | [] when options.List && options.Filter.IsSome -> Error "Use --list without --filter to discover full names."
        | [] -> Ok options
        | "--project" :: value :: rest when options.Project.IsNone && not (String.IsNullOrWhiteSpace value) ->
            parse { options with Project = Some value } rest
        | "--filter" :: value :: rest when options.Filter.IsNone && not (String.IsNullOrWhiteSpace value) ->
            parse { options with Filter = Some value } rest
        | "--list" :: rest when not options.List -> parse { options with List = true } rest
        | "--self-test" :: rest when not options.SelfTest -> parse { options with SelfTest = true } rest
        | argument :: _ -> Error $"Invalid, repeated or incomplete argument: {argument}\n{usage}"

    parse initial arguments

let discoverProjects root =
    // Discover source projects, never generated copies under build/artifact directories.
    let excluded = Set.ofList [ ".git"; "bin"; "obj"; "artifacts"; "node_modules" ]

    let rec discover directory =
        seq {
            yield! Directory.EnumerateFiles(directory, "*.Tests.fsproj")

            for child in Directory.EnumerateDirectories directory do
                if
                    not (excluded.Contains(Path.GetFileName child))
                    && not (File.GetAttributes(child).HasFlag FileAttributes.ReparsePoint)
                then
                    yield! discover child
        }

    discover root |> Seq.sort |> Seq.toList

let selectProjects (selector: string option) (projects: string list) =
    let selected =
        match selector with
        | None -> projects
        | Some name ->
            projects
            |> List.filter (fun path ->
                let stem = Path.GetFileNameWithoutExtension path

                stem.Equals(name, StringComparison.OrdinalIgnoreCase)
                || stem.Equals($"FootballEngine.{name}.Tests", StringComparison.OrdinalIgnoreCase))

    let duplicateNames =
        selected
        |> List.groupBy (fun path -> Path.GetFileNameWithoutExtension(path).ToUpperInvariant())
        |> List.exists (fun (_, paths) -> paths.Length > 1)

    match selected with
    | [] -> Error "No test projects selected. Check --project and the *.Tests.fsproj naming convention."
    | _ when selector.IsSome && selected.Length > 1 -> Error "Ambiguous test project selection. Use its complete name."
    | _ when duplicateNames -> Error "Test project names must be unique to avoid report collisions."
    | _ -> Ok selected

let validateReport path =
    try
        let document = XDocument.Load(path: string)

        if isNull document.Root || document.Root.Name.LocalName <> "testsuites" then
            Error $"Invalid JUnit root: {path}"
        else
            let cases = document.Descendants(XName.Get "testcase") |> Seq.toArray

            let rejected =
                cases
                |> Array.filter (fun test ->
                    test.Elements()
                    |> Seq.exists (fun child ->
                        Set.contains child.Name.LocalName (Set.ofList [ "failure"; "error"; "skipped" ])))

            if cases.Length = 0 then
                Error $"Zero tests executed: {path}. Check the filter; an empty selection is not success."
            elif rejected.Length > 0 then
                Error $"{rejected.Length} failed, errored or skipped test(s): {path}"
            else
                Ok cases.Length
    with ex ->
        Error $"Missing or unreadable JUnit report: {path}: {ex.Message}"

let runProcess root logPath arguments =
    let info = ProcessStartInfo("dotnet")
    info.WorkingDirectory <- root
    info.UseShellExecute <- false
    info.RedirectStandardOutput <- true
    info.RedirectStandardError <- true
    info.Environment["DOTNET_NOLOGO"] <- "true"
    info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] <- "true"

    for argument in arguments do
        info.ArgumentList.Add argument

    use log = new StreamWriter(logPath: string)
    let gate = obj ()

    let write (line: string) =
        if not (isNull line) then
            lock gate (fun () ->
                log.WriteLine line
                log.Flush()
                Console.WriteLine line)

    use child = new Process(StartInfo = info)
    use interruption = new System.Threading.CancellationTokenSource()
    child.OutputDataReceived.Add(fun event -> write event.Data)
    child.ErrorDataReceived.Add(fun event -> write event.Data)

    let cancel =
        ConsoleCancelEventHandler(fun _ event ->
            event.Cancel <- true
            interruption.Cancel()

            try
                if not child.HasExited then
                    child.Kill(true)
            with :? InvalidOperationException ->
                // Exit can race cancellation after the process has already released its handle.
                ())

    try
        if not (child.Start()) then
            Error "Could not start dotnet."
        else
            Console.CancelKeyPress.AddHandler cancel
            child.BeginOutputReadLine()
            child.BeginErrorReadLine()
            child.WaitForExit()
            interruption.Token.ThrowIfCancellationRequested()

            if child.ExitCode = 0 then
                Ok()
            else
                Error $"dotnet exited with code {child.ExitCode}. See {logPath}"
    finally
        Console.CancelKeyPress.RemoveHandler cancel

let runProject root results options (project: string) =
    let name = Path.GetFileNameWithoutExtension project

    let log suffix =
        Path.Combine(results, $"{name}.{suffix}.log")

    let report = Path.Combine(results, $"{name}.xml")
    printfn "\n=== %s ===" name

    match runProcess root (log "build") [ "build"; project; "--configuration"; "Release"; "--nologo" ] with
    | Error error -> Error error
    | Ok() ->
        let selection =
            match options.Filter with
            | Some prefix -> [ "--filter"; prefix ]
            | None -> []

        let expecto =
            if options.List then
                [ "--list-tests"; "--no-spinner"; "--colours"; "0" ]
            else
                [ "--fail-on-focused-tests"
                  "--no-spinner"
                  "--colours"
                  "0"
                  "--junit-summary"
                  report ]
                @ selection

        let arguments =
            [ "run"
              "--project"
              project
              "--configuration"
              "Release"
              "--no-build"
              "--no-launch-profile"
              "--" ]
            @ expecto

        match runProcess root (log "tests") arguments with
        | Error error -> Error error
        | Ok() when options.List -> Ok 0
        | Ok() -> validateReport report

let run root options =
    match discoverProjects root |> selectProjects options.Project with
    | Error error -> Error error
    | Ok projects ->
        let runId =
            DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")
            + "-"
            + Guid.NewGuid().ToString("N")

        let results = Path.Combine(root, "artifacts", "tests", runId)
        Directory.CreateDirectory results |> ignore
        printfn "Reports and logs: %s" results

        // Sequential projects avoid competing builds and noisy timing; Expecto owns case-level parallelism.
        let outcomes =
            projects
            |> List.map (fun project -> project, runProject root results options project)

        let failures =
            outcomes
            |> List.choose (fun (project, result) ->
                match result with
                | Ok _ -> None
                | Error error -> Some $"{project}: {error}")

        if not failures.IsEmpty then
            Error(String.concat Environment.NewLine failures)
        elif options.List then
            printfn "Discovery only: no game tests executed."
            Ok()
        else
            let count =
                outcomes |> List.sumBy (fun (_, result) -> result |> Result.defaultValue 0)

            printfn "Validated %d passing tests across %d project(s). Reports: %s" count projects.Length results
            Ok()
