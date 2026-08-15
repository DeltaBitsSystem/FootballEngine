namespace FootballEngine.World.Phases

open FootballEngine.Domain
open FootballEngine.World

module FinancePhase =

    let private deductDailyWages (gs: GameState) : GameState =
        let updatedClubs =
            gs.Players
            |> Map.toSeq
            |> Seq.choose (fun (_, p) ->
                match p.Affiliation with
                | Contracted(clubId, c) -> Some(clubId, c.Salary / 7m)
                | _ -> None)
            |> Seq.groupBy fst
            |> Seq.fold
                (fun clubs (clubId, wages) ->
                    let total = wages |> Seq.sumBy snd
                    clubs |> Map.change clubId (Option.map (Club.adjustBudget -total)))
                gs.Clubs

        { gs with Clubs = updatedClubs }

    let make: WorldPhase =
        { Tag = Finance
          Frequency = Daily
          Run = fun _clock state -> deductDailyWages state }
