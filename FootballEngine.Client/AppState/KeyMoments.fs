namespace FootballEngine

open FootballEngine.Core.Match
open FootballEngine.LiveMatch
open FootballEngine.Settings

/// Viewer pacing: decides when the match is shown in real time and when it is fast-forwarded. It reads what the
/// runtime already knows after an advance and never feeds anything back into the simulation, so no value here is
/// simulation balance.
module KeyMoments =

    type Pace =
        | Cruise
        | Watch

    /// How far from the goal it attacks the possessing side must be for play to be worth watching: inside the box,
    /// where a shot is a real chance. A wider zone turned ordinary final-third possession into watched time and
    /// stretched a match to tens of real minutes, which is what this mode exists to avoid.
    let dangerZoneMeters = 22.0

    /// A loose ball is only a chance inside the box; further out it is mostly a contested clearance.
    let looseBallZoneMeters = 32.0

    /// A set piece this close to goal is a chance in itself, so it is watched from the award; a corner flag sits
    /// about 34 m from the centre of the goal, so anything shorter would never show a corner.
    let setPieceZoneMeters = 35.0

    /// Watching continues this long after the last trigger so play at the edge of the zone does not flicker
    /// between fast and real time.
    let watchHoldSeconds = 4

    /// A goal stops the match only after the finish and the celebration have been seen.
    let goalStopDelaySeconds = 4

    /// A tiring player is a signal for the manager, not an emergency; below this the choice of a substitution is open.
    let tiredCondition = 0.55

    /// Fast-forward speed relative to real time: fast enough to skip quiet play, slow enough to read as a game flowing.
    let cruiseSpeedMultiplier = 12.0

    /// One second of play per advance in Cruise: short enough that the danger zone is noticed before the shot it
    /// leads to, long enough to be cheap.
    let cruiseSliceTicks = 40

    type OnPitchPlayer =
        {
            Id: PlayerId
            Name: string
            /// 0.0 exhausted .. 1.0 fresh.
            Condition: float
        }

    type PendingRestart =
        { Kind: SetPieceKind
          Side: ClubSide
          X: float
          Y: float }

    /// What the runtime knows after one advance.
    type SliceInput =
        {
            Tick: int<subtick>
            SubticksPerSecond: int
            Events: MatchEvent[]
            BallX: float
            BallY: float
            Possession: ClubSide option
            HomeAttacksPositiveX: bool
            PendingRestart: PendingRestart option
            /// False during goal pauses, reviews and other flow states where a loose ball is not play.
            InPlay: bool
            UserSide: ClubSide
            UserPlayers: OnPitchPlayer list
            IsUserPlayer: PlayerId -> bool
            NameOf: PlayerId -> string
        }

    type Director =
        {
            Pace: Pace
            WatchUntilTick: int<subtick>
            TiredAlerted: Set<int>
            /// Reasons waiting to be shown, with the tick from which they may stop the match.
            Queue: (int<subtick> * KeyMomentReason) list
        }

    let init: Director =
        { Pace = Watch
          WatchUntilTick = 0<subtick>
          TiredAlerted = Set.empty
          Queue = [] }

    /// Reads the slice's facts off the world. Attack direction is fixed (home attacks +X for the whole match), so
    /// it is a constant of the pitch rather than something read per half.
    let inputOf
        (world: World)
        (userSide: ClubSide)
        (nameOf: PlayerId -> string)
        (events: MatchEvent[])
        (tick: int<subtick>)
        : SliceInput =
        let team = if userSide = ClubSide.Home then world.Home else world.Away
        let frame = team.Frame

        let userPlayers =
            [ for slot in 0 .. team.RosterIds.Length - 1 do
                  if MatchWorld.isActiveSlot frame.Occupancy[slot] then
                      let id = team.RosterIds[slot]

                      yield
                          { Id = id
                            Name = nameOf id
                            Condition = float frame.Condition[slot] } ]

        let ball = world.State.Ball

        { Tick = tick
          SubticksPerSecond = SimulationClock.defaultClock.SubTicksPerSecond
          Events = events
          BallX = float ball.Position.X
          BallY = float ball.Position.Y
          Possession =
            match ball.Control with
            | Controlled(side, _)
            | Receiving(side, _, _) -> Some side
            | _ -> None
          HomeAttacksPositiveX = true
          PendingRestart =
            match world.State.Flow with
            | MatchFlow.Restart restart ->
                Some
                    { Kind = restart.Kind
                      Side = restart.Side
                      X = float restart.Spot.X
                      Y = float restart.Spot.Y }
            | _ -> None
          InPlay = (world.State.Flow = MatchFlow.Live)
          UserSide = userSide
          UserPlayers = userPlayers
          IsUserPlayer = fun id -> Array.contains id team.RosterIds
          NameOf = nameOf }

    let private priority reason =
        match reason with
        | GoalScored _ -> 0
        | RedCardShown _ -> 1
        | RivalChange _
        | RivalShift _ -> 2
        | PlayerTiring _ -> 3

    let private idOf (PlayerId id) = id

    let private goalX attacksPositive (side: ClubSide) =
        let sign =
            if (side = ClubSide.Home) = attacksPositive then
                1.0
            else
                -1.0

        sign * float Pitch.HalfLength

    let private distanceToAttackedGoal attacksPositive side x y =
        let dx = x - goalX attacksPositive side
        sqrt (dx * dx + y * y)

    let private inDangerZone (input: SliceInput) =
        let ballInZone =
            match input.Possession with
            | Some side ->
                distanceToAttackedGoal input.HomeAttacksPositiveX side input.BallX input.BallY
                <= dangerZoneMeters
            // A ball in flight or loose has no possessor, yet a shot, a rebound or a clearance gone wrong near a goal is
            // exactly what the manager came to see; without this a goal can land in the gap between two Watch windows.
            // A restart or a goal pause is excluded: a goal kick waits with a loose ball inside the zone and a ball
            // resting in the net is not a chance.
            | None when input.InPlay && input.PendingRestart.IsNone ->
                distanceToAttackedGoal input.HomeAttacksPositiveX ClubSide.Home input.BallX input.BallY
                <= looseBallZoneMeters
                || distanceToAttackedGoal input.HomeAttacksPositiveX ClubSide.Away input.BallX input.BallY
                   <= looseBallZoneMeters
            | None -> false

        let restartInZone =
            match input.PendingRestart with
            | Some restart ->
                match restart.Kind with
                | SetPieceKind.Penalty
                | SetPieceKind.Corner
                | SetPieceKind.FreeKick ->
                    distanceToAttackedGoal input.HomeAttacksPositiveX restart.Side restart.X restart.Y
                    <= setPieceZoneMeters
                | _ -> false
            | None -> false

        ballInZone || restartInZone

    let private isPlayEvent (event: MatchEvent) =
        match event.Data with
        | EventData.Shot _
        | EventData.Goal _
        | EventData.RedCardIssued _
        | EventData.SetPiece(SetPieceKind.Penalty, _)
        | EventData.SetPieceAt(SetPieceKind.Penalty, _, _, _) -> true
        | _ -> false

    /// The rival shift of this slice for a side, when the coach made a move.
    let private rivalShiftOf (input: SliceInput) (side: ClubSide) =
        input.Events
        |> Array.tryPick (fun event ->
            match event.Data with
            | EventData.ManagerShift(s, shift) when s = side -> Some shift
            | _ -> None)

    let private reasonOf (input: SliceInput) (event: MatchEvent) : (int<subtick> * KeyMomentReason) option =
        let after seconds =
            event.Tick + seconds * input.SubticksPerSecond * 1<subtick>

        match event.Data with
        | EventData.Goal(side, _, _, _) -> Some(after goalStopDelaySeconds, GoalScored(side = input.UserSide))
        | EventData.RedCardIssued player ->
            Some(event.Tick, RedCardShown(input.NameOf player, input.IsUserPlayer player))
        | EventData.SubstitutionApplied(outgoing, incoming) when not (input.IsUserPlayer outgoing) ->
            Some(event.Tick, RivalChange $"{input.NameOf outgoing} → {input.NameOf incoming}")
        // Going back to the base plan undoes a move the viewer already stopped for; a second stop to say "he is
        // back" is noise, and the instruction change that carries it is dropped below for the same reason.
        | EventData.ManagerShift(_, ManagerShift.BackToPlan) -> None
        | EventData.ManagerShift(side, shift) when side <> input.UserSide -> Some(event.Tick, RivalShift shift)
        // Every rival move emits a TeamInstruction on the same tick as its ManagerShift; the shift is the stop and
        // says what he did, so only an instruction change without one (a scripted or legacy change) stops on its own.
        // The user's own changes never reach here because they are the manager's own doing.
        | EventData.TeamInstruction side when side <> input.UserSide ->
            match rivalShiftOf input side with
            | Some _ -> None
            | None -> Some(event.Tick, RivalChange "")
        | _ -> None

    /// `All` is the full-match option: it never fast-forwards and never stops, so the manager sees every minute.
    let step (mode: HighlightFilter) (input: SliceInput) (director: Director) : Director * KeyMomentReason option =
        match mode with
        | HighlightFilter.All ->
            { director with
                Pace = Watch
                Queue = [] },
            None
        | HighlightFilter.KeyMoments
        | HighlightFilter.GoalsOnly ->
            let watching = inDangerZone input || (input.Events |> Array.exists isPlayEvent)

            let watchUntil =
                if watching then
                    max director.WatchUntilTick (input.Tick + watchHoldSeconds * input.SubticksPerSecond * 1<subtick>)
                else
                    director.WatchUntilTick

            let admitted reason =
                match mode, reason with
                | HighlightFilter.GoalsOnly, GoalScored _ -> true
                | HighlightFilter.GoalsOnly, _ -> false
                | _ -> true

            let fromEvents =
                input.Events
                |> Array.choose (reasonOf input)
                |> Array.filter (fun (_, reason) -> admitted reason)
                |> List.ofArray

            // Marked on queueing, not on showing, so a tiring player is announced once even if a goal goes first.
            let tiring =
                if admitted (PlayerTiring("", 0)) then
                    input.UserPlayers
                    |> List.filter (fun (p: OnPitchPlayer) ->
                        p.Condition < tiredCondition && not (director.TiredAlerted.Contains(idOf p.Id)))
                else
                    []

            let tiredAlerted =
                tiring
                |> List.fold (fun (set: Set<int>) (p: OnPitchPlayer) -> set.Add(idOf p.Id)) director.TiredAlerted

            let queued =
                director.Queue
                @ fromEvents
                @ (tiring
                   |> List.map (fun p -> input.Tick, PlayerTiring(p.Name, int (p.Condition * 100.0))))

            let ready, waiting =
                queued |> List.partition (fun (readyAt, _) -> readyAt <= input.Tick)

            let stop =
                match ready with
                | [] -> None
                | _ -> Some(ready |> List.minBy (fun (readyAt, reason) -> priority reason, readyAt))

            let leftover =
                match stop with
                | Some chosen ->
                    waiting
                    @ (ready |> List.filter (fun item -> not (obj.ReferenceEquals(item, chosen))))
                | None -> waiting

            { Pace = if input.Tick < watchUntil then Watch else Cruise
              WatchUntilTick = watchUntil
              TiredAlerted = tiredAlerted
              Queue = leftover },
            stop |> Option.map snd
