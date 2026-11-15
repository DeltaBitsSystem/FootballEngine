module OrganizedProbe

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

// These are initial conditions and observational cadence, not engine tuning.
let private seeds = [| 1UL; 2UL; 3UL |]
let private horizon = 120
let private cases = [| "open"; "deep"; "late"; "tired"; "lifecycle" |]

let private sha (bytes: byte[]) = Convert.ToHexString(SHA256.HashData bytes)
let private hashText (text: string) = Encoding.UTF8.GetBytes text |> sha
let private fileHash path = File.ReadAllBytes path |> sha
let private dist x y x2 y2 = sqrt ((x - x2) ** 2.0 + (y - y2) ** 2.0)
let private unwrap = function Ok value -> value | Error error -> failwithf "invalid fixture config: %A" error

[<Struct>]
type private PlayerPoint = { Id: PlayerId; Side: ClubSide; Slot: int; X: float; Y: float; Z: float }
type private Boundary =
    { Tick: int; BallX: float; BallY: float; BallZ: float; Control: string
      Carrier: PlayerId option; Players: PlayerPoint[] }

let private boundary (world: World) =
    let ball = world.State.Ball
    let carrier =
        match ball.Control with
        | BallControl.Controlled(_, id) | BallControl.Receiving(_, id, _) -> Some id
        | _ -> None
    let players =
        [| for side in [ ClubSide.Home; ClubSide.Away ] do
               let team = MatchWorld.team world side
               for slot in MatchWorld.activeSlots team.Frame do
                   yield { Id = team.RosterIds[slot]; Side = side; Slot = slot
                           X = float team.Frame.PosX[slot]; Y = float team.Frame.PosY[slot]; Z = float team.Frame.PosZ[slot] } |]
    { Tick = int world.State.Tick; BallX = float ball.Position.X; BallY = float ball.Position.Y
      BallZ = float ball.Position.Z; Control = string ball.Control; Carrier = carrier; Players = players }

let private actor = function
    | EventData.Possession(_, id) -> Some id
    | EventData.Dribble(id, _, _, _) -> Some id
    | EventData.Tackle(id, _) -> Some id
    | EventData.Interception(id, _) -> Some id
    | EventData.PhysicalContact record -> Some record.Actor
    | _ -> None

let private relevant = function
    | EventData.Possession _ | EventData.BallLoose _ | EventData.Dribble _ | EventData.Tackle _
    | EventData.Interception _ | EventData.Pass _ | EventData.FootContact _ | EventData.PhysicalContact _ -> true
    | _ -> false

let private eventId event = let (EventId id) = MatchEvent.id event in id
let private observationId (ObservationId id) = id

let private measurement (boundary: Boundary) player =
    let point = player |> Option.bind (fun id -> boundary.Players |> Array.tryFind (fun item -> item.Id = id))
    let carrier = boundary.Carrier |> Option.bind (fun id -> boundary.Players |> Array.tryFind (fun item -> item.Id = id))
    let nullable = function Some value -> Nullable value | None -> Nullable()
    let distance3 x y z x2 y2 z2 = sqrt ((x - x2) ** 2.0 + (y - y2) ** 2.0 + (z - z2) ** 2.0)
    {| BoundaryTick = boundary.Tick; Ball = [| boundary.BallX; boundary.BallY; boundary.BallZ |]; Control = boundary.Control
       Actor = player |> Option.map string |> Option.defaultValue ""; Carrier = boundary.Carrier |> Option.map string |> Option.defaultValue ""
       ActorPosition = point |> Option.map (fun p -> [| p.X; p.Y; p.Z |]) |> Option.defaultValue [||]
       CarrierPosition = carrier |> Option.map (fun p -> [| p.X; p.Y; p.Z |]) |> Option.defaultValue [||]
       ActorBallXY = point |> Option.map (fun p -> dist p.X p.Y boundary.BallX boundary.BallY) |> nullable
       ActorBallXYZ = point |> Option.map (fun p -> distance3 p.X p.Y p.Z boundary.BallX boundary.BallY boundary.BallZ) |> nullable
       CarrierBallXY = carrier |> Option.map (fun p -> dist p.X p.Y boundary.BallX boundary.BallY) |> nullable
       CarrierBallXYZ = carrier |> Option.map (fun p -> distance3 p.X p.Y p.Z boundary.BallX boundary.BallY boundary.BallZ) |> nullable
       ActorCarrierXY = match point, carrier with Some p, Some c -> Nullable(dist p.X p.Y c.X c.Y) | _ -> Nullable()
       ActorCarrierXYZ = match point, carrier with Some p, Some c -> Nullable(distance3 p.X p.Y p.Z c.X c.Y c.Z) | _ -> Nullable() |}

type private RecordedEvent =
    { Event: MatchEvent; BoundaryTick: int; Origin: string; Before: Boundary; After: Boundary; Roots: ObservationId[] }

