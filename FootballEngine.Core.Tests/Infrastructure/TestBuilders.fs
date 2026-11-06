namespace FootballEngine.Core.Tests.Infrastructure

open FootballEngine.Core.Match

module TestBuilders =

    let requireMatchOutcome =
        function
        | Ok outcome -> outcome
        | Error error -> failwithf "test fixture match failed: %A" error

    let makeProfile (id: int) (role: SlotRole) : PlayerProfile =
        let baseAttr =
            match role with
            | SlotRole.Goalkeeper -> 70.0f
            | SlotRole.Striker -> 75.0f
            | _ -> 65.0f

        { Id = PlayerId id
          Role = role
          IsGK = role = SlotRole.Goalkeeper
          Pace = baseAttr
          Strength = baseAttr
          Stamina = baseAttr
          Agility = baseAttr
          Vision = baseAttr
          Passing = baseAttr
          Finishing = baseAttr
          Crossing = baseAttr
          Dribbling = baseAttr
          Tackling = baseAttr
          Marking = baseAttr
          Positioning = baseAttr
          Anticipation = baseAttr
          Composure = baseAttr
          Aggression = baseAttr
          Flair = baseAttr
          Teamwork = baseAttr
          DecisionMaking = baseAttr
          Personality =
            { Composure = 0.5f
              Aggression = 0.5f
              RiskTaking = 0.5f
              Flair = 0.5f
              Leadership = 0.5f
              Consistency = 0.5f
              BigGameMentality = 0.5f }
          Morphology = PhysicalMorphology.standard
          InjuryProne = 0.1f }

    let makeLineup (offset: int) (slotCount: int) : Lineup =
        let roles =
            [| SlotRole.Goalkeeper
               SlotRole.Defender
               SlotRole.Defender
               SlotRole.Defender
               SlotRole.Defender
               SlotRole.DefensiveMid
               SlotRole.CenterMid
               SlotRole.CenterMid
               SlotRole.Winger
               SlotRole.AttackingMid
               SlotRole.Striker |]

        let profiles =
            [| for i in 0 .. slotCount - 1 -> makeProfile (offset + i + 1) roles.[i % roles.Length] |]

        let basePositions =
            [| for i in 0 .. slotCount - 1 ->
                   { X = -30.0<meter> + (float (i % 4) * 10.0<meter>)
                     Y = -20.0<meter> + (float (i % 3) * 10.0<meter>)
                     Z = 0.0<meter>
                     Vx = 0.0<meterPerSecond>
                     Vy = 0.0<meterPerSecond>
                     Vz = 0.0<meterPerSecond> } |]

        { Profiles = profiles
          BasePositions = basePositions
          SlotCount = slotCount
          StarterCount = min 11 slotCount }

    /// A realistic 4-3-3 with per-role base positions (Home attacks +X).
    /// Roles and coordinates are chosen so a geometric position test can assert
    /// GK in goal, defenders deep, striker forward and wingers wide.
    let makeFormationLineup (offset: int) : Lineup =
        let rolesAndPos =
            [| SlotRole.Goalkeeper, (-45.0, 0.0)
               SlotRole.FullBack, (-30.0, -20.0)
               SlotRole.Defender, (-35.0, -6.0)
               SlotRole.Defender, (-35.0, 6.0)
               SlotRole.FullBack, (-30.0, 20.0)
               SlotRole.CenterMid, (-15.0, 0.0)
               SlotRole.CenterMid, (-12.0, -12.0)
               SlotRole.CenterMid, (-12.0, 12.0)
               SlotRole.Winger, (-5.0, -26.0)
               SlotRole.Striker, (6.0, 0.0)
               SlotRole.Winger, (-5.0, 26.0) |]

        let profiles =
            [| for i, (r, _) in rolesAndPos |> Array.indexed -> makeProfile (offset + i + 1) r |]

        let basePositions =
            [| for _, (x, y) in rolesAndPos ->
                   { X = x * 1.0<meter>
                     Y = y * 1.0<meter>
                     Z = 0.0<meter>
                     Vx = 0.0<meterPerSecond>
                     Vy = 0.0<meterPerSecond>
                     Vz = 0.0<meterPerSecond> } |]

        { Profiles = profiles
          BasePositions = basePositions
          SlotCount = rolesAndPos.Length
          StarterCount = rolesAndPos.Length }

    let defaultCtx () : MatchContext =
        // Fixture tactics mirror engine scales (intensities 0..1, line in
        // meters would be realistic but mid intensities keep sims neutral);
        // Familiarity 0.5 matches the unseen-formation default in clubRef.
        { HomeClub =
            { Id = 1UL
              Name = "Home FC"
              BaseTactics =
                { Formation = "4-3-3"
                  DefensiveLine = 0.5f
                  PressIntensity = 0.5f
                  Width = 0.5f
                  Tempo = 0.5f
                  Directness = 0.5f
                  Mentality = 0.5f
                  PressTriggerZone = 0.5f }
              Chemistry = 0.6
              AttackPrior = Map.empty
              Familiarity = 0.5f
              HomeAdvantage =
                { CrowdSupport = 0.5f
                  Familiarity = 0.5f } }
          AwayClub =
            { Id = 2UL
              Name = "Away FC"
              BaseTactics =
                { Formation = "4-4-2"
                  DefensiveLine = 0.4f
                  PressIntensity = 0.4f
                  Width = 0.6f
                  Tempo = 0.5f
                  Directness = 0.4f
                  Mentality = 0.5f
                  PressTriggerZone = 0.5f }
              Chemistry = 0.6
              AttackPrior = Map.empty
              Familiarity = 0.5f
              HomeAdvantage =
                { CrowdSupport = 0.5f
                  Familiarity = 0.5f } }
          HomeLineup = makeLineup 0 11
          AwayLineup = makeLineup 100 11
          HomeParticipants = MatchLineupBoundary.fromLineup (makeLineup 0 11)
          AwayParticipants = MatchLineupBoundary.fromLineup (makeLineup 100 11)
          SubstitutionRules =
            { MaxSubstitutions = BalanceConfig.defaultConfig.Manager.MaxSubs
              WindowStarts =
                [| BalanceConfig.defaultConfig.Manager.FirstSubWindowMin
                   BalanceConfig.defaultConfig.Manager.SecondSubWindowMin
                   BalanceConfig.defaultConfig.Manager.ThirdSubWindowMin |]
              AllowExtraTime = true }
          Config = BalanceConfig.defaultConfig
          TieResolution = MatchTieResolution.AllowDraw }

    let emptyBall: BallState =
        { Position =
            { X = 0.0<meter>
              Y = 0.0<meter>
              Z = 0.0<meter>
              Vx = 0.0<meterPerSecond>
              Vy = 0.0<meterPerSecond>
              Vz = 0.0<meterPerSecond> }
          Radius = BalanceConfig.defaultBall.Radius
          Spin =
            { X = 0.0<radianPerSecond>
              Y = 0.0<radianPerSecond>
              Z = 0.0<radianPerSecond> }
          Orientation = System.Numerics.Quaternion.Identity
          FootMotion = None
          PendingStrike = None
          Control = BallControl.Free
          Trajectory = None
          LastTouch = None
          StationarySince = None
          GKHoldSince = None
          ArrivalGate = None
          PreviousPosition = ValueNone
          Impulse = None }

    let restartState (kind: SetPieceKind) (side: ClubSide) (spot: Vec2) (phase: RestartPhase) : RestartState =
        { Id = RestartId 1UL
          Cause = MatchStart
          Kind = kind
          Side = side
          Spot = spot
          AwardTick = 0<subtick>
          Plan = None
          Phase = phase
          CommandIssued = false
          ContactObserved = false
          Replanned = false }

    let makeTeamWorld (lineup: Lineup) : TeamWorld =
        let n = lineup.SlotCount
        let allocF32 () = Array.init n (fun _ -> 0.0f)

        let frame =
            { PosX = allocF32 ()
              PosY = allocF32 ()
              VelX = allocF32 ()
              VelY = allocF32 ()
              PosZ = allocF32 ()
              VelZ = allocF32 ()
              TargetVelX = allocF32 ()
              TargetVelY = allocF32 ()
              TargetFacing = allocF32 ()
              JumpRequested = Array.create n false
              JumpSequence = Array.zeroCreate<uint64> n
              BodyFacing = allocF32 ()
              BodyAngularVelocity = allocF32 ()
              BodyAngularAcceleration = allocF32 ()
              BodyTravelDistance = allocF32 ()
              ContactGeneration = Array.zeroCreate<int> n
              ContactState = Array.create n BodyContactState.stable
              ContactActions = Array.create n None
              Condition = Array.init n (fun _ -> 1.0f)
              Mental =
                { Composure = allocF32 ()
                  Confidence = allocF32 ()
                  Aggression = allocF32 ()
                  Focus = allocF32 ()
                  Risk = allocF32 () }
              Intent =
                { Kind = Array.init n (fun _ -> IntentCode.None)
                  TargetX = allocF32 ()
                  TargetY = allocF32 ()
                  Phase = Array.init n (fun _ -> IntentPhase.None)
                  CommittedUntil = Array.init n (fun _ -> 0<subtick>)
                  CommittedAt = Array.init n (fun _ -> 0<subtick>)
                  ExitTrigger = Array.init n (fun _ -> ExitTrigger.None) }
              Occupancy = Array.init n (fun i -> OccupancyKind.Active i)
              SlotRoles = Array.init n (fun i -> lineup.Profiles.[i].Role)
              DefensiveRoles = Array.init n (fun _ -> DefensiveRole.Unassigned)
              CollectiveIntents =
                Array.init n (fun _ ->
                    { Pressing = 1.0f
                      Holding = 1.0f
                      Attacking = 1.0f
                      Balanced = 1.0f })
              SupportX = allocF32 ()
              SupportY = allocF32 ()
              VisibilityTm = Array.init n (fun _ -> true)
              VisibilityOpp = Array.init n (fun _ -> true)
              OppsNear = Array.zeroCreate<int> n
              PressCount = Array.zeroCreate<int> n
              LaneClear = Array.init n (fun _ -> true)
              FaceCone = Array.init n (fun _ -> true)
              Cognitive = Array.init n (fun _ -> 1.0f)
              RoleBias = Array.init n (fun _ -> 1.0f)
              RunLookup = System.Collections.Generic.Dictionary<int, RunAssignment>()
              ActiveCount = n
              SlotCount = n
              ActiveCache = ActiveSlotsCache() }

        { Frame = frame
          Tactics = (defaultCtx ()).HomeClub.BaseTactics
          RosterIds = lineup.Profiles |> Array.map _.Id
          Memory =
            Array.init n (fun _ ->
                { LastTouchTick = 0<subtick>
                  SuccessfulDuels = 0
                  FailedDuels = 0
                  PassesMade = 0
                  PassesCompleted = 0
                  ShotsTaken = 0
                  Yellows = 0
                  CriticalFlagged = false
                  PassFailMod = 0.0
                  PassSuccessMod = 0.0
                  DuelFailMod = 0.0
                  DuelSuccessMod = 0.0
                  ShotSuccessMod = 0.0
                  StreakMod = 0.0
                  LastMemoryDecayTick = 0<subtick> })
          Cards = { Yellows = 0; Reds = 0 }
          SubsUsed = 0
          Shape =
            { TargetsX = allocF32 ()
              TargetsY = allocF32 () }
          Directive =
            { Kind = DirectiveKind.Balanced
              SinceTick = 0<subtick>
              Params =
                { LineHeight = 42.0f
                  PressIntensity = 0.5f
                  Width = 0.5f } }
          Blackboard =
            { OurPhase = PhaseKind.Attacking
              OpponentShape =
                { TargetsX = allocF32 ()
                  TargetsY = allocF32 () }
              OpponentPressure = 0
              ThreatZone = 0.0f
              WeaknessZone = 0.0f
              Urgency = 0.0f
              MomentumStreak = 0 }
          Defense = DefensiveTransitionState.create n
          Emergent =
            { Compactness = 0.5f
              PressingIntensity = 0.5f
              WingplayBias = 0.5f
              FatigueFactor = 1.0f
              ChannelCenter = 0.0f
              ChannelLeft = 0.0f
              ChannelRight = 0.0f
              DefenseLine = 0.0f }
          Intercept =
            { ComputedAt = 0<subtick>
              SelfStep = 0
              TeammateStep = 0
              OpponentStep = 0
              BallStep = 0
              SlotSteps = Array.create n 0 }
          Stats =
            { Shots = 0
              OnTarget = 0
              Passes = 0
              PassesCompleted = 0
              Fouls = 0
              Corners = 0
              Offsides = 0
              Touches = 0
              Dribbles = 0
              Crosses = 0
              XgSum = 0.0 }
          CFrame =
            { LastPerceivedTick = 0<subtick>
              LastDecidedTick = 0<subtick>
              SupportAttackComputed = false }
          EventWindow = EventWindow.create 1024
          Substitution =
            { Phase = SubstitutionPhase.NoSubstitution
              Outgoing = None
              Incoming = None
              RequestedAt = None
              AppliedAt = None
              UsedIncoming = Set.empty }
          Participation = ParticipationBook.create ()
          PendingActions = System.Collections.Generic.Dictionary<int, PendingPlayerAction>()
          Attack = CollectiveAttackState.create Map.empty
          LastCarrierSlot = -1
          LastRoleAssignmentTick = -1<subtick>
          FormationCache = None
          LateralPairs = None
          GoalkeeperSlot = None
          OwnPositions = Array.empty
          MirroredPositions = Array.empty
          FormationTargets = Array.empty
          EvaluatorScratch = Array.empty }

    let minimalWorld () : World =
        let ctx = defaultCtx ()

        { State =
            { Tick = 0<subtick>
              Clock =
                { Tick = 0<subtick>
                  Phase = ClockPhase.FirstHalf }
              Flow = MatchFlow.Live
              HomeScore = 0
              AwayScore = 0
              Ball = emptyBall
              Momentum = 0.0
              LastAttackingClub = None
              Possession =
                { HomeTouches = 0
                  AwayTouches = 0
                  LastGainer = None }
              Stoppage =
                { FirstHalfAdded = 0<subtick>
                  SecondHalfAdded = 0<subtick>
                  Decided = false
                  Half1Fingerprint = 0 }
              Advantage = AdvantageState.NoAdvantage
              VARReview = None
              Shootout =
                { HomeGoals = 0
                  AwayGoals = 0
                  HomeKicks = 0
                  AwayKicks = 0
                  Scheduled = false
                  Complete = false } }
          Officials =
            { Referee = { X = 0.0<meter>; Y = 0.0<meter> }
              NearAssistant = { X = 0.0<meter>; Y = -Pitch.HalfWidth }
              FarAssistant = { X = 0.0<meter>; Y = Pitch.HalfWidth }
              Signal = RefereeSignal.NoSignal }
          Home = makeTeamWorld ctx.HomeLineup
          Away =
            { makeTeamWorld ctx.AwayLineup with
                Tactics = ctx.AwayClub.BaseTactics }
          AppliedEventIds = System.Collections.Generic.HashSet<EventId>()
          Observation = ObservationBuffer(4096)
          Reviews = RefereeReviewBook()
          Motion = MotionEvidenceJournal(256)
          Contact = ContactScratch(ctx.HomeLineup.SlotCount + ctx.AwayLineup.SlotCount)
          NextRestartId = 1UL
          NextContactId = 1UL }

    let worldWithBallAt (x: float<meter>) (y: float<meter>) (world: World) : World =
        world.State <-
            { world.State with
                Ball =
                    { world.State.Ball with
                        Position =
                            { world.State.Ball.Position with
                                X = x
                                Y = y } } }

        world

    let worldWithScore (home: int) (away: int) (world: World) : World =
        world.State <-
            { world.State with
                HomeScore = home
                AwayScore = away }

        world

    /// A ctx whose two lineups use the realistic 4-3-3 base positions.
    let formationCtx () : MatchContext =
        { (defaultCtx ()) with
            HomeLineup = makeFormationLineup 0
            AwayLineup = makeFormationLineup 100 }

    /// A world built from the realistic formation lineups, with each team's
    /// frame pre-placed on its base positions (as MatchFactory does).
    let formationWorld () : World =
        let ctx = formationCtx ()
        let world = minimalWorld ()

        for side, lineup in [ ClubSide.Home, ctx.HomeLineup; ClubSide.Away, ctx.AwayLineup ] do
            let team = MatchWorld.team world side

            for i in 0 .. lineup.SlotCount - 1 do
                let bx = lineup.BasePositions.[i]
                let x = if side = ClubSide.Home then bx.X else -bx.X
                team.Frame.PosX.[i] <- float32 x
                team.Frame.PosY.[i] <- float32 bx.Y

        world

module TestDataLock =

    let valid: FootballEngine.Domain.DataLock =
        { FormatVersion = 1
          EngineVersion = "test-engine"
          ContentApi = 1
          Packages = []
          DependencyLockHash = "sha-test-dependencies"
          ResolvedSportsHash = "sha-test-sports"
          ResolvedPresentationHash = "sha-test-presentation"
          RuntimeIdTable = []
          Snapshot =
            { SportsContent = "sha-test-sports"
              Translations = "sha-test-translations"
              Assets = "sha-test-assets" }
          RequiredAssets = [] }
