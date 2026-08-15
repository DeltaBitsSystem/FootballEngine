module FootballEngine.MatchAccumulator

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open FootballEngine.Domain

type AccumulatedMatch =
    { [<JsonPropertyName("timestamp")>]
      Timestamp: string
      [<JsonPropertyName("home")>]
      Home: string
      [<JsonPropertyName("away")>]
      Away: string
      [<JsonPropertyName("homeScore")>]
      HomeScore: int
      [<JsonPropertyName("awayScore")>]
      AwayScore: int
      [<JsonPropertyName("eventCount")>]
      EventCount: int
      [<JsonPropertyName("goalsHome")>]
      GoalsHome: int
      [<JsonPropertyName("goalsAway")>]
      GoalsAway: int
      [<JsonPropertyName("shotsHome")>]
      ShotsHome: int
      [<JsonPropertyName("shotsAway")>]
      ShotsAway: int
      [<JsonPropertyName("passesHome")>]
      PassesHome: int
      [<JsonPropertyName("passesAway")>]
      PassesAway: int
      [<JsonPropertyName("foulsHome")>]
      FoulsHome: int
      [<JsonPropertyName("foulsAway")>]
      FoulsAway: int
      [<JsonPropertyName("cornersHome")>]
      CornersHome: int
      [<JsonPropertyName("cornersAway")>]
      CornersAway: int
      [<JsonPropertyName("dribblesHome")>]
      DribblesHome: int
      [<JsonPropertyName("dribblesAway")>]
      DribblesAway: int
      [<JsonPropertyName("possessionHome")>]
      PossessionHome: float
      [<JsonPropertyName("events")>]
      Events: JsonElement }

type AccumulatedMatchSummary =
    { [<JsonPropertyName("timestamp")>]
      Timestamp: string
      [<JsonPropertyName("home")>]
      Home: string
      [<JsonPropertyName("away")>]
      Away: string
      [<JsonPropertyName("homeScore")>]
      HomeScore: int
      [<JsonPropertyName("awayScore")>]
      AwayScore: int
      [<JsonPropertyName("eventCount")>]
      EventCount: int
      [<JsonPropertyName("goalsHome")>]
      GoalsHome: int
      [<JsonPropertyName("goalsAway")>]
      GoalsAway: int
      [<JsonPropertyName("shotsHome")>]
      ShotsHome: int
      [<JsonPropertyName("shotsAway")>]
      ShotsAway: int
      [<JsonPropertyName("passesHome")>]
      PassesHome: int
      [<JsonPropertyName("passesAway")>]
      PassesAway: int
      [<JsonPropertyName("foulsHome")>]
      FoulsHome: int
      [<JsonPropertyName("foulsAway")>]
      FoulsAway: int
      [<JsonPropertyName("cornersHome")>]
      CornersHome: int
      [<JsonPropertyName("cornersAway")>]
      CornersAway: int
      [<JsonPropertyName("dribblesHome")>]
      DribblesHome: int
      [<JsonPropertyName("dribblesAway")>]
      DribblesAway: int
      [<JsonPropertyName("possessionHome")>]
      PossessionHome: float }

let private accumFile = Path.Combine("diag", "matches.json")
let private lockObj = obj ()

let private jsonOptions =
    let opts = JsonSerializerOptions(WriteIndented = false)
    opts

let private ensureDir () =
    let dir = DirectoryInfo("diag")

    if not dir.Exists then
        dir.Create() |> ignore

let appendMatch
    (homeName: string)
    (awayName: string)
    (homeId: ClubId)
    (awayId: ClubId)
    (homeScore: int)
    (awayScore: int)
    (events: MatchEvent list)
    =
    ensureDir ()

    let metrics = MatchMetrics.extract events homeId awayId

    let eventsJson =
        events
        |> List.map (fun e ->
            {| tick = e.SubTick
               playerId = int e.PlayerId
               clubId = int e.ClubId
               ``type`` = string e.Type |})
        |> fun els -> JsonSerializer.Serialize(els, jsonOptions)

    let match_ =
        { Timestamp = DateTime.UtcNow.ToString("o")
          Home = homeName
          Away = awayName
          HomeScore = homeScore
          AwayScore = awayScore
          EventCount = events.Length
          GoalsHome = metrics.GoalsHome
          GoalsAway = metrics.GoalsAway
          ShotsHome = metrics.ShotsHome
          ShotsAway = metrics.ShotsAway
          PassesHome = metrics.PassesHome
          PassesAway = metrics.PassesAway
          FoulsHome = metrics.FoulsHome
          FoulsAway = metrics.FoulsAway
          CornersHome = metrics.CornersHome
          CornersAway = metrics.CornersAway
          DribblesHome = metrics.DribblesHome
          DribblesAway = metrics.DribblesAway
          PossessionHome = metrics.PossessionHome
          Events = JsonSerializer.Deserialize<JsonElement>(eventsJson) }

    let line = JsonSerializer.Serialize(match_, jsonOptions)

    lock lockObj (fun _ ->
        use sw = new StreamWriter(accumFile, append = true)
        sw.WriteLine(line))

    accumFile

let readAll () : AccumulatedMatchSummary list =
    ensureDir ()

    if not (File.Exists accumFile) then
        []
    else
        File.ReadAllLines(accumFile)
        |> Array.choose (fun line ->
            if String.IsNullOrWhiteSpace(line) then
                None
            else
                try
                    Some(JsonSerializer.Deserialize<AccumulatedMatchSummary>(line))
                with _ ->
                    None)
        |> Array.toList

let count () : int =
    ensureDir ()

    if not (File.Exists accumFile) then
        0
    else
        File.ReadAllLines(accumFile)
        |> Array.filter (fun l -> not (String.IsNullOrWhiteSpace l))
        |> Array.length

let clear () =
    if File.Exists accumFile then
        File.Delete accumFile

let readRaw () : string list =
    ensureDir ()

    if not (File.Exists accumFile) then
        []
    else
        File.ReadAllLines(accumFile) |> Array.toList