let private runCase diagnostic seed side y scenario intensity =
    let original = TestBuilders.formationCtx ()
    let baseCtx = { original with AwayClub = { original.AwayClub with BaseTactics = original.HomeClub.BaseTactics } }
    let club = if side = ClubSide.Home then baseCtx.HomeClub else baseCtx.AwayClub
    let tactics = { club.BaseTactics with PressIntensity = intensity; PressTriggerZone = 1.0f }
    let ctx =
        if side = ClubSide.Home then
            { baseCtx with HomeClub = { club with BaseTactics = tactics } }
        else
            { baseCtx with AwayClub = { club with BaseTactics = tactics } }
    let world = MatchFactory.create ctx
    let team = MatchWorld.team world side
    let rivalSide = MatchWorld.oppOf side
    let rival = MatchWorld.team world rivalSide
    let dir = MatchMath.goalDirection side
    let positions =
        [| -48.0, 0.0; -24.0, -16.0; -27.0, -5.0; -27.0, 5.0; -24.0, 16.0
           -4.0, 0.0; -2.0, -4.0; -2.0, 4.0; 8.0, -18.0; -2.0, 0.0; 8.0, 18.0 |]
    for slot in 0 .. team.Frame.SlotCount - 1 do
        let x, py = positions[slot % positions.Length]
        let x = if scenario = "late" && slot > 0 then x - 24.0 else x
        team.Frame.PosX[slot] <- float32 (x * dir)
        team.Frame.PosY[slot] <- float32 (if slot = 0 then py else py + y)
        team.Frame.Condition[slot] <- if scenario = "tired" then 0.3f else 1.0f
    for slot in 0 .. rival.Frame.SlotCount - 1 do
        let x = if slot = 9 then 0.0 elif scenario = "deep" && slot = 8 then -45.0 else 28.0
        rival.Frame.PosX[slot] <- float32 (x * dir)
        rival.Frame.PosY[slot] <- float32 (if slot = 9 then y elif slot = 8 && scenario = "deep" then 0.0 else snd positions[slot % positions.Length])
    world.State <-
        { world.State with
            Flow = MatchFlow.Live
            Ball = { TestBuilders.emptyBall with Position = { TestBuilders.emptyBall.Position with X = 0.0<meter>; Y = y * 1.0<meter> } } }
    let initialGeometry =
        JsonSerializer.Serialize(
            {| TeamX = team.Frame.PosX; TeamY = team.Frame.PosY; TeamVx = team.Frame.VelX; TeamVy = team.Frame.VelY
               TeamCondition = team.Frame.Condition; RivalX = rival.Frame.PosX; RivalY = rival.Frame.PosY
               RivalVx = rival.Frame.VelX; RivalVy = rival.Frame.VelY; RivalCondition = rival.Frame.Condition
               BallX = float world.State.Ball.Position.X; BallY = float world.State.Ball.Position.Y |})
        |> hashText
    let effective = EffectiveConfig.fromBalance ctx.Config |> unwrap
    let input = { MatchId = seed; Context = ctx; Seed = seed; EffectiveConfig = effective; EngineVersion = "organized-sensitivity-v1" }
    let transitionCfg = ctx.Config.DefensiveTransition
    let rest1, rest2 = TransitionGeometry.restDefenders world side
    let eligible slot =
        slot <> rest1 && slot <> rest2
        && float team.Frame.Condition[slot] >= transitionCfg.MinimumPressCondition
        && float team.Frame.PosX[slot] * dir <= float world.State.Ball.Position.X * dir + float transitionCfg.BeatenMargin
    let eligibleCount = MatchWorld.activeSlots team.Frame |> Array.filter (fun slot -> TransitionGeometry.outfielder team.Frame slot && eligible slot) |> Array.length
    let primaryCandidate, primaryArrival =
        TransitionGeometry.choose ctx team.Frame (TransitionGeometry.pressPoint ctx world side) (eligible >> not) None transitionCfg.HandoffAdvantage
    let coverCandidate, coverArrival =
        TransitionGeometry.chooseFor transitionCfg.CoverEffort ctx team.Frame (TransitionGeometry.coverPoint ctx world side)
            (fun slot -> not (eligible slot) || Some slot = primaryCandidate) None transitionCfg.HandoffAdvantage
    let depthRunners =
        if rest2 < 0 then 0
        else
            MatchWorld.activeSlots rival.Frame
            |> Array.filter (fun slot ->
                TransitionGeometry.outfielder rival.Frame slot
                && float rival.Frame.PosX[slot] * dir < float team.Frame.PosX[rest2] * dir - float transitionCfg.DepthSafety
                && abs (float rival.Frame.PosY[slot]) <= float transitionCfg.CentralHalfWidth)
            |> Array.length
    let arrivalValue value =
        if Double.IsFinite(float value) && float value < Double.MaxValue then Nullable(float value) else Nullable()
    let viability =
        {| Stage = "initial fixture before scheduler; candidates are not commitments"; RestSlots = [| rest1; rest2 |]
           EligibleCount = eligibleCount; PrimaryCandidate = defaultArg primaryCandidate -1; PrimaryArrivalSeconds = arrivalValue primaryArrival
           CoverCandidate = defaultArg coverCandidate -1; CoverArrivalSeconds = arrivalValue coverArrival
           PrimaryWindowSeconds = float transitionCfg.LocalArrivalWindow; CoverWindowSeconds = float transitionCfg.CoverArrivalWindow
           MinimumPressCondition = transitionCfg.MinimumPressCondition; MinimumPressIntensity = transitionCfg.MinimumPressIntensity
           PressGateX = TeamDecide.pressGate team.Tactics; BallTeamX = float world.State.Ball.Position.X * dir
           DepthRunners = depthRunners |}
    let buffer: EventBuffer = { Events = ResizeArray(); Applied = ResizeArray(); Flags = { Bits = 0UL } }
    // Initial possession is an explicit fixture input reduced by the real tick entry, not an inferred outcome.
    let initialEvent = { Tick = 0<subtick>; Data = EventData.Possession(rivalSide, rival.RosterIds[9]) }
    buffer.Events.Add initialEvent
    let fixtureEvents = System.Collections.Generic.HashSet<uint64>()
    fixtureEvents.Add(eventId initialEvent) |> ignore
    let queuedAt = System.Collections.Generic.Dictionary<uint64, ResizeArray<int>>()
    let proposals = ResizeArray<RecordedEvent>()
    let systemProposals = ResizeArray<RecordedEvent>()
    let commits = ResizeArray<RecordedEvent>()
    let mutable observationCursor = 0UL
    let fixture event =
        fixtureEvents.Add(eventId event) |> ignore
        buffer.Events.Add event
    let instrument (spec: SystemSpec) =
        { spec with
            Step = fun system rng world buffer ->
                let before = boundary world
                let firstEvent = buffer.Events.Count
                let next = spec.Step system rng world buffer
                if buffer.Events.Count > firstEvent then
                    let after = boundary world
                    for index in firstEvent .. buffer.Events.Count - 1 do
                        let event = buffer.Events[index]
                        if relevant event.Data then
                            systemProposals.Add
                                { Event = event; BoundaryTick = before.Tick; Origin = spec.Name
                                  Before = before; After = after; Roots = [||] }
                next }
    let schedule =
        if diagnostic then Scheduler.systems |> List.map instrument |> ScheduleCompiler.compile
        else ScheduleCompiler.compile Scheduler.systems
    let sctx = { Ctx = ctx; Clock = SimulationClock.defaultClock }
    let output = Array.zeroCreate<MatchEvent> 256
    let mutable rng = Rng.ofSeed seed
    let mutable firstPrimary, firstCover, firstTick, firstReach = -1, -1, -1, -1
    let mutable trackingFirst = false
    let mutable firstX, firstY, startDistance, minDistance = 0.0, 0.0, -1.0, Double.PositiveInfinity
    let mutable primaryChanges, coverChanges = 0, 0
    let mutable previousPrimary, previousCover = -1, -1
    let mutable path = 0.0
    let slotPaths = Array.zeroCreate<float> team.Frame.SlotCount
    let mutable duplicatePrimaryTicks, duplicateCoverTicks, duplicateOutletTicks = 0, 0, 0
    let mutable pressWithoutCoverTicks, pressWithoutDepthTicks = 0, 0
    let mutable minProtectors, maxProtectors = Int32.MaxValue, 0
    let mutable looseCleared, looseValid, recoveryCleared, restartCleared, lossStarted, lossEligible = false, false, false, false, false, false
    let mutable looseTransition = "not-sampled"
    let trace = ResizeArray<_>()
    let previousX = Array.copy team.Frame.PosX
    let previousY = Array.copy team.Frame.PosY
    let events = ResizeArray<MatchEvent>()
    for step in 0 .. horizon - 1 do
        if scenario = "lifecycle" then
            let tick = world.State.Tick
            if step = 10 then
                fixture { Tick = tick; Data = EventData.BallLoose(world.State.Ball.Position.X, world.State.Ball.Position.Y) }
            elif step = 1 then
                lossEligible <- world.State.Possession.LastGainer = Some rivalSide
                fixture { Tick = tick; Data = EventData.Possession(side, team.RosterIds[9]) }
            elif step = 20 then
                fixture { Tick = tick; Data = EventData.SetPiece(SetPieceKind.ThrowIn, side) }
        let before = if diagnostic then Some(boundary world) else None
        let struct (next, _, count) = Stepper.stepInto schedule sctx rng world buffer output
        rng <- next
        for index in 0 .. count - 1 do events.Add output[index]
        if diagnostic then
            let after = boundary world
            let before = before.Value
            let newObservations = world.Observation.SnapshotSince observationCursor
            observationCursor <- newObservations.Total
            for index in 0 .. count - 1 do
                let event = output[index]
                if relevant event.Data then
                    let id = eventId event
                    let roots =
                        newObservations.Entries |> Array.choose (fun entry ->
                            match entry.Data with
                            | EventCommitted fact when fact = event -> Some entry.Id
                            | _ -> None)
                    let hasFixture = fixtureEvents.Contains id
                    let hasProposal = queuedAt.ContainsKey id
                    let origin =
                        if hasFixture && hasProposal then "ambiguous-fixture-and-runtime-event-id"
                        elif hasFixture then "fixture-scripted"
                        elif hasProposal then "runtime-queued"
                        else "runtime-immediate-or-unobserved-queue"
                    commits.Add { Event = event; BoundaryTick = before.Tick; Origin = origin; Before = before; After = after; Roots = roots }
            for event in buffer.Events do
                if relevant event.Data then
                    let id = eventId event
                    let times =
                        match queuedAt.TryGetValue id with
                        | true, value -> value
                        | _ ->
                            let value = ResizeArray<int>()
                            queuedAt[id] <- value
                            value
                    times.Add before.Tick
                    proposals.Add { Event = event; BoundaryTick = before.Tick; Origin = "runtime-queued"; Before = before; After = after; Roots = [||] }
        let mutable primary, cover, primaryCount, coverCount, protectors = -1, -1, 0, 0, 0
        let outlets = System.Collections.Generic.HashSet<PlayerId>()
        let protectorDetails = ResizeArray<_>()
        let screenDetails = ResizeArray<_>()
        let mutable duplicateOutlet = false
        let mutable depthX = Double.PositiveInfinity
        for slot in MatchWorld.activeSlots team.Frame do
            let x, py = float team.Frame.PosX[slot], float team.Frame.PosY[slot]
            let movement = dist x py (float previousX[slot]) (float previousY[slot])
            path <- path + movement
            slotPaths[slot] <- slotPaths[slot] + movement
            previousX[slot] <- team.Frame.PosX[slot]
            previousY[slot] <- team.Frame.PosY[slot]
            match team.Defense.Tasks[slot] with
            | ValueSome task ->
                match task.Role with
                | DefensiveRole.FirstDefender -> primary <- slot; primaryCount <- primaryCount + 1
                | DefensiveRole.Cover -> cover <- slot; coverCount <- coverCount + 1
                | DefensiveRole.ProtectDepth ->
                    protectors <- protectors + 1
                    depthX <- min depthX (float task.Target.X * dir)
                    let opponentSlot = task.Opponent |> ValueOption.toOption |> Option.bind (fun id -> rival.RosterIds |> Array.tryFindIndex ((=) id))
                    let relativeDepth = opponentSlot |> Option.map (fun index -> float task.Target.X * dir - float rival.Frame.PosX[index] * dir)
                    protectorDetails.Add {| Slot = slot; Player = string team.RosterIds[slot]; Opponent = string task.Opponent
                                            TargetX = float task.Target.X; TargetY = float task.Target.Y
                                            TargetRelativeToOpponent = match relativeDepth with Some value -> Nullable value | None -> Nullable() |}
                | _ -> ()
                if task.Role = DefensiveRole.Screen then
                    screenDetails.Add {| Slot = slot; Player = string team.RosterIds[slot]; Outlet = string task.Opponent |}
                    match task.Opponent with
                    | ValueSome id when not (outlets.Add id) -> duplicateOutlet <- true
                    | _ -> ()
            | ValueNone -> ()
        if primaryCount > 1 then duplicatePrimaryTicks <- duplicatePrimaryTicks + 1
        if coverCount > 1 then duplicateCoverTicks <- duplicateCoverTicks + 1
        if duplicateOutlet then duplicateOutletTicks <- duplicateOutletTicks + 1
        if primary >= 0 && cover < 0 then pressWithoutCoverTicks <- pressWithoutCoverTicks + 1
        if primary >= 0 && protectors < 2 then pressWithoutDepthTicks <- pressWithoutDepthTicks + 1
        if primary <> previousPrimary then primaryChanges <- primaryChanges + 1; previousPrimary <- primary
        if cover <> previousCover then coverChanges <- coverChanges + 1; previousCover <- cover
        if primary >= 0 && firstPrimary < 0 then
            firstPrimary <- primary
            firstCover <- cover
            firstTick <- step
            trackingFirst <- true
            let target = team.Defense.Tasks[primary].Value.Target
            firstX <- float target.X
            firstY <- float target.Y
            startDistance <- dist (float team.Frame.PosX[primary]) (float team.Frame.PosY[primary]) firstX firstY
        if trackingFirst && primary <> firstPrimary then trackingFirst <- false
        if trackingFirst then
            let distance = dist (float team.Frame.PosX[firstPrimary]) (float team.Frame.PosY[firstPrimary]) firstX firstY
            minDistance <- min minDistance distance
            let ball = world.State.Ball.Position
            let toBall = dist (float team.Frame.PosX[firstPrimary]) (float team.Frame.PosY[firstPrimary]) (float ball.X) (float ball.Y)
            if firstReach < 0 && toBall <= float ctx.Config.Ball.FootReach then firstReach <- step
        if primary >= 0 then
            minProtectors <- min minProtectors protectors
            maxProtectors <- max maxProtectors protectors
        let tasksEmpty = team.Defense.Tasks |> Array.forall _.IsNone
        if scenario = "lifecycle" && step = 10 then
            looseCleared <- tasksEmpty
            match team.Defense.Episode with
            | Some episode when episode.EndedAt.IsNone ->
                looseTransition <- string episode.Mode
                looseValid <-
                    (if primary < 0 then episode.Primary.IsNone else episode.Primary = Some primary)
                    && (if cover < 0 then episode.Cover.IsNone else episode.Cover = Some cover)
            | _ ->
                looseTransition <- "none"
                looseValid <- tasksEmpty
        if scenario = "lifecycle" && step = 1 then
            recoveryCleared <- tasksEmpty
            lossStarted <- rival.Defense.Episode |> Option.exists (fun episode -> episode.EndedAt.IsNone)
        if scenario = "lifecycle" && step = 20 then restartCleared <- tasksEmpty
        let ball = world.State.Ball.Position
        let mutable deepestOpponent = Double.PositiveInfinity
        for slot in MatchWorld.activeSlots rival.Frame do
            if TransitionGeometry.outfielder rival.Frame slot then deepestOpponent <- min deepestOpponent (float rival.Frame.PosX[slot] * dir)
        trace.Add {| Tick = step; Primary = primary; Cover = cover; Protectors = protectors
                     ProtectorDetails = protectorDetails.ToArray(); Screens = screenDetails.ToArray()
                     Transition = (match team.Defense.Episode with Some e when e.EndedAt.IsNone -> string e.Mode | _ -> "none")
                     DeepestTarget = if Double.IsInfinity depthX then Nullable() else Nullable depthX
                     DeepestOpponent = if Double.IsInfinity deepestOpponent then Nullable() else Nullable deepestOpponent
                     BallX = float ball.X; BallY = float ball.Y; BallControl = string world.State.Ball.Control
                     PrimaryX = if primary < 0 then Nullable() else Nullable(float team.Frame.PosX[primary])
                     PrimaryY = if primary < 0 then Nullable() else Nullable(float team.Frame.PosY[primary])
                     PrimaryTargetX = if primary < 0 then Nullable() else Nullable(float team.Defense.Tasks[primary].Value.Target.X)
                     PrimaryTargetY = if primary < 0 then Nullable() else Nullable(float team.Defense.Tasks[primary].Value.Target.Y) |}
    let snapshot = world.Observation.Snapshot()
    let audit = if diagnostic then Some(Observation.audit snapshot) else None
    let evidence =
        snapshot.Entries |> Array.choose (fun entry ->
            match entry.Data with
            | OrganizedDefenseEvaluated value when value.Side = side ->
                Some {| Tick = int entry.Tick; Kind = "organized"; Episode = ""; Cause = string entry.Cause; Reason = string value.Reason; Primary = string value.Primary; Cover = string value.Cover |}
            | TransitionEvaluated value when value.Side = side ->
                Some {| Tick = int entry.Tick; Kind = "transition"; Episode = string value.Episode; Cause = string entry.Cause; Reason = string value.Reason; Primary = string value.Primary; Cover = string value.Cover |}
            | _ -> None)
    let contactObservations =
        if diagnostic then
            snapshot.Entries
            |> Array.choose (fun entry ->
                match entry.Data with
                | EventCommitted { Data = EventData.PhysicalContact value } ->
                    Some {| Id = observationId entry.Id; Tick = int entry.Tick; Cause = string entry.Cause
                            Actor = string value.Actor; Other = value.Other |> Option.map string |> Option.defaultValue ""
                            Action = value.Action |> Option.map string |> Option.defaultValue ""; BallTouched = value.BallTouched
                            Surface = string value.SurfaceA; ControlBefore = string value.ControlBefore.Kind
                            ControlAfter = string value.ControlAfter.Kind; NormalImpulse = value.NormalImpulse
                            TangentialImpulse = value.TangentialImpulse |}
                | _ -> None)
        else
            [||]
    let chain root =
        match Observation.chain root snapshot with
        | Error error -> {| Root = observationId root; Error = error; Missing = [||]; Cycles = [||]; Future = [||]; Nodes = [||] |}
        | Ok result ->
            let nodes =
                result.Entries |> Array.map (fun entry ->
                    let contact =
                        match entry.Data with
                        | EventCommitted { Data = EventData.PhysicalContact value } -> Some value
                        | _ -> None

                    {| Id = observationId entry.Id; Tick = int entry.Tick; Cause = string entry.Cause
                       Kind = (match entry.Data with EventCommitted { Data = EventData.PhysicalContact _ } -> "PhysicalContact" | DecisionChosen _ -> "DecisionChosen" | EventCommitted _ -> "EventCommitted" | _ -> "other")
                       Text = Observation.explain entry
                       ContactActor = contact |> Option.map (fun value -> string value.Actor) |> Option.defaultValue ""
                       ContactOther = contact |> Option.bind (fun value -> value.Other) |> Option.map string |> Option.defaultValue ""
                       ContactAction = contact |> Option.bind (fun value -> value.Action) |> Option.map string |> Option.defaultValue ""
                       ContactBallTouched = match contact with Some value -> Nullable value.BallTouched | None -> Nullable()
                       ContactSurface = contact |> Option.map (fun value -> string value.SurfaceA) |> Option.defaultValue ""
                       ContactControlBefore = contact |> Option.map (fun value -> string value.ControlBefore.Kind) |> Option.defaultValue ""
                       ContactControlAfter = contact |> Option.map (fun value -> string value.ControlAfter.Kind) |> Option.defaultValue "" |})
            {| Root = observationId root; Error = ""; Missing = result.MissingCauses |> Array.map string
               Cycles = result.Cycles |> Array.map observationId
               Future = result.FutureCauses |> Array.map (fun (child, source) -> sprintf "%d->%d" (observationId child) (observationId source))
               Nodes = nodes |}
    let proposalData (record: RecordedEvent) =
        let event = record.Event
        {| EventId = eventId event; EventTick = int event.Tick; QueuedAfterStepTick = record.BoundaryTick
           Source = record.Origin; Data = sprintf "%A" event.Data
           BeforeStep = measurement record.Before (actor event.Data); AfterStep = measurement record.After (actor event.Data) |}
    let systemProposalData (record: RecordedEvent) =
        let event = record.Event
        {| EventId = eventId event; EventTick = int event.Tick; SystemTick = record.BoundaryTick
           System = record.Origin; Data = sprintf "%A" event.Data
           BeforeSystem = measurement record.Before (actor event.Data); AfterSystem = measurement record.After (actor event.Data) |}
    let committedData (record: RecordedEvent) =
        let event = record.Event
        let sameProposals = proposals |> Seq.filter (fun candidate -> eventId candidate.Event = eventId event) |> Seq.map proposalData |> Seq.toArray
        {| EventId = eventId event; EventTick = int event.Tick; DrainedAtStepTick = record.BoundaryTick
           Source = record.Origin; Data = sprintf "%A" event.Data
           BeforeDrainStep = measurement record.Before (actor event.Data); AfterDrainStep = measurement record.After (actor event.Data)
           ProposalMatchesByEventId = sameProposals; ObservationRoots = record.Roots |> Array.map observationId
           SystemProposalMatchesByEventId = systemProposals |> Seq.filter (fun candidate -> eventId candidate.Event = eventId event) |> Seq.map systemProposalData |> Seq.toArray
           Chains = record.Roots |> Array.map chain |}
    {| Seed = seed; Side = string side; BallY = y; Scenario = scenario; PressIntensity = intensity
       ConfigHash = hashText effective.Content; InputHash = MatchJob.inputHash input; InitialGeometryHash = initialGeometry
       TacticsHash = hashText (sprintf "%A" tactics)
       InitialViability = viability
       InitialPossession = string rivalSide; ScriptedEvents = if scenario = "lifecycle" then [| "1:Possession"; "10:BallLoose"; "20:ThrowIn" |] else [||]
       FootReach = float ctx.Config.Ball.FootReach; FootContactHeight = float ctx.Config.Ball.FootContactHeight
       FirstPrimary = firstPrimary; FirstCover = firstCover; FirstPressTick = firstTick
       FirstPrimaryId = if firstPrimary < 0 then "" else string team.RosterIds[firstPrimary]
       FirstCoverId = if firstCover < 0 then "" else string team.RosterIds[firstCover]
       FirstReachTick = firstReach; FirstTargetX = firstX; FirstTargetY = firstY; InitialTargetDistance = startDistance
       MinimumTargetDistance = if Double.IsInfinity minDistance then -1.0 else minDistance
       TeamPathMeters = path; FirstPrimaryTotalPathMeters = if firstPrimary < 0 then -1.0 else slotPaths[firstPrimary]
       PrimaryChanges = primaryChanges; CoverChanges = coverChanges
       DuplicatePrimaryTicks = duplicatePrimaryTicks; DuplicateCoverTicks = duplicateCoverTicks; DuplicateOutletTicks = duplicateOutletTicks
       PressWithoutCoverTicks = pressWithoutCoverTicks; PressWithoutDepthTicks = pressWithoutDepthTicks
       MinProtectorsWhilePressing = if minProtectors = Int32.MaxValue then 0 else minProtectors
       MaxProtectorsWhilePressing = maxProtectors; LooseCleared = looseCleared; LooseValid = looseValid; LooseTransition = looseTransition
       RecoveryCleared = recoveryCleared; LossEligible = lossEligible; LossStarted = lossStarted; RestartCleared = restartCleared
       Evidence = evidence; ObservationTotal = snapshot.Total; ObservationEvicted = snapshot.Evicted; ObservationUnlinked = snapshot.Unlinked
       AuditMissing = audit |> Option.map (fun value -> value.MissingCauses.Length) |> Option.defaultValue 0
       AuditAmbiguous = audit |> Option.map (fun value -> value.AmbiguousCauses.Length) |> Option.defaultValue 0
       AuditDuplicateOccurrences = audit |> Option.map (fun value -> value.DuplicateOccurrences.Length) |> Option.defaultValue 0
       AuditFuture = audit |> Option.map (fun value -> value.FutureCauses.Length) |> Option.defaultValue 0
       AuditMissingLinks = audit |> Option.map (fun value -> value.MissingCauses |> Array.map (fun (id, cause) -> {| Observation = observationId id; Cause = string cause |})) |> Option.defaultValue [||]
       AuditAmbiguousLinks = audit |> Option.map (fun value -> value.AmbiguousCauses |> Array.map (fun (id, cause) -> {| Observation = observationId id; Cause = string cause |})) |> Option.defaultValue [||]
       EventsHash = MatchOutcomeHash.eventsHash (List.ofSeq events); FinalRng = string rng
       Diagnostic = diagnostic
       ContactObservations = contactObservations
       ControlEvents = if diagnostic then commits |> Seq.map committedData |> Seq.toArray else [||]
       QueuedEvents = if diagnostic then proposals |> Seq.map proposalData |> Seq.toArray else [||]
       SystemQueuedEvents = if diagnostic then systemProposals |> Seq.map systemProposalData |> Seq.toArray else [||]
       SportingEvents = events |> Seq.choose (fun event ->
           match event.Data with
           | EventData.Possession _ | EventData.BallLoose _ | EventData.Pass _ | EventData.Shot _ | EventData.Dribble _ | EventData.Tackle _ | EventData.Interception _
           | EventData.SetPiece _ | EventData.DefensiveTransition _ -> Some {| Tick = int event.Tick; Data = sprintf "%A" event.Data |}
           | _ -> None) |> Seq.toArray
       Trace = trace.ToArray() |}

