// Day-to-day entry point: one place for the commands a change goes through, so nobody has to remember which
// projects sit outside the solution or which Training command measures what.
//
//   dotnet fsi scripts/dev.fsx build              solution plus the separately built test and benchmark projects
//   dotnet fsi scripts/dev.fsx test               fast tier (edit loop)
//   dotnet fsi scripts/dev.fsx test --full        every suite: the gate
//   dotnet fsi scripts/dev.fsx format [--check]   Fantomas over the repository
//   dotnet fsi scripts/dev.fsx profile            one headless match, time and allocation per system
//   dotnet fsi scripts/dev.fsx perf               the profile held to perf/budget.txt: a regression fails here
//   dotnet fsi scripts/dev.fsx hash [n]           event hash over n tuning seeds: a pure refactor must not change it
//   dotnet fsi scripts/dev.fsx check              build, then the fast tier: before every commit

open System
open System.Diagnostics
open System.IO

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let run (file: string) (args: string list) =
    let info = ProcessStartInfo(file, WorkingDirectory = root, UseShellExecute = false)
    args |> List.iter info.ArgumentList.Add
    printfn "> %s %s" file (String.concat " " args)
    use child = Process.Start info
    child.WaitForExit()
    child.ExitCode

let rec sequence steps =
    match steps with
    | [] -> 0
    | step :: rest ->
        match step () with
        | 0 -> sequence rest
        | code -> code

let build () =
    sequence
        [ for project in
              [ "FootballEngine.slnx"
                "FootballEngine.Client.Tests/FootballEngine.Client.Tests.fsproj"
                "FootballEngine.Benchmarks/FootballEngine.Benchmarks.fsproj" ] ->
              fun () -> run "dotnet" [ "build"; project; "-c"; "Release"; "--nologo" ] ]

let test full =
    run "dotnet" ([ "fsi"; "scripts/test.fsx" ] @ (if full then [] else [ "--fast" ]))

let training args =
    sequence
        [ fun () -> run "dotnet" [ "build"; "Training"; "-c"; "Release"; "--nologo" ]
          fun () -> run "dotnet" ([ "Training/bin/Release/net10.0/Training.dll" ] @ args) ]

let usage () =
    eprintfn "usage: dotnet fsi scripts/dev.fsx build | test [--full] | format [--check] | profile | perf | hash [n] | check"
    2

let code =
    match fsi.CommandLineArgs |> Array.skip 1 |> Array.toList with
    | [ "build" ] -> build ()
    | [ "test" ] -> test false
    | [ "test"; "--full" ] -> test true
    | [ "format" ] -> run "dotnet" [ "fantomas"; "." ]
    | [ "format"; "--check" ] -> run "dotnet" [ "fantomas"; "."; "--check" ]
    | [ "profile" ] -> training [ "profile" ]
    | [ "perf" ] -> training [ "profile"; "--gate"; "perf/budget.txt" ]
    | [ "hash" ] -> training [ "hash"; "4" ]
    | [ "hash"; n ] -> training [ "hash"; n ]
    | [ "check" ] -> sequence [ build; fun () -> test false ]
    | _ -> usage ()

exit code
