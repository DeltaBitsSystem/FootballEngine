namespace FootballEngine.Core.Tests.Systems.Referee

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module FoulsTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private foulEvent (fouler: PlayerId) (fouled: PlayerId) (fx: float<meter>) (fy: float<meter>) : MatchEvent =
        { Tick = 0<subtick>
          Data = EventData.Foul(fouler, fouled, FoulSeverity.Tactical, CardDecision.NoCard, (fx, fy)) }

    let private runFoul (world: World) (ev: MatchEvent) : EventBuffer =
        let buf = buffer ()
        buf.Applied.Add ev
        RefereeFouls.step sctx (Rng.ofSeed 42UL) world buf |> ignore
        buf

    let foulsTests =
        testList
            "FootballEngine.Core.Systems.Referee.FoulsTests"
            [ testCase "foul inside the box awards a penalty" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  // Home attacks +X; Away attacks -X. A Home foul on an Away
                  // player inside Away's box (near X = -52.5) is a penalty.
                  let foulX = -42.5<meter>
                  let fouler = ctx.HomeLineup.Profiles.[5].Id
                  let fouled = ctx.AwayLineup.Profiles.[6].Id

                  let buf = runFoul world (foulEvent fouler fouled foulX 0.0<meter>)

                  Expect.exists
                      buf.Events
                      (fun ev ->
                          match ev.Data with
                          | EventData.SetPiece(SetPieceKind.Penalty, ClubSide.Away) -> true
                          | _ -> false)
                      "a box foul on the away side is a penalty for away")

              testCase "foul outside the box awards a free kick" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let fx = 30.0<meter> // midfield, no box, no promising attack.
                  let fy = 10.0<meter>
                  let fouler = ctx.HomeLineup.Profiles.[5].Id
                  let fouled = ctx.AwayLineup.Profiles.[6].Id

                  let buf = runFoul world (foulEvent fouler fouled fx fy)

                  Expect.exists
                      buf.Events
                      (fun ev ->
                          match ev.Data with
                          | EventData.SetPiece(SetPieceKind.FreeKick, _) -> true
                          | _ -> false)
                      "a midfield foul is a free kick")

              testCase "advantage plays on when the fouled team is advancing" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  // Away attacks -X. An Away teammate is 10 m ahead of the foul
                  // spot, so the fouled team keeps playing (no set piece).
                  world.Away.Frame.PosX.[7] <- -40.0f
                  world.Away.Frame.PosY.[7] <- 0.0f

                  let fouler = ctx.HomeLineup.Profiles.[5].Id
                  let fouled = ctx.AwayLineup.Profiles.[6].Id

                  let buf = runFoul world (foulEvent fouler fouled -30.0<meter> 0.0<meter>)

                  Expect.isFalse
                      (buf.Events
                       |> Seq.exists (fun ev ->
                           match ev.Data with
                           | EventData.SetPiece _ -> true
                           | _ -> false))
                      "advantage means no set piece") ]
