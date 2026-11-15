namespace FootballEngine.Core.Match

/// What a rival manager sees when he looks at the match, already reduced to the numbers his decision needs.
[<Struct>]
type ManagerRead =
    { Minute: int
      /// Goals scored minus goals conceded by his side.
      ScoreDiff: int
      /// Share of the recent xG he created, -1 (all theirs) to 1 (all his), before his own misjudgement.
      Momentum: float
      MinutesSinceMove: int
      /// A goal or a red card since his last move.
      EventSinceMove: bool
      OwnRedSinceReview: bool
      Current: ManagerShift voption }

/// A rival manager's decision: the shift he announces, the directive that carries it and the changes that go with it.
[<Struct>]
type ManagerMove =
    { Shift: ManagerShift
      Directive: DirectiveKind
      /// Multiplies the tactical shift of the move: parking the bus asks for twice the retreat of defending.
      Intensity: float
      Intent: SubstitutionIntent }

/// The decision rules of a rival manager, free of any match state so they can be tested on a table of reads.
module ManagerBrain =

    let private clamp (lo: float) (hi: float) (v: float) = max lo (min hi v)

    let private lerp (a: float) (b: float) (t: float) = a + (b - a) * t

    /// Minutes between a goal, a red card or half-time and the review it causes: slow readers and calm coaches wait.
    let reactionDelayMinutes (cfg: RivalManagerConfig) (profile: ManagerProfile) : float =
        cfg.MaxReactionDelayMin
        * (1.0 - float profile.Reading)
        * (1.0 - float profile.Temperament * cfg.TemperamentHaste)

    /// Whole-minute review time: a review can only happen on a minute the coach can act in.
    let reviewMinuteAfter (cfg: RivalManagerConfig) (profile: ManagerProfile) (minute: int) : int =
        minute + int (ceil (reactionDelayMinutes cfg profile))

    /// Hotheads change the plan again sooner than calm coaches do.
    let minutesBetweenMoves (cfg: RivalManagerConfig) (profile: ManagerProfile) : int =
        int (
            round (
                lerp (float cfg.MinMinutesBetweenMoves) (float cfg.HotheadMinutesBetweenMoves) (float profile.Temperament)
            )
        )

    /// Momentum from the decayed xG each side created; the epsilon keeps a quiet spell from reading as a siege.
    let momentum (cfg: RivalManagerConfig) (xgFor: float) (xgAgainst: float) : float =
        (xgFor - xgAgainst) / (xgFor + xgAgainst + cfg.MomentumEpsilon)

    let private move shift directive intensity intent : ManagerMove voption =
        ValueSome
            { Shift = shift
              Directive = directive
              Intensity = intensity
              Intent = intent }

    /// `noise` is a standard-normal draw: the coach misjudges the momentum by it, scaled by how badly he reads the game.
    let decide (cfg: RivalManagerConfig) (profile: ManagerProfile) (read: ManagerRead) (noise: float) : ManagerMove voption =
        let boldness = float profile.Boldness
        let reading = float profile.Reading

        let perceived =
            clamp -1.0 1.0 (read.Momentum + noise * cfg.ReadNoise * (1.0 - reading))

        let current = read.Current
        let isCurrent shift = current = ValueSome shift
        let chaseMinute = int (round (lerp (float cfg.ChaseMinuteCautious) (float cfg.ChaseMinuteBold) boldness))

        let protectMinute =
            int (round (lerp (float cfg.ProtectMinuteCautious) (float cfg.ProtectMinuteBold) boldness))

        let losing = read.ScoreDiff < 0
        let leading = read.ScoreDiff > 0

        if read.OwnRedSinceReview && not (isCurrent ManagerShift.RedCardReshape) then
            // A red card cannot wait for the pause between moves: ten men change shape now.
            move ManagerShift.RedCardReshape DirectiveKind.Defend 1.0 SubstitutionIntent.Defend
        elif read.MinutesSinceMove < minutesBetweenMoves cfg profile && not read.EventSinceMove then
            ValueNone
        elif
            losing
            && read.ScoreDiff >= -1
            && read.Minute >= cfg.AllOutMinute
            && boldness >= cfg.AllOutBoldness
            && not (isCurrent ManagerShift.AllOut)
        then
            move ManagerShift.AllOut DirectiveKind.Attack 1.0 SubstitutionIntent.Attack
        elif
            losing
            && read.Minute >= chaseMinute
            && not (isCurrent ManagerShift.ChaseGame)
            && not (isCurrent ManagerShift.AllOut)
        then
            move ManagerShift.ChaseGame DirectiveKind.Attack 1.0 SubstitutionIntent.Attack
        elif leading && read.Minute >= protectMinute && not (isCurrent ManagerShift.ProtectLead) then
            if boldness <= cfg.ParkBusBoldness then
                move ManagerShift.ProtectLead DirectiveKind.ParkTheBus 2.0 SubstitutionIntent.Defend
            else
                move ManagerShift.ProtectLead DirectiveKind.Defend 1.0 SubstitutionIntent.Defend
        elif
            read.Minute >= cfg.ReadSettleMinute
            && perceived <= -cfg.StormMomentum
            && not losing
            && not (isCurrent ManagerShift.WeatherStorm)
            && not (isCurrent ManagerShift.ProtectLead)
        then
            move ManagerShift.WeatherStorm DirectiveKind.Defend 1.0 SubstitutionIntent.Defend
        elif
            read.Minute >= cfg.ReadSettleMinute
            && perceived >= cfg.InitiativeMomentum
            && read.ScoreDiff = 0
            && boldness >= cfg.InitiativeBoldness
            && not (isCurrent ManagerShift.TakeInitiative)
        then
            move ManagerShift.TakeInitiative DirectiveKind.Attack 1.0 SubstitutionIntent.Attack
        else
            // The cause of the current shift is gone: the coach returns to the plan he started with.
            let causeGone =
                match current with
                | ValueSome ManagerShift.ChaseGame
                | ValueSome ManagerShift.AllOut -> not losing
                | ValueSome ManagerShift.ProtectLead -> not leading
                | ValueSome ManagerShift.WeatherStorm -> perceived > -cfg.StormMomentum * 0.5
                | ValueSome ManagerShift.TakeInitiative ->
                    read.ScoreDiff <> 0 || perceived < cfg.InitiativeMomentum * 0.5
                | ValueSome ManagerShift.RedCardReshape
                | ValueSome ManagerShift.BackToPlan
                | ValueNone -> false

            if causeGone then
                move ManagerShift.BackToPlan DirectiveKind.Balanced 0.0 SubstitutionIntent.Keep
            else
                ValueNone
