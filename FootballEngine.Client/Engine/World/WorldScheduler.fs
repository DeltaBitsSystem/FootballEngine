namespace FootballEngine.World

open FootballEngine.Domain
open FootballEngine.World.AgentTypes
open FootballEngine.World.Phases
open FootballEngine.World.Supervisors
open FootballEngine.World.Agents

module WorldScheduler =

    let private buildPhases (state: GameState) : WorldPhase list =
        let financeAgents =
            state.Clubs |> Map.toList |> List.map (fst >> ClubAgent.makeFinance)

        let youthAgents = state.Clubs |> Map.toList |> List.map (fst >> ClubAgent.makeYouth)
        let financePhase = mkPhase financeAgents FinanceSupervisor.resolve Finance Daily
        let youthPhase = mkPhase youthAgents YouthSupervisor.resolve Youth Seasonal

        [ financePhase
          MatchPhase.make
          TransferPhase.make state
          DevelopmentPhase.make
          youthPhase
          SeasonPhase.make ]

    let tickWith (exclude: Set<PhaseTag>) (clock: WorldClock) (state: GameState) : GameState * WorldClock =
        let newState =
            buildPhases state
            |> List.filter (fun p -> not (exclude.Contains p.Tag))
            |> List.filter (fun p -> p.Frequency = OnDemand || WorldTime.shouldRun p.Frequency clock.Current)
            |> List.fold (fun s p -> p.Run clock s) state

        newState, WorldClockOps.advance clock

    let tick (clock: WorldClock) (state: GameState) : GameState * WorldClock = tickWith Set.empty clock state
