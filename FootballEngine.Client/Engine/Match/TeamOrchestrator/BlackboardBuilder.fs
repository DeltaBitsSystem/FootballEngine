module BlackboardBuilder

open FootballEngine
open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.Types.InfluenceTypes
open SimStateOps

let private detectOurPhase (state: SimState) (clubSide: ClubSide) : TeamPhase =
    let weHaveBall =
        match state.Ball.Control with
        | Controlled(side, _)
        | Receiving(side, _, _) -> side = clubSide
        | _ -> false

    let justChanged = state.PossessionHistory.LastChangeTick > int state.SubTick - 80

    if justChanged then TeamPhase.Transition
    elif weHaveBall then TeamPhase.Attacking
    else TeamPhase.Defending

let detectOpponentShape (oppFrame: TeamFrame) (attackDir: AttackDir) : OpponentShape =
    let mutable totalX = 0.0f
    let mutable count = 0

    for i = 0 to oppFrame.SlotCount - 1 do
        match oppFrame.Physics.Occupancy[i] with
        | OccupancyKind.Active _ ->
            totalX <- totalX + oppFrame.Physics.PosX[i]
            count <- count + 1
        | _ -> ()

    if count = 0 then
        OpponentShape.MidBlock
    else
        let avgX = totalX / float32 count
        let avgM = float avgX * 1.0<meter>

        let threshold =
            match attackDir with
            | LeftToRight -> 35.0<meter>
            | RightToLeft -> 70.0<meter>

        let highThreshold =
            match attackDir with
            | LeftToRight -> 50.0<meter>
            | RightToLeft -> 55.0<meter>

        if avgM > highThreshold then OpponentShape.HighLine
        elif avgM < threshold then OpponentShape.LowBlock
        else OpponentShape.MidBlock

let detectOpponentPressure (oppFrame: TeamFrame) (ballPos: Spatial) : OpponentPressure =
    let ballX = float32 ballPos.X
    let ballY = float32 ballPos.Y
    let mutable forwardCount = 0
    let mutable activeCount = 0

    for i = 0 to oppFrame.SlotCount - 1 do
        match oppFrame.Physics.Occupancy[i] with
        | OccupancyKind.Active _ ->
            activeCount <- activeCount + 1
            let px = oppFrame.Physics.PosX[i]
            let py = oppFrame.Physics.PosY[i]
            let dx = px - ballX
            let dy = py - ballY

            if dx * dx + dy * dy < 225.0f then
                forwardCount <- forwardCount + 1
        | _ -> ()

    if activeCount = 0 then
        OpponentPressure.NoPress
    else
        let ratio = float forwardCount / float activeCount

        if ratio > 0.5 then OpponentPressure.HighPress
        elif ratio > 0.25 then OpponentPressure.MidPress
        else OpponentPressure.NoPress

let computeThreatZones (influence: InfluenceFrame) (clubSide: ClubSide) : FlankZone[] =
    let oppThreshold = 0.3f
    let mutable threats = ResizeArray<int>()

    for cell = 0 to GridSize - 1 do
        let oppInfluence =
            if clubSide = HomeClub then
                -influence.ContestedGrid[cell]
            else
                influence.ContestedGrid[cell]

        if oppInfluence > oppThreshold then
            threats.Add(cell)

    if threats.Count = 0 then
        [||]
    else
        threats
        |> Seq.map (fun cell ->
            let _, cy = cellToCenter cell
            PitchZoneOps.toFlank (float cy * 1.0<meter>))
        |> Seq.distinct
        |> Seq.toArray

let computeWeaknessZones (influence: InfluenceFrame) (clubSide: ClubSide) : FlankZone[] =
    let ourThreshold = 0.3f
    let mutable weaknesses = ResizeArray<int>()

    for cell = 0 to GridSize - 1 do
        let ourInfluence =
            if clubSide = HomeClub then
                influence.ContestedGrid[cell]
            else
                -influence.ContestedGrid[cell]

        let passSafety = float influence.AttackerPassSafety[cell]

        if ourInfluence > ourThreshold && passSafety > 0.4 then
            weaknesses.Add(cell)

    if weaknesses.Count = 0 then
        [||]
    else
        weaknesses
        |> Seq.map (fun cell ->
            let _, cy = cellToCenter cell
            PitchZoneOps.toFlank (float cy * 1.0<meter>))
        |> Seq.distinct
        |> Seq.toArray

