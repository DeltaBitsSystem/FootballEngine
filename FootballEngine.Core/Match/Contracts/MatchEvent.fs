namespace FootballEngine.Core.Match

/// Bit i = EventKind i occurred this tick. Scheduler matches flags in O(1).
[<Struct>]
type EventFlags = { Bits: uint64 }

[<Struct>]
type EventId = EventId of uint64

type AttackPattern = ThirdMan | AttractSwitch | ExploitDepth | SecureProgression
type AttackStage = Preparing | Connected | Exploiting
type AttackEnd = Completed | LostBall | TimedOut | Countered | ParticipantLeft | InstructionsChanged | Stopped | Deviated
type AttackChange = Started of AttackPattern | Advanced of AttackStage * EventId | Ended of AttackEnd * float * learningKey: int option

module EventId =

    let value (EventId id) = id

type TransitionMode =
    | Assessing
    | Counterpress
    | Retreat

type TransitionReason =
    | AwaitingAssessment
    | LocalAdvantage
    | ArrivalTooLate
    | Outnumbered
    | MissingCover
    | DepthThreat
    | TacticalRestraint
    | PressureBroken
    | PressWindowElapsed

type TransitionEnd =
    | Recovered
    | PressureBeaten
    | Retreated
    | Interrupted
    | Stabilized

type TransitionChange =
    | Lost
    | ModeSelected of TransitionMode * TransitionReason
    | Handoff of first: PlayerId option * cover: PlayerId option
    | Finished of TransitionEnd

[<Struct>]
type AdvantageIncident =
    { Source: EventId
      Tick: int<subtick>
      Side: ClubSide
      Fouler: PlayerId
      Fouled: PlayerId
      X: float<meter>
      Y: float<meter> }

/// Scheduler bits are assigned explicitly in EventFlags, independently of DU declaration order.
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
    | PlayerBodyFlight
    | FootContact
    | FootStrikeCancelled
    | DefensiveTransition
    | TeamInstruction
    | CollectiveAttack
    | PhysicalContact
    | Woodwork
    | KeeperDive

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
            | EventKind.PlayerBodyFlight -> 38
            | EventKind.FootContact -> 39
            | EventKind.FootStrikeCancelled -> 40
            | EventKind.DefensiveTransition -> 41
            | EventKind.TeamInstruction -> 42
            | EventKind.CollectiveAttack -> 43
            | EventKind.PhysicalContact -> 44
            | EventKind.Woodwork -> 45
            | EventKind.KeeperDive -> 46

        { Bits = 1UL <<< bit }

    let union (a: EventFlags) (b: EventFlags) : EventFlags = { Bits = a.Bits ||| b.Bits }

    let intersects (a: EventFlags) (b: EventFlags) : bool = (a.Bits &&& b.Bits) <> 0UL

    // Build once from the actual DU so additions cannot leave an incomplete aggregate mask.
    let all: EventFlags =
        Microsoft.FSharp.Reflection.FSharpType.GetUnionCases typeof<EventKind>
        |> Array.map (fun case -> Microsoft.FSharp.Reflection.FSharpValue.MakeUnion(case, [||]) :?> EventKind)
        |> Array.fold (fun flags kind -> union flags (ofKind kind)) { Bits = 0UL }

[<Struct>]
type ShotResult =
    | OnTarget of saved: bool
    | OffTarget
    | Blocked

type GoalFramePart =
    | Post
    | Crossbar
    | Net

