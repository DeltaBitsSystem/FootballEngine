namespace FootballEngine.Core.Match

/// Bit i = EventKind i occurred this tick. Scheduler matches flags in O(1).
/// Bit i = EventKind i occurred this tick. Scheduler matches flags in O(1).
[<Struct>]
type EventFlags = { Bits: uint64 }

/// <= 64 cases. Tag order fixed at P1 — flags depend on it.
type EventKind =
    | PossessionGained
    | PossessionLost
    | BallLoose
    | PassLaunched
    | PassCompleted
    | ShotLaunched
    | ShotResult
    | Goal
    | Foul
    | Card
    | SetPieceAwarded
    | ThrowIn
    | Corner
    | GoalKick
    | Tackle
    | Save
    | CrossAttempt
    | Dribble
    | Interception
    | Injury
    | Substitution
    | VARDecision
    | KickOff
    | HalfTime
    | FullTime
    | MomentumShifted
    | ConditionCritical
    | RedCard

module EventFlags =

    // NOTE: every case is qualified (EventKind.X) — bare case names collide with
    // SetPieceKind/EventData cases in this namespace and break F# resolution.
    let ofKind (kind: EventKind) : EventFlags =
        let bit =
            match kind with
            | EventKind.PossessionGained -> 0
            | EventKind.PossessionLost -> 1
            | EventKind.BallLoose -> 2
            | EventKind.PassLaunched -> 3
            | EventKind.PassCompleted -> 4
            | EventKind.ShotLaunched -> 5
            | EventKind.ShotResult -> 6
            | EventKind.Goal -> 7
            | EventKind.Foul -> 8
            | EventKind.Card -> 9
            | EventKind.SetPieceAwarded -> 10
            | EventKind.ThrowIn -> 11
            | EventKind.Corner -> 12
            | EventKind.GoalKick -> 13
            | EventKind.Tackle -> 14
            | EventKind.Save -> 15
            | EventKind.CrossAttempt -> 16
            | EventKind.Dribble -> 17
            | EventKind.Interception -> 18
            | EventKind.Injury -> 19
            | EventKind.Substitution -> 20
            | EventKind.VARDecision -> 21
            | EventKind.KickOff -> 22
            | EventKind.HalfTime -> 23
            | EventKind.FullTime -> 24
            | EventKind.MomentumShifted -> 25
            | EventKind.ConditionCritical -> 26
            | EventKind.RedCard -> 27

        { Bits = 1UL <<< bit }

    let union (a: EventFlags) (b: EventFlags) : EventFlags = { Bits = a.Bits ||| b.Bits }

    let intersects (a: EventFlags) (b: EventFlags) : bool = (a.Bits &&& b.Bits) <> 0UL

    /// Union of every kind bit (all 28 events of P1).
    let all: EventFlags = { Bits = 0x0FFFFFFFUL }

[<Struct>]
type ShotResult =
    | OnTarget of saved: bool
    | OffTarget
    | Blocked
    | Post

type EventData =
    | Possession of side: ClubSide * player: PlayerId
    | Pass of passer: PlayerId * receiver: PlayerId option * distance: float<meter> * quality: float * completed: bool
    | Shot of shooter: PlayerId * result: ShotResult * xG: float * speed: float<meterPerSecond>
    | Goal of side: ClubSide * scorer: PlayerId option * assist: PlayerId option * xG: float
    | Foul of
        fouler: PlayerId *
        fouled: PlayerId *
        severity: FoulSeverity *
        card: CardDecision *
        position: (float<meter> * float<meter>)
    | Card of player: PlayerId * card: CardColor
    | SetPiece of kind: SetPieceKind * side: ClubSide
    | BallLoose of x: float<meter> * y: float<meter>
    | Interception of interceptor: PlayerId * passer: PlayerId
    | Tackle of winner: PlayerId * loser: PlayerId
    | Save of gk: PlayerId * shooter: PlayerId
    | CrossAttempt of crosser: PlayerId
    | Dribble of dribbler: PlayerId * succeeded: bool
    | Injury of player: PlayerId * severity: int
    | Substitution of playerOut: PlayerId * playerIn: PlayerId
    | VARDecision of decision: VARResult
    | MomentumShift of toSide: ClubSide
    | KickOff of side: ClubSide
    | HalfTime
    | FullTime
    | ConditionCritical of player: PlayerId * condition: int
    | RedCardIssued of player: PlayerId

