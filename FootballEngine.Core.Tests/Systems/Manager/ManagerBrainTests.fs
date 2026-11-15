namespace FootballEngine.Core.Tests.Systems.Manager

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

/// The rival manager's brain: its decision rules on a table of reads, its system behaviour on a hand-built world,
/// and the contract that the side the user commands never meets it.
module ManagerBrainTests =

    let private cfg = BalanceConfig.defaultConfig.RivalManager

    let private profile reading boldness temperament : ManagerProfile =
        { Autonomous = true
          Reading = reading
          Boldness = boldness
          Temperament = temperament
          Adaptability = 0.5f }

    let private read minute scoreDiff : ManagerRead =
        { Minute = minute
          ScoreDiff = scoreDiff
          Momentum = 0.0
          MinutesSinceMove = 99
          EventSinceMove = false
          OwnRedSinceReview = false
          Current = ValueNone }

    let private shiftOf (move: ManagerMove voption) =
        move |> ValueOption.map _.Shift

    // ---- The world the system tests run on: eleven starters and four players on the bench ----

    let private benchRoles =
        [| SlotRole.Striker
           SlotRole.Winger
           SlotRole.Defender
           SlotRole.DefensiveMid |]

    let private lineupWithBench offset =
        let lineup = TestBuilders.makeLineup offset 15

        { lineup with
            Profiles =
                lineup.Profiles
                |> Array.mapi (fun i p ->
                    if i >= 11 then
                        { p with Role = benchRoles.[i - 11] }
                    else
                        p) }

    let private tiredBoundary offset =
        let boundary = MatchLineupBoundary.fromLineup (lineupWithBench offset)

        { boundary with
            Starters =
                boundary.Starters
                |> Array.mapi (fun i p ->
                    if i = 5 || i = 8 then
                        { p with InitialCondition = 0.3f }
                    else
                        p) }

    let private contextWith (away: ManagerProfile) (home: ManagerProfile) : MatchContext =
        let ctx = TestBuilders.defaultCtx ()

        { ctx with
            HomeLineup = lineupWithBench 0
            AwayLineup = lineupWithBench 100
            HomeParticipants = tiredBoundary 0
            AwayParticipants = tiredBoundary 100
            HomeClub = { ctx.HomeClub with Manager = home }
            AwayClub = { ctx.AwayClub with Manager = away } }

    let private minuteTick (minute: int) : int<subtick> =
        minute * int SimulationClock.minuteSubticks * 1<subtick>

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    /// A fresh match world at a given tick, with every starter rested so only what a test sets up matters.
    let private worldAt (ctx: MatchContext) (tick: int<subtick>) (homeGoals: int) (awayGoals: int) : World =
        let world = MatchFactory.create ctx

        for team in [ world.Home; world.Away ] do
            for slot in 0..10 do
                team.Frame.Condition.[slot] <- 1.0f

        world.State <-
            { world.State with
                Tick = tick
                Clock = { world.State.Clock with Tick = tick }
                HomeScore = homeGoals
                AwayScore = awayGoals }

        world

    let private step (ctx: MatchContext) (world: World) (applied: EventData list) : MatchEvent list =
        let b = buffer ()

        for data in applied do
            b.Applied.Add { Tick = world.State.Tick; Data = data }

        ManagerSystem.step
            { Ctx = ctx
              Clock = SimulationClock.defaultClock }
            (Rng.ofSeed 7UL)
            world
            b
        |> ignore

        List.ofSeq b.Events

    let private shiftsOf (side: ClubSide) (events: MatchEvent list) =
        events
        |> List.choose (fun ev ->
            match ev.Data with
            | EventData.ManagerShift(s, shift) when s = side -> Some shift
            | _ -> None)

    let private instructionsOf (side: ClubSide) (events: MatchEvent list) =
        events
        |> List.filter (fun ev -> ev.Data = EventData.TeamInstruction side)

    let private substitutions (events: MatchEvent list) =
        events
        |> List.choose (fun ev ->
            match ev.Data with
            | EventData.SubstitutionRequested(out, inn) -> Some(out, inn)
            | _ -> None)

    let private idOf (ctx: MatchContext) (slot: int) = ctx.AwayLineup.Profiles.[slot].Id

    let brainTests =
        testList
            "FootballEngine.Core.Systems.Manager.ManagerBrain"
            [ testCase "a bold coach chases a deficit earlier than a cautious one reading the same match" (fun _ ->
                  let bold = profile 1.0f 0.9f 0.2f
                  let cautious = profile 1.0f 0.1f 0.2f

                  Expect.equal
                      (ManagerBrain.decide cfg bold (read 60 -1) 0.0 |> shiftOf)
                      (ValueSome ManagerShift.ChaseGame)
                      "the bold coach is already chasing at 60'"

                  Expect.equal
                      (ManagerBrain.decide cfg cautious (read 60 -1) 0.0 |> shiftOf)
                      ValueNone
                      "the cautious coach still waits at 60'"

                  Expect.equal
                      (ManagerBrain.decide cfg cautious (read 78 -1) 0.0 |> shiftOf)
                      (ValueSome ManagerShift.ChaseGame)
                      "but he goes for it in the last quarter hour")

              testCase "a very cautious coach parks the bus where a bold one only defends" (fun _ ->
                  let cautious = ManagerBrain.decide cfg (profile 1.0f 0.1f 0.2f) (read 85 1) 0.0
                  let bold = ManagerBrain.decide cfg (profile 1.0f 0.8f 0.2f) (read 85 1) 0.0

                  Expect.equal (shiftOf cautious) (ValueSome ManagerShift.ProtectLead) "both protect the lead"
                  Expect.equal (cautious |> ValueOption.map _.Directive) (ValueSome DirectiveKind.ParkTheBus) "the cautious one closes the shop"
                  Expect.equal (shiftOf bold) (ValueSome ManagerShift.ProtectLead) "the bold one protects it too"
                  Expect.equal (bold |> ValueOption.map _.Directive) (ValueSome DirectiveKind.Defend) "but only defends"
                  Expect.isGreaterThan
                      (cautious |> ValueOption.map _.Intensity |> ValueOption.defaultValue 0.0)
                      (bold |> ValueOption.map _.Intensity |> ValueOption.defaultValue 0.0)
                      "and retreats further")

              testCase "only a bold coach goes all out for a one-goal deficit at the end" (fun _ ->
                  Expect.equal
                      (ManagerBrain.decide cfg (profile 1.0f 0.9f 0.2f) (read 85 -1) 0.0 |> shiftOf)
                      (ValueSome ManagerShift.AllOut)
                      "the gambler throws everything in"

                  Expect.equal
                      (ManagerBrain.decide cfg (profile 1.0f 0.4f 0.2f) (read 85 -1) 0.0 |> shiftOf)
                      (ValueSome ManagerShift.ChaseGame)
                      "a pragmatic coach keeps to the plain chase")

              testCase "a coach who reads the game reacts to a goal sooner than one who does not" (fun _ ->
                  let sharp = profile 0.95f 0.5f 0.5f
                  let blunt = profile 0.25f 0.5f 0.5f
                  Expect.isLessThan (ManagerBrain.reviewMinuteAfter cfg sharp 50) (ManagerBrain.reviewMinuteAfter cfg blunt 50) "sharp reviews first"
                  Expect.equal (ManagerBrain.reviewMinuteAfter cfg (profile 1.0f 0.5f 0.5f) 50) 50 "perfect reading reacts on the spot"

                  Expect.isLessThan
                      (ManagerBrain.reactionDelayMinutes cfg (profile 0.5f 0.5f 0.9f))
                      (ManagerBrain.reactionDelayMinutes cfg (profile 0.5f 0.5f 0.1f))
                      "a hothead reacts before a calm coach")

              testCase "no move inside the pause between moves unless a goal or a red card lifts it" (fun _ ->
                  let coach = profile 1.0f 0.9f 0.0f
                  let recent = { read 70 -1 with MinutesSinceMove = cfg.MinMinutesBetweenMoves - 1 }

                  Expect.equal (ManagerBrain.decide cfg coach recent 0.0 |> shiftOf) ValueNone "still inside the pause"

                  Expect.equal
                      (ManagerBrain.decide cfg coach { recent with EventSinceMove = true } 0.0 |> shiftOf)
                      (ValueSome ManagerShift.ChaseGame)
                      "a goal lifts it"

                  Expect.equal
                      (ManagerBrain.decide cfg coach { recent with OwnRedSinceReview = true } 0.0 |> shiftOf)
                      (ValueSome ManagerShift.RedCardReshape)
                      "so does his own red card"

                  Expect.equal
                      (ManagerBrain.decide cfg coach { recent with MinutesSinceMove = cfg.MinMinutesBetweenMoves } 0.0 |> shiftOf)
                      (ValueSome ManagerShift.ChaseGame)
                      "and so does the pause running out"

                  Expect.isLessThan
                      (ManagerBrain.minutesBetweenMoves cfg (profile 1.0f 0.5f 1.0f))
                      (ManagerBrain.minutesBetweenMoves cfg (profile 1.0f 0.5f 0.0f))
                      "hotheads change again sooner")

              testCase "a coach who reads poorly makes the wrong storm or initiative call more often" (fun _ ->
                  // The match is level and the true momentum is nearly neutral: any storm or initiative call is a misjudgement.
                  let wrongCalls reading =
                      [ for seed in 1..600 do
                            let noise, _ = Rng.normal 0.0 1.0 (Rng.ofSeed (uint64 seed * 0x9E3779B97F4A7C15UL))
                            let decision = ManagerBrain.decide cfg (profile reading 0.9f 0.2f) { read 40 0 with Momentum = 0.1 } noise

                            match shiftOf decision with
                            | ValueSome ManagerShift.WeatherStorm
                            | ValueSome ManagerShift.TakeInitiative -> 1
                            | _ -> 0 ]
                      |> List.sum

                  let poor = wrongCalls 0.25f
                  let good = wrongCalls 0.95f
                  Expect.isGreaterThan poor 60 "a poor reader misjudges a quiet spell often"
                  Expect.isLessThan good 5 "a good reader hardly ever does"

                  // And when the siege is real, the poor reader misses it more.
                  let missed reading =
                      [ for seed in 1..600 do
                            let noise, _ = Rng.normal 0.0 1.0 (Rng.ofSeed (uint64 seed * 0x9E3779B97F4A7C15UL))
                            let decision = ManagerBrain.decide cfg (profile reading 0.5f 0.2f) { read 40 0 with Momentum = -0.5 } noise
                            if shiftOf decision = ValueSome ManagerShift.WeatherStorm then 0 else 1 ]
                      |> List.sum

                  Expect.isGreaterThan (missed 0.25f) (missed 0.95f + 40) "the poor reader sits through a siege more often")

              testCase "the coach returns to his plan when what moved him is gone" (fun _ ->
                  let coach = profile 1.0f 0.9f 0.0f

                  Expect.equal
                      (ManagerBrain.decide cfg coach { read 70 0 with Current = ValueSome ManagerShift.ChaseGame } 0.0 |> shiftOf)
                      (ValueSome ManagerShift.BackToPlan)
                      "he equalised, the chase is over"

                  Expect.equal
                      (ManagerBrain.decide cfg coach { read 70 -1 with Current = ValueSome ManagerShift.ChaseGame } 0.0 |> shiftOf)
                      ValueNone
                      "still behind, the chase goes on"

                  Expect.equal
                      (ManagerBrain.decide cfg coach { read 70 0 with Current = ValueSome ManagerShift.ProtectLead } 0.0 |> shiftOf)
                      (ValueSome ManagerShift.BackToPlan)
                      "he conceded, the lead is gone")

              testCase "a quiet spell cannot read as a siege" (fun _ ->
                  Expect.isLessThan (abs (ManagerBrain.momentum cfg 0.0 0.05)) 0.4 "a few hundredths of xG is no domination"
                  Expect.isGreaterThan (ManagerBrain.momentum cfg 0.0 1.5) -1.0 "the ratio stays inside its bounds"
                  Expect.isLessThan (ManagerBrain.momentum cfg 0.0 1.5) -0.8 "a real siege reads as one")

              ]

    let systemTests =
        testList
            "FootballEngine.Core.Systems.Manager.ManagerBrainSystem"
            [ testCase "an autonomous side that is losing chases: tactics move and the viewer is told why" (fun _ ->
                  let ctx = contextWith (profile 1.0f 0.9f 0.2f) ManagerProfile.human
                  let world = worldAt ctx (minuteTick 60) 2 0
                  let before = world.Away.Tactics
                  let events = step ctx world []

                  Expect.equal (shiftsOf ClubSide.Away events) [ ManagerShift.ChaseGame ] "the shift is announced"
                  Expect.hasLength (instructionsOf ClubSide.Away events) 1 "and paired with the instruction it caused"
                  Expect.isGreaterThan world.Away.Tactics.Mentality before.Mentality "he asks for more risk"
                  Expect.isGreaterThan world.Away.Tactics.DefensiveLine before.DefensiveLine "and a higher line"
                  Expect.equal world.Away.Directive.Kind DirectiveKind.Attack "the directive carries it"
                  Expect.equal world.Away.Brain.Shift (ValueSome ManagerShift.ChaseGame) "and the brain remembers"
                  Expect.isEmpty (shiftsOf ClubSide.Home events) "the commanded side is not touched"
                  Expect.isEmpty (instructionsOf ClubSide.Home events) "no instruction is made for it")

              testCase "one lone chance in a quiet spell is no storm, sustained pressure over a quarter of an hour is" (fun _ ->
                  // A sharp coach (reading 1.0) hears no noise, so only the decayed xG memory can move him; he is
                  // level and cautious, so no other rule competes with the storm.
                  let ctx = contextWith (profile 1.0f 0.2f 0.1f) ManagerProfile.human

                  let freshWorld () =
                      let world = worldAt ctx (minuteTick 30) 0 0
                      world.Away.Brain.LastReviewMinute <- 25
                      world

                  let lone = freshWorld ()
                  lone.Home.Stats.XgSum <- 0.1
                  Expect.isEmpty (step ctx lone []) "a single 0.1 xG effort after the settle minute is not domination"
                  Expect.equal lone.Away.Brain.Shift ValueNone "he did not dig in"

                  // Two reviews five minutes apart, the opponent creating real chances in between.
                  let siege = worldAt ctx (minuteTick 20) 0 0
                  siege.Away.Brain.LastReviewMinute <- 15
                  siege.Home.Stats.XgSum <- 0.15
                  let first = step ctx siege []
                  Expect.isEmpty (shiftsOf ClubSide.Away first) "after one modest chance he is still calm"

                  siege.State <- { siege.State with Tick = minuteTick 25; Clock = { siege.State.Clock with Tick = minuteTick 25 } }
                  siege.Home.Stats.XgSum <- 0.35
                  let second = step ctx siege []
                  Expect.equal (shiftsOf ClubSide.Away second) [ ManagerShift.WeatherStorm ] "the chances add up: he digs in"
                  Expect.hasLength (instructionsOf ClubSide.Away second) 1 "and the viewer is told")

              testCase "the shift is measured from the starting plan and undone when its cause goes" (fun _ ->
                  let ctx = contextWith (profile 1.0f 0.9f 0.2f) ManagerProfile.human
                  let world = worldAt ctx (minuteTick 60) 2 0
                  let plan = world.Away.Tactics
                  step ctx world [] |> ignore
                  world.State <- { world.State with Tick = minuteTick 70; Clock = { world.State.Clock with Tick = minuteTick 70 }; AwayScore = 2 }
                  let events = step ctx world []

                  Expect.equal (shiftsOf ClubSide.Away events) [ ManagerShift.BackToPlan ] "level again: back to the plan"
                  Expect.equal world.Away.Tactics plan "the starting plan is restored exactly"
                  Expect.equal world.Away.Brain.Shift ValueNone "and no shift is left active")

              testCase "a goal wakes a sharp coach at once and a blunt one only after his delay" (fun _ ->
                  let react reading =
                      let ctx = contextWith (profile reading 0.9f 0.1f) ManagerProfile.human
                      // 58:20 — not a minute boundary: only the goal can make him look now.
                      let tick = minuteTick 58 + 1200<subtick>
                      let world = worldAt ctx tick 2 0
                      world.Away.Brain.LastReviewMinute <- 57
                      step ctx world [ EventData.Goal(ClubSide.Home, None, None, 0.0) ]

                  Expect.equal (shiftsOf ClubSide.Away (react 1.0f)) [ ManagerShift.ChaseGame ] "the sharp coach reacts on the spot"
                  Expect.isEmpty (react 0.25f) "the blunt coach is still working it out")

              testCase "his own red card reshapes the team immediately" (fun _ ->
                  let ctx = contextWith (profile 1.0f 0.5f 0.5f) ManagerProfile.human
                  let world = worldAt ctx (minuteTick 40 + 600<subtick>) 0 0
                  let before = world.Away.Tactics
                  let events = step ctx world [ EventData.RedCardIssued(idOf ctx 3) ]

                  Expect.equal (shiftsOf ClubSide.Away events) [ ManagerShift.RedCardReshape ] "ten men change shape"
                  Expect.isLessThan world.Away.Tactics.Mentality before.Mentality "more cautious"
                  Expect.equal world.Away.Directive.Kind DirectiveKind.Defend "and defending")

              testCase "a red card for the other side is not his business" (fun _ ->
                  let ctx = contextWith (profile 0.6f 0.5f 0.5f) ManagerProfile.human
                  let world = worldAt ctx (minuteTick 40 + 600<subtick>) 0 0
                  let events = step ctx world [ EventData.RedCardIssued ctx.HomeLineup.Profiles.[3].Id ]
                  Expect.isEmpty events "nothing happens")

              testCase "the commanded side runs today's code: nothing on a goal, only the minute schedule" (fun _ ->
                  let ctx = contextWith ManagerProfile.human ManagerProfile.human
                  let world = worldAt ctx (minuteTick 70 + 600<subtick>) 0 1
                  Expect.isEmpty (step ctx world [ EventData.Goal(ClubSide.Away, None, None, 0.0) ]) "a goal tick between minutes changes nothing for him"

                  let atMinute = worldAt ctx (minuteTick 70) 0 1
                  let events = step ctx atMinute []
                  Expect.equal atMinute.Home.Directive.Kind DirectiveKind.Attack "the old late-game push still happens"
                  Expect.isEmpty (shiftsOf ClubSide.Home events) "silently, as it always did")

              testCase "a hothead who is losing at the break asks for a second change, a calm coach does not" (fun _ ->
                  let queued temperament =
                      let ctx = contextWith (profile 1.0f 0.5f temperament) ManagerProfile.human
                      let world = worldAt ctx (minuteTick 45 + 600<subtick>) 1 0
                      step ctx world [ EventData.HalfTime ] |> ignore
                      world.Away.Brain.QueuedChanges

                  Expect.equal (queued 1.0f) cfg.HotheadHalfTimeChanges "the hothead queues his changes"
                  Expect.equal (queued 0.2f) 0 "the calm coach waits for the second half")

              testCase "queued changes go out one at a time on the minute" (fun _ ->
                  let ctx = contextWith (profile 1.0f 0.5f 1.0f) ManagerProfile.human
                  let world = worldAt ctx (minuteTick 46) 1 0
                  world.Away.Brain.QueuedChanges <- 2
                  world.Away.Brain.QueuedIntent <- SubstitutionIntent.Attack
                  world.Away.Brain.LastReviewMinute <- 46
                  let events = step ctx world []

                  Expect.hasLength (substitutions events) 1 "one change now"
                  Expect.equal world.Away.Brain.QueuedChanges 1 "one still owed"

                  let _, inn = substitutions events |> List.head
                  let incomingRole = ctx.AwayLineup.Profiles |> Array.find (fun p -> p.Id = inn) |> _.Role
                  Expect.isTrue (incomingRole = SlotRole.Striker || incomingRole = SlotRole.Winger) "a forward comes on")

              testCase "a chase brings a forward on for a tired defensive player" (fun _ ->
                  let ctx = contextWith (profile 1.0f 0.9f 0.2f) ManagerProfile.human
                  let world = worldAt ctx (minuteTick 60) 2 0
                  world.Away.Frame.Condition.[5] <- 0.4f
                  let events = step ctx world []

                  match substitutions events |> List.tryFind (fun (out, _) -> out = idOf ctx 5) with
                  | Some(_, inn) ->
                      let role = ctx.AwayLineup.Profiles |> Array.find (fun p -> p.Id = inn) |> _.Role
                      Expect.isTrue (role = SlotRole.Striker || role = SlotRole.Winger || role = SlotRole.AttackingMid) "a forward comes on"
                  | None -> failtest "the tired holding midfielder should be the one to go")

              testCase "protecting a lead brings a defender on for a forward" (fun _ ->
                  let ctx = contextWith (profile 1.0f 0.5f 0.2f) ManagerProfile.human
                  let world = worldAt ctx (minuteTick 80) 0 1
                  let events = step ctx world []

                  Expect.equal (shiftsOf ClubSide.Away events) [ ManagerShift.ProtectLead ] "he protects the lead"

                  match substitutions events with
                  | [ out, inn ] ->
                      let roleOf id = ctx.AwayLineup.Profiles |> Array.find (fun p -> p.Id = id) |> _.Role
                      Expect.isTrue (List.contains (roleOf out) [ SlotRole.Striker; SlotRole.Winger; SlotRole.AttackingMid ]) "a forward goes off"
                      Expect.isTrue (List.contains (roleOf inn) [ SlotRole.Defender; SlotRole.FullBack; SlotRole.DefensiveMid ]) "a defender comes on"
                  | other -> failtestf "expected one change, got %A" other)

              testCase "a coach who reads the game replaces a tired player with someone who plays his role" (fun _ ->
                  let swap reading =
                      let ctx = contextWith (profile reading 0.5f 0.2f) ManagerProfile.human
                      // Bench slot 12 (winger) rates best on the crude score, slot 14 (holding midfielder) matches the role.
                      ctx.AwayLineup.Profiles.[12] <- { ctx.AwayLineup.Profiles.[12] with Pace = 99.0f; Dribbling = 99.0f; Finishing = 99.0f; Passing = 99.0f }
                      let world = worldAt ctx (minuteTick 60) 0 0
                      world.Away.Frame.Condition.[5] <- 0.3f

                      step ctx world []
                      |> substitutions
                      |> List.tryFind (fun (out, _) -> out = idOf ctx 5)
                      |> Option.map snd
                      |> Option.map (fun inn -> ctx.AwayLineup.Profiles |> Array.findIndex (fun p -> p.Id = inn))

                  Expect.equal (swap 0.9f) (Some 14) "the sharp coach brings the holding midfielder on"
                  Expect.equal (swap 0.2f) (Some 12) "the blunt coach brings the best-rated player on, whatever his role")

              testCase "a coach who reads the game takes a booked defender off late in the match" (fun _ ->
                  let ctx = contextWith (profile 0.9f 0.5f 0.2f) ManagerProfile.human
                  let world = worldAt ctx (minuteTick 65) 0 0
                  world.Away.Memory.[1].Yellows <- 1
                  let events = step ctx world []

                  match substitutions events with
                  | [ out, inn ] ->
                      Expect.equal out (idOf ctx 1) "the booked defender goes"
                      Expect.equal (ctx.AwayLineup.Profiles |> Array.find (fun p -> p.Id = inn) |> _.Role) SlotRole.Defender "a defender replaces him"
                      Expect.isEmpty (shiftsOf ClubSide.Away events) "a change of players is no change of plan"
                  | other -> failtestf "expected one change, got %A" other

                  let blunt = contextWith (profile 0.3f 0.5f 0.2f) ManagerProfile.human
                  let bluntWorld = worldAt blunt (minuteTick 65) 0 0
                  bluntWorld.Away.Memory.[1].Yellows <- 1
                  Expect.isEmpty (substitutions (step blunt bluntWorld [])) "a coach who reads poorly leaves him on")

              testCase "a shift is part of the event record and has its own kind" (fun _ ->
                  let ev: MatchEvent = { Tick = 5<subtick>; Data = EventData.ManagerShift(ClubSide.Away, ManagerShift.AllOut) }
                  Expect.equal (MatchEvent.kind ev) EventKind.ManagerShift "the scheduler can trigger on it"
                  Expect.isTrue (MatchEvent.isRecord ev) "it stays on the fixture record the viewer reads"

                  let ids =
                      [ ManagerShift.ChaseGame; ManagerShift.AllOut; ManagerShift.ProtectLead; ManagerShift.WeatherStorm
                        ManagerShift.TakeInitiative; ManagerShift.BackToPlan; ManagerShift.RedCardReshape ]
                      |> List.map (fun shift -> MatchEvent.id { ev with Data = EventData.ManagerShift(ClubSide.Away, shift) })
                      |> List.distinct

                  Expect.hasLength ids 7 "every shift hashes differently, so none is lost as a duplicate")

              testCase "the schedule wakes the manager on goals, red cards and the break" (fun _ ->
                  let manager = Scheduler.systems |> List.find (fun s -> s.Name = "ManagerSystem")

                  let wakes kind =
                      manager.Triggers
                      |> List.exists (function
                          | Trigger.OnEvent flags -> EventFlags.intersects flags (EventFlags.ofKind kind)
                          | _ -> false)

                  Expect.isTrue (wakes EventKind.Goal) "goal"
                  Expect.isTrue (wakes EventKind.RedCard) "red card"
                  Expect.isTrue (wakes EventKind.HalfTime) "half-time"
                  Expect.isFalse (wakes EventKind.Foul) "and nothing else") ]

    // ---- Whole matches ----

    let private strongHome =
        let weak = contextWith (profile 0.9f 0.9f 0.5f) ManagerProfile.human
        let boosted (l: Lineup) =
            { l with
                Profiles =
                    l.Profiles
                    |> Array.map (fun p ->
                        { p with
                            Pace = p.Pace + 25.0f
                            Passing = p.Passing + 25.0f
                            Finishing = p.Finishing + 25.0f
                            Dribbling = p.Dribbling + 25.0f
                            Tackling = p.Tackling + 25.0f
                            Positioning = p.Positioning + 25.0f }) }

        { weak with HomeLineup = boosted weak.HomeLineup }

    let private run (ctx: MatchContext) (seed: uint64) : MatchOutcome =
        let effective =
            match EffectiveConfig.fromBalance ctx.Config with
            | Ok effective -> effective
            | Error error -> failtestf "config: %A" error

        let input =
            { MatchId = 8UL
              Context = ctx
              Seed = seed
              EffectiveConfig = effective
              EngineVersion = "tests" }

        match MatchSession.create Scheduler.systems input MatchRunMode.Fast with
        | Error error -> failtestf "session: %A" error
        | Ok session ->
            match MatchSession.finish session with
            | Ok outcome -> outcome
            | Error error -> failtestf "match: %A" error

    /// Event hashes of four-seed matches played by the engine before rival managers existed, with both sides
    /// commanded and two tired starters each so the old manager acts. Recorded from the commit before this change.
    let private preChangeHashes = [ 1UL, 893044327239632373UL; 2UL, 1751300825762803254UL; 3UL, 12286449040674974461UL ]

    let matchTests =
        testList
            "FootballEngine.Core.Systems.Manager.ManagerBrainMatch"
            [ testCase "with both sides commanded the match is the match it was before rival managers existed" (fun _ ->
                  let commanded = contextWith ManagerProfile.human ManagerProfile.human

                  // The hashes predate the referee's leniency toward booked players; at 1.0 it is the old referee,
                  // so the comparison still isolates the manager path.
                  let ctx =
                      { commanded with
                          Config =
                              { commanded.Config with
                                  Referee =
                                      { commanded.Config.Referee with
                                          BookedLeniency = 1.0
                                          BookedRecklessLeniency = 1.0 } } }

                  for seed, expected in preChangeHashes do
                      let outcome = run ctx seed
                      Expect.equal outcome.EventsHash expected $"seed {seed}: the old path is untouched"

                      Expect.isFalse
                          (outcome.Events |> List.exists (fun ev -> MatchEvent.kind ev = EventKind.ManagerShift))
                          "and no shift is ever announced")

              testCase "an autonomous side that is losing pairs every instruction with a shift, the commanded side never gets one" (fun _ ->
                  let mutable shifts = 0

                  for seed in 1UL .. 3UL do
                      let outcome = run strongHome seed

                      let at kind side =
                          outcome.Events
                          |> List.choose (fun ev ->
                              match ev.Data with
                              | EventData.TeamInstruction s when kind = 0 && s = side -> Some ev.Tick
                              | EventData.ManagerShift(s, _) when kind = 1 && s = side -> Some ev.Tick
                              | _ -> None)

                      Expect.equal (at 0 ClubSide.Away) (at 1 ClubSide.Away) $"seed {seed}: each instruction has its shift at the same tick"
                      Expect.isEmpty (at 0 ClubSide.Home) "the commanded side received no instruction it did not give"
                      Expect.isEmpty (at 1 ClubSide.Home) "and no shift"
                      shifts <- shifts + (at 1 ClubSide.Away).Length

                  Expect.isGreaterThan shifts 0 "the autonomous coach did change his plan somewhere in the corpus") ]
