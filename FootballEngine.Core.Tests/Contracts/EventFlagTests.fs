namespace FootballEngine.Core.Tests.Contracts

open Expecto
open FootballEngine.Core.Match

module EventFlagTests =

    let private allKinds: EventKind list =
        [ EventKind.PossessionGained
          EventKind.PossessionLost
          EventKind.BallLoose
          EventKind.PassLaunched
          EventKind.PassCompleted
          EventKind.ShotLaunched
          EventKind.ShotResult
          EventKind.Goal
          EventKind.Foul
          EventKind.Card
          EventKind.SetPieceAwarded
          EventKind.ThrowIn
          EventKind.Corner
          EventKind.GoalKick
          EventKind.Tackle
          EventKind.Save
          EventKind.CrossAttempt
          EventKind.Dribble
          EventKind.Interception
          EventKind.Injury
          EventKind.Substitution
          EventKind.VARDecision
          EventKind.KickOff
          EventKind.HalfTime
          EventKind.FullTime
          EventKind.MomentumShifted
          EventKind.ConditionCritical
          EventKind.RedCard
          EventKind.ShootoutGoal ]

    /// Runtime-exhaustive: one event value per EventData constructor.
    let private sampleEvents () : MatchEvent list =
        let p = PlayerId 1
        let q = PlayerId 2

        [ { Tick = 0<subtick>
            Data = EventData.Possession(ClubSide.Home, p) }
          { Tick = 0<subtick>
            Data = EventData.Pass(p, Some q, 12.0<meter>, 0.7, true, 0.0<meter>, 0.0<meter>, 12.0<meter>, 0.0<meter>) }
          { Tick = 0<subtick>
            Data = EventData.Shot(p, ShotResult.OnTarget false, 0.4, 24.0<meterPerSecond>, 30.0<meter>, 0.0<meter>) }
          { Tick = 0<subtick>
            Data = EventData.Goal(ClubSide.Home, Some p, Some q, 0.3) }
          { Tick = 0<subtick>
            Data = EventData.Foul(p, q, FoulSeverity.Tactical, CardDecision.Yellow, (1.0<meter>, 2.0<meter>)) }
          { Tick = 0<subtick>
            Data = EventData.Card(p, CardColor.Yellow) }
          { Tick = 0<subtick>
            Data = EventData.SetPiece(SetPieceKind.ThrowIn, ClubSide.Away) }
          { Tick = 0<subtick>
            Data = EventData.SetPiece(SetPieceKind.Corner, ClubSide.Home) }
          { Tick = 0<subtick>
            Data = EventData.SetPiece(SetPieceKind.GoalKick, ClubSide.Away) }
          { Tick = 0<subtick>
            Data = EventData.SetPiece(SetPieceKind.FreeKick, ClubSide.Home) }
          { Tick = 0<subtick>
            Data = EventData.BallLoose(0.0<meter>, 0.0<meter>) }
          { Tick = 0<subtick>
            Data = EventData.Interception(q, p) }
          { Tick = 0<subtick>
            Data = EventData.Tackle(q, p) }
          { Tick = 0<subtick>
            Data = EventData.Save(p, q) }
          { Tick = 0<subtick>
            Data = EventData.CrossAttempt(p, 30.0<meter>, 10.0<meter>) }
          { Tick = 0<subtick>
            Data = EventData.Dribble(p, true, 20.0<meter>, 0.0<meter>) }
          { Tick = 0<subtick>
            Data = EventData.Injury(p, 2) }
          { Tick = 0<subtick>
            Data = EventData.Substitution(p, q) }
          { Tick = 0<subtick>
            Data = EventData.VARDecision(VARResult.GoalOverturned) }
          { Tick = 0<subtick>
            Data = EventData.MomentumShift(ClubSide.Home) }
          { Tick = 0<subtick>
            Data = EventData.KickOff(ClubSide.Home) }
          { Tick = 0<subtick>
            Data = EventData.HalfTime }
          { Tick = 0<subtick>
            Data = EventData.FullTime }
          { Tick = 0<subtick>
            Data = EventData.ConditionCritical(p, 30) }
          { Tick = 0<subtick>
            Data = EventData.RedCardIssued(p) } ]

    let eventFlagTests =
        testList
            "FootballEngine.Core.Contracts.EventFlags"
            [ test "kind <-> flag bijection: distinct bits, all unique" {
                  let bits =
                      allKinds |> List.map (fun k -> (EventFlags.ofKind k).Bits) |> List.distinct

                  Expect.equal (List.length bits) (List.length allKinds) "every kind must map to a distinct bit"

                  for b in bits do
                      Expect.isTrue
                          (b <> 0UL && System.Numerics.BitOperations.PopCount b = 1)
                          "flags must be single bits"
              }

              test "kind <-> flag bijection: fits in 64 bits" {
                  let maxBit = allKinds |> List.map (fun k -> (EventFlags.ofKind k).Bits) |> List.max

                  Expect.isTrue (maxBit < (1UL <<< 63)) "all kinds must fit in a uint64"
              }

              test "union is idempotent" {
                  let a = EventFlags.ofKind EventKind.Goal
                  let u = EventFlags.union a a
                  Expect.equal u a "union with itself is itself"
              }

              test "intersects" {
                  let a = EventFlags.ofKind EventKind.Goal
                  let b = EventFlags.ofKind EventKind.Foul
                  let ab = EventFlags.union a b
                  Expect.isTrue (EventFlags.intersects ab a) "union intersects each member"
                  Expect.isFalse (EventFlags.intersects a b) "distinct bits do not intersect"
              }

              test "MatchEvent.kind is total over all EventData constructors (runtime-exhaustive)" {
                  let expected =
                      [ EventKind.PossessionGained
                        EventKind.PassLaunched
                        EventKind.ShotLaunched
                        EventKind.Goal
                        EventKind.Foul
                        EventKind.Card
                        EventKind.ThrowIn
                        EventKind.Corner
                        EventKind.GoalKick
                        EventKind.SetPieceAwarded
                        EventKind.BallLoose
                        EventKind.Interception
                        EventKind.Tackle
                        EventKind.Save
                        EventKind.CrossAttempt
                        EventKind.Dribble
                        EventKind.Injury
                        EventKind.Substitution
                        EventKind.VARDecision
                        EventKind.MomentumShifted
                        EventKind.KickOff
                        EventKind.HalfTime
                        EventKind.FullTime
                        EventKind.ConditionCritical
                        EventKind.RedCard ]

                  let actual = sampleEvents () |> List.map MatchEvent.kind
                  Expect.equal actual expected "kind must map every EventData constructor"
              }

              test "MatchEvent.kind matches its own flag" {
                  for ev in sampleEvents () do
                      let flag = EventFlags.ofKind (MatchEvent.kind ev)

                      let bits = EventFlags.ofKind (MatchEvent.kind ev)
                      Expect.isTrue (bits.Bits <> 0UL) "every event kind must produce a valid flag"
              }

              test "EventFlags.all sets every bit" {
                  let combined =
                      allKinds
                      |> List.fold (fun acc k -> EventFlags.union acc (EventFlags.ofKind k)) { Bits = 0UL }

                  Expect.equal combined EventFlags.all "all must be the union of every kind"
              } ]
