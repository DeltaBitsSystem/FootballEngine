namespace FootballEngine.Core.Match

/// How a coach of one philosophy lines up when nothing about the opponent changes his mind. The formation is a
/// `Formation` case name ("F433"): a string keeps the preset overridable from the JSON config like every other value.
/// The values are absolute slider positions (0..4).
type PhilosophyPreset =
    { PreferredFormation: string
      Values: FootballEngine.Domain.TacticalInstructions }

/// Rival managers: how a coach studies the user, which plan he prepares, how he reads and reacts inside the match,
/// and how his skill moves the background model. Coach skill (CA/PA, 0..200) enters only through `reading` and
/// `learning` (CA/200 and PA/200), so every number here is a plain gameplay value.
type RivalManagerConfig =
    { // Study of the user's recipe.
        /// How many of the user's recent engine matches the world keeps for rivals to read.
        StudyWindow: int
        /// Awareness gained per recipe match at learning 1: 1 - exp(-matches * learning * rate). At 0.3 a coach with
        /// PA 180 needs about two matches of a winning recipe to react to it and one with PA 80 about five.
        StudyRate: float
        /// Points per game under which the user's recipe is no threat at all; the threat scales linearly up to 3.
        ThreatPpgFloor: float
        /// Gaussian noise (CA points) on the perceived strength of the user's squad for a coach with reading 0.
        StrengthNoise: float
        /// A perceived gap in CA points below which the coach sees himself as the clear underdog.
        UnderdogGap: float
        // Approach selection.
        /// Threat above which a coach with enough adaptability prepares an answer to the recipe.
        CounterThreatMin: float
        /// Imitation a coach needs before he copies the recipe that beat him.
        CopyImitationMin: int
        /// Share of his best formation's suitability a copied formation must keep, so he never copies a shape his squad cannot play.
        CopySuitabilityMin: float
        /// Pride a coach needs to seek revenge.
        RevengePrideMin: int
        /// Goal margin of a loss that counts as a humiliation.
        HumiliationMargin: int
        /// Same bar as CopySuitabilityMin for any other formation switch (preferred shape, counter shape).
        FormationSuitabilityMin: float
        /// Instruction steps a counter moves at full intensity (threat x reading).
        CounterMaxSteps: float
        /// Intensity from which a counter also changes the formation.
        CounterFormationIntensity: float
        /// Corridor share above which the user's pattern counts as centre-heavy.
        CentreHeavyShare: float
        /// Left plus right corridor share above which the user's pattern counts as wide.
        WideHeavyShare: float
        /// Long-pass share above which the user's pattern counts as direct.
        DirectLongPassShare: float
        /// Short-pass share above which the user's pattern counts as possession play.
        PossessionShortPassShare: float
        /// Boldness (1..20) from which a coach answers a possession side by pressing rather than sitting deep.
        PressAnswerBoldness: int
        /// Shift (relative slider steps, so negative values are valid) applied to a coach's own identity when he seeks revenge: braver and higher up the pitch.
        RevengeShift: FootballEngine.Domain.TacticalInstructions
        /// Shift for a cautious underdog: deeper, slower to commit, direct on the break.
        UnderdogCautiousShift: FootballEngine.Domain.TacticalInstructions
        /// Shift for a bold underdog: he takes the fight to the stronger side.
        UnderdogBoldShift: FootballEngine.Domain.TacticalInstructions
        /// Boldness (1..20) from which an underdog is bold.
        UnderdogBoldnessPivot: int
        /// How much a tired starter costs in the selection score of a coach who reads the squad perfectly.
        RotationWeight: float
        PossessionPreset: PhilosophyPreset
        PressingPreset: PhilosophyPreset
        CounterPreset: PhilosophyPreset
        DirectPreset: PhilosophyPreset
        FortressPreset: PhilosophyPreset
        // Inside the match.
        /// Regular review cadence in match minutes.
        ReviewIntervalMin: int
        /// Longest a coach with reading 0 and temperament 0 waits to react to a goal, a red card or half-time.
        MaxReactionDelayMin: float
        /// Share of the reaction delay a temperament of 1 removes: hotheads react on the spot.
        TemperamentHaste: float
        /// Minutes between two moves of a calm coach.
        MinMinutesBetweenMoves: int
        /// Minutes between two moves of a coach with temperament 1.
        HotheadMinutesBetweenMoves: int
        /// Gaussian noise on the perceived momentum (-1..1) of a coach with reading 0.
        ReadNoise: float
        /// xG added to both sides of the momentum ratio, so a quiet spell cannot read as total domination.
        MomentumEpsilon: float
        /// Minutes for the xG a coach remembers to count half: his read of who is on top spans spells, not one chance.
        MomentumHalfLifeMin: float
        /// Before this minute a coach does not judge who dominates: the opening exchanges say nothing yet.
        ReadSettleMinute: int
        /// Perceived momentum at or under minus this reads as being overrun.
        StormMomentum: float
        /// Perceived momentum at or over this reads as dominating.
        InitiativeMomentum: float
        /// Minute a bold coach (boldness 1) goes to chase a deficit.
        ChaseMinuteBold: int
        /// Minute a cautious coach (boldness 0) goes to chase a deficit.
        ChaseMinuteCautious: int
        /// From this minute a bold coach throws everything at a one-goal deficit.
        AllOutMinute: int
        /// Boldness (0..1) from which a coach goes all out.
        AllOutBoldness: float
        /// Minute a cautious coach starts protecting a lead.
        ProtectMinuteCautious: int
        /// Minute a bold coach starts protecting a lead.
        ProtectMinuteBold: int
        /// Boldness (0..1) under which a coach parks the bus instead of merely defending.
        ParkBusBoldness: float
        /// Boldness (0..1) from which a coach takes the initiative in a level match he dominates.
        InitiativeBoldness: float
        /// Reading from which a coach swaps players by role instead of by raw rating, and reads card risk.
        SmartChangeReadingMin: float
        /// Minute after which a coach with enough reading replaces a booked defender.
        CardRiskMinute: int
        /// Temperament (0..1) from which a coach is a hothead: he changes again sooner and doubles up at half-time.
        HotheadTemperament: float
        /// Changes a hothead who is losing at half-time makes at once.
        HotheadHalfTimeChanges: int
        /// Mentality (engine scale 0..1) a move adds or, for the three drops, takes away, so the move survives the next
        /// directive review.
        ChaseMentalityShift: float
        AllOutMentalityShift: float
        ProtectMentalityDrop: float
        StormMentalityDrop: float
        InitiativeMentalityShift: float
        RedCardMentalityDrop: float
        /// Line height change (meters) for attacking and defending moves.
        AttackLineShift: float
        DefendLineShift: float
        // Background model and seasons.
        /// Mean-attribute points a full unit of reading difference is worth between two coaches in a background match.
        /// At 4 the best coach against the worst (0.75 apart) gains 3 attribute points, a ~5% scoring edge.
        CoachStrengthPoints: float
        /// Age until which a coach's CA still grows toward his PA.
        CoachPeakEndAge: int
        /// Share of the gap to PA a young coach closes each season.
        CoachGrowthRate: float
        /// CA a coach loses each season past his peak.
        CoachDeclinePerYear: int
        /// Lowest CA any coach can fall to.
        CoachSkillFloor: int
        /// Years a rivalry record is kept without a new meeting.
        RivalMemoryYears: int
    }
