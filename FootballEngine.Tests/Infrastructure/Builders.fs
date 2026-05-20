module FootballEngine.Tests.Infrastructure.Builders

open System
open FootballEngine
open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.TeamOrchestrator
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.Types.TacticsConfig
open SimStateOps

let defaultPhysical: PhysicalStats =
    { Acceleration = 10; Pace = 10; Agility = 10; Balance = 10
      JumpingReach = 10; Stamina = 10; Strength = 10 }

let defaultTechnical: TechnicalStats =
    { Finishing = 10; LongShots = 10; Dribbling = 10; BallControl = 10
      Passing = 10; Crossing = 10; Tackling = 10; Marking = 10
      Heading = 10; FreeKick = 10; Penalty = 10 }

let defaultMental: MentalStats =
    { Aggression = 10; Composure = 10; Vision = 10; Positioning = 10
      Bravery = 10; WorkRate = 10; Concentration = 10; Leadership = 10 }

let defaultGk: GoalkeeperStats =
    { Reflexes = 10; Handling = 10; Kicking = 10; OneOnOne = 10; AerialReach = 10 }

let makePlayer (id: PlayerId) (pos: Position) (skill: int) : Player =
    let s = Math.Clamp(skill, 1, 20)
    { Id = id; Name = $"P{id}"; Birthday = DateTime(1995, 1, 1); Nationality = "AR"
      Position = pos; PreferredFoot = Right; Height = 180; Weight = 75
      Physical = { defaultPhysical with Acceleration = s; Pace = s; Agility = s; Balance = s; Strength = s; Stamina = s }
      Technical = { defaultTechnical with Finishing = s; Dribbling = s; BallControl = s; Passing = s; Tackling = s; Crossing = s; Heading = s }
      Mental = { defaultMental with Aggression = s; Composure = s; Vision = s; Positioning = s; WorkRate = s; Concentration = s }
      Goalkeeping = { defaultGk with Reflexes = s; Handling = s; OneOnOne = s; AerialReach = s }
      Condition = 100; MatchFitness = 100; Morale = 50; Status = Available
      CurrentSkill = skill; PotentialSkill = skill; Reputation = 50
      Affiliation = Contracted(1, { Salary = 1000m; ExpiryYear = 2030 })
      TrainingSchedule = { Focus = TrainingAllRound; Intensity = TrainingLight }
      ExperienceModifiers = ExperienceModifiers.defaultModifiers }

let makeGk (id: PlayerId) (skill: int) (reflexes: int) (handling: int) : Player =
    let p = makePlayer id GK skill
    { p with Goalkeeping = { p.Goalkeeping with Reflexes = reflexes; Handling = handling } }

let eliteAttacker id pos = makePlayer id pos 20
let worstAttacker id pos = makePlayer id pos 1
let eliteGk id = makeGk id 20 20 20
let weakGk id = makeGk id 1 1 1

let homeClub: Club =
    { Id = 100; Name = "Home"; Nationality = "AR"; Reputation = 50
      PlayerIds = []; StaffIds = []; Budget = 1_000_000m; Morale = 50
      BoardObjective = LeagueObjective MidTable }

let awayClub: Club =
    { Id = 200; Name = "Away"; Nationality = "AR"; Reputation = 50
      PlayerIds = []; StaffIds = []; Budget = 1_000_000m; Morale = 50
      BoardObjective = LeagueObjective MidTable }

let spatialAt (x: float) (y: float) : Spatial =
    { X = x * 1.0<meter>; Y = y * 1.0<meter>; Z = 0.0<meter>
      Vx = 0.0<meter / second>; Vy = 0.0<meter / second>; Vz = 0.0<meter / second> }

let buildSimState
    (homePlayers: Player[]) (homePositions: (float * float)[])
    (awayPlayers: Player[]) (awayPositions: (float * float)[])
    : MatchContext * SimState =
    let hPos = homePositions |> Array.map (fun (x, y) -> spatialAt x y)
    let aPos = awayPositions |> Array.map (fun (x, y) -> spatialAt x y)
    let homeRoster = PlayerRoster.build homePlayers
    let awayRoster = PlayerRoster.build awayPlayers
    let homeFrame = TeamFrame.init homeRoster hPos
    let awayFrame = TeamFrame.init awayRoster aPos

    let ctx: MatchContext =
        { Home = homeClub; Away = awayClub; HomeCoach = Unchecked.defaultof<Staff>
          AwayCoach = Unchecked.defaultof<Staff>; HomePlayers = homePlayers
          AwayPlayers = awayPlayers; HomeBasePositions = hPos; AwayBasePositions = aPos
          HomeChemistry = ChemistryGraph.init homePlayers.Length
          AwayChemistry = ChemistryGraph.init awayPlayers.Length
          IsKnockoutMatch = false; Config = BalanceConfig.defaultConfig
          HomeRoster = homeRoster; AwayRoster = awayRoster }

    let homeTeam = TeamSimState()
    homeTeam.Frame <- homeFrame
    homeTeam.ShapeTargetX <- Array.zeroCreate homeFrame.SlotCount
    homeTeam.ShapeTargetY <- Array.zeroCreate homeFrame.SlotCount
    homeTeam.Tactics <- TeamTactics.Balanced
    homeTeam.Instructions <- Some TacticalInstructions.defaultInstructions

    let awayTeam = TeamSimState()
    awayTeam.Frame <- awayFrame
    awayTeam.ShapeTargetX <- Array.zeroCreate awayFrame.SlotCount
    awayTeam.ShapeTargetY <- Array.zeroCreate awayFrame.SlotCount
    awayTeam.Tactics <- TeamTactics.Balanced
    awayTeam.Instructions <- Some TacticalInstructions.defaultInstructions

    let state = SimState()
    state.HomeBasePositions <- hPos
    state.AwayBasePositions <- aPos
    state.HomeAttackDir <- LeftToRight
    state.BallXSmooth <- 52.5<meter>
    state.Ball <- SimStateOps.defaultBall
    state.Home <- homeTeam
    state.Away <- awayTeam
    state.Config <- BalanceConfig.defaultConfig
    ctx, state

