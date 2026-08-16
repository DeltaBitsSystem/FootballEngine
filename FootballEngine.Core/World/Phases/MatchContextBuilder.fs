namespace FootballEngine.World.Phases

open FootballEngine.Core.Match
open FootballEngine.Domain
open FsToolkit.ErrorHandling

/// Builds a MatchContext from the persisted game state. The only place the
/// World layer talks to the match engine.
module MatchContextBuilder =

    let private metersX (v: float) : float<meter> = (v - 0.5) * Pitch.Length

    let private metersY (v: float) : float<meter> = (v - 0.5) * Pitch.Width

    let private lineupOf (gs: GameState) (clubId: ClubId) : FootballEngine.Core.Match.Lineup option =
        match GameState.headCoach clubId gs with
        | None -> None
        | Some coach ->
            coach.Attributes.Coaching.Lineup
            |> Option.map (fun lu ->
                let slots =
                    lu.Slots |> List.filter (fun s -> s.PlayerId.IsSome) |> List.sortBy _.Index

                let profiles =
                    slots
                    |> List.map (fun s -> Projection.toProfile gs.Players[s.PlayerId.Value])
                    |> Array.ofList

                let basePositions =
                    slots
                    |> List.map (fun s ->
                        { X = metersX s.X
                          Y = metersY s.Y
                          Z = 0.0<meter>
                          Vx = 0.0<meterPerSecond>
                          Vy = 0.0<meterPerSecond>
                          Vz = 0.0<meterPerSecond> })
                    |> Array.ofList

                { Profiles = profiles
                  BasePositions = basePositions
                  SlotCount = slots.Length })

    let private tacticsOf (lu: Lineup) : TacticsConfig =
        let i =
            lu.Instructions |> Option.defaultValue TacticalInstructions.defaultInstructions

        { Formation = string lu.Formation
          DefensiveLine = float32 i.DefensiveLine
          PressIntensity = float32 i.PressingIntensity
          Width = float32 i.Width
          Tempo = float32 i.Tempo
          Directness = float32 i.Directness }

    let private clubRef (gs: GameState) (clubId: ClubId) (lu: Lineup) : ClubRef =
        let club = gs.Clubs[clubId]

        { Id = uint64 clubId
          Name = club.Name
          BaseTactics = tacticsOf lu
          Chemistry = club.CoordinationMemory.PressingCoordination
          HomeAdvantage =
            { CrowdSupport = 1.0f
              Familiarity = 1.0f } }

    let build (gs: GameState) (fixture: MatchFixture) (config: BalanceConfig) : MatchContext option =
        option {
            let! hc = GameState.headCoach fixture.HomeClubId gs
            let! ac = GameState.headCoach fixture.AwayClubId gs
            let! hlu = hc.Attributes.Coaching.Lineup
            let! alu = ac.Attributes.Coaching.Lineup
            let! hl = lineupOf gs fixture.HomeClubId
            let! al = lineupOf gs fixture.AwayClubId

            return
                { HomeClub = clubRef gs fixture.HomeClubId hlu
                  AwayClub = clubRef gs fixture.AwayClubId alu
                  HomeLineup = hl
                  AwayLineup = al
                  Config = config
                  IsKnockout = MatchFixture.isKnockout fixture }
        }

    /// Deterministic per-fixture seed: stable replays across sessions.
    let seedOf (fixtureId: MatchId) : uint64 =
        uint64 fixtureId * 2654435761UL + 1013904223UL

    let unwrapPid (FootballEngine.Core.Match.PlayerId n) = n

    /// Injured player ids from the engine event stream.
    let injuredOf (events: MatchEvent list) : Set<PlayerId> =
        events
        |> List.choose (fun ev ->
            match ev.Data with
            | EventData.Injury(player, _) -> Some(unwrapPid player)
            | _ -> None)
        |> Set.ofList
