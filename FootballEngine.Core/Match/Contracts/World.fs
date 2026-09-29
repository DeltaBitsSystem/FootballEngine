namespace FootballEngine.Core.Match

/// A multi-step plan: the carrier's mind maps out future receivers after the
/// current pass, so the team moves as one coordinated unit. The plan is
/// written once by Decision, consumed by Intent, and cleared on possession
/// change, carrier change, or expiry.
type ChainPlan =
    { FutureReceivers: (int * float<meter> * float<meter>)[]
      ExpiresAt: int<subtick> }

type ParticipationInterval =
    { PlayerId: PlayerId
      Role: SlotRole
      StartAt: int<subtick>
      mutable EndAt: int<subtick> option }

/// Participation is owned by the match so substitutions, dismissals and
/// injuries close the same interval that later becomes the World statistic.
type ParticipationBook() =
    member val Intervals = System.Collections.Generic.List<ParticipationInterval>()

module ParticipationBook =

    let create () = ParticipationBook()

    let start (book: ParticipationBook) (playerId: PlayerId) (role: SlotRole) (at: int<subtick>) =
        if
            book.Intervals
            |> Seq.exists (fun interval -> interval.PlayerId = playerId && interval.EndAt.IsNone)
        then
            ()
        else
            book.Intervals.Add
                { PlayerId = playerId
                  Role = role
                  StartAt = at
                  EndAt = None }

    let stop (book: ParticipationBook) (playerId: PlayerId) (at: int<subtick>) =
        let mutable stopped = false

        for i = book.Intervals.Count - 1 downto 0 do
            if not stopped then
                let interval = book.Intervals.[i]

                if interval.PlayerId = playerId && interval.EndAt.IsNone then
                    interval.EndAt <- Some(max interval.StartAt at)
                    stopped <- true

    let snapshot (book: ParticipationBook) (endAt: int<subtick>) : ParticipationInterval[] =
        book.Intervals
        |> Seq.map (fun interval ->
            { interval with
                EndAt = Some(max interval.StartAt (interval.EndAt |> Option.defaultValue endAt)) })
        |> Array.ofSeq

/// One team's live match state. All books are owned by this match (no globals).
type TeamWorld =
    { mutable Frame: TeamFrame
      RosterIds: PlayerId[]
      mutable Memory: PlayerMemory[]
      mutable Cards: CardsBook
      mutable SubsUsed: int
      mutable Shape: ShapeTargets
      mutable Directive: TeamDirective
      mutable Blackboard: Blackboard
      mutable Emergent: EmergentState
      mutable Intercept: InterceptState
      mutable Stats: TeamStats
      mutable CFrame: CognitiveFrame
      mutable EventWindow: EventWindow
      mutable Substitution: SubstitutionState
      Participation: ParticipationBook
      mutable ActivePlan: ChainPlan option
      mutable LastCarrierSlot: int
      mutable LastRoleAssignmentTick: int<subtick>
      mutable FormationCache: FormationModel option
      mutable LateralPairs: (int * int)[] option
      mutable GoalkeeperSlot: int option
      mutable OwnPositions: PositionPoint[]
      mutable MirroredPositions: PositionPoint[]
      mutable FormationTargets: PositionPoint[]
      mutable EvaluatorScratch: PositionPoint[] }

[<Struct>]
type RefereeSignal =
    | NoSignal
    | Whistle
    | RestartDirection of SetPieceKind * ClubSide

[<Struct>]
type MatchOfficial =
    { X: float<meter>
      Y: float<meter> }

[<Struct>]
type OfficialsState =
    { Referee: MatchOfficial
      NearAssistant: MatchOfficial
      FarAssistant: MatchOfficial
      Signal: RefereeSignal }

/// The single object systems see. All arrays/books owned by this match.
type World =
    { mutable State: MatchState
      mutable Officials: OfficialsState
      Home: TeamWorld
      Away: TeamWorld
      AppliedEventIds: System.Collections.Generic.HashSet<EventId>
      mutable NextRestartId: uint64 }
