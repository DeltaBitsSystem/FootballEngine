// validate_fixes.fsx
#r "/home/pablo/Projects/FootballEngine/FootballEngine.Tests/bin/Debug/net10.0/FootballEngine.Tests.dll"
#r "/home/pablo/Projects/FootballEngine/FootballEngine.Client/bin/Debug/net10.0/FootballEngine.Client.dll"

open System
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.SimStateOps
open FootballEngine.Tests.Infrastructure.Builders

let clock = SimulationClock.defaultClock

// ---------- Harness ----------
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

let buildMatch () =
  let hp = makeLineup 14 14
  let ap = makeLineup 14 14
  let ctx, state = buildSimState hp homePos ap awayPos
  state.Flow <- MatchFlow.Live
  ctx, state

// ---------- Fix-C: BalanceConfig calibrada ----------
let balancedConfig (c: BalanceConfig) : BalanceConfig =
  { c with
      Decision =
        { c.Decision with
            ShootDistPenaltyMax = 0.06 // era 0.5 (doble castigo por distancia)
            ShootDistPenaltyDivisor = 60.0
            ShootMinThreshold = 0.22 // era 0.35
            ShootSTBonus = 0.12 }
      Shot = { c.Shot with AngleSpreadBase = 0.30 } // era 0.15 rad (sigma ~17 deg)
      GK = { c.GK with SaveBaseProbability = 0.32 } }

// ---------- Fix-A: anclaje del portero ----------
let gkAnchorTarget (state: SimState) (side: ClubSide) : float<meter> * float<meter> =
  let dir = MatchSpatial.attackDirFor side state
  let defendLine = if dir = LeftToRight then 0.0<meter> else PitchLength
  let outDir = if dir = LeftToRight then 1.0 else -1.0
  let ballX = state.Ball.Position.X
  let ballY = state.Ball.Position.Y
  let ballOut = (ballX - defendLine) * outDir

  let gkX =
    if ballOut < 1.0<meter> || ballOut > PenaltyAreaDepth then
      defendLine + outDir * 1.5<meter>
    else
      defendLine + outDir * (max 1.0<meter> (ballOut - 0.8<meter>))

  let gkY = min (PostFarY + 3.0<meter>) (max (PostNearY - 3.0<meter>) ballY)
  gkX, gkY

let enforceGKAnchor (ctx: MatchContext) (state: SimState) =
  for side in [| HomeClub; AwayClub |] do
    let team = if side = HomeClub then state.Home else state.Away
    let roster = if side = HomeClub then ctx.HomePlayers else ctx.AwayPlayers

    for i = 0 to team.Frame.SlotCount - 1 do
      match team.Frame.Physics.Occupancy[i] with
      | OccupancyKind.Active ri when roster[ri].Position = GK ->
        let tx, ty = gkAnchorTarget state side
        team.Frame.Physics.PosX[i] <- float32 (float tx)
        team.Frame.Physics.PosY[i] <- float32 (float ty)
      | _ -> ()

// ---------- Fix-B: resolver balon congelado en Contesting ----------
let resolveContest (ctx: MatchContext) (state: SimState) (rng: Random) (_subTick: int) : bool =
  match state.Ball.Control with
  | Contesting _ ->
    let bx = state.Ball.Position.X
    let by = state.Ball.Position.Y
    let radius = 8.0<meter>

    let nearest (side: ClubSide) =
      let team = if side = HomeClub then state.Home else state.Away
      let roster = if side = HomeClub then ctx.HomePlayers else ctx.AwayPlayers
      let mutable bestRosterIdx = -1
      let mutable bestDist = System.Double.MaxValue * 1.0<meter>

      for i = 0 to team.Frame.SlotCount - 1 do
        match team.Frame.Physics.Occupancy[i] with
        | OccupancyKind.Active ri ->
          let px = float team.Frame.Physics.PosX[i] * 1.0<meter>
          let py = float team.Frame.Physics.PosY[i] * 1.0<meter>
          let dM = sqrt ((px - bx) * (px - bx) + (py - by) * (py - by))

          if dM <= radius && dM < bestDist then
            bestDist <- dM
            bestRosterIdx <- ri
        | _ -> ()

      if bestRosterIdx >= 0 then
        Some(roster[bestRosterIdx], float bestDist)
      else
        None

    let skill (p: Player) (distM: float) =
      (MathPipelines.normStat p.Technical.Dribbling
       + MathPipelines.normStat p.Physical.Strength
       + MathPipelines.normStat p.Mental.Positioning)
      / 3.0
      + (1.0 - distM / 8.0)

    match nearest HomeClub, nearest AwayClub with
    | Some(hp, hd), Some(ap, ad) ->
      let hS = skill hp hd
      let aS = skill ap ad
      let pHome = 1.0 / (1.0 + System.Math.Exp(-2.5 * (hS - aS)))

      let winnerSide, winner =
        if rng.NextDouble() < pHome then
          HomeClub, hp
        else
          AwayClub, ap

      state.Ball <-
        { state.Ball with
            Control = Controlled(winnerSide, winner.Id)
            LastTouchBy = Some winner.Id
            Trajectory = None }

      true
    | Some(hp, _), None ->
      state.Ball <-
        { state.Ball with
            Control = Controlled(HomeClub, hp.Id)
            LastTouchBy = Some hp.Id
            Trajectory = None }

      true
    | None, Some(ap, _) ->
      state.Ball <-
        { state.Ball with
            Control = Controlled(AwayClub, ap.Id)
            LastTouchBy = Some ap.Id
            Trajectory = None }

      true
    | None, None -> false
  | _ -> false

