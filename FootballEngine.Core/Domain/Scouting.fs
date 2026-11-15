namespace FootballEngine.Domain

open System
open FootballEngine.Core.Match

type ScoutingTarget =
    | PlayerTarget of FootballEngine.Domain.PlayerId
    | PositionTarget of Position

type ScoutingAssignmentStatus =
    | AssignmentAssigned
    | AssignmentInProgress
    | AssignmentReady
    | AssignmentCancelled

type ScoutingAssignment =
    { Id: ScoutingAssignmentId
      Target: ScoutingTarget
      Scout: StaffId
      Cost: decimal
      Started: DateTime
      Due: DateTime
      Status: ScoutingAssignmentStatus }

type PlayerKnowledge =
    { Player: FootballEngine.Domain.PlayerId
      CurrentSkillRange: int * int
      PotentialRange: int * int
      WageRange: decimal * decimal
      Confidence: float
      ObservedOn: DateTime
      Sources: StaffId list
      ReportKey: string }

type RecruitmentNeed =
    { Position: Position
      DesiredRole: ContractRole
      MinimumMinutes: int
      Priority: int
      Reason: string }

type RecruitmentReport =
    { Assignment: ScoutingAssignmentId
      Player: FootballEngine.Domain.PlayerId
      Knowledge: PlayerKnowledge
      Evidence: string list }

module ScoutingAssignment =
    let assign id target scout cost started due =
        { Id = id
          Target = target
          Scout = scout
          Cost = cost
          Started = started
          Due = due
          Status = AssignmentAssigned }

module PlayerKnowledge =
    let stableKey (assignment: ScoutingAssignment) =
        $"scout:{assignment.Id}:{assignment.Scout}:{assignment.Due.Ticks}"

    let preserve (knowledge: PlayerKnowledge) (sameReportKey: string) = knowledge.ReportKey = sameReportKey

/// One club's play pattern over a match, expressed as fractions so matches of
/// different lengths are comparable: corridor usage, pass length, directness,
/// crossing rate and shot zones. The tactical signature (formation +
/// instructions) is stamped by the caller — it is known before kickoff, not
/// readable from on-pitch events.
type PatternVector =
    { CorridorLeft: float
      CorridorCenter: float
      CorridorRight: float
      PassShort: float
      PassMedium: float
      PassLong: float
      Directness: float
      CrossRate: float
      ShotNear: float
      ShotArea: float
      ShotFar: float
      Formation: string
      Instructions: TacticalInstructions }

/// What one club remembers about one rival: the last match plus the
/// decay-blended history, so a single hot match cannot masquerade as a habit.
type OpponentMemory =
    { OpponentId: ClubId
      Pattern: PatternVector
      Weighted: PatternVector
      MatchesSeen: float
      LastSeen: DateTime }

module PatternVector =

    let empty: PatternVector =
        { CorridorLeft = 0.0
          CorridorCenter = 0.0
          CorridorRight = 0.0
          PassShort = 0.0
          PassMedium = 0.0
          PassLong = 0.0
          Directness = 0.0
          CrossRate = 0.0
          ShotNear = 0.0
          ShotArea = 0.0
          ShotFar = 0.0
          // Empty means "no signature recorded", not a shape: an unknown
          // signature is a different thing from a changed one.
          Formation = ""
          Instructions = TacticalInstructions.defaultInstructions }

    /// The signature is the lineup's tactical identity: recorded once by the
    /// caller so the counter-plan can punish repetition of a known shape.
    let withSignature (formation: string) (instructions: TacticalInstructions) (v: PatternVector) : PatternVector =
        { v with
            Formation = formation
            Instructions = instructions }

/// File-local clamps: MatchMath lives in Shared.fs, which compiles after this
/// file, so re-declaring them here keeps the build order sane.
module internal ScoutMath =
    let clamp (lo: float) (hi: float) (v: float) : float = min hi (max lo v)
    let clamp01 (v: float) : float = clamp 0.0 1.0 v
    let clampF32 (lo: float32) (hi: float32) (v: float32) : float32 = min hi (max lo v)