let run (argv: string[]) =
    let output = if argv.Length > 0 then argv[0] else "docs/evidence/collective-intelligence/organized-sensitivity.json"
    if File.Exists output then failwithf "refusing to overwrite existing evidence: %s" output
    let rows =
        [| for seed in seeds do
               for side in [ ClubSide.Home; ClubSide.Away ] do
                   for y in [ 0.0; 18.0 ] do
                       for scenario in cases do
                           for intensity in [ 0.0f; 1.0f ] do
                               yield runCase false seed side y scenario intensity |]
    let failures = ResizeArray<string>()
    for row in rows do
        if row.DuplicatePrimaryTicks <> 0 || row.DuplicateCoverTicks <> 0 || row.DuplicateOutletTicks <> 0 then
            failures.Add(sprintf "%d %s %g %s %g duplicate duties" row.Seed row.Side row.BallY row.Scenario row.PressIntensity)
        if row.PressWithoutCoverTicks <> 0 || row.PressWithoutDepthTicks <> 0 then
            failures.Add(sprintf "%d %s %g %s %g press without cover/depth: %d/%d" row.Seed row.Side row.BallY row.Scenario row.PressIntensity row.PressWithoutCoverTicks row.PressWithoutDepthTicks)
        if row.ObservationEvicted <> 0UL then
            failures.Add(sprintf "%d %s %g %s %g observation eviction" row.Seed row.Side row.BallY row.Scenario row.PressIntensity)
        if row.Scenario = "lifecycle" && not (row.LooseValid && row.RecoveryCleared && row.RestartCleared) then
            failures.Add(sprintf "%d %s %g lifecycle %g cleanup: loose=%b transition=%s recovery=%b restart=%b" row.Seed row.Side row.BallY row.PressIntensity row.LooseValid row.LooseTransition row.RecoveryCleared row.RestartCleared)
        if row.Scenario = "lifecycle" && (not row.LossEligible || not row.LossStarted) then
            failures.Add(sprintf "%d %s %g lifecycle %g possession: prior rival=%b loss episode=%b" row.Seed row.Side row.BallY row.PressIntensity row.LossEligible row.LossStarted)
    let find seed side y scenario intensity =
        rows |> Array.find (fun row -> row.Seed = seed && row.Side = side && row.BallY = y && row.Scenario = scenario && row.PressIntensity = intensity)
    let paired =
        [| for seed in seeds do
               for side in [ "Home"; "Away" ] do
                   for y in [ 0.0; 18.0 ] do
                       for scenario in cases do
                           let low = find seed side y scenario 0.0f
                           let high = find seed side y scenario 1.0f
                           if low.ConfigHash <> high.ConfigHash || low.InitialGeometryHash <> high.InitialGeometryHash then
                               failures.Add(sprintf "%d %s %g %s: pair has different config or initial geometry" seed side y scenario)
                           yield {| Seed = seed; Side = side; BallY = y; Scenario = scenario
                                    LowInputHash = low.InputHash; HighInputHash = high.InputHash
                                    LowFirstPressTick = low.FirstPressTick; HighFirstPressTick = high.FirstPressTick
                                    LowFirstReachTick = low.FirstReachTick; HighFirstReachTick = high.FirstReachTick
                                    LowTeamPathMeters = low.TeamPathMeters; HighTeamPathMeters = high.TeamPathMeters
                                    LowPrimaryChanges = low.PrimaryChanges; HighPrimaryChanges = high.PrimaryChanges
                                    LowCoverChanges = low.CoverChanges; HighCoverChanges = high.CoverChanges |} |]
    for seed in seeds do
        for side in [ "Home"; "Away" ] do
            for y in [ 0.0; 18.0 ] do
                let restrained = find seed side y "open" 0.0f
                let active = find seed side y "open" 1.0f
                if restrained.Trace[0].Primary >= 0 || active.FirstPressTick <> 0 || active.FirstCover < 0 then
                    failures.Add(sprintf "%d %s %g open: tactical press/cover did not differ at initial evaluation" seed side y)
                if active.InitialTargetDistance < 0.0 || active.MinimumTargetDistance >= active.InitialTargetDistance then
                    failures.Add(sprintf "%d %s %g open: assigned primary did not physically approach its original target while assigned" seed side y)
                if active.MinProtectorsWhilePressing < 2 then
                    failures.Add(sprintf "%d %s %g open: pressing lost a depth protector" seed side y)
                let deep = find seed side y "deep" 1.0f
                if deep.Trace[0].Primary >= 0 || not (deep.Evidence |> Array.exists (fun item -> item.Tick = 0 && item.Reason = "OrganizedDepthThreat")) then
                    failures.Add(sprintf "%d %s %g deep: initial depth threat did not inhibit pressure" seed side y)
                for scenario in [ "late"; "tired" ] do
                    let constrained = find seed side y scenario 1.0f
                    if constrained.Trace[0].Primary >= 0 || not (constrained.Evidence |> Array.exists (fun item -> item.Tick = 0 && item.Reason = "OrganizedUnavailable")) then
                        failures.Add(sprintf "%d %s %g %s: initial constraint did not prevent pressure" seed side y scenario)
        for y in [ 0.0; 18.0 ] do
            let home = find seed "Home" y "open" 1.0f
            let away = find seed "Away" y "open" 1.0f
            if home.FirstPrimary <> away.FirstPrimary || home.FirstCover <> away.FirstCover
               || abs (home.FirstTargetX + away.FirstTargetX) > 0.001
               || abs (home.FirstTargetY - away.FirstTargetY) > 0.001 then
                failures.Add(sprintf "%d %g open: initial mirror responsibility/target mismatch" seed y)
            for tick in 0 .. 3 do
                let h, a = home.Trace[tick], away.Trace[tick]
                if h.Primary <> home.FirstPrimary || a.Primary <> away.FirstPrimary
                   || not h.PrimaryX.HasValue || not a.PrimaryX.HasValue
                   || not h.PrimaryY.HasValue || not a.PrimaryY.HasValue
                   || abs (h.PrimaryX.GetValueOrDefault() + a.PrimaryX.GetValueOrDefault()) > 0.001
                   || abs (h.PrimaryY.GetValueOrDefault() - a.PrimaryY.GetValueOrDefault()) > 0.001 then
                    failures.Add(sprintf "%d %g open: mirror movement differs at tick %d" seed y tick)
    let root = Directory.GetCurrentDirectory()
    let source =
        [| "Tools/CollectiveIntelligence/OrganizedProbe.fs"; "Tools/CollectiveIntelligence/PairedProbe.fs"; "Tools/CollectiveIntelligence/CollectiveIntelligence.fsproj"
           "FootballEngine.Core.Tests/Infrastructure/TestBuilders.fs"; "FootballEngine.Core/Match/Systems/Team/Position.fs"
           "FootballEngine.Core/Match/Systems/Team/DefensiveTransition.fs"; "FootballEngine.Core/Match/Systems/Team/DefenseLine.fs"
           "FootballEngine.Core/Match/Systems/Players/Intent.fs"; "FootballEngine.Core/Match/Systems/Players/Steering.fs"
           "FootballEngine.Core/Match/Stepper/EventApply.fs"; "FootballEngine.Core/Match/Stepper/Stepper.fs"; "FootballEngine.Core/Match/Stepper/SystemsTable.fs" |]
    let payload =
        {| Format = 1; Interpretation = "Tactical sensitivity, not implementation isolation: PressIntensity can affect multiple systems. Identical seeds do not guarantee identical trajectories. Initial possession and lifecycle inputs are explicit scripted events, not inferred causes."
           Command = "dotnet run --project Tools/CollectiveIntelligence/CollectiveIntelligence.fsproj -c Release -- organized <output>"
           Seeds = seeds; HorizonSubticks = horizon; Source = source |> Array.map (fun path -> {| Path = path; Sha256 = fileHash (Path.Combine(root, path)) |})
           AssemblySha256 = fileHash typeof<World>.Assembly.Location; Failures = failures.ToArray(); Pairs = paired; Cases = rows |}
    let directory = Path.GetDirectoryName output
    if not (String.IsNullOrWhiteSpace directory) then Directory.CreateDirectory directory |> ignore
    File.WriteAllText(output, JsonSerializer.Serialize(payload, JsonSerializerOptions(WriteIndented = true)))
    printfn "Organized sensitivity: %d runs, %d failures; %s" rows.Length failures.Count output
    for scenario in cases do
        for intensity in [ 0.0f; 1.0f ] do
            let subset = rows |> Array.filter (fun row -> row.Scenario = scenario && row.PressIntensity = intensity)
            let press = subset |> Array.filter (fun row -> row.FirstPressTick >= 0) |> Array.length
            let reach = subset |> Array.filter (fun row -> row.FirstReachTick >= 0) |> Array.length
            let approach = subset |> Array.filter (fun row -> row.FirstPressTick >= 0 && row.MinimumTargetDistance < row.InitialTargetDistance) |> Array.length
            printfn "%-9s intensity %.0f: %d runs, press %d, physical approach %d, first-assignment foot reach %d" scenario intensity subset.Length press approach reach
    for failure in failures do eprintfn "%s" failure
    if failures.Count = 0 then 0 else 1

