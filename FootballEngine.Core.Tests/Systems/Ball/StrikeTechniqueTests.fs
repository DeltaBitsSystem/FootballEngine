namespace FootballEngine.Core.Tests.Systems.Ball

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module StrikeTechniqueTests =

    let private ctx = TestBuilders.defaultCtx ()
    let private cfg = ctx.Config.StrikeTechnique

    let private impulse (vx: float) (vy: float) kind : BallImpulse =
        BallPhysics.createImpulse
            ctx.HomeLineup.Profiles.[5].Id
            { X = 0.0<meter>
              Y = 0.0<meter>
              Z = 0.0<meter> }
            { X = vx * 1.0<meterPerSecond>
              Y = vy * 1.0<meterPerSecond>
              Z = 0.0<meterPerSecond> }
            { Top = 0.0<radianPerSecond>
              Side = 0.0<radianPerSecond>
              Roll = 0.0<radianPerSecond> }
            None
            kind
            (SimTick.ofSubTicks 0<subtick>)
            ExecutionContext.OpenPlay

    /// A kicker at the centre facing +x with everyone else far away, so pressure is only what a test adds.
    let private kicker (passing: float32) =
        let profiles = Array.copy ctx.HomeLineup.Profiles

        profiles[5] <-
            { profiles[5] with
                Passing = passing
                Composure = 50.0f }

        let c =
            { ctx with
                HomeLineup =
                    { ctx.HomeLineup with
                        Profiles = profiles } }

        let world = TestBuilders.minimalWorld ()

        for side in [ ClubSide.Home; ClubSide.Away ] do
            let frame = (MatchWorld.team world side).Frame

            for slot in MatchWorld.activeSlots frame do
                frame.PosX[slot] <- if side = ClubSide.Home then -40.0f else 40.0f
                frame.PosY[slot] <- float32 slot * 3.0f - 15.0f

        world.Home.Frame.PosX[5] <- 0.0f
        world.Home.Frame.PosY[5] <- 0.0f
        world.Home.Frame.BodyFacing[5] <- 0.0f
        c, world

    let private sideSpin surface foot =
        let c, world = kicker 60.0f

        let struct (shaped, _) =
            StrikeTechnique.shape
                c
                world
                ClubSide.Home
                5
                foot
                surface
                (impulse 15.0 0.0 StrikeKind.Pass)
                (Rng.ofSeed 1UL)

        float shaped.AngularVelocity.Side

    /// Measured standard deviation of the launch direction over many strikes, so the spread is observed rather
    /// than read back from the formula.
    let private measuredSpread (c: MatchContext) world surface =
        let mutable rng = Rng.ofSeed 7UL
        let angles = ResizeArray()

        for _ in 1..4000 do
            let struct (shaped, next) =
                StrikeTechnique.shape
                    c
                    world
                    ClubSide.Home
                    5
                    ContactFoot.Right
                    surface
                    (impulse 15.0 0.0 StrikeKind.Pass)
                    rng

            rng <- next
            angles.Add(atan2 (float shaped.LinearVelocity.Y) (float shaped.LinearVelocity.X))

        let mean = Seq.average angles
        sqrt (angles |> Seq.averageBy (fun a -> (a - mean) ** 2.0))

    let strikeTechniqueTests =
        testList
            "FootballEngine.Core.Systems.Ball.StrikeTechniqueTests"
            [ testCase "the surface follows the foot, the direction across the body and the pace" (fun _ ->
                  let choose vx vy foot =
                      StrikeTechnique.choose cfg (impulse vx vy StrikeKind.Pass) foot 0.0
                  // Facing +x, the right boot's outer side is -y.
                  Expect.equal
                      (choose 12.0 0.0 ContactFoot.Right)
                      StrikeSurface.Inside
                      "a firm pass ahead is side-footed"

                  Expect.equal
                      (choose 24.0 0.0 ContactFoot.Right)
                      StrikeSurface.Instep
                      "a driven ball is struck with the laces"

                  Expect.equal
                      (choose 8.0 -8.0 ContactFoot.Right)
                      StrikeSurface.Outside
                      "45 degrees to the right is the right boot's outside"

                  Expect.equal
                      (choose 8.0 -8.0 ContactFoot.Left)
                      StrikeSurface.Inside
                      "the same pass is the left boot's inside"

                  Expect.equal (choose 8.0 8.0 ContactFoot.Left) StrikeSurface.Outside "mirrored for the left boot"

                  let curled =
                      { impulse 26.0 0.0 StrikeKind.Shot with
                          AngularVelocity =
                              { Top = 0.0<radianPerSecond>
                                Side = 8.0<radianPerSecond>
                                Roll = 0.0<radianPerSecond> } }

                  Expect.equal
                      (StrikeTechnique.choose cfg curled ContactFoot.Right 0.0)
                      StrikeSurface.Inside
                      "a curled strike is bent with the inside")

              testCase
                  "the inside curls toward the kicking foot, the outside away, and the instep barely spins"
                  (fun _ ->
                      Expect.isGreaterThan
                          (sideSpin StrikeSurface.Inside ContactFoot.Right)
                          0.0
                          "right inside curls right to left"

                      Expect.isLessThan
                          (sideSpin StrikeSurface.Inside ContactFoot.Left)
                          0.0
                          "left inside curls left to right"

                      Expect.isLessThan
                          (sideSpin StrikeSurface.Outside ContactFoot.Right)
                          0.0
                          "right outside curls left to right"

                      Expect.isGreaterThan
                          (abs (sideSpin StrikeSurface.Outside ContactFoot.Right))
                          (sideSpin StrikeSurface.Inside ContactFoot.Right)
                          "the outside of the boot spins harder"

                      Expect.floatClose
                          Accuracy.high
                          (sideSpin StrikeSurface.Instep ContactFoot.Right)
                          0.0
                          "the laces drive through the ball")

              testCase "dispersion grows with the surface, poor technique, pressure and lost balance" (fun _ ->
                  let c, world = kicker 60.0f
                  let inside = measuredSpread c world StrikeSurface.Inside
                  let instep = measuredSpread c world StrikeSurface.Instep
                  let poorCtx, poorWorld = kicker 20.0f
                  let poor = measuredSpread poorCtx poorWorld StrikeSurface.Inside

                  let pressedCtx, pressed = kicker 60.0f
                  pressed.Away.Frame.PosX[3] <- 1.5f
                  pressed.Away.Frame.PosY[3] <- 0.0f
                  let underPressure = measuredSpread pressedCtx pressed StrikeSurface.Inside

                  let staggeredCtx, staggered = kicker 60.0f

                  staggered.Home.Frame.ContactState[5] <-
                      { staggered.Home.Frame.ContactState[5] with
                          Status = BodyContactStatus.BodyStaggering }

                  let offBalance = measuredSpread staggeredCtx staggered StrikeSurface.Inside

                  // The sample matches the model it comes from within sampling error (~1.1 % for 4000 draws).
                  let expected = StrikeTechnique.spread cfg StrikeSurface.Inside 0.6 0 0.5 false
                  Expect.isLessThan (abs (inside - expected) / expected) 0.05 "the sampled spread is the modelled one"
                  Expect.isGreaterThan instep (inside * 1.5) "the instep is less precise than the side foot"
                  Expect.isGreaterThan poor (inside * 1.3) "a poor passer strays further"
                  Expect.isGreaterThan underPressure (inside * 1.2) "a challenger close by hurries the strike"
                  Expect.isGreaterThan offBalance (inside * 1.5) "a staggering kicker is less precise")

              testCase "a shot keeps the finishing model's own aim and curl" (fun _ ->
                  let c, world = kicker 60.0f
                  let shot = impulse 26.0 2.0 StrikeKind.Shot

                  let struct (shaped, _) =
                      StrikeTechnique.shape
                          c
                          world
                          ClubSide.Home
                          5
                          ContactFoot.Right
                          StrikeSurface.Instep
                          shot
                          (Rng.ofSeed 3UL)

                  Expect.equal shaped shot "the surface does not count the shot's miss twice")

              testCase "a curled shot bends toward its aim whichever goal it attacks" (fun _ ->
                  // Real aerodynamics, not the sign helper alone: the spin it asks for must push the ball toward the
                  // aim side for a shot travelling +X and for one travelling -X.
                  let bend side vx aimY =
                      let spin = 10.0 * Shot.curlSense side 0.0 aimY

                      let struct (_, ay, _) =
                          BallAero.flightAcceleration ctx.Config.Ball (1.0 / 40.0) vx 0.0 1.0 0.0 0.0 spin

                      ay

                  Expect.isGreaterThan
                      (bend ClubSide.Home 25.0 3.0)
                      0.0
                      "home shot aimed left of the striker bends left"

                  Expect.isLessThan (bend ClubSide.Home 25.0 -3.0) 0.0 "home shot aimed right bends right"
                  Expect.isGreaterThan (bend ClubSide.Away -25.0 3.0) 0.0 "away shot aimed at +Y bends toward +Y"
                  Expect.isLessThan (bend ClubSide.Away -25.0 -3.0) 0.0 "away shot aimed at -Y bends toward -Y") ]
