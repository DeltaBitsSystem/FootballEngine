namespace Training

open FootballEngine.Core.Match

/// Extracts the aggregate match metrics the calibrator cares about.
module Metrics =

    let ofWorld (world: World) : MatchMetrics =
        let sumTeam f =
            [ ClubSide.Home; ClubSide.Away ]
            |> List.sumBy (fun s -> f (MatchWorld.team world s))

        let passesMade = sumTeam (fun t -> t.Memory |> Array.sumBy (fun m -> m.PassesMade))

        let passesCompleted =
            sumTeam (fun t -> t.Memory |> Array.sumBy (fun m -> m.PassesCompleted))

        let poss = world.State.Possession
        let totalTouches = poss.HomeTouches + poss.AwayTouches

        { Goals = float (world.State.HomeScore + world.State.AwayScore)
          Shots = float (sumTeam (fun t -> t.Stats.Shots))
          OnTarget = float (sumTeam (fun t -> t.Stats.OnTarget))
          PassPct =
            if passesMade > 0 then
                100.0 * float passesCompleted / float passesMade
            else
                0.0
          Fouls = float (sumTeam (fun t -> t.Stats.Fouls))
          Yellows = float (sumTeam (fun t -> t.Cards.Yellows))
          Corners = float (sumTeam (fun t -> t.Stats.Corners))
          Offsides = float (sumTeam (fun t -> t.Stats.Offsides))
          PossessionHome =
            if totalTouches > 0 then
                100.0 * float poss.HomeTouches / float totalTouches
            else
                50.0 }

    let toPairs (m: MatchMetrics) : (string * float) list =
        [ "goals", m.Goals
          "shots", m.Shots
          "onTarget", m.OnTarget
          "passPct", m.PassPct
          "fouls", m.Fouls
          "yellows", m.Yellows
          "corners", m.Corners
          "offsides", m.Offsides
          "possession", m.PossessionHome ]

    /// Average metrics across matches.
    let aggregate (list: MatchMetrics list) : MatchMetrics =
        if list.IsEmpty then
            failwith "aggregate: empty metrics list"
        else
            let n = float list.Length

            let sum f =
                list |> List.sumBy f |> (fun v -> v / n)

            { Goals = sum (fun m -> m.Goals)
              Shots = sum (fun m -> m.Shots)
              OnTarget = sum (fun m -> m.OnTarget)
              PassPct = sum (fun m -> m.PassPct)
              Fouls = sum (fun m -> m.Fouls)
              Yellows = sum (fun m -> m.Yellows)
              Corners = sum (fun m -> m.Corners)
              Offsides = sum (fun m -> m.Offsides)
              PossessionHome = sum (fun m -> m.PossessionHome) }