module PatternExtractor =

    type private Counts =
        { Left: int
          Center: int
          Right: int
          Short: int
          Medium: int
          Long: int
          Forward: int
          Passes: int
          Crosses: int
          Possessions: int
          Near: int
          Area: int
          Far: int }

    let private zero: Counts =
        { Left = 0
          Center = 0
          Right = 0
          Short = 0
          Medium = 0
          Long = 0
          Forward = 0
          Passes = 0
          Crosses = 0
          Possessions = 0
          Near = 0
          Area = 0
          Far = 0 }

    /// The scouting fold must stay a pure function of the event list: same
    /// events, same vector, forever — persisted patterns are part of the
    /// deterministic save, so any hidden state would corrupt replays.
    let extract
        (cfg: ScoutConfig)
        (homeId: ClubId)
        (awayId: ClubId)
        (clubOfPlayer: PlayerId -> ClubId option)
        (events: MatchEvent list)
        : PatternVector =
        let clubOfSide (side: ClubSide) : ClubId =
            match side with
            | ClubSide.Home -> homeId
            | ClubSide.Away -> awayId

        // The API carries both clubs but no target, so the subject is read from
        // the events themselves: the club of the first action owns the pattern
        // and every other club's events are noise for this read.
        let subject =
            events
            |> List.tryPick (fun ev ->
                match ev.Data with
                | EventData.Pass(p, _, _, _, _, _, _, _, _)
                | EventData.Shot(p, _, _, _, _, _)
                | EventData.Dribble(p, _, _, _)
                | EventData.CrossAttempt(p, _, _) -> clubOfPlayer p
                | _ -> None)
            |> Option.orElseWith (fun () ->
                events
                |> List.tryPick (fun ev ->
                    match ev.Data with
                    | EventData.Possession(side, _) -> Some(clubOfSide side)
                    | _ -> None))

        match subject with
        | None -> PatternVector.empty
        | Some clubId ->
            // Home attacks +X and defends -X, so direction and the goal under
            // attack flip with the side — the corridor sign convention is the
            // same as TeamEmergent's (y <= -10 left, |y| < 10 center, y >= 10 right).
            let goalX =
                if clubId = homeId then
                    Pitch.HalfLength
                else
                    -Pitch.HalfLength

            let forwardOf (fromX: float<meter>) (toX: float<meter>) =
                if clubId = homeId then toX > fromX else toX < fromX

            let c =
                events
                |> List.fold
                    (fun (a: Counts) ev ->
                        match ev.Data with
                        | EventData.Possession(side, _) when clubOfSide side = clubId ->
                            { a with
                                Possessions = a.Possessions + 1 }
                        | EventData.Pass(passer, _, distance, _, _, fromX, fromY, toX, toY) when
                            clubOfPlayer passer = Some clubId
                            ->
                            let a =
                                if fromY <= -10.0<meter> then
                                    { a with Left = a.Left + 1 }
                                elif fromY >= 10.0<meter> then
                                    { a with Right = a.Right + 1 }
                                else
                                    { a with Center = a.Center + 1 }

                            let a =
                                if distance <= cfg.ShortPassMaxMeters then
                                    { a with Short = a.Short + 1 }
                                elif distance >= cfg.LongPassMinMeters then
                                    { a with Long = a.Long + 1 }
                                else
                                    { a with Medium = a.Medium + 1 }

                            { a with
                                Passes = a.Passes + 1
                                Forward = a.Forward + if forwardOf fromX toX then 1 else 0 }
                        | EventData.Shot(shooter, _, _, _, x, y) when clubOfPlayer shooter = Some clubId ->
                            let a =
                                if y <= -10.0<meter> then { a with Left = a.Left + 1 }
                                elif y >= 10.0<meter> then { a with Right = a.Right + 1 }
                                else { a with Center = a.Center + 1 }

                            let d = abs (float x - float goalX)

                            if d <= float cfg.NearShotMaxMeters then
                                { a with Near = a.Near + 1 }
                            elif d <= float cfg.AreaShotMaxMeters then
                                { a with Area = a.Area + 1 }
                            else
                                { a with Far = a.Far + 1 }
                        | EventData.Dribble(dribbler, _, x, y) when clubOfPlayer dribbler = Some clubId ->
                            if y <= -10.0<meter> then { a with Left = a.Left + 1 }
                            elif y >= 10.0<meter> then { a with Right = a.Right + 1 }
                            else { a with Center = a.Center + 1 }
                        | EventData.CrossAttempt(crosser, x, y) when clubOfPlayer crosser = Some clubId ->
                            let a =
                                if y <= -10.0<meter> then { a with Left = a.Left + 1 }
                                elif y >= 10.0<meter> then { a with Right = a.Right + 1 }
                                else { a with Center = a.Center + 1 }

                            { a with Crosses = a.Crosses + 1 }
                        | _ -> a)
                    zero

            let frac n total =
                if total = 0 then 0.0 else float n / float total

            let corridors = c.Left + c.Center + c.Right
            let passes = c.Short + c.Medium + c.Long
            let shots = c.Near + c.Area + c.Far

            let directness =
                if c.Passes = 0 then
                    0.0
                else
                    float c.Forward / float c.Passes

            let crossRate =
                if c.Possessions = 0 then
                    0.0
                else
                    float c.Crosses / float c.Possessions

            { PatternVector.empty with
                CorridorLeft = frac c.Left corridors
                CorridorCenter = frac c.Center corridors
                CorridorRight = frac c.Right corridors
                PassShort = frac c.Short passes
                PassMedium = frac c.Medium passes
                PassLong = frac c.Long passes
                Directness = directness
                CrossRate = crossRate
                ShotNear = frac c.Near shots
                ShotArea = frac c.Area shots
                ShotFar = frac c.Far shots
                // Events cannot reveal the lineup's shape; the caller stamps
                // the signature separately via PatternVector.withSignature.
                Formation = ""
                Instructions = TacticalInstructions.defaultInstructions }

