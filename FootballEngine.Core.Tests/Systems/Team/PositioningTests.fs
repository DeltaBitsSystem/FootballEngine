namespace FootballEngine.Core.Tests.Systems.Team

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

/// Geometry guarantees for team shape: each role keeps its zone of the pitch,
/// and no active player is ever held off the pitch.
module PositioningTests =

    let private sctx (ctx: MatchContext) =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private freshDirective (world: World) (side: ClubSide) (tick: int<subtick>) : unit =
        let team = MatchWorld.team world side
        team.Directive.Kind <- DirectiveKind.Attack
        team.Directive.SinceTick <- tick - 1<subtick>

    /// Home attacks +X. A striker must be ahead of every defender, the GK must
    /// sit at the back, and wingers must be wider than the central block.
    let private roleGeometryTests ctx =
        let lineup = ctx.HomeLineup

        let gkSlot =
            lineup.Profiles |> Array.findIndex (fun p -> p.Role = SlotRole.Goalkeeper)

        let strikerSlot =
            lineup.Profiles |> Array.findIndex (fun p -> p.Role = SlotRole.Striker)

        let defenderSlots =
            lineup.Profiles
            |> Array.indexed
            |> Array.filter (fun (_, p) -> p.Role = SlotRole.Defender || p.Role = SlotRole.FullBack)
            |> Array.map fst

        let wingerSlots =
            lineup.Profiles
            |> Array.indexed
            |> Array.filter (fun (_, p) -> p.Role = SlotRole.Winger)
            |> Array.map fst

        testList
            "role geometry"
            [ testCase "striker is ahead of every defender" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  world.State <- { world.State with Tick = 4<subtick> }
                  freshDirective world ClubSide.Home world.State.Tick

                  TeamPosition.step (sctx ctx) (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  let strikerX = world.Home.Shape.TargetsX.[strikerSlot]

                  let maxDefX =
                      defenderSlots |> Array.map (fun s -> world.Home.Shape.TargetsX.[s]) |> Array.max

                  Expect.isGreaterThan strikerX maxDefX "striker target is ahead of the deepest defender")

              testCase "wingers are wider than the central block" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  world.State <- { world.State with Tick = 4<subtick> }
                  freshDirective world ClubSide.Home world.State.Tick

                  TeamPosition.step (sctx ctx) (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  let wingerWidth =
                      wingerSlots
                      |> Array.map (fun s -> abs (float world.Home.Shape.TargetsY.[s]))
                      |> Array.max

                  let wingerSlotsSet = Set.ofArray wingerSlots

                  let centralWidth =
                      [ for s in MatchWorld.activeSlots world.Home.Frame do
                            if
                                s <> strikerSlot
                                && not (Array.contains s defenderSlots)
                                && s <> gkSlot
                                && not (wingerSlotsSet.Contains s)
                            then
                                abs (float world.Home.Shape.TargetsY.[s]) ]
                      |> List.max

                  Expect.isGreaterThan wingerWidth centralWidth "wingers sit wider than the central midfield block")

              testCase "goalkeeper stays at the back and central" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  world.State <- { world.State with Tick = 4<subtick> }
                  freshDirective world ClubSide.Home world.State.Tick

                  TeamPosition.step (sctx ctx) (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  let gkX = world.Home.Shape.TargetsX.[gkSlot]
                  let gkY = abs (float world.Home.Shape.TargetsY.[gkSlot])
                  Expect.isLessThan gkY 3.0 "GK target stays central"
                  Expect.isLessThan gkX (world.Home.Shape.TargetsX.[strikerSlot]) "GK target is behind the striker")

              testCase "resting shape targets never leave the pitch" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  world.State <- { world.State with Tick = 4<subtick> }
                  freshDirective world ClubSide.Home world.State.Tick

                  TeamPosition.step (sctx ctx) (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  for s in MatchWorld.activeSlots world.Home.Frame do
                      Expect.isLessThanOrEqual
                          (abs (float world.Home.Shape.TargetsX.[s]))
                          (float Pitch.HalfLength)
                          (sprintf "slot %d X target stays inside the pitch" s)

                      Expect.isLessThanOrEqual
                          (abs (float world.Home.Shape.TargetsY.[s]))
                          (float Pitch.HalfWidth)
                          (sprintf "slot %d Y target stays inside the pitch" s)) ]

    /// Over a full deterministic match, no active player is ever left off the
    /// pitch — the bug this guards against is players wandering out and
    /// sticking there during loose balls / set pieces. Samples the live world
    /// at regular intervals so the transient bug (not just the final state) is
    /// caught. A small tolerance allows a momentary chase up to the line.
    let private offPitchTests ctx =
        let systems = Scheduler.systems

        testList
            "off-pitch"
            [ testCase "no active player is ever stuck off-pitch during the match" (fun _ ->
                  let world = MatchFactory.create ctx

                  let systemCtx =
                      { Ctx = ctx
                        Clock = SimulationClock.defaultClock }

                  let buffer =
                      { Events = ResizeArray()
                        Flags = { EventFlags.Bits = 0UL }
                        Applied = ResizeArray() }

                  let mutable rng = Rng.ofSeed 1234UL
                  let mutable guard = 0
                  let sampleEvery = 200<tickDelta> * 40<subtick / tickDelta>
                  let mutable checked = 0

                  while world.State.Flow <> MatchFlow.MatchEnded && guard < 400000 do
                      let rng', _, _ = Stepper.step systems systemCtx rng world buffer
                      rng <- rng'
                      guard <- guard + 1

                      if world.State.Tick % sampleEvery = 0<subtick> then
                          checked <- checked + 1

                          for side in [ ClubSide.Home; ClubSide.Away ] do
                              let frame = (MatchWorld.team world side).Frame

                              for slot in MatchWorld.activeSlots frame do
                                  let x = abs (float frame.PosX.[slot])
                                  let y = abs (float frame.PosY.[slot])

                                  Expect.isLessThanOrEqual
                                      x
                                      (float Pitch.HalfLength + 1.0)
                                      (sprintf "%A slot %d X off-pitch at tick %d" side slot world.State.Tick)

                                  Expect.isLessThanOrEqual
                                      y
                                      (float Pitch.HalfWidth + 1.0)
                                      (sprintf "%A slot %d Y off-pitch at tick %d" side slot world.State.Tick)

                  Expect.isGreaterThan checked 10 "sampled the match at multiple instants"
                  Expect.equal world.State.Flow MatchFlow.MatchEnded "match reached full time") ]

    let positioningTests =
        let ctx = TestBuilders.formationCtx ()
        testList "FootballEngine.Core.Systems.Team.PositioningTests" [ roleGeometryTests ctx; offPitchTests ctx ]
