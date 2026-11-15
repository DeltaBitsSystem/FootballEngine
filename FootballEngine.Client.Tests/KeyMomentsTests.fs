module FootballEngine.Client.Tests.KeyMomentsTests

open Expecto
open FootballEngine
open FootballEngine.Core.Match
open FootballEngine.LiveMatch
open FootballEngine.Settings
open FootballEngine.World.WorldRunner

let private perSecond = SimulationClock.defaultClock.SubTicksPerSecond
let private seconds (value: int) = value * perSecond * 1<subtick>
let private userSide = ClubSide.Home
let private userIds = set [ 1; 2; 3 ]

let private baseInput tick : KeyMoments.SliceInput =
    { Tick = tick
      SubticksPerSecond = perSecond
      Events = [||]
      BallX = 0.0
      BallY = 0.0
      Possession = Some ClubSide.Home
      HomeAttacksPositiveX = true
      PendingRestart = None
      InPlay = true
      UserSide = userSide
      UserPlayers =
        [ { Id = PlayerId 1
            Name = "Perez"
            Condition = 0.9 } ]
      IsUserPlayer = fun (PlayerId id) -> userIds.Contains id
      NameOf = fun (PlayerId id) -> $"P{id}" }

let private nearAwayGoal tick =
    { baseInput tick with
        BallX = 40.0
        BallY = 5.0 }

let private withEvent tick data (input: KeyMoments.SliceInput) =
    { input with
        Events = [| { Tick = tick; Data = data } |] }

let private goalEvent side =
    EventData.Goal(side, Some(PlayerId 1), None, 0.3)

let private stepKeyMoments = KeyMoments.step HighlightFilter.KeyMoments

