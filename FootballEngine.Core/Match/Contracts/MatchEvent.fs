namespace FootballEngine.Core.Match

/// Bit i = EventKind i occurred this tick. Scheduler matches flags in O(1).
[<Struct>]
type EventFlags = { Bits: uint64 }

[<Struct>]
type EventId = EventId of uint64

module EventId =

    let value (EventId id) = id

/// The flag table is a uint64, so this DU is capped at 64 cases and its tag
/// order is frozen (P1): each case's index IS its flag bit — inserting or
/// reordering a case silently corrupts flag matching.
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
    | ShootoutGoal
    | Offside
    | Advantage
    | SubstitutionRequested
    | SubstitutionAccepted
    | SubstitutionRejected
    | SubstitutionApplied
    | ShootoutStarted
    | ShootoutKick
    | ShootoutFinished

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
            | EventKind.ShootoutGoal -> 28
            | EventKind.Offside -> 29
            | EventKind.Advantage -> 30
            | EventKind.SubstitutionRequested -> 31
            | EventKind.SubstitutionAccepted -> 32
            | EventKind.SubstitutionRejected -> 33
            | EventKind.SubstitutionApplied -> 34
            | EventKind.ShootoutStarted -> 35
            | EventKind.ShootoutKick -> 36
            | EventKind.ShootoutFinished -> 37

        { Bits = 1UL <<< bit }

    let union (a: EventFlags) (b: EventFlags) : EventFlags = { Bits = a.Bits ||| b.Bits }

    let intersects (a: EventFlags) (b: EventFlags) : bool = (a.Bits &&& b.Bits) <> 0UL

    /// Union of every kind bit.
    let all: EventFlags = { Bits = (1UL <<< 38) - 1UL }

[<Struct>]
type ShotResult =
    | OnTarget of saved: bool
    | OffTarget
    | Blocked
    | Post

type EventData =
    | Possession of side: ClubSide * player: PlayerId
    | Pass of
        passer: PlayerId *
        receiver: PlayerId option *
        distance: float<meter> *
        quality: float *
        completed: bool *
        fromX: float<meter> *
        fromY: float<meter> *
        toX: float<meter> *
        toY: float<meter>
    | Shot of
        shooter: PlayerId *
        result: ShotResult *
        xG: float *
        speed: float<meterPerSecond> *
        x: float<meter> *
        y: float<meter>
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
    | CrossAttempt of crosser: PlayerId * x: float<meter> * y: float<meter>
    | Dribble of dribbler: PlayerId * succeeded: bool * x: float<meter> * y: float<meter>
    | Injury of player: PlayerId * severity: int
    | Substitution of playerOut: PlayerId * playerIn: PlayerId
    | VARDecision of decision: VARResult
    | MomentumShift of toSide: ClubSide
    | KickOff of side: ClubSide
    | HalfTime
    | FullTime
    | ConditionCritical of player: PlayerId * condition: int
    | RedCardIssued of player: PlayerId
    | ShootoutGoal of side: ClubSide
    | Offside of player: PlayerId
    | AdvantageStarted of
        side: ClubSide *
        fouler: PlayerId *
        fouled: PlayerId *
        x: float<meter> *
        y: float<meter> *
        expiresAt: int<subtick>
    | AdvantageRealized of side: ClubSide
    | AdvantageRecalled of side: ClubSide
    | AdvantageExpired of side: ClubSide
    | SubstitutionRequested of playerOut: PlayerId * playerIn: PlayerId
    | SubstitutionAccepted of playerOut: PlayerId * playerIn: PlayerId
    | SubstitutionRejected of playerOut: PlayerId * playerIn: PlayerId
    | SubstitutionApplied of playerOut: PlayerId * playerIn: PlayerId
    | ShootoutStarted
    | ShootoutKick of side: ClubSide * taker: PlayerId * scored: bool
    | ShootoutFinished

type MatchEvent = { Tick: int<subtick>; Data: EventData }

