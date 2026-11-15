namespace FootballEngine.Core.Match

/// Builds a fresh World for every run so matches share no mutable state: every
/// array is allocated here and nothing falls back to defaults — identical seeds
/// therefore produce identical matches.
module MatchFactory =

    let private makeTeam
        (ctx: MatchContext)
        (side: ClubSide)
        (lineup: Lineup)
        (boundary: MatchLineupBoundary)
        : TeamWorld =
        let n = lineup.SlotCount
        let mutable frame = TeamFrame.create n

        for i in 0 .. n - 1 do
            let p = lineup.Profiles.[i]
            frame.SlotRoles.[i] <- p.Role

            match boundary.Eligibility |> Map.tryFind p.Id with
            | Some true ->
                let participant =
                    Array.append boundary.Starters boundary.Substitutes
                    |> Array.tryFind (fun candidate -> candidate.Id = p.Id)

                frame.Condition.[i] <- participant |> Option.map _.InitialCondition |> Option.defaultValue 1.0f
            | _ -> frame.Condition.[i] <- 0.0f

            let bx = lineup.BasePositions.[i]
            let x = if side = ClubSide.Home then bx.X else -bx.X
            frame.PosX.[i] <- float32 x
            frame.PosY.[i] <- float32 bx.Y
            frame.BodyFacing.[i] <- if side = ClubSide.Home then 0.0f else System.MathF.PI

        let activeCount = min n lineup.StarterCount

        for i in activeCount .. n - 1 do
            frame.Occupancy.[i] <-
                OccupancyKind.Sidelined
                    { Id = lineup.Profiles.[i].Id
                      Reason = PlayerOutReason.Substituted }

        frame.ActiveCount <- activeCount

        let memory =
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

        let mutable shape =
            { TargetsX = Array.zeroCreate<float32> n
              TargetsY = Array.zeroCreate<float32> n }

        for i in 0 .. n - 1 do
            shape.TargetsX.[i] <- frame.PosX.[i]
            shape.TargetsY.[i] <- frame.PosY.[i]

        let participation = ParticipationBook.create ()

        for i in 0 .. activeCount - 1 do
            let profile = lineup.Profiles.[i]
            ParticipationBook.start participation profile.Id profile.Role 0<subtick>
            frame.ContactGeneration.[i] <- ParticipationBook.generation participation profile.Id

        { Frame = frame
          Tactics = (if side = ClubSide.Home then ctx.HomeClub else ctx.AwayClub).BaseTactics
          RosterIds = lineup.Profiles |> Array.map _.Id
          Memory = memory
          Cards = { Yellows = 0; Reds = 0 }
          SubsUsed = 0
          Shape = shape
          Directive =
            { Kind = DirectiveKind.Balanced
              SinceTick = 0<subtick>
              Params =
                { LineHeight = 42.0f
                  PressIntensity = 0.5f
                  Width = 0.5f } }
          Blackboard =
            { OurPhase = PhaseKind.Defending
              OpponentShape =
                { TargetsX = Array.zeroCreate<float32> n
                  TargetsY = Array.zeroCreate<float32> n }
              OpponentPressure = 0
              ThreatZone = 0.5f
              WeaknessZone = 0.5f
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
          Brain = ManagerBrainState()
          CFrame =
            { LastPerceivedTick = 0<subtick>
              LastDecidedTick = 0<subtick>
              SupportAttackComputed = false }
          EventWindow = EventWindow.create ctx.Config.Match.EventWindowCapacity
          Substitution =
            { Phase = SubstitutionPhase.NoSubstitution
              Outgoing = None
              Incoming = None
              RequestedAt = None
              AppliedAt = None
              UsedIncoming = Set.empty }
          Participation = participation
          Proposals = System.Collections.Generic.Dictionary<int, PendingPlayerAction>()
          PendingActions = System.Collections.Generic.Dictionary<int, PendingPlayerAction>()
          Latched =
            { OurPhase = PhaseKind.Defending
              InTransition = false
              PendingSlots = 0UL }
          Attack =
            CollectiveAttackState.create (
                if side = ClubSide.Home then
                    ctx.HomeClub.AttackPrior
                else
                    ctx.AwayClub.AttackPrior
            )
          LastCarrierSlot = -1
          LastRoleAssignmentTick = -1<subtick>
          Dive = ValueNone
          FormationCache = None
          LateralPairs = None
          GoalkeeperSlot = lineup.Profiles |> Array.tryFindIndex (fun p -> p.Role = SlotRole.Goalkeeper)
          OwnPositions = Array.empty
          MirroredPositions = Array.empty
          FormationTargets = Array.empty
          EvaluatorScratch = Array.empty }

    let create (ctx: MatchContext) : World =
        let home = makeTeam ctx ClubSide.Home ctx.HomeLineup ctx.HomeParticipants
        let away = makeTeam ctx ClubSide.Away ctx.AwayLineup ctx.AwayParticipants

        let shootout: ShootoutBook =
            { HomeGoals = 0
              AwayGoals = 0
              HomeKicks = 0
              AwayKicks = 0
              Scheduled = false
              Complete = false }

        let state =
            { Tick = 0<subtick>
              Clock =
                { Tick = 0<subtick>
                  Phase = ClockPhase.FirstHalf }
              Flow = MatchFlow.PreMatch
              HomeScore = 0
              AwayScore = 0
              Ball =
                { Position =
                    { X = 0.0<meter>
                      Y = 0.0<meter>
                      Z = 0.0<meter>
                      Vx = 0.0<meterPerSecond>
                      Vy = 0.0<meterPerSecond>
                      Vz = 0.0<meterPerSecond> }
                  Radius = ctx.Config.Ball.Radius
                  Spin =
                    { X = 0.0<radianPerSecond>
                      Y = 0.0<radianPerSecond>
                      Z = 0.0<radianPerSecond> }
                  Orientation = System.Numerics.Quaternion.Identity
                  FootMotion = None
                  AnnouncedTouch = ValueNone
                  PendingStrike = None
                  Control = BallControl.Free
                  Trajectory = None
                  LastTouch = None
                  StationarySince = None
                  GKHoldSince = None
                  ArrivalGate = None
                  PreviousPosition = ValueNone
                  FrameContact = ValueNone
                  Impulse = None }
              Momentum = 0.0
              LastAttackingClub = Some ClubSide.Home
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
              Shootout = shootout }

        { State = state
          Officials =
            { Referee = { X = 0.0<meter>; Y = 0.0<meter> }
              NearAssistant = { X = 0.0<meter>; Y = -Pitch.HalfWidth }
              FarAssistant = { X = 0.0<meter>; Y = Pitch.HalfWidth }
              Signal = RefereeSignal.NoSignal }
          Home = home
          Away = away
          AppliedEventIds = System.Collections.Generic.HashSet<EventId>()
          Cognition = CognitionSide()
          Observation = ObservationBuffer(4096)
          Reviews = RefereeReviewBook()
          Motion = MotionEvidenceJournal(256)
          Contact = ContactScratch(home.Frame.SlotCount + away.Frame.SlotCount)
          NextRestartId = 1UL
          NextContactId = 1UL }
