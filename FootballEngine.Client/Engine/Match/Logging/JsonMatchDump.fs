module FootballEngine.JsonMatchDump

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open FootballEngine.Domain

type MatchEventDto =
    { [<JsonPropertyName("tick")>]
      Tick: int
      [<JsonPropertyName("playerId")>]
      PlayerId: int
      [<JsonPropertyName("clubId")>]
      ClubId: int
      [<JsonPropertyName("type")>]
      Type: string
      [<JsonPropertyName("position")>]
      Position: {| X: float; Y: float |} option
      [<JsonPropertyName("quality")>]
      Quality: float option
      [<JsonPropertyName("xG")>]
      XG: float option
      [<JsonPropertyName("passMean")>]
      PassMean: float option
      [<JsonPropertyName("distancePenalty")>]
      DistancePenalty: float option
      [<JsonPropertyName("controlProb")>]
      ControlProb: float option
      [<JsonPropertyName("interceptorId")>]
      InterceptorId: int option
      [<JsonPropertyName("gkSaveAttempt")>]
      GKSaveAttempt: bool option
      [<JsonPropertyName("gkSaveProb")>]
      GKSaveProb: float option
      [<JsonPropertyName("shotSpeed")>]
      ShotSpeed: float option
      [<JsonPropertyName("shotAngle")>]
      ShotAngle: float option
      [<JsonPropertyName("detail")>]
      Detail: string option }

type MatchDump =
    { [<JsonPropertyName("home")>]
      Home: string
      [<JsonPropertyName("away")>]
      Away: string
      [<JsonPropertyName("homeScore")>]
      HomeScore: int
      [<JsonPropertyName("awayScore")>]
      AwayScore: int
      [<JsonPropertyName("events")>]
      Events: MatchEventDto[] }

let private eventTypeToString (t: MatchEventType) : string * string option =
    match t with
    | Goal -> "Goal", None
    | GoalKick -> "GoalKick", None
    | OwnGoal -> "OwnGoal", None
    | Assist -> "Assist", None
    | YellowCard -> "YellowCard", None
    | RedCard -> "RedCard", None
    | Injury desc -> "Injury", Some desc
    | SubstitutionIn -> "SubstitutionIn", None
    | SubstitutionOut -> "SubstitutionOut", None
    | PenaltyAwarded scored -> "PenaltyAwarded", Some(string scored)
    | FreeKick scored -> "FreeKick", Some(string scored)
    | Corner -> "Corner", None
    | PassCompleted(from, to_) -> "PassCompleted", Some(sprintf "%d->%d" from to_)
    | PassLaunched(from, to_) -> "PassLaunched", Some(sprintf "%d->%d" from to_)
    | PassIncomplete from -> "PassIncomplete", Some(string from)
    | PassDeflected(from, by) -> "PassDeflected", Some(sprintf "%d->%d" from by)
    | PassIntercepted(from, by) -> "PassIntercepted", Some(sprintf "%d->%d" from by)
    | PassMisplaced(from, to_) -> "PassMisplaced", Some(sprintf "%d->%d" from to_)
    | DribbleSuccess -> "DribbleSuccess", None
    | DribbleFail -> "DribbleFail", None
    | DribbleKeep -> "DribbleKeep", None
    | TackleSuccess -> "TackleSuccess", None
    | TackleFail -> "TackleFail", None
    | CrossAttempt success -> "CrossAttempt", Some(string success)
    | CrossLaunched(from, to_) -> "CrossLaunched", Some(sprintf "%d->%d" from to_)
    | LongBall success -> "LongBall", Some(string success)
    | ShotBlocked -> "ShotBlocked", None
    | ShotLaunched -> "ShotLaunched", None
    | ShotOnTarget -> "ShotOnTarget", None
    | ShotOffTarget -> "ShotOffTarget", None
    | Save -> "Save", None
    | SaveCaught(sh, gk) -> "SaveCaught", Some(sprintf "sh=%d gk=%d" sh gk)
    | SaveParried(sh, gk) -> "SaveParried", Some(sprintf "sh=%d gk=%d" sh gk)
    | GKPunch gk -> "GKPunch", Some(string gk)
    | GKDistribution(gk, t) -> "GKDistribution", Some(sprintf "%d->%d" gk t)
    | IndirectFreeKickAwarded team -> "IndirectFreeKick", Some(string team)
    | FoulCommitted -> "FoulCommitted", None
    | KickOff -> "KickOff", None

let private toDto (e: MatchEvent) : MatchEvent = e

let dumpMatch (homeName: string) (awayName: string) (homeScore: int) (awayScore: int) (events: MatchEvent list) =
    let dir = DirectoryInfo("diag")

    if not dir.Exists then
        dir.Create() |> ignore

    let timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")
    let fileName = sprintf "diag/match_%s.json" timestamp

    let dtos =
        events
        |> List.map (fun e ->
            let typeName, detail = eventTypeToString e.Type

            { Tick = e.SubTick
              PlayerId = e.PlayerId
              ClubId = e.ClubId
              Type = typeName
              Position = e.Context.Position |> Option.map (fun p -> {| X = p.X; Y = p.Y |})
              Quality = e.Context.Quality
              XG = e.Context.ExpectedGoal
              PassMean = e.Context.PassMean
              DistancePenalty = e.Context.DistancePenalty
              ControlProb = e.Context.ControlProb
              InterceptorId = e.Context.InterceptorId
              GKSaveAttempt = e.Context.GKSaveAttempt
              GKSaveProb = e.Context.GKSaveProb
              ShotSpeed = e.Context.ShotSpeed
              ShotAngle = e.Context.ShotAngle
              Detail = detail })
        |> List.toArray

    let dump =
        { Home = homeName
          Away = awayName
          HomeScore = homeScore
          AwayScore = awayScore
          Events = dtos }

    let options = JsonSerializerOptions(WriteIndented = true)
    let json = JsonSerializer.Serialize(dump, options)
    File.WriteAllText(fileName, json)
    fileName