let unitTests =
    testList
        "unit"
        [ testCase "possession inside the danger zone is watched"
          <| fun _ ->
              let next, stop = stepKeyMoments (nearAwayGoal (seconds 10)) KeyMoments.init
              Expect.equal next.Pace KeyMoments.Watch "a chance is being built"
              Expect.isNone stop "watching alone never stops the match"

          testCase "the ball leaving the zone keeps Watch until the hold expires, then Cruise"
          <| fun _ ->
              let inZone, _ = stepKeyMoments (nearAwayGoal (seconds 10)) KeyMoments.init
              let outside tick = { baseInput tick with BallX = -10.0 }
              let soon, _ = stepKeyMoments (outside (seconds 12)) inZone
              Expect.equal soon.Pace KeyMoments.Watch "two seconds after the zone is inside the hysteresis"
              let late, _ = stepKeyMoments (outside (seconds 15)) soon
              Expect.equal late.Pace KeyMoments.Cruise "the hold of four seconds has expired"

          testCase "the rival's attacking zone is the other goal; a loose ball counts near either goal only"
          <| fun _ ->
              let awayPossession =
                  { baseInput (seconds 10) with
                      BallX = 40.0
                      Possession = Some ClubSide.Away }

              let next, _ = stepKeyMoments awayPossession KeyMoments.init
              Expect.equal next.Pace KeyMoments.Cruise "away attacks the goal at -X, 40 m is far from it"

              let looseNearGoal =
                  { nearAwayGoal (seconds 10) with
                      Possession = None }

              let next, _ = stepKeyMoments looseNearGoal KeyMoments.init
              Expect.equal next.Pace KeyMoments.Watch "a ball in flight or loose near a goal is a chance in the making"

              let looseMidfield =
                  { baseInput (seconds 10) with
                      Possession = None }

              let next, _ = stepKeyMoments looseMidfield KeyMoments.init
              Expect.equal next.Pace KeyMoments.Cruise "a loose ball far from both goals is quiet"

          testCase "a pending corner near goal is watched from the award"
          <| fun _ ->
              let input =
                  { baseInput (seconds 10) with
                      Possession = None
                      PendingRestart =
                          Some
                              { Kind = SetPieceKind.Corner
                                Side = ClubSide.Home
                                X = 52.0
                                Y = 33.0 } }

              let next, _ = stepKeyMoments input KeyMoments.init
              Expect.equal next.Pace KeyMoments.Watch "a corner is a chance"

          testCase "a goal stops exactly once, only after four seconds past the goal tick"
          <| fun _ ->
              let goalTick = seconds 100
              let quiet tick = { baseInput tick with BallX = -10.0 }

              let first, stop0 =
                  stepKeyMoments (quiet goalTick |> withEvent goalTick (goalEvent ClubSide.Home)) KeyMoments.init

              Expect.isNone stop0 "the finish has not been seen yet"
              let mid, stop1 = stepKeyMoments (quiet (goalTick + seconds 3)) first
              Expect.isNone stop1 "three seconds is still inside the delay"
              let after, stop2 = stepKeyMoments (quiet (goalTick + seconds 4)) mid
              Expect.equal stop2 (Some(GoalScored true)) "four seconds after the goal the user's goal stops the match"
              let _, stop3 = stepKeyMoments (quiet (goalTick + seconds 6)) after
              Expect.isNone stop3 "the same goal never stops twice"

          testCase "a rival goal reports the rival as scorer"
          <| fun _ ->
              let tick = seconds 50

              let d, _ =
                  stepKeyMoments (baseInput tick |> withEvent tick (goalEvent ClubSide.Away)) KeyMoments.init

              let _, stop = stepKeyMoments (baseInput (tick + seconds 4)) d
              Expect.equal stop (Some(GoalScored false)) "the side that scored decides the wording"

          testCase "a user player crossing the tiring threshold stops once, not on later slices"
          <| fun _ ->
              let tired value tick =
                  { baseInput tick with
                      UserPlayers =
                          [ { Id = PlayerId 1
                              Name = "Perez"
                              Condition = value } ] }

              let d1, s1 = stepKeyMoments (tired 0.60 (seconds 10)) KeyMoments.init
              Expect.isNone s1 "above the threshold nothing happens"
              let d2, s2 = stepKeyMoments (tired 0.48 (seconds 11)) d1
              Expect.equal s2 (Some(PlayerTiring("Perez", 48))) "crossing the threshold announces the player"
              let d3, s3 = stepKeyMoments (tired 0.47 (seconds 12)) d2
              Expect.isNone s3 "already announced"
              let _, s4 = stepKeyMoments (tired 0.30 (seconds 400)) d3
              Expect.isNone s4 "never repeated for the same player in the match"

          testCase "a rival substitution stops, the user's own substitution does not"
          <| fun _ ->
              let tick = seconds 60
              let rival = EventData.SubstitutionApplied(PlayerId 20, PlayerId 21)
              let own = EventData.SubstitutionApplied(PlayerId 1, PlayerId 2)

              let _, stop =
                  stepKeyMoments (baseInput tick |> withEvent tick rival) KeyMoments.init

              Expect.equal stop (Some(RivalChange "P20 → P21")) "the rival changed"

              let _, ownStop =
                  stepKeyMoments (baseInput tick |> withEvent tick own) KeyMoments.init

              Expect.isNone ownStop "the manager made that change"

          testCase "a rival ManagerShift stops with the shift it carries"
          <| fun _ ->
              let tick = seconds 60

              for shift in
                  [ ManagerShift.ChaseGame
                    ManagerShift.AllOut
                    ManagerShift.ProtectLead
                    ManagerShift.WeatherStorm
                    ManagerShift.TakeInitiative
                    ManagerShift.RedCardReshape ] do
                  let _, stop =
                      stepKeyMoments
                          (baseInput tick |> withEvent tick (EventData.ManagerShift(ClubSide.Away, shift)))
                          KeyMoments.init

                  Expect.equal stop (Some(RivalShift shift)) $"the rival's {shift} is the stop"

          testCase "a rival move yields one stop: the TeamInstruction that accompanies its shift adds none"
          <| fun _ ->
              let tick = seconds 60

              let input =
                  { baseInput tick with
                      Events =
                        [| { Tick = tick; Data = EventData.TeamInstruction ClubSide.Away }
                           { Tick = tick; Data = EventData.ManagerShift(ClubSide.Away, ManagerShift.ChaseGame) } |] }

              let director, first = stepKeyMoments input KeyMoments.init
              Expect.equal first (Some(RivalShift ManagerShift.ChaseGame)) "the shift is the stop"
              Expect.isEmpty director.Queue "nothing else is waiting behind it"

              let _, again = stepKeyMoments (baseInput (seconds 61)) director
              Expect.isNone again "no second stop for the same move"

          testCase "a bare rival TeamInstruction without a shift still stops, the user's own never does"
          <| fun _ ->
              let tick = seconds 60

              let _, rival =
                  stepKeyMoments (baseInput tick |> withEvent tick (EventData.TeamInstruction ClubSide.Away)) KeyMoments.init

              Expect.equal rival (Some(RivalChange "")) "an unexplained rival change is still announced"

              let own = baseInput tick |> withEvent tick (EventData.TeamInstruction userSide)
              let _, ownStop = stepKeyMoments own KeyMoments.init
              Expect.isNone ownStop "the manager made that change"

              let ownWithShift =
                  { own with
                      Events =
                        Array.append
                            own.Events
                            [| { Tick = tick; Data = EventData.ManagerShift(userSide, ManagerShift.ChaseGame) } |] }

              let _, ownShiftStop = stepKeyMoments ownWithShift KeyMoments.init
              Expect.isNone ownShiftStop "a shift on the user's own side never stops the match"

          testCase "the rival going back to his plan does not stop the match"
          <| fun _ ->
              let tick = seconds 90

              let input =
                  { baseInput tick with
                      Events =
                        [| { Tick = tick; Data = EventData.TeamInstruction ClubSide.Away }
                           { Tick = tick; Data = EventData.ManagerShift(ClubSide.Away, ManagerShift.BackToPlan) } |] }

              let _, stop = stepKeyMoments input KeyMoments.init
              Expect.isNone stop "undoing a move the viewer already stopped for is not a new moment"

          testCase "a rival shift outranks a tired player and yields to a goal and a red card"
          <| fun _ ->
              let tick = seconds 60
              let tired = { baseInput tick with UserPlayers = [ { Id = PlayerId 1; Name = "Perez"; Condition = 0.3 } ] }

              let shift = { Tick = tick; Data = EventData.ManagerShift(ClubSide.Away, ManagerShift.AllOut) }
              let red = { Tick = tick; Data = EventData.RedCardIssued(PlayerId 20) }

              let _, first = stepKeyMoments { tired with Events = [| shift |] } KeyMoments.init
              Expect.equal first (Some(RivalShift ManagerShift.AllOut)) "the shift comes before the tiring player"

              let _, second = stepKeyMoments { tired with Events = [| shift; red |] } KeyMoments.init
              Expect.equal second (Some(RedCardShown("P20", false))) "a red card is more urgent than a tactical change"

          testCase "a goal outranks a red card and a tired player in the same slice, and the rest follow"
          <| fun _ ->
              let tick = seconds 70

              let input =
                  { baseInput tick with
                      Events =
                          [| { Tick = tick - seconds 5
                               Data = goalEvent ClubSide.Home }
                             { Tick = tick
                               Data = EventData.RedCardIssued(PlayerId 20) } |]
                      UserPlayers =
                          [ { Id = PlayerId 1
                              Name = "Perez"
                              Condition = 0.3 } ] }

              let d1, s1 = stepKeyMoments input KeyMoments.init
              Expect.equal s1 (Some(GoalScored true)) "goal first"
              let d2, s2 = stepKeyMoments (baseInput (tick + seconds 1)) d1
              Expect.equal s2 (Some(RedCardShown("P20", false))) "then the red card"
              let _, s3 = stepKeyMoments (baseInput (tick + seconds 2)) d2
              Expect.equal s3 (Some(PlayerTiring("Perez", 30))) "then the tired player"

          testCase "All is always Watch and never stops"
          <| fun _ ->
              let tick = seconds 10

              let input =
                  { baseInput tick with
                      BallX = -30.0
                      Events =
                          [| { Tick = tick
                               Data = goalEvent ClubSide.Home } |]
                      UserPlayers =
                          [ { Id = PlayerId 1
                              Name = "Perez"
                              Condition = 0.1 } ] }

              let next, stop = KeyMoments.step HighlightFilter.All input KeyMoments.init
              Expect.equal next.Pace KeyMoments.Watch "the full match is never fast-forwarded"
              Expect.isNone stop "and never interrupted"

              let later, stop =
                  KeyMoments.step HighlightFilter.All (baseInput (seconds 5000)) next

              Expect.equal later.Pace KeyMoments.Watch "not even in quiet play"
              Expect.isNone stop "nor later"

          testCase "GoalsOnly stops for goals but not for red cards, tiring or rival changes"
          <| fun _ ->
              let step = KeyMoments.step HighlightFilter.GoalsOnly
              let tick = seconds 10

              let noisy =
                  { baseInput tick with
                      Events =
                          [| { Tick = tick
                               Data = EventData.RedCardIssued(PlayerId 20) }
                             { Tick = tick
                               Data = EventData.SubstitutionApplied(PlayerId 20, PlayerId 21) } |]
                      UserPlayers =
                          [ { Id = PlayerId 1
                              Name = "Perez"
                              Condition = 0.1 } ] }

              let d, stop = step noisy KeyMoments.init
              Expect.isNone stop "none of these is a goal"
              let d, stop = step (baseInput (tick + seconds 10)) d
              Expect.isNone stop "and they are not queued for later"

              let withGoal =
                  baseInput (tick + seconds 20)
                  |> withEvent (tick + seconds 20) (goalEvent ClubSide.Home)

              let d, _ = step withGoal d
              let _, stop = step (baseInput (tick + seconds 24)) d
              Expect.equal stop (Some(GoalScored true)) "goals still stop" ]

