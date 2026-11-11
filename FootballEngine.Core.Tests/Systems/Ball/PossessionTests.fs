namespace FootballEngine.Core.Tests.Systems.Ball

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module PossessionTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private place (world: World) (side: ClubSide) (slot: int) (x: float<meter>) (y: float<meter>) : unit =
        let team = MatchWorld.team world side
        team.Frame.PosX.[slot] <- float32 x
        team.Frame.PosY.[slot] <- float32 y

    let private placeAllFar (world: World) : unit =
        for s in [ ClubSide.Home; ClubSide.Away ] do
            for i in MatchWorld.activeSlots (MatchWorld.team world s).Frame do
                place world s i -60.0<meter> 40.0<meter>

    let private applyEvents (world: World) (buf: EventBuffer) : unit =
        for ev in buf.Events do
            EventApply.apply ctx world ev

    let possessionTests =
        testList
            "FootballEngine.Core.Systems.Ball.PossessionTests"
            [ testCase "Carrier touches momentum without dragging ball position" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let pid = ctx.HomeLineup.Profiles.[5].Id

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, pid) } }

                  place world ClubSide.Home 5 0.0<meter> 0.0<meter>
                  let before = world.State.Ball.Position.X

                  let buf = buffer ()
                  BallPossession.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents world buf

                  Expect.equal world.State.Ball.Position.X before "touch cannot teleport the ball"
                  Expect.isGreaterThan (float world.State.Ball.Position.Vx) 0.0 "touch drives the next physical step")

              testCase "Controlled ball remains inside when its carrier reaches a pitch boundary" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let pid = ctx.HomeLineup.Profiles.[5].Id

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = Pitch.HalfLength - 0.1<meter>
                                          Y = Pitch.HalfWidth }
                                  Control = BallControl.Controlled(ClubSide.Home, pid) } }

                  place world ClubSide.Home 5 Pitch.HalfLength Pitch.HalfWidth
                  BallPossession.step sctx (Rng.ofSeed 3UL) world (buffer ()) |> ignore

                  Expect.isLessThanOrEqual world.State.Ball.Position.X Pitch.HalfLength "ball x remains legal"
                  Expect.isLessThanOrEqual world.State.Ball.Position.Y Pitch.HalfWidth "ball y remains legal")

              testCase "Receiving grace ends after the grace period" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let pid = ctx.HomeLineup.Profiles.[5].Id
                  let grace = ctx.Config.Ball.ReceptionGrace

                  world.State <-
                      { world.State with
                          Tick = 0<subtick>
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Receiving(ClubSide.Home, pid, 0<subtick>) } }

                  for t in 1 .. (int grace - 1) do
                      world.State <-
                          { world.State with
                              Tick = t * 1<subtick> }

                  let buf = buffer ()
                  BallPossession.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents world buf

                  match world.State.Ball.Control with
                  | BallControl.Receiving _ -> ()
                  | other -> failtestf "expected still Receiving, got %A" other

                  world.State <-
                      { world.State with
                          Tick = (int grace + 1) * 1<subtick> }

                  let buf = buffer ()
                  BallPossession.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents world buf

                  match world.State.Ball.Control with
                  | BallControl.Controlled(ClubSide.Home, pid') -> Expect.equal pid' pid "controlled after grace"
                  | other -> failtestf "expected Controlled, got %A" other)

              testCase "Proximity cannot resolve a loose-ball contest" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = 5.0<meter>
                                          Y = 0.0<meter> }
                                  Control = BallControl.Contesting ClubSide.Home } }

                  place world ClubSide.Home 5 5.0<meter> 0.0<meter>
                  place world ClubSide.Away 5 5.5<meter> 0.0<meter>

                  let buf = buffer ()
                  BallPossession.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents world buf

                  Expect.isEmpty buf.Events "a nearby player has not made physical contact"
                  Expect.equal world.State.Ball.Control (BallControl.Contesting ClubSide.Home) "the race remains unresolved")

              testCase "Nobody near a loose ball: no contest" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = 40.0<meter>
                                          Y = 0.0<meter> }
                                  Control = BallControl.Contesting ClubSide.Home } }

                  let buf = buffer ()
                  BallPossession.step sctx (Rng.ofSeed 3UL) world buf |> ignore

                  Expect.isEmpty buf.Events "no contest")

              testCase "Elapsed time cannot pick up a free ball" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          Tick = 100<subtick>
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = 1.0<meter>
                                          Y = 0.0<meter> }
                                  Control = BallControl.Free
                                  StationarySince = Some 0<subtick> } }

                  place world ClubSide.Home 1 1.0<meter> 0.0<meter>

                  let buf = buffer ()
                  BallPossession.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents world buf

                  Expect.equal world.State.Ball.Control BallControl.Free "free ball awaits physical contact"
                  Expect.isFalse
                      (buf.Events |> Seq.exists (fun event -> match event.Data with EventData.Possession _ -> true | _ -> false))
                      "stationary time is not a pickup")

              testCase "A reachable loose ball does not wait for an idle timer" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          Tick = 100<subtick>
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = 1.0<meter>
                                          Y = 0.0<meter> }
                                  Control = BallControl.Free
                                  StationarySince = Some 90<subtick> } }

                  place world ClubSide.Home 1 1.0<meter> 0.0<meter>

                  let buf = buffer ()
                  BallPossession.step sctx (Rng.ofSeed 3UL) world buf |> ignore

                  Expect.isEmpty buf.Events "reach alone is not a recorded touch"
                  Expect.equal world.State.Ball.Control BallControl.Free "the ball remains loose until contact")

              testCase "press intent cannot win a loose ball without contact" (fun _ ->
                  let c = TestBuilders.defaultCtx ()

                  c.HomeLineup.Profiles.[5] <-
                      { c.HomeLineup.Profiles.[5] with
                          Pace = 90.0f }

                  c.AwayLineup.Profiles.[5] <-
                      { c.AwayLineup.Profiles.[5] with
                          Pace = 50.0f }

                  let sctx' =
                      { Ctx = c
                        Clock = SimulationClock.defaultClock }

                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = 0.0<meter>
                                          Y = 0.0<meter> }
                                  Control = BallControl.Contesting ClubSide.Home } }

                  place world ClubSide.Home 5 0.6<meter> 0.0<meter>
                  place world ClubSide.Away 5 0.7<meter> 0.0<meter>

                  (MatchWorld.team world ClubSide.Away).Frame.Intent.Kind.[5] <- IntentCode.Press

                  let buf = buffer ()
                  BallPossession.step sctx' (Rng.ofSeed 3UL) world buf |> ignore

                  Expect.isEmpty buf.Events "intent changes movement, not the contact result"
                  Expect.equal world.State.Ball.Control (BallControl.Contesting ClubSide.Home) "neither side wins by scoring")

              testCase "keeper cannot acquire a contested ball by proximity" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world
                  place world ClubSide.Home 0 -50.0<meter> 0.0<meter>

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = -50.0<meter>
                                          Y = 0.0<meter> }
                                  Control = BallControl.Contesting ClubSide.Home } }

                  let buf = buffer ()
                  BallPossession.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents world buf

                  Expect.equal world.State.Ball.Control (BallControl.Contesting ClubSide.Home) "keeper needs a physical save or foot contact"
                  Expect.isEmpty buf.Events "keeper proximity cannot create a catch")

              testCase "a running carrier touches on the stride, with the foot swinging under the hips" (fun _ ->
                  let gait = ctx.Config.Gait
                  let pid = ctx.HomeLineup.Profiles.[5].Id

                  // Phase now, phase one tick earlier is derived by the system from speed; the touch must fall only
                  // on a crossing of the touch point, and the swinging foot is the one opposite the support half.
                  let touchAt phase speed =
                      let world = TestBuilders.minimalWorld ()
                      place world ClubSide.Home 5 0.0<meter> 0.0<meter>
                      let frame = (MatchWorld.team world ClubSide.Home).Frame
                      frame.VelX.[5] <- float32 speed
                      frame.GaitPhase.[5] <- float32 phase
                      let tick = world.State.Tick
                      let centre = world.State.Ball.Position

                      world.State <-
                          { world.State with
                              Ball =
                                  { world.State.Ball with
                                      Control = BallControl.Controlled(ClubSide.Home, pid)
                                      FootMotion =
                                          Some
                                              { Actor = pid
                                                Foot = ContactFoot.Right
                                                Surface = StrikeSurface.Inside
                                                Kind = FootTouchKind.Carry
                                                StartedAt = tick - 100<subtick>
                                                ContactAt = tick - 100<subtick>
                                                EndsAt = tick - 90<subtick>
                                                Point = { X = centre.X; Y = centre.Y; Z = centre.Z }
                                                BallCenter = { X = centre.X; Y = centre.Y; Z = centre.Z } } } }

                      BallPossession.step sctx (Rng.ofSeed 3UL) world (buffer ()) |> ignore

                      match world.State.Ball.FootMotion with
                      | Some motion when motion.ContactAt > tick -> Some motion.Foot
                      | _ -> None

                  let cycles = Gait.cyclesPerSecond gait PhysicalMorphology.standard 5.0
                  let step = cycles * float (SimulationClock.dt SimulationClock.defaultClock)
                  let point = gait.CarryTouchPhase
                  Expect.equal (touchAt (point + 0.5 * step) 5.0) (Some ContactFoot.Right) "left support: the right foot swings through"
                  Expect.equal (touchAt (point + 0.5 + 0.5 * step) 5.0) (Some ContactFoot.Left) "right support: the left foot swings through"
                  Expect.equal (touchAt (point + 0.25) 5.0) None "mid-flight between touch points: no touch"
                  Expect.equal (touchAt (point + 1.5 * step) 5.0) None "the crossing was a tick ago and is not repeated"
                  // A standing carrier's clock holds, so only the touch interval paces it.
                  Expect.isSome (touchAt (point + 0.25) 0.0) "standing, the interval alone is enough")

              testCase "a carry touch announces the next one, which lands on its tick and face, or is withdrawn" (fun _ ->
                  let pid = ctx.HomeLineup.Profiles.[5].Id

                  let journal (world: World) =
                      let records = Array.zeroCreate<MotionEvidence> 256

                      match world.Motion.Read(world.Motion.OldestSequence, System.Int32.MaxValue * 1<subtick>, records) with
                      | Ok(struct (_, count)) -> records[.. count - 1]
                      | Error error -> failtestf "journal unreadable: %A" error

                  let running () =
                      let world = TestBuilders.minimalWorld ()
                      place world ClubSide.Home 5 0.0<meter> 0.0<meter>
                      let frame = (MatchWorld.team world ClubSide.Home).Frame
                      frame.VelX.[5] <- 5.0f

                      world.State <-
                          { world.State with
                              Ball =
                                  { world.State.Ball with
                                      Control = BallControl.Controlled(ClubSide.Home, pid) } }

                      // First touch: a reception, after which the next carry touch is known.
                      BallPossession.step sctx (Rng.ofSeed 3UL) world (buffer ()) |> ignore
                      world

                  let world = running ()

                  let announced =
                      match world.State.Ball.AnnouncedTouch with
                      | ValueSome announced -> announced
                      | ValueNone -> failtest "a running carrier's touch must announce the next"

                  let prepared =
                      journal world
                      |> Array.filter (fun record -> record.TaskId = announced.TaskId)

                  Expect.equal (prepared |> Array.map _.Phase) [| MotionEvidencePhase.Prepared |] "published ahead, once"
                  Expect.equal prepared[0].Family MotionFamily.Dribble "as a carry touch"
                  Expect.isGreaterThan prepared[0].CommitAt (world.State.Tick + 1<subtick>) "for a later tick"
                  Expect.isLessThanOrEqual prepared[0].StartedAt prepared[0].CommitAt "its swing starts before contact"

                  // Advance the clock without moving anything: the committed touch fires exactly on its tick.
                  let mutable touchedAt = None

                  while touchedAt.IsNone && world.State.Tick < announced.Motion.ContactAt do
                      world.State <- { world.State with Tick = world.State.Tick + 1<subtick> }
                      BallPossession.step sctx (Rng.ofSeed 3UL) world (buffer ()) |> ignore

                      match world.State.Ball.FootMotion with
                      | Some motion when motion.ContactAt > prepared[0].OccurredAt + 1<subtick> -> touchedAt <- Some motion
                      | _ -> ()

                  match touchedAt with
                  | None -> failtest "the announced touch never landed"
                  | Some motion ->
                      Expect.equal motion.ContactAt announced.Motion.ContactAt "on the announced tick"
                      Expect.equal motion.Foot announced.Motion.Foot "with the announced boot"

                  let confirmed =
                      journal world
                      |> Array.filter (fun record -> record.TaskId = announced.TaskId && record.Phase = MotionEvidencePhase.Confirmed)

                  Expect.equal confirmed.Length 1 "the touch confirms the announced task rather than opening another"

                  // Losing the ball withdraws whatever touch is announced at that moment.
                  let pending =
                      match world.State.Ball.AnnouncedTouch with
                      | ValueSome next -> next
                      | ValueNone -> failtest "the confirmed touch announces the following one"

                  world.State <-
                      { world.State with
                          Ball = { world.State.Ball with Control = BallControl.Free } }

                  BallFootContact.step sctx (Rng.ofSeed 3UL) world (buffer ()) |> ignore
                  Expect.isTrue world.State.Ball.AnnouncedTouch.IsNone "no announcement survives the loss"

                  let cancelled =
                      journal world
                      |> Array.filter (fun record -> record.TaskId = pending.TaskId && record.Phase = MotionEvidencePhase.Cancelled)

                  Expect.equal cancelled.Length 1 "the viewer is told the swing will not land")

              testCase "stride cadence rises with speed, falls with leg length and stops when standing" (fun _ ->
                  let gait = ctx.Config.Gait
                  let standard = PhysicalMorphology.standard
                  let tall = PhysicalMorphology.ofStature 2.0f 85.0f ContactFoot.Right
                  Expect.equal (Gait.cyclesPerSecond gait standard 0.1) 0.0 "standing does not cycle"
                  Expect.isGreaterThan (Gait.cyclesPerSecond gait standard 7.0) (Gait.cyclesPerSecond gait standard 3.0) "faster is quicker"
                  Expect.isLessThan (Gait.cyclesPerSecond gait tall 5.0) (Gait.cyclesPerSecond gait standard 5.0) "longer legs step slower"
                  // Literature: ~2.7-3.2 steps/s between 3 and 5.5 m/s for a 1.8 m runner.
                  let steps speed = 2.0 * Gait.cyclesPerSecond gait standard speed
                  Expect.isTrue (steps 3.0 > 2.5 && steps 3.0 < 3.0) $"3 m/s: {steps 3.0:F2} steps/s"
                  Expect.isTrue (steps 5.5 > 3.0 && steps 5.5 < 3.5) $"5.5 m/s: {steps 5.5:F2} steps/s") ]
