module FootballEngine.Tests.Diagnostics.MatchDiagnoster

open System
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.Tests.Infrastructure.Builders
open FootballEngine.Tests.Infrastructure.Runners

type BatchResult =
    { MatchCount: int
      AvgGoalsPer90: float
      AvgShotsPer90: float
      ShotConversionPct: float
      GoalsWithNoShot: int
      PassAccuracyPct: float
      AvgPassesPer90: float
      AvgCornersPer90: float
      AvgFoulsPer90: float
      AvgDribblesPer90: float
      PossessionHomePct: float
      HomeWins: int
      AwayWins: int
      Draws: int
      MaxBallSpeed: float
      BallInBounds: bool
      PlayersMoved: bool
      FatigueAccumulated: bool
      MatchReachedFulltime: bool
      Exceptions: int }

let private fullMatchTicks = 10 * 40 * 60

let private makeTestLineup (gkSkill: int) (outfieldSkill: int) : Player[] =
    [| makeGk 1 gkSkill gkSkill gkSkill
       makePlayer 2 DL outfieldSkill
       makePlayer 3 DC outfieldSkill
       makePlayer 4 DC outfieldSkill
       makePlayer 5 DR outfieldSkill
       makePlayer 6 DM outfieldSkill
       makePlayer 7 MC outfieldSkill
       makePlayer 8 MC outfieldSkill
       makePlayer 9 AML outfieldSkill
       makePlayer 10 AMC outfieldSkill
       makePlayer 11 ST outfieldSkill |]

let private homePositions =
    [| 50.0, 34.0
       35.0, 10.0
       38.0, 34.0
       38.0, 54.0
       35.0, 68.0
       45.0, 34.0
       52.0, 20.0
       52.0, 54.0
       60.0, 10.0
       62.0, 34.0
       70.0, 34.0 |]

let private awayPositions =
    [| 100.0, 34.0
       91.0, 56.0
       91.0, 43.0
       91.0, 25.0
       91.0, 12.0
       75.0, 34.0
       65.0, 46.0
       65.0, 22.0
       45.0, 53.0
       40.0, 34.0
       37.0, 34.0 |]