module MatchEvent =

    /// The one sanctioned boilerplate: keeps the EventKind -> bit mapping total
    /// and compiler-checked.
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
        | EventData.SetPiece(SetPieceKind.KickOff, _) -> EventKind.KickOff
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
        | EventData.ShootoutGoal _ -> EventKind.ShootoutGoal
        | EventData.Offside _ -> EventKind.Offside
        | EventData.AdvantageStarted _
        | EventData.AdvantageRealized _
        | EventData.AdvantageRecalled _
        | EventData.AdvantageExpired _ -> EventKind.Advantage
        | EventData.SubstitutionRequested _ -> EventKind.SubstitutionRequested
        | EventData.SubstitutionAccepted _ -> EventKind.SubstitutionAccepted
        | EventData.SubstitutionRejected _ -> EventKind.SubstitutionRejected
        | EventData.SubstitutionApplied _ -> EventKind.SubstitutionApplied
        | EventData.ShootoutStarted -> EventKind.ShootoutStarted
        | EventData.ShootoutKick _ -> EventKind.ShootoutKick
        | EventData.ShootoutFinished -> EventKind.ShootoutFinished

    /// EventData has no mutable identity. This deterministic key gives the
    /// structural reducer a stable idempotence boundary until the causal ledger
    /// introduces explicit event ids.
    let id (ev: MatchEvent) : EventId =
        let text = sprintf "%d|%A" (int ev.Tick) ev.Data
        let mutable hash = 1469598103934665603UL

        for b in System.Text.Encoding.UTF8.GetBytes text do
            hash <- (hash ^^^ uint64 b) * 1099511628211UL

        EventId hash

    /// For tests/diagnostics only — the viewer derives its own text from EventData.
    let describe (ev: MatchEvent) : string =
        match ev.Data with
        | EventData.Possession(side, _) -> sprintf "Possession: %A" side
        | EventData.Pass(_, _, distance, _, completed, _, _, _, _) ->
            sprintf "Pass %.1fm completed=%b" distance completed
        | EventData.Shot(_, result, _, speed, _, _) -> sprintf "Shot %A %.1fm/s" result speed
        | EventData.Goal(side, _, _, _) -> sprintf "GOAL: %A" side
        | EventData.Foul(_, _, severity, _, _) -> sprintf "Foul: %A" severity
        | EventData.Card(_, card) -> sprintf "Card: %A" card
        | EventData.SetPiece(kind, _) -> sprintf "SetPiece: %A" kind
        | EventData.BallLoose(_, _) -> "Ball loose"
        | EventData.Interception(_, _) -> "Interception"
        | EventData.Tackle(_, _) -> "Tackle"
        | EventData.Save(_, _) -> "Save"
        | EventData.CrossAttempt _ -> "Cross attempt"
        | EventData.Dribble(_, succeeded, _, _) -> sprintf "Dribble succeeded=%b" succeeded
        | EventData.Injury(_, severity) -> sprintf "Injury severity=%d" severity
        | EventData.Substitution(_, _) -> "Substitution"
        | EventData.VARDecision decision -> sprintf "VAR: %A" decision
        | EventData.MomentumShift toSide -> sprintf "Momentum: %A" toSide
        | EventData.KickOff side -> sprintf "KickOff: %A" side
        | EventData.HalfTime -> "Half time"
        | EventData.FullTime -> "Full time"
        | EventData.ConditionCritical(_, condition) -> sprintf "Condition critical %d" condition
        | EventData.RedCardIssued _ -> "Red card issued"
        | EventData.ShootoutGoal side -> sprintf "Shootout goal: %A" side
        | EventData.Offside _ -> "Offside"
        | EventData.AdvantageStarted(side, _, _, _, _, _) -> sprintf "Advantage: %A" side
        | EventData.AdvantageRealized side -> sprintf "Advantage realized: %A" side
        | EventData.AdvantageRecalled side -> sprintf "Advantage recalled: %A" side
        | EventData.AdvantageExpired side -> sprintf "Advantage expired: %A" side
        | EventData.SubstitutionRequested _ -> "Substitution requested"
        | EventData.SubstitutionAccepted _ -> "Substitution accepted"
        | EventData.SubstitutionRejected _ -> "Substitution rejected"
        | EventData.SubstitutionApplied _ -> "Substitution applied"
        | EventData.ShootoutStarted -> "Shootout started"
        | EventData.ShootoutKick(side, taker, scored) -> sprintf "Shootout kick: %A %A scored=%b" side taker scored
        | EventData.ShootoutFinished -> "Shootout finished"