module ScoutingMemory =

    let empty: Map<ClubId, OpponentMemory> = Map.empty

    /// Each sighting is a fresh observation; the weighted history decays
    /// exponentially, so a rival that changed its habits is read correctly
    /// instead of being held hostage by old matches.
    let update
        (cfg: ScoutConfig)
        (now: DateTime)
        (opponent: ClubId)
        (v: PatternVector)
        (mem: Map<ClubId, OpponentMemory>)
        : Map<ClubId, OpponentMemory> =
        match mem.TryFind opponent with
        | None ->
            mem.Add(
                opponent,
                { OpponentId = opponent
                  Pattern = v
                  Weighted = v
                  MatchesSeen = 1.0
                  LastSeen = now }
            )
        | Some old ->
            let deltaDays = (now - old.LastSeen).TotalDays
            let decay = exp (-deltaDays * log 2.0 / cfg.MemoryHalfLifeDays)
            let n = decay * old.MatchesSeen + 1.0
            let w = old.Weighted

            let weighted =
                { CorridorLeft = (decay * old.MatchesSeen * w.CorridorLeft + v.CorridorLeft) / n
                  CorridorCenter = (decay * old.MatchesSeen * w.CorridorCenter + v.CorridorCenter) / n
                  CorridorRight = (decay * old.MatchesSeen * w.CorridorRight + v.CorridorRight) / n
                  PassShort = (decay * old.MatchesSeen * w.PassShort + v.PassShort) / n
                  PassMedium = (decay * old.MatchesSeen * w.PassMedium + v.PassMedium) / n
                  PassLong = (decay * old.MatchesSeen * w.PassLong + v.PassLong) / n
                  Directness = (decay * old.MatchesSeen * w.Directness + v.Directness) / n
                  CrossRate = (decay * old.MatchesSeen * w.CrossRate + v.CrossRate) / n
                  ShotNear = (decay * old.MatchesSeen * w.ShotNear + v.ShotNear) / n
                  ShotArea = (decay * old.MatchesSeen * w.ShotArea + v.ShotArea) / n
                  ShotFar = (decay * old.MatchesSeen * w.ShotFar + v.ShotFar) / n
                  // The signature is a label, not a measurement: the newest
                  // sighting wins, there is nothing to average.
                  Formation = v.Formation
                  Instructions = v.Instructions }

            mem.Add(
                opponent,
                { old with
                    Pattern = v
                    Weighted = weighted
                    MatchesSeen = n
                    LastSeen = now }
            )

    /// Rivals unseen beyond the window are dropped so long saves don't grow
    /// unbounded — forgetting is a feature, not a leak.
    let prune (cfg: ScoutConfig) (now: DateTime) (mem: Map<ClubId, OpponentMemory>) : Map<ClubId, OpponentMemory> =
        mem
        |> Map.filter (fun _ m -> (now - m.LastSeen).TotalDays <= cfg.RivalPruneAfterDays)

