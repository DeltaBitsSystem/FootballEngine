namespace FootballEngine.World.Phases

open FootballEngine.Core.Match
open FootballEngine.Domain
open FsToolkit.ErrorHandling

/// Builds a MatchContext from the persisted game state. The only place the
/// World layer talks to the match engine.
module MatchContextBuilder =

    let private metersX (v: float) : float<meter> = (v - 0.5) * Pitch.Length

    let private metersY (v: float) : float<meter> = (v - 0.5) * Pitch.Width

    let private readinessOf (t: TrainingConfig) (p: Player) : float32 =
        let w = float32 t.ReadinessConditionWeight

        let mixed =
            w * (float32 p.Condition / 100.0f)
            + (1.0f - w) * (float32 p.MatchFitness / 100.0f)

        float32 t.ReadinessFloor + (1.0f - float32 t.ReadinessFloor) * mixed

    let private roleOfPosition (position: Position) : SlotRole =
        match position with
        | Position.GK -> SlotRole.Goalkeeper
        | Position.DC -> SlotRole.Defender
        | Position.DL
        | Position.DR
        | Position.WBL
        | Position.WBR -> SlotRole.FullBack
        | Position.DM -> SlotRole.DefensiveMid
        | Position.MC -> SlotRole.CenterMid
        | Position.AMC -> SlotRole.AttackingMid
        | Position.ML
        | Position.MR
        | Position.AML
        | Position.AMR -> SlotRole.Winger
        | Position.ST -> SlotRole.Striker

    let private eligibleForMatch (clubId: ClubId) (player: Player) : bool =
        Player.clubOf player = Some clubId && player.Status = Available

    let private lineupOf
        (gs: GameState)
        (clubId: ClubId)
        (training: TrainingConfig)
        : (FootballEngine.Core.Match.Lineup * MatchLineupBoundary) option =
        match GameState.headCoach clubId gs, gs.Clubs |> Map.tryFind clubId with
        | Some coach, Some club ->
            match coach.Attributes.Coaching.Lineup with
            | None -> None
            | Some domainLineup ->
                let slots = domainLineup.Slots |> List.sortBy _.Index

                let starterData =
                    slots
                    |> List.choose (fun slot ->
                        slot.PlayerId
                        |> Option.bind (fun playerId ->
                            gs.Players |> Map.tryFind playerId |> Option.map (fun player -> slot, player)))

                let starterIds = starterData |> List.map (snd >> _.Id)

                let validStarters =
                    starterData.Length = 11
                    && starterIds |> List.distinct |> List.length = starterIds.Length
                    && starterData |> List.forall (snd >> eligibleForMatch clubId)

                if not validStarters then
                    None
                else
                    let starterSet = starterIds |> Set.ofList

                    let benchPlayers =
                        club.PlayerIds
                        |> List.distinct
                        |> List.choose (fun playerId -> gs.Players |> Map.tryFind playerId)
                        |> List.filter (fun player ->
                            not (Set.contains player.Id starterSet) && eligibleForMatch clubId player)

                    let starterProfiles =
                        starterData
                        |> List.map (fun (slot, player) ->
                            { Projection.toProfile player with
                                Id = FootballEngine.Core.Match.PlayerId player.Id
                                Role = roleOfPosition slot.Role }
                            |> Projection.withReadiness (readinessOf training player))

                    let benchProfiles =
                        benchPlayers
                        |> List.map (fun player ->
                            Projection.toProfile player
                            |> Projection.withReadiness (readinessOf training player))

                    let profiles =
                        Array.append (Array.ofList starterProfiles) (Array.ofList benchProfiles)

                    let starterPositions =
                        starterData
                        |> List.map (fun (slot, _) ->
                            { X = metersX slot.X
                              Y = metersY slot.Y
                              Z = 0.0<meter>
                              Vx = 0.0<meterPerSecond>
                              Vy = 0.0<meterPerSecond>
                              Vz = 0.0<meterPerSecond> })
                        |> Array.ofList

                    let benchPositions =
                        Array.create
                            benchPlayers.Length
                            { X = 0.0<meter>
                              Y = 0.0<meter>
                              Z = 0.0<meter>
                              Vx = 0.0<meterPerSecond>
                              Vy = 0.0<meterPerSecond>
                              Vz = 0.0<meterPerSecond> }

                    let lineup =
                        { Profiles = profiles
                          BasePositions = Array.append starterPositions benchPositions
                          SlotCount = profiles.Length
                          StarterCount = starterData.Length }

                    let roster =
                        club.PlayerIds
                        |> List.distinct
                        |> List.choose (fun playerId -> gs.Players |> Map.tryFind playerId)

                    let roles =
                        roster
                        |> List.map (fun player ->
                            FootballEngine.Core.Match.PlayerId player.Id, roleOfPosition player.Position)
                        |> Map.ofList

                    let eligibility =
                        roster
                        |> List.map (fun player ->
                            FootballEngine.Core.Match.PlayerId player.Id, eligibleForMatch clubId player)
                        |> Map.ofList

                    let participantOf (profile: PlayerProfile) (condition: float32) =
                        { Id = profile.Id
                          Role = profile.Role
                          Eligible = true
                          InitialCondition = condition }

                    let starters =
                        starterData
                        |> List.mapi (fun index (_, player) ->
                            participantOf profiles.[index] (float32 player.Condition / 100.0f))
                        |> Array.ofList

                    let substitutes =
                        benchPlayers
                        |> List.mapi (fun index player ->
                            participantOf profiles.[starterData.Length + index] (float32 player.Condition / 100.0f))
                        |> Array.ofList

                    Some(
                        lineup,
                        { Starters = starters
                          Substitutes = substitutes
                          Eligibility = eligibility
                          Roles = roles }
                    )
        | _ -> None

    /// Slider units (ints 0..4) mean nothing to the engine: intensities run 0..1
    /// and the line runs in meters (~20 deep .. ~60 high). Normalizing here keeps
    /// every consumer honest; raw slider math made 1..4 behave identically.
    let private tacticsOf (lu: Lineup) : TacticsConfig =
        let plan = TacticalPlan.fromLineup lu
        let i = plan.Instructions

        let norm (v: int) = float32 v / 4.0f

        { Formation = string plan.Formation
          DefensiveLine = 20.0f + float32 i.DefensiveLine * 10.0f
          PressIntensity = norm i.PressingIntensity
          Width = norm i.Width
          Tempo = norm i.Tempo
          Directness = norm i.Directness
          Mentality = norm i.Mentality
          PressTriggerZone = norm i.PressTriggerZone }

    /// The signature is what the scouting memory keys its trust on: the same
    /// identity check CounterPlan.ofMemory runs, so both sides agree on what
    /// counts as "the rival changed".
    let private signatureOf (lu: Lineup) : string * TacticalInstructions =
        lu |> TacticalPlan.fromLineup |> TacticalPlan.signature

    /// The counter-planning injection seam: the club counters only rivals it
    /// has seen before, and a rival it has never met gets the club's own plan.
    let adjustTactics
        (counterConfig: CounterConfig)
        (mem: Map<ClubId, OpponentMemory>)
        (opponent: ClubId)
        (opponentSignature: string * TacticalInstructions)
        (baseTactics: TacticsConfig)
        : TacticsConfig =
        match mem |> Map.tryFind opponent with
        | None -> baseTactics
        | Some memory ->
            CounterPlan.apply counterConfig baseTactics (CounterPlan.ofMemory counterConfig opponentSignature memory)

    let private clubRef
        (counter: CounterConfig)
        (gs: GameState)
        (clubId: ClubId)
        (rivalId: ClubId)
        (rivalSignature: string * TacticalInstructions)
        (lu: Lineup)
        : ClubRef =
        let club = gs.Clubs[clubId]

        { Id = uint64 clubId
          Name = club.Name
          // The counter leans on what the club already believes about this
          // specific rival; no memory means the club's own tactics stand.
          BaseTactics = adjustTactics counter club.ScoutingMemory rivalId rivalSignature (tacticsOf lu)
          Chemistry = club.CoordinationMemory.PressingCoordination
          AttackPrior = club.CoordinationMemory.AttackLearning
          // Unfamiliar shapes execute the plan sloppily; drilled shapes snap.
          // New shapes start at 0.5, never zero — eleven professionals still know football.
          Familiarity =
            club.CoordinationMemory.TacticalFamiliarity
            |> Map.tryFind (string lu.Formation)
            |> Option.defaultValue 0.5
            |> float32
          HomeAdvantage =
            { CrowdSupport = 1.0f
              Familiarity = 1.0f } }

    let build (gs: GameState) (fixture: MatchFixture) (config: BalanceConfig) : MatchContext option =
        option {
            let! hc = GameState.headCoach fixture.HomeClubId gs
            let! ac = GameState.headCoach fixture.AwayClubId gs
            let! hlu = hc.Attributes.Coaching.Lineup
            let! alu = ac.Attributes.Coaching.Lineup
            let! hl, hp = lineupOf gs fixture.HomeClubId config.Training
            let! al, ap = lineupOf gs fixture.AwayClubId config.Training

            let substitutionRules =
                { MaxSubstitutions = config.Manager.MaxSubs
                  WindowStarts =
                    [| config.Manager.FirstSubWindowMin
                       config.Manager.SecondSubWindowMin
                       config.Manager.ThirdSubWindowMin |]
                  AllowExtraTime = true }

            return
                { HomeClub = clubRef config.Counter gs fixture.HomeClubId fixture.AwayClubId (signatureOf alu) hlu
                  AwayClub = clubRef config.Counter gs fixture.AwayClubId fixture.HomeClubId (signatureOf hlu) alu
                  HomeLineup = hl
                  AwayLineup = al
                  HomeParticipants = hp
                  AwayParticipants = ap
                  SubstitutionRules = substitutionRules
                  Config = config
                  TieResolution =
                    if MatchFixture.isKnockout fixture then
                        MatchTieResolution.ExtraTimeThenPenalties
                    else
                        MatchTieResolution.AllowDraw }
        }

    /// Save-aware helper: derives effective BalanceConfig from SaveSettings (Save > default).
    let buildWithSave
        (gs: GameState)
        (fixture: MatchFixture)
        (saveOpt: FootballEngine.Settings.SaveSettings option)
        : MatchContext option =
        let balance = WeightMergeSettings.mergeWithOverridesSave saveOpt
        build gs fixture balance

    // Fixture identity splits one execution's stream without depending on worker order.
    let seedOf (executionSeed: uint64) (fixtureId: MatchId) : uint64 =
        MatchSeed.ofMatch (uint64 fixtureId) 0UL executionSeed

    let unwrapPid (FootballEngine.Core.Match.PlayerId n) = n

    /// Injured player ids from the engine event stream.
    let injuredOf (events: MatchEvent list) : Set<PlayerId> =
        events
        |> List.choose (fun ev ->
            match ev.Data with
            | EventData.Injury(player, _) -> Some(unwrapPid player)
            | _ -> None)
        |> Set.ofList
