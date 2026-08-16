namespace FootballEngine.Core.Tests.Systems.Referee

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module CardsTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private runCards (world: World) (cards: CardColor list) : EventBuffer =
        let pid = ctx.HomeLineup.Profiles.[5].Id
        let buf = buffer ()

        for c in cards do
            buf.Applied.Add
                { Tick = 0<subtick>
                  Data = EventData.Card(pid, c) }

        RefereeCards.step sctx (Rng.ofSeed 42UL) world buf |> ignore
        buf

    let cardsTests =
        testList
            "FootballEngine.Core.Systems.Referee.CardsTests"
            [ testCase "second yellow issues a red card event" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let buf = runCards world [ CardColor.Yellow; CardColor.Yellow ]

                  Expect.equal world.Home.Cards.Yellows 2 "two yellows accrued"

                  Expect.exists
                      buf.Events
                      (fun ev ->
                          match ev.Data with
                          | EventData.RedCardIssued _ -> true
                          | _ -> false)
                      "second yellow escalates to a red")

              testCase "single yellow does not escalate" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let buf = runCards world [ CardColor.Yellow ]

                  Expect.equal world.Home.Cards.Yellows 1 "one yellow accrued"

                  Expect.isFalse
                      (buf.Events
                       |> Seq.exists (fun ev ->
                           match ev.Data with
                           | EventData.RedCardIssued _ -> true
                           | _ -> false))
                      "a single yellow is not a red")

              testCase "red card bookkeeping counts a straight red" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let buf = runCards world [ CardColor.Red ]

                  Expect.equal world.Home.Cards.Reds 1 "red card booked"
                  Expect.equal world.Home.Cards.Yellows 0 "no yellows mixed in") ]