type EventData =
    | CollectiveAttack of side: ClubSide * episode: uint64 * change: AttackChange
    | Possession of side: ClubSide * player: PlayerId
    | DefensiveTransition of side: ClubSide * episode: EventId * change: TransitionChange
    | TeamInstruction of ClubSide
    | FootContact of
        player: PlayerId *
        foot: ContactFoot *
        kind: FootTouchKind *
        x: float<meter> *
        y: float<meter> *
        z: float<meter>
    | PhysicalContact of ContactRecord
    | FootStrikeCancelled of player: PlayerId
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
    | SetPieceAt of kind: SetPieceKind * side: ClubSide * x: float<meter> * y: float<meter>
    | RestartAwarded of RestartAward
    | RestartPlanned of plan: RestartPlan
    | RestartAuthorized of id: RestartId
    | RestartCommandIssued of id: RestartId * actor: PlayerId
    | RestartContact of id: RestartId * actor: PlayerId * kind: SetPieceKind
    | RestartCompleted of id: RestartId
    | RestartPlanChanged of id: RestartId * reason: RestartPlanChangeReason
    | BallLoose of x: float<meter> * y: float<meter>
    | Interception of interceptor: PlayerId * passer: PlayerId
    | Tackle of winner: PlayerId * loser: PlayerId
    | Save of gk: PlayerId * shooter: PlayerId
    | CrossAttempt of crosser: PlayerId * x: float<meter> * y: float<meter>
    /// The ball struck a post or the crossbar of the goal `goal` defends.
    | Woodwork of goal: ClubSide * part: GoalFramePart * x: float<meter> * y: float<meter> * z: float<meter>
    /// A keeper commits to a save: where his gloves will meet the ball and when, so the viewer can stage the dive.
    | KeeperDive of keeper: PlayerId * x: float<meter> * y: float<meter> * z: float<meter> * contactTick: int<subtick>
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
    | AdvantageStarted of incident: AdvantageIncident * expiresAt: int<subtick>
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

    | PlayerBodyFlight of player: PlayerId * sequence: uint64 * airborne: bool

type MatchEvent = { Tick: int<subtick>; Data: EventData }