let runBatch
    (n: int)
    (homeGkSkill: int)
    (homeOutfieldSkill: int)
    (awayGkSkill: int)
    (awayOutfieldSkill: int)
    : BatchResult =
    let homePlayers = makeTestLineup homeGkSkill homeOutfieldSkill
    let awayPlayers = makeTestLineup awayGkSkill awayOutfieldSkill

    let results =
        Array.Parallel.init n (fun _ ->
            try
                let ctx, state = buildSimState homePlayers homePositions awayPlayers awayPositions
                state.Flow <- MatchFlow.Live
                let finalState, events = advanceTicks fullMatchTicks ctx state
                let metrics = MatchMetrics.extract events ctx.Home.Id ctx.Away.Id

                let totalShots =
                    metrics.ShotsHome
                    + metrics.ShotsAway
                    + metrics.ShotsOnTargetHome
                    + metrics.ShotsOnTargetAway

                let totalGoals = metrics.GoalsHome + metrics.GoalsAway

                let shotConversion =
                    if totalShots > 0 then
                        float totalGoals / float totalShots * 100.0
                    else
                        0.0

                let goalWithNoShot =
                    events
                    |> List.filter (fun e -> e.Type = MatchEventType.Goal)
                    |> List.filter (fun e ->
                        not (
                            events
                            |> List.exists (fun ev -> ev.Type = MatchEventType.ShotLaunched && ev.ClubId = e.ClubId)
                        ))
                    |> List.length

                let totalPasses = metrics.PassesHome + metrics.PassesAway
                let avgPassAcc = (metrics.PassAccuracyHome + metrics.PassAccuracyAway) / 2.0

                let maxSpeed = 0.0

                let ballInBounds =
                    let bx = finalState.Ball.Position.X
                    let by = finalState.Ball.Position.Y
                    bx >= 0.0<meter> && bx <= PitchLength && by >= 0.0<meter> && by <= PitchWidth

                let playersMoved =
                    let px = finalState.Home.Frame.Physics.PosX
                    let py = finalState.Home.Frame.Physics.PosY
                    let mutable moved = false

                    for i in 0 .. px.Length - 1 do
                        if px.[i] <> 0.0f || py.[i] <> 0.0f then
                            moved <- true

                    moved

                let fatigueAccumulated =
                    finalState.Home.Frame.Condition |> Array.exists (fun c -> c < 100.0f)

                Some
                    { MatchCount = 1
                      AvgGoalsPer90 = float totalGoals
                      AvgShotsPer90 = float totalShots
                      ShotConversionPct = shotConversion
                      GoalsWithNoShot = goalWithNoShot
                      PassAccuracyPct = avgPassAcc * 100.0
                      AvgPassesPer90 = float totalPasses
                      AvgCornersPer90 = float (metrics.CornersHome + metrics.CornersAway)
                      AvgFoulsPer90 = float (metrics.FoulsHome + metrics.FoulsAway)
                      AvgDribblesPer90 = float (metrics.DribblesHome + metrics.DribblesAway)
                      PossessionHomePct = metrics.PossessionHome * 100.0
                      HomeWins = if metrics.IsHomeWin then 1 else 0
                      AwayWins = if metrics.IsAwayWin then 1 else 0
                      Draws = if metrics.IsDraw then 1 else 0
                      MaxBallSpeed = maxSpeed
                      BallInBounds = ballInBounds
                      PlayersMoved = playersMoved
                      FatigueAccumulated = fatigueAccumulated
                      MatchReachedFulltime = finalState.Flow = MatchEnded
                      Exceptions = 0 }
            with ex ->
                Some
                    { MatchCount = 1
                      AvgGoalsPer90 = 0.0
                      AvgShotsPer90 = 0.0
                      ShotConversionPct = 0.0
                      GoalsWithNoShot = 0
                      PassAccuracyPct = 0.0
                      AvgPassesPer90 = 0.0
                      AvgCornersPer90 = 0.0
                      AvgFoulsPer90 = 0.0
                      AvgDribblesPer90 = 0.0
                      PossessionHomePct = 50.0
                      HomeWins = 0
                      AwayWins = 0
                      Draws = 0
                      MaxBallSpeed = 0.0
                      BallInBounds = false
                      PlayersMoved = false
                      FatigueAccumulated = false
                      MatchReachedFulltime = false
                      Exceptions = 1 })

    let valid = results |> Array.choose id
    let count = float valid.Length

    if valid.Length = 0 then
        { MatchCount = 0
          AvgGoalsPer90 = 0.0
          AvgShotsPer90 = 0.0
          ShotConversionPct = 0.0
          GoalsWithNoShot = 0
          PassAccuracyPct = 0.0
          AvgPassesPer90 = 0.0
          AvgCornersPer90 = 0.0
          AvgFoulsPer90 = 0.0
          AvgDribblesPer90 = 0.0
          PossessionHomePct = 50.0
          HomeWins = 0
          AwayWins = 0
          Draws = 0
          MaxBallSpeed = 0.0
          BallInBounds = false
          PlayersMoved = false
          FatigueAccumulated = false
          MatchReachedFulltime = false
          Exceptions = valid.Length }
    else
        { MatchCount = valid.Length
          AvgGoalsPer90 = valid |> Array.sumBy (fun r -> r.AvgGoalsPer90) |> (fun s -> s / count)
          AvgShotsPer90 = valid |> Array.sumBy (fun r -> r.AvgShotsPer90) |> (fun s -> s / count)
          ShotConversionPct = valid |> Array.sumBy (fun r -> r.ShotConversionPct) |> (fun s -> s / count)
          GoalsWithNoShot = valid |> Array.sumBy (fun r -> r.GoalsWithNoShot)
          PassAccuracyPct = valid |> Array.sumBy (fun r -> r.PassAccuracyPct) |> (fun s -> s / count)
          AvgPassesPer90 = valid |> Array.sumBy (fun r -> r.AvgPassesPer90) |> (fun s -> s / count)
          AvgCornersPer90 = valid |> Array.sumBy (fun r -> r.AvgCornersPer90) |> (fun s -> s / count)
          AvgFoulsPer90 = valid |> Array.sumBy (fun r -> r.AvgFoulsPer90) |> (fun s -> s / count)
          AvgDribblesPer90 = valid |> Array.sumBy (fun r -> r.AvgDribblesPer90) |> (fun s -> s / count)
          PossessionHomePct = valid |> Array.sumBy (fun r -> r.PossessionHomePct) |> (fun s -> s / count)
          HomeWins = valid |> Array.sumBy (fun r -> r.HomeWins)
          AwayWins = valid |> Array.sumBy (fun r -> r.AwayWins)
          Draws = valid |> Array.sumBy (fun r -> r.Draws)
          MaxBallSpeed = valid |> Array.maxBy (fun r -> r.MaxBallSpeed) |> (fun r -> r.MaxBallSpeed)
          BallInBounds = valid |> Array.forall (fun r -> r.BallInBounds)
          PlayersMoved = valid |> Array.exists (fun r -> r.PlayersMoved)
          FatigueAccumulated = valid |> Array.exists (fun r -> r.FatigueAccumulated)
          MatchReachedFulltime = valid |> Array.forall (fun r -> r.MatchReachedFulltime)
          Exceptions = valid |> Array.sumBy (fun r -> r.Exceptions) }

