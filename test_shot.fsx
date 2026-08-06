#r "FootballEngine.Tests/bin/Debug/net10.0/FootballEngine.Tests.dll"
#r "FootballEngine.Client/bin/Debug/net10.0/FootballEngine.Client.dll"

open System
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.Tests.Infrastructure.Builders
open FootballEngine.Tests.Infrastructure.Runners
open SimStateOps

let homePos =
  [| 50.0, 34.0
     35.0, 10.0
     38.0, 34.0
     38.0, 54.0
     35.0, 68.0
     45.0, 34.0
     52.0, 20.0
     52.0, 54.0
     60.0, 10.0
     62.0, 34.0
     70.0, 34.0 |]

let awayPos =
  [| 100.0, 34.0
     91.0, 56.0
     91.0, 43.0
     91.0, 25.0
     91.0, 12.0
     75.0, 34.0
     65.0, 46.0
     65.0, 22.0
     45.0, 53.0
     40.0, 34.0
     37.0, 34.0 |]

let makeLineup gk out =
  [| makeGk 1 gk gk gk
     makePlayer 2 DL out
     makePlayer 3 DC out
     makePlayer 4 DC out
     makePlayer 5 DR out
     makePlayer 6 DM out
     makePlayer 7 MC out
     makePlayer 8 MC out
     makePlayer 9 AML out
     makePlayer 10 AMC out
     makePlayer 11 ST out |]

let runDetailed () =
  let hp = makeLineup 14 14
  let ap = makeLineup 14 14
  let ctx, state = buildSimState hp homePos ap awayPos
  state.Flow <- MatchFlow.Live
  let shooter = hp[10]

  state.Ball <-
    { state.Ball with
        Position =
          { X = 90.0<meter>
            Y = 34.0<meter>
            Z = 0.0<meter>
            Vx = 28.0<meter / second>
            Vy = 0.0<meter / second>
            Vz = 5.0<meter / second> }
        Control = Airborne
        LastTouchBy = Some shooter.Id
        Trajectory =
          Some
            { OriginX = 90.0<meter>
              OriginY = 34.0<meter>
              TargetX = 105.0<meter>
              TargetY = 34.0<meter>
              LaunchSubTick = 0<subtick>
              EstimatedArrivalSubTick = 40<subtick>
              KickerId = shooter.Id
              PeakHeight = 1.27<meter>
              Intent = Struck(shooter.Id, 0.8, 28.0, 0.0, 0.5) } }

  let cfg = ctx.Config.Physics
  let gkCfg = ctx.Config.GK

  printfn
    "CFG: AirDrag=%.3f GroundFriction=%.3f GroundRestitution=%.3f StopThreshold=%.2f AirborneThreshold=%.2f"
    cfg.AirDrag
    cfg.GroundFriction
    cfg.GroundRestitution
    cfg.BallStopThreshold
    (float cfg.AirborneCheckThreshold)

  printfn
    "GK: saveRadius=%.2f BaseProb=%.2f"
    (float (gkCfg.DiveReach + gkCfg.CollectionRadius))
    gkCfg.SaveBaseProbability

  printfn ""

  let getGKpos (ss: SimState) =
    let af = getFrame ss AwayClub
    let ar = getRoster ctx AwayClub

    [| 0 .. af.SlotCount - 1 |]
    |> Array.tryPick (fun i ->
      match af.Physics.Occupancy[i] with
      | OccupancyKind.Active ri when ar.Players[ri].Position = GK ->
        Some(float af.Physics.PosX[i], float af.Physics.PosY[i])
      | _ -> None)

  let mutable ss = state
  let mutable tick = 0
  let mutable prevX = 0.0
  let mutable prevZ = 0.0
  let mutable prevVx = 0.0
  let mutable prevVz = 0.0

  printfn "tick  ball_X  ball_Z     Vx     Vy    Vz    dX    GK_X  GKdist traj? ctrl       events"

  while tick < 300 && ss.Flow <> MatchEnded do
    let b = ss.Ball
    let dx = float b.Position.X - prevX
    let gkPos = getGKpos ss
    let gkX = gkPos |> Option.map fst |> Option.defaultValue nan

    let gkDist =
      gkPos
      |> Option.map (fun (gx, gy) -> sqrt ((gx - float b.Position.X) ** 2.0 + (gy - float b.Position.Y) ** 2.0))
      |> Option.defaultValue nan

    if
      tick < 5
      || tick % 5 = 0
      || b.Position.X >= 103.0 < m
      || dx < -0.1
      || (b.Trajectory.IsNone && tick > 0)
    then
      let evs =
        ss.TickDomainOutputs
        |> Seq.map (fun e ->
          match e with
          | DomainEvent.Emit m -> string m.Type
          | DomainEvent.BallUpdate _ -> "BallUpd"
          | DomainEvent.EmitSemantic s -> $"{s}"
          | _ -> "")
        |> String.concat ";"

      let trajS = if b.Trajectory.IsSome then "Y" else "N"

      printfn
        "%-4d %7.2f %7.2f %7.2f %7.2f %7.2f %7.2f %7.2f %7.2f  %s   %-10s [%s]"
        tick
        (float b.Position.X)
        (float b.Position.Z)
        (float b.Position.Vx)
        (float b.Position.Vy)
        (float b.Position.Vz)
        dx
        gkX
        gkDist
        trajS
        (string b.Control)
        evs

    let result = MatchStepper.updateOne ctx defaultClock [||] ss
    ss <- result.State
    prevX <- float b.Position.X
    prevZ <- float b.Position.Z
    prevVx <- float b.Position.Vx
    prevVz <- float b.Position.Vz
    tick <- tick + 1

  let b = ss.Ball

  printfn
    "\nFINAL: ball=(%.2f, %.2f, %.2f) v=(%.1f,%.1f,%.1f) ctrl=%s"
    (float b.Position.X)
    (float b.Position.Y)
    (float b.Position.Z)
    (float b.Position.Vx)
    (float b.Position.Vy)
    (float b.Position.Vz)
    (string b.Control)

runDetailed ()