module MatchEvent =

    let private fnv (h: uint64) (v: uint64) : uint64 = (h ^^^ v) * 1099511628211UL

    let private hashBool h value = fnv h (if value then 1UL else 0UL)

    let private hashFloat (h: uint64) (value: float<'u>) =
        fnv h (uint64 (System.BitConverter.DoubleToInt64Bits(float value)))

    let private hashPlayer h (PlayerId id) = fnv h (uint64 (uint32 id))

    let private hashPlayerOption h value =
        match value with
        | None -> fnv h 0UL
        | Some player -> hashPlayer (fnv h 1UL) player

    let private hashSide h side =
        match side with
        | ClubSide.Home -> fnv h 0UL
        | ClubSide.Away -> fnv h 1UL

    let private hashSetPiece h kind =
        match kind with
        | SetPieceKind.KickOff -> fnv h 0UL
        | SetPieceKind.ThrowIn -> fnv h 1UL
        | SetPieceKind.Corner -> fnv h 2UL
        | SetPieceKind.GoalKick -> fnv h 3UL
        | SetPieceKind.FreeKick -> fnv h 4UL
        | SetPieceKind.Penalty -> fnv h 5UL

    let private hashShotResult h result =
        match result with
        | ShotResult.OnTarget saved -> hashBool (fnv h 0UL) saved
        | ShotResult.OffTarget -> fnv h 1UL
        | ShotResult.Blocked -> fnv h 2UL

    let private hashFoulSeverity h severity =
        match severity with
        | FoulSeverity.Minor -> fnv h 0UL
        | FoulSeverity.Tactical -> fnv h 1UL
        | FoulSeverity.YellowWorthy -> fnv h 2UL
        | FoulSeverity.RedWorthy -> fnv h 3UL

    let private hashCardDecision h card =
        match card with
        | CardDecision.NoCard -> fnv h 0UL
        | CardDecision.Yellow -> fnv h 1UL
        | CardDecision.Red -> fnv h 2UL

    let private hashCardColor h card =
        match card with
        | CardColor.Yellow -> fnv h 0UL
        | CardColor.Red -> fnv h 1UL

    let private hashVarResult h result =
        match result with
        | VARResult.GoalStands -> fnv h 0UL
        | VARResult.GoalOverturned -> fnv h 1UL

    let private hashContactSurface (h: uint64) (surface: ContactSurface) =
        let tag =
            match surface with
            | ContactSurface.ContactTorso -> 0UL
            | ContactSurface.ContactHead -> 1UL
            | ContactSurface.ContactLeg -> 2UL
            | ContactSurface.ContactFoot -> 3UL
            | ContactSurface.ContactHand -> 4UL
            | ContactSurface.ContactBall -> 5UL

        fnv h tag

    let private hashBodySupport (h: uint64) (support: BodySupport) =
        let tag =
            match support with
            | BodySupport.NoBodySupport -> 0UL
            | BodySupport.LeftFootSupport -> 1UL
            | BodySupport.RightFootSupport -> 2UL
            | BodySupport.DoubleFootSupport -> 3UL

        fnv h tag

    let private hashContactControl (h: uint64) (control: ContactControlSnapshot) =
        let tag =
            match control.Kind with
            | ContactControlKind.UncontrolledBall -> 0UL
            | ContactControlKind.ControlledBall -> 1UL
            | ContactControlKind.ReceivingBall -> 2UL
            | ContactControlKind.ContestedBall -> 3UL
            | ContactControlKind.HeldBall -> 4UL

        let h = fnv h tag
        let h =
            match control.Side with
            | None -> fnv h 0UL
            | Some side -> hashSide (fnv h 1UL) side

        hashPlayerOption h control.Player

    let private hashContact (h: uint64) (contact: ContactRecord) =
        let h = fnv h (ContactId.value contact.Id)
        let h = hashPlayer h contact.Actor
        let h = fnv h (uint64 (uint32 contact.ActorGeneration))
        let h = hashPlayerOption h contact.Other
        let h =
            match contact.OtherGeneration with
            | None -> fnv h 0UL
            | Some generation -> fnv (fnv h 1UL) (uint64 (uint32 generation))

        let h =
            match contact.Action with
            | None -> fnv h 0UL
            | Some action -> fnv (fnv h 1UL) (ActionId.value action)

        let h =
            match contact.ActionKind with
            | None -> fnv h 0UL
            | Some kind ->
                let tag =
                    match kind with
                    | ContactActionKind.ContactContain -> 0UL
                    | ContactActionKind.ContactShield -> 1UL
                    | ContactActionKind.ContactShoulderChallenge -> 2UL
                    | ContactActionKind.ContactStandingTackle -> 3UL
                    | ContactActionKind.ContactSlidingTackle -> 4UL
                    | ContactActionKind.ContactTrap -> 5UL
                    | ContactActionKind.ContactStrike -> 6UL
                    | ContactActionKind.ContactHeader -> 7UL

                fnv (fnv h 1UL) tag

        let h = fnv h (uint64 (uint32 (int contact.Tick)))
        let h = hashFloat h contact.Fraction
        let h = hashFloat (hashFloat (hashFloat h contact.PointX) contact.PointY) contact.PointZ
        let h = hashFloat (hashFloat (hashFloat h contact.NormalX) contact.NormalY) contact.NormalZ
        let h = hashContactSurface (hashContactSurface h contact.SurfaceA) contact.SurfaceB
        let h =
            hashFloat
                (hashFloat
                    (hashFloat h contact.RelativeVelocityX)
                    contact.RelativeVelocityY)
                contact.RelativeVelocityZ

        let h = hashFloat (hashFloat h contact.NormalImpulse) contact.TangentialImpulse
        let h = hashBodySupport (hashBodySupport h contact.SupportBefore) contact.SupportAfter
        let h = hashBool h contact.BallTouched
        let h =
            match contact.BallTouchOrder with
            | None -> fnv h 0UL
            | Some order -> fnv (fnv h 1UL) (uint64 (uint32 order))

        hashContactControl (hashContactControl h contact.ControlBefore) contact.ControlAfter

    let private hashEventData h data =
        match data with
        | EventData.CollectiveAttack(side, episode, change) ->
            let h = fnv (hashSide (fnv h 48UL) side) episode
            match change with
            | Started pattern ->
                let tag = match pattern with ThirdMan -> 0UL | AttractSwitch -> 1UL | ExploitDepth -> 2UL | SecureProgression -> 3UL
                fnv (fnv h 0UL) tag
            | Advanced(stage, cause) ->
                let tag = match stage with Preparing -> 0UL | Connected -> 1UL | Exploiting -> 2UL
                fnv (fnv (fnv h 1UL) tag) (EventId.value cause)
            | Ended(reason, reward, key) ->
                let tag = match reason with Completed -> 0UL | LostBall -> 1UL | TimedOut -> 2UL | Countered -> 3UL | ParticipantLeft -> 4UL | InstructionsChanged -> 5UL | Stopped -> 6UL | Deviated -> 7UL
                let h = hashFloat (fnv (fnv h 2UL) tag) reward
                match key with None -> fnv h 0UL | Some key -> fnv (fnv h 1UL) (uint64 key)
        | EventData.TeamInstruction side -> hashSide (fnv h 47UL) side
        | EventData.DefensiveTransition(side, episode, change) ->
            let h = fnv (hashSide (fnv h 46UL) side) (EventId.value episode)
            match change with
            | Lost -> fnv h 0UL
            | ModeSelected(mode, reason) ->
                let modeTag = match mode with Assessing -> 0UL | Counterpress -> 1UL | Retreat -> 2UL
                let reasonTag =
                    match reason with
                    | AwaitingAssessment -> 0UL | LocalAdvantage -> 1UL | ArrivalTooLate -> 2UL
                    | Outnumbered -> 3UL | MissingCover -> 4UL | DepthThreat -> 5UL
                    | TacticalRestraint -> 6UL | PressureBroken -> 7UL | PressWindowElapsed -> 8UL
                fnv (fnv (fnv h 1UL) modeTag) reasonTag
            | Handoff(first, cover) -> hashPlayerOption (hashPlayerOption (fnv h 2UL) first) cover
            | Finished outcome ->
                let tag = match outcome with Recovered -> 0UL | PressureBeaten -> 1UL | Retreated -> 2UL | Interrupted -> 3UL | Stabilized -> 4UL
                fnv (fnv h 3UL) tag
        | EventData.Possession(side, player) -> hashPlayer (hashSide (fnv h 0UL) side) player
        | EventData.Pass(passer, receiver, distance, quality, completed, fromX, fromY, toX, toY) ->
            let h = hashPlayer (fnv h 1UL) passer
            let h = hashPlayerOption h receiver
            let h = hashFloat h distance
            let h = hashFloat h quality
            let h = hashBool h completed
            let h = hashFloat h fromX
            let h = hashFloat h fromY
            let h = hashFloat h toX
            hashFloat h toY
        | EventData.Shot(shooter, result, xG, speed, x, y) ->
            let h = hashPlayer (fnv h 2UL) shooter
            let h = hashShotResult h result
            let h = hashFloat h xG
            let h = hashFloat h speed
            let h = hashFloat h x
            hashFloat h y
        | EventData.Goal(side, scorer, assist, xG) ->
            let h = hashSide (fnv h 3UL) side
            let h = hashPlayerOption h scorer
            let h = hashPlayerOption h assist
            hashFloat h xG
        | EventData.Foul(fouler, fouled, severity, card, (x, y)) ->
            let h = hashPlayer (fnv h 4UL) fouler
            let h = hashPlayer h fouled
            let h = hashFoulSeverity h severity
            let h = hashCardDecision h card
            let h = hashFloat h x
            hashFloat h y
        | EventData.Card(player, card) -> hashCardColor (hashPlayer (fnv h 5UL) player) card
        | EventData.SetPiece(kind, side) -> hashSide (hashSetPiece (fnv h 6UL) kind) side
        | EventData.SetPieceAt(kind, side, x, y) ->
            hashFloat (hashFloat (hashSide (hashSetPiece (fnv h 35UL) kind) side) x) y
        | EventData.RestartAwarded award ->
            let h = fnv h 36UL
            let h = fnv h (RestartId.value award.Id)
            let h = hashSetPiece h award.Kind
            let h = hashSide h award.Side
            let h = hashFloat h award.X
            hashFloat h award.Y
        | EventData.RestartPlanned plan -> hashPlayer (fnv (fnv h 37UL) (RestartId.value plan.Id)) plan.Executor
        | EventData.RestartAuthorized id -> fnv (fnv h 38UL) (RestartId.value id)
        | EventData.RestartCommandIssued(id, actor) -> hashPlayer (fnv (fnv h 39UL) (RestartId.value id)) actor
        | EventData.RestartContact(id, actor, kind) ->
            hashPlayer (hashSetPiece (fnv (fnv h 40UL) (RestartId.value id)) kind) actor
        | EventData.RestartCompleted id -> fnv (fnv h 41UL) (RestartId.value id)
        | EventData.RestartPlanChanged(id, reason) ->
            let reasonTag =
                match reason with
                | RestartPlanChangeReason.ExecutorInvalidated -> 0UL
                | RestartPlanChangeReason.ParticipantInvalidated _ -> 1UL
                | RestartPlanChangeReason.PlanningFailed -> 2UL

            fnv (fnv (fnv h 42UL) (RestartId.value id)) reasonTag
        | EventData.BallLoose(x, y) -> hashFloat (hashFloat (fnv h 7UL) x) y
        | EventData.FootStrikeCancelled player -> hashPlayer (fnv h 45UL) player
        | EventData.FootContact(player, foot, kind, x, y, z) ->
            let tag =
                match kind with
                | FootTouchKind.Carry -> 0UL
                | FootTouchKind.Receive -> 1UL
                | FootTouchKind.Kick StrikeKind.Pass -> 2UL
                | FootTouchKind.Kick StrikeKind.Shot -> 3UL
                | FootTouchKind.Kick StrikeKind.Cross -> 4UL
                | FootTouchKind.Kick StrikeKind.Clearance -> 5UL
                | FootTouchKind.Kick(StrikeKind.Restart kind) ->
                    match kind with
                    | SetPieceKind.KickOff -> 6UL
                    | SetPieceKind.ThrowIn -> 7UL
                    | SetPieceKind.Corner -> 8UL
                    | SetPieceKind.GoalKick -> 9UL
                    | SetPieceKind.FreeKick -> 10UL
                    | SetPieceKind.Penalty -> 11UL

            let h =
                fnv (fnv (hashPlayer (fnv h 44UL) player) tag) (if foot = ContactFoot.Left then 0UL else 1UL)

            hashFloat (hashFloat (hashFloat h x) y) z
        | EventData.PhysicalContact contact -> hashContact (fnv h 49UL) contact
        | EventData.Interception(interceptor, passer) -> hashPlayer (hashPlayer (fnv h 8UL) interceptor) passer
        | EventData.Tackle(winner, loser) -> hashPlayer (hashPlayer (fnv h 9UL) winner) loser
        | EventData.Save(gk, shooter) -> hashPlayer (hashPlayer (fnv h 10UL) gk) shooter
        | EventData.CrossAttempt(crosser, x, y) -> hashFloat (hashFloat (hashPlayer (fnv h 11UL) crosser) x) y
        | EventData.Woodwork(goal, part, x, y, z) ->
            let partCode =
                match part with
                | GoalFramePart.Post -> 0UL
                | GoalFramePart.Crossbar -> 1UL
                | GoalFramePart.Net -> 2UL

            hashFloat (hashFloat (hashFloat (fnv (hashSide (fnv h 50UL) goal) partCode) x) y) z
        | EventData.KeeperDive(keeper, x, y, z, contactTick) ->
            let h = hashFloat (hashFloat (hashFloat (hashPlayer (fnv h 51UL) keeper) x) y) z
            fnv h (uint64 (uint32 (int contactTick)))
        | EventData.Dribble(dribbler, succeeded, x, y) ->
            let h = hashPlayer (fnv h 12UL) dribbler
            let h = hashBool h succeeded
            let h = hashFloat h x
            hashFloat h y
        | EventData.Injury(player, severity) -> fnv (hashPlayer (fnv h 13UL) player) (uint64 (uint32 severity))
        | EventData.Substitution(playerOut, playerIn) -> hashPlayer (hashPlayer (fnv h 14UL) playerOut) playerIn
        | EventData.VARDecision decision -> hashVarResult (fnv h 15UL) decision
        | EventData.MomentumShift toSide -> hashSide (fnv h 16UL) toSide
        | EventData.KickOff side -> hashSide (fnv h 17UL) side
        | EventData.HalfTime -> fnv h 18UL
        | EventData.FullTime -> fnv h 19UL
        | EventData.ConditionCritical(player, condition) ->
            fnv (hashPlayer (fnv h 20UL) player) (uint64 (uint32 condition))
        | EventData.RedCardIssued player -> hashPlayer (fnv h 21UL) player
        | EventData.ShootoutGoal side -> hashSide (fnv h 22UL) side
        | EventData.Offside player -> hashPlayer (fnv h 23UL) player
        | EventData.AdvantageStarted(incident, expiresAt) ->
            let h = fnv (fnv h 24UL) (EventId.value incident.Source)
            let h = fnv h (uint64 (uint32 (int incident.Tick)))
            let h = hashSide h incident.Side
            let h = hashPlayer h incident.Fouler
            let h = hashPlayer h incident.Fouled
            let h = hashFloat h incident.X
            let h = hashFloat h incident.Y
            fnv h (uint64 (uint32 (int expiresAt)))
        | EventData.AdvantageRealized side -> hashSide (fnv h 25UL) side
        | EventData.AdvantageRecalled side -> hashSide (fnv h 26UL) side
        | EventData.AdvantageExpired side -> hashSide (fnv h 27UL) side
        | EventData.SubstitutionRequested(playerOut, playerIn) ->
            hashPlayer (hashPlayer (fnv h 28UL) playerOut) playerIn
        | EventData.SubstitutionAccepted(playerOut, playerIn) -> hashPlayer (hashPlayer (fnv h 29UL) playerOut) playerIn
        | EventData.SubstitutionRejected(playerOut, playerIn) -> hashPlayer (hashPlayer (fnv h 30UL) playerOut) playerIn
        | EventData.SubstitutionApplied(playerOut, playerIn) -> hashPlayer (hashPlayer (fnv h 31UL) playerOut) playerIn
        | EventData.ShootoutStarted -> fnv h 32UL
        | EventData.ShootoutKick(side, taker, scored) ->
            let h = hashSide (fnv h 33UL) side
            let h = hashPlayer h taker
            hashBool h scored
        | EventData.ShootoutFinished -> fnv h 34UL
        | EventData.PlayerBodyFlight(player, sequence, airborne) ->
            hashBool (fnv (hashPlayer (fnv h 43UL) player) sequence) airborne

    let private structuralHash (ev: MatchEvent) : uint64 =
        // Hash fields directly so event identity never pays for formatted strings or reflection.
        hashEventData (fnv 1469598103934665603UL (uint64 (uint32 (int ev.Tick)))) ev.Data

    /// The one sanctioned boilerplate: keeps the EventKind -> bit mapping total
    /// and compiler-checked.
    let kind (ev: MatchEvent) : EventKind =
        match ev.Data with
        | EventData.CollectiveAttack _ -> EventKind.CollectiveAttack
        | EventData.TeamInstruction _ -> EventKind.TeamInstruction
        | EventData.DefensiveTransition _ -> EventKind.DefensiveTransition
        | EventData.Possession _ -> EventKind.PossessionGained
        | EventData.Pass _ -> EventKind.PassLaunched
        | EventData.Shot _ -> EventKind.ShotLaunched
        | EventData.Goal _ -> EventKind.Goal
        | EventData.Foul _ -> EventKind.Foul
        | EventData.Card _ -> EventKind.Card
        | EventData.SetPiece(SetPieceKind.ThrowIn, _) -> EventKind.ThrowIn
        | EventData.SetPieceAt(SetPieceKind.ThrowIn, _, _, _) -> EventKind.ThrowIn
        | EventData.SetPiece(SetPieceKind.Corner, _) -> EventKind.Corner
        | EventData.SetPieceAt(SetPieceKind.Corner, _, _, _) -> EventKind.Corner
        | EventData.SetPiece(SetPieceKind.GoalKick, _) -> EventKind.GoalKick
        | EventData.SetPieceAt(SetPieceKind.GoalKick, _, _, _) -> EventKind.GoalKick
        | EventData.SetPiece(SetPieceKind.KickOff, _) -> EventKind.KickOff
        | EventData.SetPieceAt(SetPieceKind.KickOff, _, _, _) -> EventKind.KickOff
        | EventData.SetPiece _
        | EventData.SetPieceAt _ -> EventKind.SetPieceAwarded
        | EventData.RestartAwarded _ -> EventKind.SetPieceAwarded
        | EventData.RestartPlanned _ -> EventKind.SetPieceAwarded
        | EventData.RestartAuthorized _ -> EventKind.SetPieceAwarded
        | EventData.RestartCommandIssued _ -> EventKind.SetPieceAwarded
        | EventData.RestartContact _ -> EventKind.SetPieceAwarded
        | EventData.RestartCompleted _ -> EventKind.SetPieceAwarded
        | EventData.RestartPlanChanged _ -> EventKind.SetPieceAwarded
        | EventData.BallLoose _ -> EventKind.BallLoose
        | EventData.Interception _ -> EventKind.Interception
        | EventData.Tackle _ -> EventKind.Tackle
        | EventData.Save _ -> EventKind.Save
        | EventData.CrossAttempt _ -> EventKind.CrossAttempt
        | EventData.Woodwork _ -> EventKind.Woodwork
        | EventData.KeeperDive _ -> EventKind.KeeperDive
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
        | EventData.PlayerBodyFlight _ -> EventKind.PlayerBodyFlight
        | EventData.FootContact _ -> EventKind.FootContact
        | EventData.PhysicalContact _ -> EventKind.PhysicalContact
        | EventData.FootStrikeCancelled _ -> EventKind.FootStrikeCancelled

    /// EventData has no mutable identity. This deterministic key gives the
    /// structural reducer a stable idempotence boundary until the causal ledger
    /// introduces explicit event ids.
    let id (ev: MatchEvent) : EventId = EventId(structuralHash ev)

    /// The sporting record a played fixture keeps. Contacts, foot strikes, body flights and the restart
    /// lifecycle are the physics of one live run (~95 % of its events): kept on every fixture they made a career
    /// save grow by tens of megabytes per match, and no consumer after the final whistle reads them.
    let isRecord (ev: MatchEvent) : bool =
        match ev.Data with
        | EventData.PhysicalContact _
        | EventData.FootContact _
        | EventData.FootStrikeCancelled _
        | EventData.PlayerBodyFlight _
        | EventData.RestartPlanned _
        | EventData.RestartAuthorized _
        | EventData.RestartCommandIssued _
        | EventData.RestartContact _
        | EventData.RestartCompleted _
        | EventData.RestartPlanChanged _ -> false
        | _ -> true

    let record (events: MatchEvent list) : MatchEvent list = List.filter isRecord events

    /// For tests/diagnostics only — the viewer derives its own text from EventData.
    let describe (ev: MatchEvent) : string =
        match ev.Data with
        | EventData.CollectiveAttack(side, episode, change) -> sprintf "Collective attack %A #%d: %A" side episode change
        | EventData.TeamInstruction side -> sprintf "Effective team instruction: %A" side
        | EventData.DefensiveTransition(side, _, change) -> sprintf "Defensive transition %A: %A" side change
        | EventData.Possession(side, _) -> sprintf "Possession: %A" side
        | EventData.Pass(_, _, distance, _, completed, _, _, _, _) ->
            sprintf "Pass %.1fm completed=%b" distance completed
        | EventData.Shot(_, result, _, speed, _, _) -> sprintf "Shot %A %.1fm/s" result speed
        | EventData.Goal(side, _, _, _) -> sprintf "GOAL: %A" side
        | EventData.Foul(_, _, severity, _, _) -> sprintf "Foul: %A" severity
        | EventData.Card(_, card) -> sprintf "Card: %A" card
        | EventData.SetPiece(kind, _) -> sprintf "SetPiece: %A" kind
        | EventData.SetPieceAt(kind, _, x, y) -> sprintf "SetPiece: %A at %.1f,%.1f" kind x y
        | EventData.RestartAwarded award -> sprintf "Restart awarded: %A" award.Kind
        | EventData.RestartPlanned _ -> "Restart planned"
        | EventData.RestartAuthorized _ -> "Restart authorized"
        | EventData.RestartCommandIssued _ -> "Restart command issued"
        | EventData.RestartContact _ -> "Restart contact"
        | EventData.FootContact(player, foot, kind, _, _, _) -> $"{player} {foot} foot contact ({kind})"
        | EventData.PhysicalContact contact ->
            sprintf "Physical contact %A (%A/%A, impulse %.3f)" contact.Id contact.SurfaceA contact.SurfaceB contact.NormalImpulse
        | EventData.FootStrikeCancelled player -> $"{player} foot strike cancelled"
        | EventData.RestartCompleted _ -> "Restart completed"
        | EventData.RestartPlanChanged _ -> "Restart plan changed"
        | EventData.BallLoose(_, _) -> "Ball loose"
        | EventData.Interception(_, _) -> "Interception"
        | EventData.Tackle(_, _) -> "Tackle"
        | EventData.Save(_, _) -> "Save"
        | EventData.CrossAttempt _ -> "Cross attempt"
        | EventData.Woodwork(_, part, _, _, _) -> sprintf "Woodwork %A" part
        | EventData.KeeperDive(keeper, _, y, z, _) -> sprintf "Keeper %A dives (%.1f, %.1f)" keeper (float y) (float z)
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
        | EventData.AdvantageStarted(incident, _) ->
            sprintf "Advantage: %A, incident %A at tick %d" incident.Side incident.Source (int incident.Tick)
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
        | EventData.PlayerBodyFlight(player, sequence, airborne) ->
            sprintf "Body flight %A #%d airborne=%b" player sequence airborne
