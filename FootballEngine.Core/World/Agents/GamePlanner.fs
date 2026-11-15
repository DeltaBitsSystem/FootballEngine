namespace FootballEngine.World.Agents

open System
open FootballEngine.Core.Match
open FootballEngine.Domain

/// What the world knows about how the user has been playing: the recipe he leans on and how well it works.
type UserRecipe =
    { Formation: Formation
      Instructions: TacticalInstructions
      /// How many of the study's matches used this recipe.
      Matches: int
      /// Points per game the recipe earned.
      Ppg: float
      /// Mean play pattern over the matches that used it; matches with no readable pattern are left out.
      Pattern: PatternVector }

/// The rival coach's plan for the user's fixture, prepared before kickoff and deterministic per fixture:
/// what the preview shows is what the match gets.
module GamePlanner =

    let private clamp01 (v: float) = max 0.0 (min 1.0 v)

    // ---- Study of the user's recipe ----

    /// Instruction values collapse to three bands (low, mid, high): a rival cannot tell a 2 from a 3 on the
    /// touchline, but he can tell a high line from a deep one.
    let private bandsOf (i: TacticalInstructions) : int list =
        [ i.Mentality
          i.DefensiveLine
          i.PressingIntensity
          i.Width
          i.Tempo
          i.Directness
          i.PressTriggerZone
          i.DefensiveShape ]
        |> List.map (fun v -> min 2 (v / 2))

    let private recipeKey (r: UserMatchRecord) : Formation * int list = r.Formation, bandsOf r.Instructions

    /// Points the user took from a match.
    let private pointsOf (r: UserMatchRecord) : float =
        if r.GoalsFor > r.GoalsAgainst then 3.0
        elif r.GoalsFor = r.GoalsAgainst then 1.0
        else 0.0

    let private meanPattern (records: UserMatchRecord list) : PatternVector =
        let readable =
            records
            |> List.map _.Pattern
            |> List.filter (fun p -> p.CorridorLeft + p.CorridorCenter + p.CorridorRight > 0.0)

        match readable with
        | [] -> PatternVector.empty
        | _ ->
            let mean (f: PatternVector -> float) = readable |> List.averageBy f

            { PatternVector.empty with
                CorridorLeft = mean _.CorridorLeft
                CorridorCenter = mean _.CorridorCenter
                CorridorRight = mean _.CorridorRight
                PassShort = mean _.PassShort
                PassMedium = mean _.PassMedium
                PassLong = mean _.PassLong
                Directness = mean _.Directness
                CrossRate = mean _.CrossRate
                ShotNear = mean _.ShotNear
                ShotArea = mean _.ShotArea
                ShotFar = mean _.ShotFar }

    /// The signature the user used most in the study window; ties go to the most recent one, because a habit
    /// that is still current matters more than one that stopped.
    let dominantRecipe (cfg: RivalManagerConfig) (gs: GameState) : UserRecipe option =
        let study = gs.UserStudy |> List.truncate cfg.StudyWindow

        match study with
        | [] -> None
        | _ ->
            let counted =
                study
                |> List.mapi (fun index r -> index, recipeKey r)
                |> List.groupBy snd
                |> List.map (fun (key, entries) -> key, List.length entries, entries |> List.map fst |> List.min)

            let key, matches, _ =
                counted |> List.sortBy (fun (_, count, newest) -> -count, newest) |> List.head

            let used = study |> List.filter (fun r -> recipeKey r = key)
            let latest = List.head used

            let recipe: UserRecipe =
                { Formation = latest.Formation
                  Instructions = latest.Instructions
                  Matches = matches
                  Ppg = used |> List.averageBy pointsOf
                  Pattern = meanPattern used }

            Some recipe

    /// How many of his matches a rival has had to notice the recipe: the faster he learns, the sooner it shows.
    let awareness (cfg: RivalManagerConfig) (coach: Staff) (recipeMatches: int) : float =
        1.0 - exp (-(float recipeMatches) * ManagerMind.learning coach * cfg.StudyRate)

    /// How dangerous the recipe looks to this coach: what he has noticed of it, weighted by how much it wins.
    let threat (cfg: RivalManagerConfig) (coach: Staff) (recipe: UserRecipe option) : float =
        match recipe with
        | None -> 0.0
        | Some r ->
            let winning = clamp01 ((r.Ppg - cfg.ThreatPpgFloor) / (3.0 - cfg.ThreatPpgFloor))
            awareness cfg coach r.Matches * winning

    // ---- Squad reading ----

    let private availablePlayers (gs: GameState) (clubId: ClubId) : Player list =
        GameState.getSquad clubId gs |> List.filter (fun p -> p.Status = Available)

    /// Greedy by slot order, like quick pick: deterministic, and a slot never takes a player already used.
    let private fillSlots (score: Player -> Position -> float) (slots: LineupSlot list) (players: Player list) =
        let _, picks =
            ((players, []), slots)
            ||> List.fold (fun (pool, acc) slot ->
                match pool with
                | [] -> pool, acc
                | _ ->
                    let best = pool |> List.sortBy (fun p -> -(score p slot.Role), p.Id) |> List.head
                    pool |> List.filter (fun p -> p.Id <> best.Id), (slot, best) :: acc)

        List.rev picks

    /// How naturally the squad fills a formation: mean star rating of the best players for its slots.
    let squadSuitability (players: Player list) (formation: Formation) : float =
        fillSlots (fun p role -> TacticalSuitability.stars p role) (FormationData.getFormation formation) players
        |> List.averageBy (fun (slot, p) -> TacticalSuitability.stars p slot.Role)

    let private strengthOf (players: Player list) : float =
        match players |> List.sortByDescending _.CurrentSkill |> List.truncate 11 with
        | [] -> 0.0
        | top -> top |> List.averageBy (fun p -> float p.CurrentSkill)

    let private parseFormation (name: string) : Formation option =
        FormationLineups.all |> List.tryFind (fun f -> string f = name)

    let private presetOf (cfg: RivalManagerConfig) (philosophy: CoachPhilosophy) : PhilosophyPreset =
        match philosophy with
        | CoachPhilosophy.Possession -> cfg.PossessionPreset
        | CoachPhilosophy.Pressing -> cfg.PressingPreset
        | CoachPhilosophy.Counter -> cfg.CounterPreset
        | CoachPhilosophy.Direct -> cfg.DirectPreset
        | CoachPhilosophy.Fortress -> cfg.FortressPreset

    /// Instruction sliders live on 0..4, so every shift is clamped back onto the scale.
    let private shifted (shift: TacticalInstructions) (i: TacticalInstructions) : TacticalInstructions =
        let add v d = max 0 (min 4 (v + d))

        { Mentality = add i.Mentality shift.Mentality
          DefensiveLine = add i.DefensiveLine shift.DefensiveLine
          PressingIntensity = add i.PressingIntensity shift.PressingIntensity
          Width = add i.Width shift.Width
          Tempo = add i.Tempo shift.Tempo
          Directness = add i.Directness shift.Directness
          PressTriggerZone = add i.PressTriggerZone shift.PressTriggerZone
          DefensiveShape = add i.DefensiveShape shift.DefensiveShape }

    let private tacticsLabelOf (i: TacticalInstructions) : TeamTactics =
        if i.PressingIntensity >= 3 then TeamTactics.Pressing
        elif i.Mentality >= 3 then TeamTactics.Attacking
        elif i.Mentality <= 1 then TeamTactics.Defensive
        else TeamTactics.Balanced

    // ---- The plan ----

    /// Where the coach's own football leads him: his philosophy's shape if the squad can play it, otherwise the
    /// shape the squad suits best.
    let private identityFormation
        (cfg: RivalManagerConfig)
        (coach: Staff)
        (players: Player list)
        (suit: Formation -> float)
        (best: float)
        : Formation =
        let preferred =
            coach.Attributes.Coaching.PreferredFormation
            |> Option.orElse (parseFormation (presetOf cfg coach.Mind.Philosophy).PreferredFormation)

        match preferred with
        | Some f when suit f >= cfg.FormationSuitabilityMin * best -> f
        | _ -> FormationLineups.all |> List.maxBy (fun f -> suit f, -(List.findIndex ((=) f) FormationLineups.all))

    /// The shape that answers an attacking one: a lone holder against trios up front, a flat four against a crowded middle.
    let private counterFormation (recipe: Formation) : Formation =
        match recipe with
        | F433
        | F343
        | F523 -> F4141
        | F4231
        | F4321
        | F4312
        | F3421
        | F442Diamond -> F442
        | F352
        | F532
        | F541 -> F433Flat
        | F442
        | F433Flat
        | F451
        | F4141 -> F4141

    /// The deltas that answer what the user's pattern does, scaled by threat x reading and spent on the slider
    /// the pattern leans on: close the middle against a central side, open up against a wide one, drop the line
    /// against long balls, press or sit deep against a side that keeps the ball.
    let private counterInstructions
        (cfg: RivalManagerConfig)
        (coach: Staff)
        (intensity: float)
        (pattern: PatternVector)
        (identity: TacticalInstructions)
        : TacticalInstructions =
        let steps = max 1 (int (round (cfg.CounterMaxSteps * intensity)))
        let wide = pattern.CorridorLeft + pattern.CorridorRight
        let slide (value: int) (delta: int) = max 0 (min 4 (value + delta))

        let width =
            if pattern.CorridorCenter >= cfg.CentreHeavyShare then
                slide identity.Width -steps
            elif wide >= cfg.WideHeavyShare then
                slide identity.Width steps
            else
                identity.Width

        let line =
            if pattern.PassLong >= cfg.DirectLongPassShare then
                slide identity.DefensiveLine -steps
            else
                identity.DefensiveLine

        let line, press =
            if pattern.PassShort >= cfg.PossessionShortPassShare then
                if coach.Mind.Boldness >= cfg.PressAnswerBoldness then
                    line, slide identity.PressingIntensity steps
                else
                    slide line -steps, slide identity.PressingIntensity -1
            else
                line, identity.PressingIntensity

        { identity with
            Width = width
            DefensiveLine = line
            PressingIntensity = press }

    /// The best XI for a shape: each slot takes the player with the best suitability, discounted by tiredness
    /// in proportion to how well the coach reads the squad's condition.
    let private selectXi
        (cfg: RivalManagerConfig)
        (reading: float)
        (formation: Formation)
        (players: Player list)
        : LineupSlot list =
        let score (p: Player) (role: Position) =
            TacticalSuitability.stars p role
            * (1.0 - cfg.RotationWeight * reading * (1.0 - float p.Condition / 100.0))

        fillSlots score (FormationData.getFormation formation) players
        |> List.map (fun (slot, p) -> { slot with PlayerId = Some p.Id })

    /// One seed per coach and fixture: the preview and the match draw the same numbers whoever asks first.
    let seedFor (gs: GameState) (fixture: MatchFixture) (coachId: StaffId) : uint64 =
        MatchSeed.ofMatch (uint64 fixture.Id) (uint64 coachId) gs.ExecutionSeed

    /// The plan the rival coach `clubId` takes into `fixture` against the user. None for the user's own club, for a
    /// fixture without the user, and when the coach cannot field eleven.
    let prepare
        (cfg: RivalManagerConfig)
        (seed: uint64)
        (gs: GameState)
        (fixture: MatchFixture)
        (clubId: ClubId)
        : (Lineup * GamePlanNote) option =
        match GameState.managedClub gs, GameState.headCoach clubId gs with
        | Some userClub, Some coach when
            userClub <> clubId
            && MatchFixture.involves userClub fixture
            && MatchFixture.involves clubId fixture
            ->
            let players = availablePlayers gs clubId

            if players.Length < 11 then
                None
            else
                let reading = ManagerMind.reading coach
                // Fixed draw order: every approach consumes the same numbers whichever one wins.
                let noise, r1 = Rng.normal 0.0 1.0 (Rng.ofSeed seed)
                let copyDraw, r2 = Rng.double r1
                let counterDraw, _ = Rng.double r2

                let table =
                    FormationLineups.all |> List.map (fun f -> f, squadSuitability players f) |> Map.ofList

                let suit f = table[f]
                let best = table |> Map.values |> Seq.max
                let identity = identityFormation cfg coach players suit best
                let preset = presetOf cfg coach.Mind.Philosophy
                let identityInstructions = preset.Values

                let recipe = dominantRecipe cfg gs
                let danger = threat cfg coach recipe

                // Rivalries are between coaches: the one the user's club has now, whoever signed the contract.
                let rival =
                    GameState.headCoach userClub gs
                    |> Option.bind (fun userCoach -> coach.Mind.Rivals |> Map.tryFind userCoach.Id)

                let perceivedGap =
                    let theirs = strengthOf (availablePlayers gs userClub)
                    strengthOf players - (theirs + noise * cfg.StrengthNoise * (1.0 - reading))

                let copy =
                    rival
                    |> Option.filter (fun r -> r.LastMargin < 0)
                    |> Option.bind (fun r -> gs.UserStudy |> List.tryFind (fun s -> s.Date.Date = r.LastMet.Date))
                    |> Option.filter (fun s ->
                        coach.Mind.Imitation >= cfg.CopyImitationMin
                        && copyDraw < float coach.Mind.Imitation / 20.0
                        && suit s.Formation >= cfg.CopySuitabilityMin * best)

                let formation, instructions, approach =
                    match copy with
                    | Some s -> s.Formation, s.Instructions, CopyRecipe s.Formation
                    | None ->
                        let revenge =
                            rival
                            |> Option.exists (fun r ->
                                coach.Mind.Pride >= cfg.RevengePrideMin && r.LastMargin <= -cfg.HumiliationMargin)

                        match recipe with
                        | _ when revenge -> identity, shifted cfg.RevengeShift identityInstructions, Revenge
                        | Some r when
                            danger >= cfg.CounterThreatMin
                            && counterDraw < float coach.Mental.Adaptability / 20.0
                            ->
                            let intensity = danger * reading

                            let wanted = counterFormation r.Formation

                            let shape =
                                if intensity >= cfg.CounterFormationIntensity
                                   && suit wanted >= cfg.FormationSuitabilityMin * best then
                                    wanted
                                else
                                    identity

                            shape,
                            counterInstructions cfg coach intensity r.Pattern identityInstructions,
                            CounterRecipe r.Formation
                        | _ when perceivedGap < -cfg.UnderdogGap ->
                            let shift =
                                if coach.Mind.Boldness >= cfg.UnderdogBoldnessPivot then
                                    cfg.UnderdogBoldShift
                                else
                                    cfg.UnderdogCautiousShift

                            identity, shifted shift identityInstructions, Underdog
                        | _ -> identity, identityInstructions, OwnIdentity

                let lineup: Lineup =
                    { Formation = formation
                      Tactics = tacticsLabelOf instructions
                      Instructions = Some instructions
                      Slots = selectXi cfg reading formation players }

                let note: GamePlanNote =
                    { OpponentClubId = userClub
                      Date = fixture.ScheduledDate
                      Formation = formation
                      Approach = approach
                      Threat = danger }

                Some(lineup, note)
        | _ -> None

    /// The rival the user faces in this fixture, when the user is managing.
    let private rivalOf (gs: GameState) (fixture: MatchFixture) : ClubId option =
        GameState.managedClub gs
        |> Option.filter (fun userClub -> MatchFixture.involves userClub fixture)
        |> Option.map (fun userClub ->
            if fixture.HomeClubId = userClub then
                fixture.AwayClubId
            else
                fixture.HomeClubId)

    /// What the preview card shows: the same function with the same seed the match will use.
    let preview (cfg: RivalManagerConfig) (gs: GameState) (fixture: MatchFixture) : (Lineup * GamePlanNote) option =
        rivalOf gs fixture
        |> Option.bind (fun clubId ->
            GameState.headCoach clubId gs
            |> Option.bind (fun coach -> prepare cfg (seedFor gs fixture coach.Id) gs fixture clubId))

    /// Writes each plan into the rival's own coach; the managed club and every fixture without the user are left alone.
    let prepareForFixtures (cfg: RivalManagerConfig) (fixtures: (MatchId * MatchFixture) list) (gs: GameState) : GameState =
        (gs, fixtures)
        ||> List.fold (fun state (_, fixture) ->
            match rivalOf state fixture with
            | None -> state
            | Some clubId ->
                match GameState.headCoach clubId state, preview cfg state fixture with
                | Some coach, Some(lineup, note) ->
                    let planned =
                        { Staff.setLineup (Some lineup) coach with
                            Mind =
                                { coach.Mind with
                                    LastPlan = Some note } }

                    GameState.updateStaff planned state
                | _ -> state)

    // ---- Memory ----

    /// Both head coaches remember a fixture between their clubs, engine or background alike.
    let recordMeeting (fixture: MatchFixture) (homeGoals: int) (awayGoals: int) (gs: GameState) : GameState =
        match GameState.headCoach fixture.HomeClubId gs, GameState.headCoach fixture.AwayClubId gs with
        | Some home, Some away ->
            let home', away' =
                ManagerMind.recordResult fixture.ScheduledDate home away homeGoals awayGoals

            gs |> GameState.updateStaff home' |> GameState.updateStaff away'
        | _ -> gs

    /// Appends one of the user's engine matches to the study, newest first, within the window.
    let recordUserMatch (cfg: RivalManagerConfig) (record: UserMatchRecord) (gs: GameState) : GameState =
        { gs with
            UserStudy = record :: gs.UserStudy |> List.truncate cfg.StudyWindow }