// ---------- Fix-D: recuperar cualquier balon quieto sin controlador ----------
// En el engine: RefereeAgent debe correr (OnEvent OR EveryN) y con un balon
// detenido + sin jugador cerca -> lo "suelta a duel del mas cercano" (drop in).
// Aqui: si el balon esta parado y NINGUN jugador activo lo controlo en Xs,
// se lo pegamos al jugador parado mas cercano dentro de 2.5m (ya deberia
// estar llegando su intent de Récuperación).
let fixStalledBall (ctx: MatchContext) (state: SimState) : bool =
  let stoppedSpeed =
    let vx = float state.Ball.Position.Vx
    let vy = float state.Ball.Position.Vy
    let vz = float state.Ball.Position.Vz
    sqrt (vx * vx + vy * vy + vz * vz) < 1.5

  if not stoppedSpeed then
    false
  else
    let bx = state.Ball.Position.X
    let by = state.Ball.Position.Y
    let mutable bestSide = HomeClub
    let mutable bestP: Player option = None
    let mutable bestDist = 2.5<meter>

    for side in [| HomeClub; AwayClub |] do
      let team = if side = HomeClub then state.Home else state.Away
      let roster = if side = HomeClub then ctx.HomePlayers else ctx.AwayPlayers

      for i = 0 to team.Frame.SlotCount - 1 do
        match team.Frame.Physics.Occupancy[i] with
        | OccupancyKind.Active ri ->
          let px = float team.Frame.Physics.PosX[i] * 1.0<meter>
          let py = float team.Frame.Physics.PosY[i] * 1.0<meter>

          let d = sqrt ((px - bx) * (px - bx) + (py - by) * (py - by))

          if d < bestDist then
            bestDist <- d
            bestP <- Some roster[ri]
            bestSide <- side
        | _ -> ()

    match bestP with
    | Some bp ->
      state.Ball <-
        { state.Ball with
            Position =
              { X = bx
                Y = by
                Z = 0.0<meter>
                Vx = 0.0<meter / second>
                Vy = 0.0<meter / second>
                Vz = 0.0<meter / second> }

            Control = Controlled(bestSide, bp.Id)
            LastTouchBy = Some bp.Id
            Trajectory = None
            GKHoldSinceSubTick = None }

      true
    | None -> false

// ---------- Metricas ----------
type Metric =
  { Label: string
    MinutesPlayed: float
    Shots: int
    ShotsOnTarget: int
    Goals: int
    Corners: int
    Passes: int
    Fouls: int
    LongBalls: int
    Dribbles: int
    XGAtLaunch: float
    XGTotal: float
    ContestResolved: int
    StalledResolved: int
    Stalls: string list }

let countEventType (e: MatchEvent) (acc: int Ref) : unit = ()