type ScoutFinding =
    { Label: string
      Strength: float
      Confidence: float }

type ScoutReport =
    { SubjectId: ClubId
      ReaderId: ClubId
      Formation: string option
      Instructions: TacticalInstructions option
      Confidence: float
      Findings: ScoutFinding list }

module ScoutReport =

    let generate
        (cfg: ScoutConfig)
        (noise: float)
        (seed: uint64)
        (readerId: ClubId)
        (subjectId: ClubId)
        (mem: OpponentMemory)
        : ScoutReport =
        let candidates =
            [ "Left corridor", mem.Weighted.CorridorLeft
              "Center corridor", mem.Weighted.CorridorCenter
              "Right corridor", mem.Weighted.CorridorRight
              "Short passing", mem.Weighted.PassShort
              "Long passing", mem.Weighted.PassLong
              "Near-range shooting", mem.Weighted.ShotNear
              "Crossing", mem.Weighted.CrossRate
              "Direct play", mem.Weighted.Directness ]

        // Confidence counts sightings, not measurements: one lone match is not
        // enough to trust a pattern, so the count starts after the first
        // sighting — that is what keeps a 1-sighting memory below the floor.
        let confidence = min 1.0 ((mem.MatchesSeen - 1.0) / cfg.ConfidenceMatches)

        if confidence < cfg.MinConfidenceToReport then
            { SubjectId = subjectId
              ReaderId = readerId
              Formation = None
              Instructions = None
              Confidence = confidence
              Findings = [] }
        else
            // AI reads the truth (noise = 0); human staff distort strengths with
            // gaussian noise drawn from one seed-stable stream, so a report is
            // deterministic per seed and reproducible across replays.
            let distorted, _ =
                candidates
                |> List.mapFold
                    (fun rng (label, strength) ->
                        if noise <= 0.0 then
                            (label, strength), rng
                        else
                            let g, rng' = Rng.normal 0.0 noise rng
                            (label, ScoutMath.clamp01 (strength + g)), rng')
                    (Rng.ofSeed seed)

            let findings =
                distorted
                |> List.filter (fun (_, s) -> s > 0.0)
                |> List.sortByDescending snd
                |> List.truncate 3
                |> List.map (fun (label, strength) ->
                    { Label = label
                      Strength = strength
                      Confidence = confidence })

            { SubjectId = subjectId
              ReaderId = readerId
              // The signature counts as recorded only when the lineup stamped
              // it: a stock-instructions memory is a numeric-only memory, and
              // the scout will not claim a shape it never verified.
              Formation =
                if mem.Pattern.Instructions = TacticalInstructions.defaultInstructions then
                    None
                else
                    Some mem.Pattern.Formation
              Instructions =
                if mem.Pattern.Instructions = TacticalInstructions.defaultInstructions then
                    None
                else
                    Some mem.Pattern.Instructions
              Confidence = confidence
              Findings = findings }

type CounterPlan =
    { Active: bool
      SideBias: float
      PressBoost: float
      LineDrop: float
      WidthTighten: float
      TempoShift: float
      DirectnessShift: float }

module CounterPlan =

    let empty: CounterPlan =
        { Active = false
          SideBias = 0.0
          PressBoost = 0.0
          LineDrop = 0.0
          WidthTighten = 0.0
          TempoShift = 0.0
          DirectnessShift = 0.0 }

    /// The counter is tenacious: it applies at full strength against any rival
    /// with a sighting, and only a changed formation (the shape the memory
    /// recorded no longer matches the current one) scales it down — the pattern
    /// values shape WHERE the counter points (side bias), not whether it
    /// exists. Instructions are deliberately ignored: tempo/directness are
    /// tactics the rival can swap without changing identity, so treating them
    /// as identity would make the counter scale itself down for having done
    /// its own job.
    let ofMemory (cfg: CounterConfig) (current: string * TacticalInstructions) (mem: OpponentMemory) : CounterPlan =
        let active = mem.MatchesSeen >= 1.0

        if not active then
            empty
        else
            let formation, _ = current

            let s =
                // An unrecorded signature ("") is not a changed one: nobody can
                // tell the rival changed its shape, so the full counter stands.
                if mem.Pattern.Formation = "" || mem.Pattern.Formation = formation then
                    1.0
                else
                    cfg.SignatureChangePenalty

            let sideBias =
                ScoutMath.clamp
                    (-cfg.MaxSideBias)
                    cfg.MaxSideBias
                    ((mem.Weighted.CorridorRight - mem.Weighted.CorridorLeft) * cfg.BiasScale * s)

            { Active = true
              SideBias = sideBias
              PressBoost = cfg.MaxPressBoost * s
              LineDrop = cfg.MaxLineDrop * s
              WidthTighten = cfg.MaxWidthTighten * s
              TempoShift = cfg.MaxTempoShift * s
              DirectnessShift = cfg.MaxDirectnessShift * s }

    /// A counter is only as sharp as the coach who draws it: adaptability decides how far he is willing to bend his
    /// own plan and reading decides how accurately he aims it.
    let scale (factor: float) (plan: CounterPlan) : CounterPlan =
        if not plan.Active then
            plan
        else
            { plan with
                SideBias = plan.SideBias * factor
                PressBoost = plan.PressBoost * factor
                LineDrop = plan.LineDrop * factor
                WidthTighten = plan.WidthTighten * factor
                TempoShift = plan.TempoShift * factor
                DirectnessShift = plan.DirectnessShift * factor }

    /// Deltas are added onto the club's own slider tactics, never replacing
    /// its identity: a deep block stays a deep block, just slightly deeper.
    /// CounterConfig is authored in slider units; TacticsConfig runs engine
    /// scales, so boosts convert here (line drop is already meters).
    let apply (cfg: CounterConfig) (t: TacticsConfig) (plan: CounterPlan) : TacticsConfig =
        if not plan.Active then
            t
        else
            // SideBias is deliberately not applied: the engine has no lateral
            // bias field (that is Phase-2 plumbing); WidthTighten already
            // compresses the opponent's strong corridor.
            { t with
                PressIntensity = ScoutMath.clampF32 0.0f 1.0f (t.PressIntensity + float32 plan.PressBoost / 4.0f)
                DefensiveLine = max 15.0f (t.DefensiveLine - float32 plan.LineDrop)
                Width = ScoutMath.clampF32 0.0f 1.0f (t.Width - float32 plan.WidthTighten / 4.0f)
                Tempo = ScoutMath.clampF32 0.0f 1.0f (t.Tempo - float32 plan.TempoShift / 4.0f)
                Directness = ScoutMath.clampF32 0.0f 1.0f (t.Directness + float32 plan.DirectnessShift / 4.0f) }
