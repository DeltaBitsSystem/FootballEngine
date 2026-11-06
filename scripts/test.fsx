#load "testing/TestRunner.fsx"
#load "testing/TestRunnerTests.fsx"

open System
open System.IO

let arguments = fsi.CommandLineArgs |> Array.skip 1 |> Array.toList

if arguments = [ "--help" ] then
    printfn "%s" TestRunner.usage
else
    match TestRunner.parseArgs arguments with
    | Error error ->
        eprintfn "%s" error
        exit 2
    | Ok options ->
        try
            TestRunnerTests.runSelfTests ()

            if not options.SelfTest then
                let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

                match TestRunner.run root options with
                | Ok() -> ()
                | Error error ->
                    eprintfn "%s" error
                    exit 1
        with
        | :? OperationCanceledException ->
            eprintfn "Test execution cancelled; no remaining projects will start."
            exit 130
        | ex ->
            eprintfn "Test infrastructure failed: %s" ex.Message
            exit 1