// ---- headless integration: one real seeded match through the director ----

type private Run =
    { Hash: uint64
      Score: int * int
      Goals: int<subtick> list
      WatchWindows: (int<subtick> * int<subtick>) list
      WatchTicks: int
      TotalTicks: int
      Stops: KeyMomentReason list
      EventKinds: Map<string, int> }

let private job () =
    let matchId, gs = LiveMatchTests.scheduledCareer ()
    let balance = WeightMergeSettings.mergeWithOverridesSave None
    let clock = FootballEngine.World.WorldClockOps.init gs.Season

    match prepareUserMatchJobWithBalance balance clock gs matchId with
    | Ok job -> job, gs
    | Error error -> failtestf "cannot prepare the match: %s" error

let private finishOf (session: MatchSession) =
    match MatchSession.finish session with
    | Ok outcome -> outcome
    | Error error -> failtestf "cannot finish: %A" error

/// Without a director: coarse constant slices, the shape of an unwatched skip.
let private plainRun () : uint64 * (int * int) =
    let job, _ = job ()

    match MatchSession.create Scheduler.systems job.Input MatchRunMode.Fast with
    | Error error -> failtestf "%A" error
    | Ok session ->
        while session.World.State.Flow <> MatchFlow.MatchEnded do
            match
                MatchSession.advanceTo (min MatchSession.maxTicks (session.World.State.Tick + 400<subtick>)) session
            with
            | Ok _ -> ()
            | Error error -> failtestf "%A" error

        let outcome = finishOf session
        outcome.EventsHash, (outcome.Final.State.HomeScore, outcome.Final.State.AwayScore)