let runLosingPushMatch () : BatchResult =
    try
        let homePlayers = makeTestLineup 13 13
        let awayPlayers = makeTestLineup 13 13

        let ctx, state = buildSimState homePlayers homePositions awayPlayers awayPositions
        state.Flow <- MatchFlow.Live

        let min70 = 7 * 40 * 60
        let remaining = fullMatchTicks - min70

        let midState, firstHalfEvents = advanceTicks min70 ctx state
        midState.HomeScore <- 0
        midState.AwayScore <- 1
        midState.HalfTimeHandled <- true
        let finalState, secondHalfEvents = advanceTicks remaining ctx midState

        let countShots (events: MatchEvent list) (clubId: ClubId) =
            events
            |> List.filter (fun e ->
                e.ClubId = clubId
                && match e.Type with
                   | MatchEventType.ShotLaunched
                   | MatchEventType.ShotOnTarget
                   | MatchEventType.ShotOffTarget
                   | MatchEventType.ShotBlocked -> true
                   | _ -> false)
            |> List.length

        let shotsFirst = countShots firstHalfEvents ctx.Home.Id
        let shotsSecond = countShots secondHalfEvents ctx.Home.Id

        let metrics =
            MatchMetrics.extract (firstHalfEvents @ secondHalfEvents) ctx.Home.Id ctx.Away.Id

        { MatchCount = 1
          AvgGoalsPer90 = float (metrics.GoalsHome + metrics.GoalsAway)
          AvgShotsPer90 = float (metrics.ShotsHome + metrics.ShotsAway)
          ShotConversionPct = 0.0
          GoalsWithNoShot = 0
          PassAccuracyPct = (metrics.PassAccuracyHome + metrics.PassAccuracyAway) / 2.0 * 100.0
          AvgPassesPer90 = float (metrics.PassesHome + metrics.PassesAway)
          AvgCornersPer90 = float (metrics.CornersHome + metrics.CornersAway)
          AvgFoulsPer90 = float (metrics.FoulsHome + metrics.FoulsAway)
          AvgDribblesPer90 = float (metrics.DribblesHome + metrics.DribblesAway)
          PossessionHomePct = metrics.PossessionHome * 100.0
          HomeWins = if metrics.IsHomeWin then 1 else 0
          AwayWins = if metrics.IsAwayWin then 1 else 0
          Draws = if metrics.IsDraw then 1 else 0
          MaxBallSpeed = 0.0
          BallInBounds = true
          PlayersMoved = true
          FatigueAccumulated = finalState.Home.Frame.Condition |> Array.exists (fun c -> c < 100.0f)
          MatchReachedFulltime = finalState.Flow = MatchEnded
          Exceptions = 0 }
    with _ ->
        { MatchCount = 0
          AvgGoalsPer90 = 0.0
          AvgShotsPer90 = 0.0
          ShotConversionPct = 0.0
          GoalsWithNoShot = 0
          PassAccuracyPct = 0.0
          AvgPassesPer90 = 0.0
          AvgCornersPer90 = 0.0
          AvgFoulsPer90 = 0.0
          AvgDribblesPer90 = 0.0
          PossessionHomePct = 50.0
          HomeWins = 0
          AwayWins = 0
          Draws = 0
          MaxBallSpeed = 0.0
          BallInBounds = false
          PlayersMoved = false
          FatigueAccumulated = false
          MatchReachedFulltime = false
          Exceptions = 1 }

// ── Shared lazy batches (used by DiagnosticTests + StatisticalContractsTests) ──

let batchBalanced = lazy runBatch 3 14 14 14 14
let batchBalanced10 = lazy runBatch 10 14 14 14 14
let batchStrongVsWeak = lazy runBatch 3 5 5 20 20
let batchWeakGK = lazy runBatch 3 8 14 14 14
let batchEliteGK = lazy runBatch 3 18 14 14 14