let computeUrgency (scoreDiff: int) (minutesLeft: float) (momentum: float) : float =
    let scoreUrgency =
        if scoreDiff < 0 then min 1.0 (float (abs scoreDiff) * 0.35)
        elif scoreDiff > 0 then 0.0
        else 0.2

    let timeUrgency =
        if minutesLeft < 10.0 then 0.5
        elif minutesLeft < 20.0 then 0.3
        elif minutesLeft < 30.0 then 0.15
        else 0.0

    let momentumUrgency =
        if momentum < -3.0 then 0.3
        elif momentum < 0.0 then 0.1
        else 0.0

    System.Math.Clamp(scoreUrgency + timeUrgency + momentumUrgency, 0.0, 1.0)

let detectJustLostBall (state: SimState) (currentSubTick: int<subtick>) : bool =
    let lastChange = state.PossessionHistory.LastChangeTick
    let delta = int currentSubTick - lastChange
    delta >= 0 && delta < 80

let computeMomentumStreak (events: ResizeArray<MatchEvent>) (currentSubTick: int) : float =
    let halfLife = 15.0 * 40.0
    let weight age = System.Math.Exp(-float age / halfLife)

    let mutable positive = 0.0
    let mutable negative = 0.0

    for i = events.Count - 1 downto 0 do
        let ev = events[i]
        let age = currentSubTick - ev.SubTick

        if age > int halfLife * 3 then
            ()
        else
            let w = weight age

            match ev.Type with
            | Goal
            | ShotOnTarget
            | PassCompleted _
            | DribbleSuccess -> positive <- positive + w
            | ShotOffTarget
            | ShotBlocked
            | PassIncomplete _
            | DribbleFail -> negative <- negative + w
            | _ -> ()

    let total = positive + negative
    if total < 0.001 then 0.0 else (positive - negative) / total

let computePressTriggerZone (tactics: TacticsConfig) (_emergent: EmergentState) : PitchZone = tactics.PressTriggerZone

let build (state: SimState) (clubSide: ClubSide) (ctx: MatchContext) : TeamBlackboard =
    let frame = getFrame state clubSide
    let oppFrame = getFrame state (ClubSide.flip clubSide)
    let attackDir = attackDirFor clubSide state

    let influence =
        if clubSide = HomeClub then
            state.HomeInfluenceFrame
        else
            state.AwayInfluenceFrame

    let ourPhase = detectOurPhase state clubSide
    let oppShape = detectOpponentShape oppFrame attackDir
    let oppPressure = detectOpponentPressure oppFrame state.Ball.Position
    let threatZones = computeThreatZones influence clubSide
    let weaknessZones = computeWeaknessZones influence clubSide

    let scoreDiff =
        if clubSide = HomeClub then
            state.HomeScore - state.AwayScore
        else
            state.AwayScore - state.HomeScore

    let minutesLeft =
        let elapsedSec = float (int state.SubTick) / 40.0
        max 0.0 (90.0 * 60.0 - elapsedSec) / 60.0

    let urgency = computeUrgency scoreDiff minutesLeft state.Momentum
    let justLostBall = detectJustLostBall state state.SubTick
    let momentumStreak = computeMomentumStreak state.MatchEvents (int state.SubTick)

    let tacticsCfg =
        tacticsConfig (getTactics state clubSide) (getInstructions state clubSide)

    let pressTriggerZone =
        computePressTriggerZone tacticsCfg (getEmergentState state clubSide)

    let ballZone = MatchSpatial.ofBallX state.Ball.Position.X attackDir

    { OurPhase = ourPhase
      OpponentShape = oppShape
      OpponentPressure = oppPressure
      ThreatZones = threatZones
      WeaknessZones = weaknessZones
      MomentumStreak = momentumStreak
      Urgency = urgency
      BallZone = ballZone
      JustLostBall = justLostBall
      PressTriggerZone = pressTriggerZone
      CoordinationMemory = CoordinationMemory.defaultMemory }