/// The runtime's loop without the UI: Cruise advances a second, Watch the live slice. `blind` removes the
/// danger-zone facts to show the window assertion detects a director that ignores them.
let private directedRun (blind: bool) : Run =
    let job, gs = job ()

    let userSide =
        if uint64 gs.UserClubId = job.Input.Context.HomeClub.Id then
            ClubSide.Home
        else
            ClubSide.Away

    match MatchSession.create Scheduler.systems job.Input MatchRunMode.Fast with
    | Error error -> failtestf "%A" error
    | Ok session ->
        let nameOf (PlayerId id) = $"#{id}"
        let mutable director = KeyMoments.init
        let mutable cruising = false
        let mutable watchTicks = 0
        let mutable windowStart: int<subtick> option = None
        let windows = ResizeArray()
        let goals = ResizeArray()
        let stops = ResizeArray()
        let kinds = System.Collections.Generic.Dictionary<string, int>()
        let liveSlice = int LiveExecutionPolicy.defaultPolicy.WorkerSlice

        while session.World.State.Flow <> MatchFlow.MatchEnded do
            let current = session.World.State.Tick
            let slice = if cruising then KeyMoments.cruiseSliceTicks else liveSlice

            match MatchSession.advanceTo (min MatchSession.maxTicks (current + slice * 1<subtick>)) session with
            | Error error -> failtestf "%A" error
            | Ok advance ->
                if not cruising then
                    watchTicks <- watchTicks + int (advance.ReachedTick - current)

                for event in advance.Events do
                    match event.Data with
                    | EventData.Goal _ -> goals.Add event.Tick
                    | _ -> ()

                    let name = (MatchEvent.kind event).ToString()

                    kinds[name] <-
                        (match kinds.TryGetValue name with
                         | true, n -> n + 1
                         | _ -> 1)

                let input =
                    KeyMoments.inputOf session.World userSide nameOf advance.Events advance.ReachedTick

                let input =
                    if blind then
                        { input with
                            BallX = 0.0
                            BallY = 0.0
                            Possession = None
                            PendingRestart = None }
                    else
                        input

                let next, stop = KeyMoments.step HighlightFilter.KeyMoments input director
                director <- next
                stop |> Option.iter stops.Add
                let wantsCruise = next.Pace = KeyMoments.Cruise

                match windowStart, wantsCruise with
                | None, false -> windowStart <- Some advance.ReachedTick
                | Some start, true ->
                    windows.Add(start, advance.ReachedTick)
                    windowStart <- None
                | _ -> ()

                cruising <- wantsCruise

        windowStart
        |> Option.iter (fun start -> windows.Add(start, session.World.State.Tick))

        let total = int session.World.State.Tick
        let outcome = finishOf session

        { Hash = outcome.EventsHash
          Score = outcome.Final.State.HomeScore, outcome.Final.State.AwayScore
          Goals = List.ofSeq goals
          WatchWindows = List.ofSeq windows
          WatchTicks = watchTicks
          TotalTicks = total
          Stops = List.ofSeq stops
          EventKinds = kinds |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq }

