namespace FootballEngine

open FootballEngine.Core.Match

[<RequireQualifiedAccess>]
type ReplayMomentKind =
    | Goal
    | Save
    | Woodwork
    | Penalty
    | FreeKick
    | Corner
    | YellowCard
    | RedCard
    | Substitution

type ReplayMoment =
    { Tick: int<subtick>
      Kind: ReplayMomentKind
      Side: ClubSide option }

module ReplayMoments =
    /// A moment is watched from its build up, so a seek lands this many seconds before the event.
    let leadSeconds = 8

    let private momentOf (event: MatchEvent) : ReplayMoment option =
        let moment kind side =
            Some
                { Tick = event.Tick
                  Kind = kind
                  Side = side }

        match event.Data with
        | EventData.Goal(side, _, _, _) -> moment ReplayMomentKind.Goal (Some side)
        | EventData.Save _ -> moment ReplayMomentKind.Save None
        | EventData.Woodwork(side, _, _, _, _) -> moment ReplayMomentKind.Woodwork (Some side)
        | EventData.SetPieceAt(SetPieceKind.Penalty, side, _, _) -> moment ReplayMomentKind.Penalty (Some side)
        | EventData.SetPieceAt(SetPieceKind.FreeKick, side, _, _) -> moment ReplayMomentKind.FreeKick (Some side)
        | EventData.SetPieceAt(SetPieceKind.Corner, side, _, _) -> moment ReplayMomentKind.Corner (Some side)
        | EventData.Card(_, CardColor.Yellow) -> moment ReplayMomentKind.YellowCard None
        | EventData.Card(_, CardColor.Red) -> moment ReplayMomentKind.RedCard None
        | EventData.SubstitutionApplied _ -> moment ReplayMomentKind.Substitution None
        | _ -> None

    let ofEvents (events: MatchEvent list) : ReplayMoment list =
        events |> List.choose momentOf |> List.sortBy _.Tick
