#load "TestRunner.fsx"

open System
open System.IO
open TestRunner

let runSelfTests () =
    let root =
        Path.Combine(Path.GetTempPath(), "footballengine-runner-" + Guid.NewGuid().ToString("N"))

    Directory.CreateDirectory root |> ignore
    let mutable checkedCases = 0

    let check name condition =
        checkedCases <- checkedCases + 1

        if not condition then
            failwith $"Runner self-test failed: {name}"

    let report (text: string) =
        let path = Path.Combine(root, "results.xml")
        File.WriteAllText(path, text)
        validateReport path

    try
        check "default arguments" (parseArgs [] |> Result.isOk)
        check "unknown argument" (parseArgs [ "--ignore-failures" ] |> Result.isError)
        check "missing project" (parseArgs [ "--project" ] |> Result.isError)
        check "empty filter" (parseArgs [ "--filter"; " " ] |> Result.isError)
        check "duplicate selection" (parseArgs [ "--project"; "Core"; "--project"; "Client" ] |> Result.isError)
        check "list filter ambiguity" (parseArgs [ "--list"; "--filter"; "x" ] |> Result.isError)
        check "self-test is exclusive" (parseArgs [ "--self-test"; "--project"; "Core" ] |> Result.isError)
        check "missing report" (validateReport (Path.Combine(root, "absent.xml")) |> Result.isError)
        check "malformed report" (report "not XML" |> Result.isError)
        check "foreign report" (report "<document />" |> Result.isError)
        check "empty selection" (report "<testsuites><testsuite /></testsuites>" |> Result.isError)

        check
            "passing report"
            (report "<testsuites><testsuite><testcase name='real' time='0.1'/></testsuite></testsuites>" = Ok 1)

        for failure in [ "failure"; "error"; "skipped" ] do
            check
                failure
                (report $"<testsuites><testsuite><testcase name='bad'><{failure}/></testcase></testsuite></testsuites>"
                 |> Result.isError)

        let makeProject directory name =
            let path = Path.Combine(root, directory)
            Directory.CreateDirectory path |> ignore
            let project = Path.Combine(path, name + ".fsproj")
            File.WriteAllText(project, "<Project />")
            project

        let core = makeProject "FootballEngine.Core.Tests" "FootballEngine.Core.Tests"
        let client = makeProject "FootballEngine.Client.Tests" "FootballEngine.Client.Tests"
        makeProject "obj/generated" "Ghost.Tests" |> ignore
        makeProject "artifacts" "Stale.Tests" |> ignore
        makeProject "Tools/Extra.Tests" "Extra.Tests" |> ignore
        let projects = discoverProjects root
        check "source discovery ignores generated projects" (projects.Length = 3)
        check "Core shorthand" (selectProjects (Some "Core") projects = Ok [ core ])
        check "full project name" (selectProjects (Some "FootballEngine.Client.Tests") projects = Ok [ client ])
        check "unknown project" (selectProjects (Some "Typo") projects |> Result.isError)
        check "empty repository" (selectProjects None [] |> Result.isError)
        check "ambiguous project" (selectProjects (Some "Core") [ core; core ] |> Result.isError)
        check "duplicate output names" (selectProjects None [ core; core ] |> Result.isError)
        printfn "Runner self-tests: %d passed (not game tests)." checkedCases
    finally
        // Only this invocation's GUID-owned fixtures are removed, never repository or user data.
        Directory.Delete(root, true)
