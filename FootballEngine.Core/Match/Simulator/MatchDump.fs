namespace FootballEngine.Core.Match

open System.Text.Json

/// Machine-readable record of a match (events + stats) for the calibration
/// pipeline (R11): training consumes the same deterministic data the engine ran.
module MatchDump =

    type private Metadata =
        { HomeClub: string
          AwayClub: string
          HomeFormation: string
          AwayFormation: string
          HomeChemistry: float
          AwayChemistry: float
          IsKnockout: bool }

    type private FinalScore = { Home: int; Away: int }

    type private EventSummary =
        { Tick: int
          Kind: string
          Data: string }

    type private Dump =
        { Metadata: Metadata
          Context: MatchContext
          FinalScore: FinalScore
          HomeStats: TeamStats
          AwayStats: TeamStats
          Events: EventSummary list }

    let private finalScore (events: MatchEvent list) : FinalScore =
        let home, away, _ =
            List.fold
                (fun (home: int, away: int, lastScoring: ClubSide option) (ev: MatchEvent) ->
                    match ev.Data with
                    | EventData.Goal(side, _, _, _) ->
                        let home' = if side = ClubSide.Home then home + 1 else home
                        let away' = if side = ClubSide.Away then away + 1 else away
                        home', away', Some side
                    | EventData.VARDecision VARResult.GoalOverturned ->
                        match lastScoring with
                        | Some ClubSide.Home -> max 0 (home - 1), away, None
                        | Some ClubSide.Away -> home, max 0 (away - 1), None
                        | None -> home, away, None
                    | _ -> home, away, lastScoring)
                (0, 0, None)
                events

        { Home = home; Away = away }

    let export (ctx: MatchContext) (events: MatchEvent list) (homeStats: TeamStats) (awayStats: TeamStats) : string =
        let metadata =
            { HomeClub = ctx.HomeClub.Name
              AwayClub = ctx.AwayClub.Name
              HomeFormation = ctx.HomeClub.BaseTactics.Formation
              AwayFormation = ctx.AwayClub.BaseTactics.Formation
              HomeChemistry = ctx.HomeClub.Chemistry
              AwayChemistry = ctx.AwayClub.Chemistry
              IsKnockout = ctx.IsKnockout }

        let summaries =
            events
            |> List.map (fun ev ->
                { Tick = int ev.Tick
                  Kind = sprintf "%A" (MatchEvent.kind ev)
                  Data = MatchEvent.describe ev })

        let model: Dump =
            { Metadata = metadata
              Context = ctx
              FinalScore = finalScore events
              HomeStats = homeStats
              AwayStats = awayStats
              Events = summaries }

        JsonSerializer.Serialize(
            model,
            JsonSerializerOptions(WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        )