let runMatch (label: string) (enableFixes: bool) (useBalanced: bool) (maxTicks: int) (seed: int) : Metric =
  let ctx0, state0 = buildMatch ()

  let ctx =
    if useBalanced then
      { ctx0 with
          Config = balancedConfig ctx0.Config }
    else
      ctx0

  let state = state0
  let rng = Random seed
  let evs = ResizeArray()
  let mutable lastEventSub = 0
  let mutable contestResolved = 0
  let mutable stalledResolved = 0
  let mutable i = 0
  let mutable run = true
  let mutable frozenTicks = 0
  let mutable staleFreeTicks = 0
  let mutable prevX = System.Double.NaN
  let mutable prevY = System.Double.NaN
  let stalls = ResizeArray<string>()
  let seenStalls = System.Collections.Generic.HashSet<string>()

  // detecta balon quieto durante ~15s sin eventos -> reporta la causa una vez
  let maybeReportStall () =
    let quietSecs = (i - lastEventSub) / 40
    let control = state.Ball.Control

    let key =
      sprintf
        "%A@%.0f,%.0f %.1f'"
        control
        (float state.Ball.Position.X)
        (float state.Ball.Position.Y)
        (float i / 2400.0)

    if quietSecs > 9 && not (seenStalls.Contains key) then
      seenStalls.Add key |> ignore
      stalls.Add key

  while run && i < maxTicks do
    if enableFixes then
      enforceGKAnchor ctx state

    let res = MatchStepper.updateOne ctx clock [||] state
    evs.AddRange res.Events

    for e in res.Events do
      if e.SubTick > lastEventSub then
        lastEventSub <- e.SubTick

    if enableFixes then
      match state.Ball.Control with
      | Contesting _ ->
        let vx = float state.Ball.Position.Vx * 1.0
        let vy = float state.Ball.Position.Vy * 1.0
        let speed = sqrt (vx * vx + vy * vy)

        let moved =
          not (System.Double.IsNaN prevX)
          && (float state.Ball.Position.X <> prevX || float state.Ball.Position.Y <> prevY)

        if speed < 0.5 && not moved then
          frozenTicks <- frozenTicks + 1

          if frozenTicks > 300 then
            let ok = resolveContest ctx state rng 0

            if ok then
              contestResolved <- contestResolved + 1
              frozenTicks <- 0
            else
              // nadie cerca: balon suelto para que las intenciones lo recojan
              state.Ball <- { state.Ball with Control = Free }
              frozenTicks <- 0
        else
          frozenTicks <- 0
      | Free
      | Airborne ->
        frozenTicks <- 0

        let moved =
          not (System.Double.IsNaN prevX)
          && (float state.Ball.Position.X <> prevX || float state.Ball.Position.Y <> prevY)

        if not moved then
          staleFreeTicks <- staleFreeTicks + 1

          if staleFreeTicks > 600 then
            // ~15s quieto: pegarselo al jugador mas cercano
            if fixStalledBall ctx state then
              stalledResolved <- stalledResolved + 1
              staleFreeTicks <- 0
        else
          staleFreeTicks <- 0
      | _ ->
        frozenTicks <- 0
        staleFreeTicks <- 0

    if i % 600 = 0 then
      maybeReportStall ()

    prevX <- float state.Ball.Position.X
    prevY <- float state.Ball.Position.Y

    if state.Flow = MatchEnded then
      run <- false

    i <- i + 1

  let mutable shots, sot, goals, corners, passes, fouls, lb, drib =
    0, 0, 0, 0, 0, 0, 0, 0

  let mutable xgLaunch = 0.0

  for e in evs do
    match e.Type with
    | MatchEventType.ShotLaunched -> shots <- shots + 1
    | MatchEventType.ShotOnTarget -> sot <- sot + 1
    | MatchEventType.Goal -> goals <- goals + 1
    | MatchEventType.Corner -> corners <- corners + 1
    | MatchEventType.PassCompleted _
    | MatchEventType.PassLaunched _
    | MatchEventType.PassIncomplete _
    | MatchEventType.PassDeflected _
    | MatchEventType.PassMisplaced _
    | MatchEventType.PassIntercepted _ -> passes <- passes + 1
    | MatchEventType.FoulCommitted -> fouls <- fouls + 1
    | MatchEventType.LongBall _ -> lb <- lb + 1
    | MatchEventType.DribbleSuccess
    | MatchEventType.DribbleFail
    | MatchEventType.DribbleKeep -> drib <- drib + 1
    | _ -> ()

    if e.Type = MatchEventType.ShotLaunched then
      xgLaunch <- xgLaunch + (e.Context.ExpectedGoal |> Option.defaultValue 0.0)

  { Label = label
    MinutesPlayed = float lastEventSub / 2400.0
    Shots = shots
    ShotsOnTarget = sot
    Goals = goals
    Corners = corners
    Passes = passes
    Fouls = fouls
    LongBalls = lb
    Dribbles = drib
    XGAtLaunch = xgLaunch
    XGTotal = float state.HomeXG + float state.AwayXG
    ContestResolved = contestResolved
    StalledResolved = stalledResolved
    Stalls = stalls |> Seq.toList }

// ---------- RUN ----------
let full = 228000

let baseline = runMatch "baseline" false false full 42
let fixes = runMatch "fix" true false full 43
let fixesBal = runMatch "fix+balance" true true full 44

let printRow (m: Metric) =
  printfn
    "| %-12s | %6.1f' | %5d | %4d | %3d | %5d | %5d | %5d | %3d | %4d | %6.2f | %6.2f | %3d | %3d |"
    m.Label
    m.MinutesPlayed
    m.Shots
    m.ShotsOnTarget
    m.Goals
    m.Corners
    m.Passes
    m.Fouls
    m.LongBalls
    m.Dribbles
    m.XGAtLaunch
    m.XGTotal
    m.ContestResolved
    m.StalledResolved

printfn
  "| Escenario   | último  | tiros | pid  | goles | corners | pases | faltas | lb | drib | xGL | xGT | resC | resS |"

printfn
  "+-------------+---------+-------+-----+-------+---------+-------+--------+----+------+------+-----+-----+-----+"

printRow baseline
printRow fixes
printRow fixesBal

for m in [ fixes; fixesBal ] do
  if m.Stalls.Length > 0 then
    printfn "\n[%s] %d stall(s):" m.Label m.Stalls.Length

    for s in m.Stalls |> List.truncate 12 do
      printfn "    %s" s

printfn
  "\nNotas:\n- B1 congelado si último minuto << 90.\n- B2 tiros/partido objetivo ~25.\n- B3 xGTotal deberia ser ~= xGAtLaunch (hoy se dobla en el save)."
