open FootballEngine.Tools

[<EntryPoint>]
let main argv =
    let outputPath =
        if argv.Length > 0 then argv.[0]
        else "weights.json"

    ConfigExtractor.run outputPath
    0