let withBallControlledBy (club: ClubSide) (pid: PlayerId) (state: SimState) : SimState =
    state.Ball <- { state.Ball with Control = Controlled(club, pid); LastTouchBy = Some pid }
    state.LastAttackingClub <- club
    state

let withBallAt (x: float) (y: float) (state: SimState) : SimState =
    state.Ball <- { state.Ball with Position = spatialAt x y }
    state

let withBallFree (state: SimState) : SimState =
    state.Ball <- { state.Ball with Control = Free }
    state

let withGKHoldingSince (sinceSubTick: int) (state: SimState) : SimState =
    state.Ball <- { state.Ball with GKHoldSinceSubTick = Some(sinceSubTick * 1<subtick>) }
    state

let withFlow (flow: MatchFlow) (state: SimState) : SimState =
    state.Flow <- flow
    state

let withKickOffPending (team: ClubSide) (state: SimState) : SimState =
    state.Flow <- RestartDelay { Kind = SetPieceKind.KickOff; Team = team; Cause = InitialKickOff; RemainingTicks = 3 * 1<tickDelta> }
    state

let withLive (state: SimState) : SimState = state |> withFlow Live

let standardHomePlayers () : Player[] =
    [| makeGk 1 15 15 15
       makePlayer 2 DL 12;  makePlayer 3 DC 13; makePlayer 4 DC 13; makePlayer 5 DR 12
       makePlayer 6 DM 12;  makePlayer 7 MC 13; makePlayer 8 MC 13
       makePlayer 9 AML 14; makePlayer 10 AMC 14; makePlayer 11 ST 15 |]

let standardHomePositions () : (float * float)[] =
    [| 5.0, 34.0; 14.0, 12.0; 14.0, 25.0; 14.0, 43.0; 14.0, 56.0
       30.0, 34.0; 40.0, 22.0; 40.0, 46.0; 60.0, 15.0; 65.0, 34.0; 68.0, 34.0 |]

let standardAwayPlayers () : Player[] =
    [| makeGk 12 15 15 15
       makePlayer 13 DL 12; makePlayer 14 DC 13; makePlayer 15 DC 13; makePlayer 16 DR 12
       makePlayer 17 DM 12; makePlayer 18 MC 13; makePlayer 19 MC 13
       makePlayer 20 AML 14; makePlayer 21 AMC 14; makePlayer 22 ST 15 |]

let standardAwayPositions () : (float * float)[] =
    [| 100.0, 34.0; 91.0, 56.0; 91.0, 43.0; 91.0, 25.0; 91.0, 12.0
       75.0, 34.0; 65.0, 46.0; 65.0, 22.0; 45.0, 53.0; 40.0, 34.0; 37.0, 34.0 |]

let buildStandardMatch () : MatchContext * SimState =
    buildSimState (standardHomePlayers ()) (standardHomePositions ())
                  (standardAwayPlayers ()) (standardAwayPositions ())

let gkHoldingScenario () : MatchContext * SimState =
    let homePlayers = standardHomePlayers ()
    let ctx, state = buildSimState homePlayers (standardHomePositions ())
                                    (standardAwayPlayers ()) (standardAwayPositions ())
    state |> withBallAt 5.0 34.0 |> withBallControlledBy HomeClub homePlayers.[0].Id |> withGKHoldingSince 0 |> withLive |> ignore
    ctx, state

let attackerInBoxScenario () : MatchContext * SimState =
    let home = [| makePlayer 1 ST 18 |]
    let away = [| makeGk 2 10 10 10 |]
    let ctx, state = buildSimState home [| 88.0, 34.0 |] away [| 101.0, 34.0 |]
    state |> withBallAt 88.0 34.0 |> withBallControlledBy HomeClub 1 |> withLive |> ignore
    ctx, state

let eliteVsWeakGKScenario () : MatchContext * SimState =
    let home = [| makePlayer 1 ST 20 |]
    let away = [| makeGk 2 1 1 1 |]
    let ctx, state = buildSimState home [| 88.0, 34.0 |] away [| 101.0, 34.0 |]
    state |> withBallAt 88.0 34.0 |> withBallControlledBy HomeClub 1 |> withLive |> ignore
    ctx, state