type MatchEvent = { Tick: int<subtick>; Data: EventData }

module MatchEvent =

    /// Total function, compiler-checked. The only boilerplate in the system — it enables the flag table.
    let kind (ev: MatchEvent) : EventKind =
        match ev.Data with
        | EventData.Possession _ -> EventKind.PossessionGained
        | EventData.Pass _ -> EventKind.PassLaunched
        | EventData.Shot _ -> EventKind.ShotLaunched
        | EventData.Goal _ -> EventKind.Goal
        | EventData.Foul _ -> EventKind.Foul
        | EventData.Card _ -> EventKind.Card
        | EventData.SetPiece(SetPieceKind.ThrowIn, _) -> EventKind.ThrowIn
        | EventData.SetPiece(SetPieceKind.Corner, _) -> EventKind.Corner
        | EventData.SetPiece(SetPieceKind.GoalKick, _) -> EventKind.GoalKick
        | EventData.SetPiece _ -> EventKind.SetPieceAwarded
        | EventData.BallLoose _ -> EventKind.BallLoose
        | EventData.Interception _ -> EventKind.Interception
        | EventData.Tackle _ -> EventKind.Tackle
        | EventData.Save _ -> EventKind.Save
        | EventData.CrossAttempt _ -> EventKind.CrossAttempt
        | EventData.Dribble _ -> EventKind.Dribble
        | EventData.Injury _ -> EventKind.Injury
        | EventData.Substitution _ -> EventKind.Substitution
        | EventData.VARDecision _ -> EventKind.VARDecision
        | EventData.MomentumShift _ -> EventKind.MomentumShifted
        | EventData.KickOff _ -> EventKind.KickOff
        | EventData.HalfTime -> EventKind.HalfTime
        | EventData.FullTime -> EventKind.FullTime
        | EventData.ConditionCritical _ -> EventKind.ConditionCritical
        | EventData.RedCardIssued _ -> EventKind.RedCard

    /// For tests/diagnostics only — the viewer derives its own text from EventData.
    let describe (ev: MatchEvent) : string =
        match ev.Data with
        | EventData.Possession(side, _) -> sprintf "Possession: %A" side
        | EventData.Pass(_, _, distance, _, completed) -> sprintf "Pass %.1fm completed=%b" distance completed
        | EventData.Shot(_, result, _, speed) -> sprintf "Shot %A %.1fm/s" result speed
        | EventData.Goal(side, _, _, _) -> sprintf "GOAL: %A" side
        | EventData.Foul(_, _, severity, _, _) -> sprintf "Foul: %A" severity
        | EventData.Card(_, card) -> sprintf "Card: %A" card
        | EventData.SetPiece(kind, _) -> sprintf "SetPiece: %A" kind
        | EventData.BallLoose(_, _) -> "Ball loose"
        | EventData.Interception(_, _) -> "Interception"
        | EventData.Tackle(_, _) -> "Tackle"
        | EventData.Save(_, _) -> "Save"
        | EventData.CrossAttempt _ -> "Cross attempt"
        | EventData.Dribble(_, succeeded) -> sprintf "Dribble succeeded=%b" succeeded
        | EventData.Injury(_, severity) -> sprintf "Injury severity=%d" severity
        | EventData.Substitution(_, _) -> "Substitution"
        | EventData.VARDecision decision -> sprintf "VAR: %A" decision
        | EventData.MomentumShift toSide -> sprintf "Momentum: %A" toSide
        | EventData.KickOff side -> sprintf "KickOff: %A" side
        | EventData.HalfTime -> "Half time"
        | EventData.FullTime -> "Full time"
        | EventData.ConditionCritical(_, condition) -> sprintf "Condition critical %d" condition
        | EventData.RedCardIssued _ -> "Red card issued"