/// A goal is "built up" when a Watch window began at least three seconds before it and still covered it.
let private buildUpSeen (run: Run) (goal: int<subtick>) =
    run.WatchWindows
    |> List.exists (fun (start, finish) -> start <= goal - seconds 3 && goal <= finish)

let private cached = lazy (directedRun false, directedRun true, plainRun ())

let integrationTests =
    testList
        "integration"
        [ testCase
              "a seeded match is paced without changing it, every goal's build-up is watched, and most play is skipped"
          <| fun _ ->
              let run, _, (plainHash, plainScore) = cached.Value
              let share = float run.WatchTicks / float run.TotalTicks

              printfn
                  "KeyMoments watch share %.1f%% of %d ticks; goals %d; stops %A; windows %d"
                  (share * 100.0)
                  run.TotalTicks
                  run.Goals.Length
                  run.Stops
                  run.WatchWindows.Length

              printfn "KeyMoments events %A" run.EventKinds

              Expect.isGreaterThan
                  run.Goals.Length
                  0
                  "the chosen match has goals, so the build-up assertion is not vacuous"

              for goal in run.Goals do
                  Expect.isTrue
                      (buildUpSeen run goal)
                      $"the build-up to the goal at tick {goal} is inside a Watch window begun 3 s earlier"

              Expect.isGreaterThan share 0.10 "some play is watched"
              // A director that only sees the present must start watching before it knows a goal is coming; on this
              // corpus the zones that still show every goal's build-up cost about 38% of match time. Lower shares need
              // look-ahead presentation (simulate ahead, present delayed), which is the next step, not a looser rule.
              Expect.isLessThan share 0.45 "most of the match is fast-forwarded"
              Expect.equal run.Hash plainHash "pacing leaves the sporting record identical"
              Expect.equal run.Score plainScore "and the final score"

          testCase "sensitivity: a director that ignores the danger zone misses a goal's build-up"
          <| fun _ ->
              let run, blind, _ = cached.Value
              // Only the Shot/Goal events of the goal's own slice trigger Watch, so some goal must lack the 3 s lead.
              let missed = blind.Goals |> List.filter (fun goal -> not (buildUpSeen blind goal))
              Expect.isNonEmpty missed "the build-up assertion fails for a blind director"
              Expect.equal blind.Hash run.Hash "and the match is the same one" ]

let tests = testList "KeyMoments" [ unitTests; integrationTests ]