let runControl (argv: string[]) =
    let output = if argv.Length > 0 then argv[0] else "docs/evidence/collective-intelligence/organized-control-diagnostic.json"
    if File.Exists output then failwithf "refusing to overwrite existing evidence: %s" output
    let full = argv |> Array.contains "--full"
    let selectedSeeds = if full then seeds else [| 1UL |]
    let selectedCases = if full then cases else [| "open" |]
    let ys = if full then [| 0.0; 18.0 |] else [| 0.0 |]
    let rows =
        [| for seed in selectedSeeds do
               for side in [ ClubSide.Home; ClubSide.Away ] do
                   for y in ys do
                       for scenario in selectedCases do
                           for intensity in [ 0.0f; 1.0f ] do
                               yield runCase true seed side y scenario intensity |]
    let violations = ResizeArray<string>()
    for row in rows do
        for contact in row.ContactObservations do
            if contact.BallTouched && contact.ControlAfter = "ReceivingBall" && contact.Surface <> "ContactFoot" then
                violations.Add(sprintf "%d %s %g %s %g contact %d: non-foot surface acquired receiving control" row.Seed row.Side row.BallY row.Scenario row.PressIntensity contact.Id)
    let root = Directory.GetCurrentDirectory()
    let sources =
        [| "Tools/CollectiveIntelligence/OrganizedProbe.fs"; "Tools/CollectiveIntelligence/PairedProbe.fs"
           "FootballEngine.Core/Match/Systems/Players/Actions/Duel.fs"; "FootballEngine.Core/Match/Systems/Ball/Possession.fs"
           "FootballEngine.Core/Match/Systems/Ball/FootContact.fs"; "FootballEngine.Core/Match/Contracts/Observation.fs"
           "FootballEngine.Core/Match/Stepper/Stepper.fs"; "FootballEngine.Core/Match/Stepper/EventApply.fs" |]
    let payload =
        {| Format = 1; Scope = if full then "complete fixed corpus" else "seed1, open, centre, both sides and intensities"
           Command = "dotnet run --project Tools/CollectiveIntelligence/CollectiveIntelligence.fsproj -c Release -- control <output> [--full]"
           Semantics = "The tool wraps each existing scheduled system without changing its trigger, order, result or RNG, recording appended events at system boundaries. Before/after are whole Stepper or system boundaries, not exact proposal, reducer, or contact instants. EventTick is proposal identity; DrainedAtStepTick is actual reducer boundary. An EventId match may be ambiguous; only Observation.chain supplies causal links. Fixture-scripted events are explicit inputs. Unlinked events are not assigned a guessed cause."
           HorizonSubticks = horizon; Source = sources |> Array.map (fun path -> {| Path = path; Sha256 = fileHash (Path.Combine(root, path)) |})
           AssemblySha256 = fileHash typeof<World>.Assembly.Location; Violations = violations.ToArray(); Cases = rows |}
    let directory = Path.GetDirectoryName output
    if not (String.IsNullOrWhiteSpace directory) then Directory.CreateDirectory directory |> ignore
    File.WriteAllText(output, JsonSerializer.Serialize(payload, JsonSerializerOptions(WriteIndented = true)))
    printfn "Control diagnostic: %d cases, %d committed relevant events, %d out-of-reach/unknown duel transfers; %s" rows.Length (rows |> Array.sumBy (fun row -> row.ControlEvents.Length)) violations.Count output
    for violation in violations do eprintfn "%s" violation
    if violations.Count > 0 || rows |> Array.exists (fun row -> row.ObservationEvicted > 0UL) then 1 else 0
