namespace FootballEngine.ModCli

open System
open FootballEngine.Data

module Program =
    let private usage () =
        Console.Error.WriteLine(
            "Usage: ModCli validate <workspace> | pack <workspace> <output.femod> | inspect <package.femod>"
        )

        2

    let private printDiagnostics (error: ModError) =
        for item in error.Diagnostics do
            let location =
                [ item.RelativePath; item.JsonPath; item.EntityId ]
                |> List.choose id
                |> String.concat ""

            Console.Error.WriteLine($"{item.Code} {location}: {item.Message} Action: {item.SuggestedAction}")

    let private runResult onSuccess result =
        match result with
        | Ok value ->
            onSuccess value
            0
        | Error error ->
            printDiagnostics error
            1

    [<EntryPoint>]
    let main argv =
        match argv with
        | [| "validate"; workspace |] ->
            ModSystem.loadPackage workspace
            |> runResult (fun selection ->
                printfn "%s@%s" selection.Package.Manifest.ModId selection.Package.Manifest.Version)
        | [| "pack"; workspace; output |] ->
            ModSystem.packWorkspace workspace output
            |> runResult (fun () -> printfn "Packed %s" output)
        | [| "inspect"; packagePath |] ->
            ModSystem.loadFemod packagePath
            |> runResult (fun selection ->
                printfn "%s@%s" selection.Package.Manifest.ModId selection.Package.Manifest.Version
                printfn "Package hash: %s" selection.Package.PackageSha256
                printfn "Sports content: %d" selection.Package.SportsContent.Count
                printfn "Translations: %d" selection.Package.Translations.Count
                printfn "Assets: %d" selection.Package.Assets.Count)
        | _ -> usage ()
