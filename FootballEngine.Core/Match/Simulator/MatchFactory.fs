namespace FootballEngine.Core.Match

/// Builds a fresh World for every run so matches share no mutable state: every
/// array is allocated here and nothing falls back to defaults — identical seeds
/// therefore produce identical matches.
module MatchFactory =

    let private makeTeam (ctx: MatchContext) (side: ClubSide) (lineup: Lineup) : TeamWorld =
        let n = lineup.SlotCount
        let mutable frame = TeamFrame.create n

        for i in 0 .. n - 1 do
            let p = lineup.Profiles.[i]
            frame.SlotRoles.[i] <- p.Role

            let bx = lineup.BasePositions.[i]
            let x = if side = ClubSide.Home then bx.X else -bx.X
            frame.PosX.[i] <- float32 x
            frame.PosY.[i] <- float32 bx.Y

        let activeCount = min n 11

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

        { Frame = frame
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
              MomentumStreak = 0
              TransitionSince = 0<subtick> }
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
          EventWindow = EventWindow.create ctx.Config.Weights.Timing.EventWindowSubTicks }

    let create (ctx: MatchContext) : World =
        let kickoffDelay =
            Flow.delayFor ctx.Config SimulationClock.defaultClock SetPieceKind.KickOff

        { State =
            { Tick = 0<subtick>
              Clock =
                { Tick = 0<subtick>
                  Phase = ClockPhase.FirstHalf }
              Flow =
                MatchFlow.RestartDelay(
                    kickoffDelay,
                    { Kind = SetPieceKind.KickOff
                      Side = ClubSide.Home
                      X = 0.0<meter>
                      Y = 0.0<meter> }
                )
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
                  Spin =
                    { Top = 0.0<radianPerSecond>
                      Side = 0.0<radianPerSecond> }
                  Control = BallControl.Free
                  Trajectory = None
                  LastTouch = None
                  StationarySince = None
                  GKHoldSince = None
                  ArrivalGate = None }
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
                  Half1Fingerprint = 0 } }
          Home = makeTeam ctx ClubSide.Home ctx.HomeLineup
          Away = makeTeam ctx ClubSide.Away ctx.AwayLineup }
