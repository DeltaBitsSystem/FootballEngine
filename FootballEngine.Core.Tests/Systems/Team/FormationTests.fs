namespace FootballEngine.Core.Tests.Systems.Team

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

/// Pure geometry and determinism of the Formation module: interpolation over
/// the anchors returns a valid result, is deterministic, and every role keeps
/// its zone of the pitch for any ball position.
module FormationTests =

    /// Anchors covering the pitch so geometry is checked across all zones.
    let private anchors =
        [| 0.0<meter>, 0.0<meter>
           -30.0<meter>, 0.0<meter>
           30.0<meter>, 0.0<meter>
           10.0<meter>, -20.0<meter>
           10.0<meter>, 20.0<meter> |]

    let private homeFormation () =
        let lineup = TestBuilders.makeFormationLineup 0
        let roles = lineup.Profiles |> Array.map (fun p -> p.Role)
        Formation.fromLineup roles lineup.BasePositions 1.0 anchors

    let private strikerAndDefenders (roles: SlotRole[]) =
        let striker = roles |> Array.findIndex (fun r -> r = SlotRole.Striker)

        let defenders =
            roles
            |> Array.indexed
            |> Array.filter (fun (_, r) -> r = SlotRole.Defender || r = SlotRole.FullBack)
            |> Array.map fst

        striker, defenders

    let formationTests =
        testList
            "FootballEngine.Core.Systems.Team.FormationTests"
            [ testCase "query returns a target for every slot" (fun _ ->
                  let f = homeFormation ()
                  let targets = Formation.query f 5.0<meter> 0.0<meter>
                  Expect.equal targets.Length f.SlotCount "one target per slot"

                  Expect.all
                      targets
                      (fun pt -> not (System.Double.IsNaN pt.X || System.Double.IsNaN pt.Y))
                      "targets are finite")

              testCase "query is deterministic" (fun _ ->
                  let f = homeFormation ()
                  let a = Formation.query f 12.0<meter> -7.0<meter>
                  let b = Formation.query f 12.0<meter> -7.0<meter>
                  Expect.sequenceEqual a b "same ball position yields identical targets")

              testCase "queryInto matches the owned query" (fun _ ->
                  let f = homeFormation ()

                  for bx, by in
                      [| -40.0<meter>, -20.0<meter>
                         0.0<meter>, 0.0<meter>
                         35.0<meter>, 22.0<meter> |] do
                      let expected = Formation.query f bx by
                      let actual = Array.zeroCreate f.SlotCount
                      Formation.queryInto f bx by actual
                      Expect.sequenceEqual actual expected "reused query preserves the target geometry")

              testCase "targets stay inside the pitch for any ball position" (fun _ ->
                  let f = homeFormation ()

                  let ballZones =
                      [| -40.0<meter>, 0.0<meter>
                         40.0<meter>, 0.0<meter>
                         20.0<meter>, -25.0<meter>
                         -20.0<meter>, 25.0<meter> |]

                  for bx, by in ballZones do
                      for pt in Formation.query f bx by do
                          Expect.isLessThanOrEqual
                              (abs pt.X)
                              (float Pitch.HalfLength)
                              (sprintf "X in pitch at ball (%f,%f)" (float bx) (float by))

                          Expect.isLessThanOrEqual
                              (abs pt.Y)
                              (float Pitch.HalfWidth)
                              (sprintf "Y in pitch at ball (%f,%f)" (float bx) (float by)))

              testCase "striker stays ahead of every defender for any ball position" (fun _ ->
                  let f = homeFormation ()
                  let lineup = TestBuilders.makeFormationLineup 0
                  let roles = lineup.Profiles |> Array.map (fun p -> p.Role)
                  let striker, defenders = strikerAndDefenders roles

                  let ballZones =
                      [| -35.0<meter>, 0.0<meter>
                         0.0<meter>, 0.0<meter>
                         35.0<meter>, 0.0<meter>
                         15.0<meter>, -20.0<meter> |]

                  for bx, by in ballZones do
                      let targets = Formation.query f bx by
                      let sX = targets.[striker].X
                      let maxDefX = defenders |> Array.map (fun s -> targets.[s].X) |> Array.max
                      Expect.isGreaterThan sX maxDefX (sprintf "striker ahead at ball (%f,%f)" (float bx) (float by)))

              testCase "wingers stay wider than the central block for any ball position" (fun _ ->
                  let f = homeFormation ()
                  let lineup = TestBuilders.makeFormationLineup 0
                  let roles = lineup.Profiles |> Array.map (fun p -> p.Role)
                  let striker, defenders = strikerAndDefenders roles

                  let wingers =
                      roles
                      |> Array.indexed
                      |> Array.filter (fun (_, r) -> r = SlotRole.Winger)
                      |> Array.map fst

                  let ballZones =
                      [| 0.0<meter>, 0.0<meter>
                         -30.0<meter>, 25.0<meter>
                         30.0<meter>, -25.0<meter> |]

                  for bx, by in ballZones do
                      let targets = Formation.query f bx by

                      let wingerWidth = wingers |> Array.map (fun s -> abs targets.[s].Y) |> Array.max

                      let centralWidth =
                          roles
                          |> Array.indexed
                          |> Array.choose (fun (s, r) ->
                              if
                                  r <> SlotRole.Goalkeeper
                                  && r <> SlotRole.Winger
                                  && s <> striker
                                  && not (Array.contains s defenders)
                              then
                                  Some(abs targets.[s].Y)
                              else
                                  None)
                          |> Array.max

                      Expect.isGreaterThan
                          wingerWidth
                          centralWidth
                          (sprintf "wingers wider at ball (%f,%f)" (float bx) (float by)))

              testCase "goalkeeper stays at the back and central" (fun _ ->
                  let f = homeFormation ()
                  let lineup = TestBuilders.makeFormationLineup 0
                  let roles = lineup.Profiles |> Array.map (fun p -> p.Role)
                  let gk = roles |> Array.findIndex (fun r -> r = SlotRole.Goalkeeper)
                  let striker = roles |> Array.findIndex (fun r -> r = SlotRole.Striker)

                  let ballZones =
                      [| -40.0<meter>, 0.0<meter>
                         40.0<meter>, 0.0<meter>
                         20.0<meter>, -25.0<meter> |]

                  for bx, by in ballZones do
                      let targets = Formation.query f bx by
                      let gkX = targets.[gk].X
                      let gkY = abs targets.[gk].Y
                      let stX = targets.[striker].X
                      Expect.isLessThan gkY 4.0 (sprintf "GK central at ball (%f,%f)" (float bx) (float by))
                      Expect.isLessThan gkX stX (sprintf "GK behind striker at ball (%f,%f)" (float bx) (float by))) ]
