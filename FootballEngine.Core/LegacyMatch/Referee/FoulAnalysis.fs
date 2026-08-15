namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract

[<Struct>]
type FoulIntent =
    | Accidental
    | Tactical
    | Aggressive

type FoulContext =
    { Zone: PitchZone
      DistanceToGoal: float<meter>
      AttackersBetweenFoulAndGoal: int
      BallDirection: AttackDir
      FoulPosition: float<meter> * float<meter>
      Reckless: bool
      ExcessiveForce: bool
      Intent: FoulIntent
      LastMan: bool
      PromisingAttack: bool
      BallMovingTowardGoal: bool }

module FoulAnalysis =

    let private countAttackersBetween
        (foulX: float<meter>)
        (goalX: float<meter>)
        (dir: AttackDir)
        (defFrame: TeamFrame)
        (defRoster: PlayerRoster)
        : int =
        let mutable count = 0
        let forward = dir = LeftToRight

        for i = 0 to defFrame.SlotCount - 1 do
            match defFrame.Physics.Occupancy[i] with
            | OccupancyKind.Active rosterIdx ->
                let playerX = float defFrame.Physics.PosX[i] * 1.0<meter>

                let between =
                    if forward then
                        foulX < playerX && playerX <= goalX
                    else
                        goalX <= playerX && playerX < foulX

                if between then
                    count <- count + 1
            | _ -> ()

        count

    let assess
        (foulX: float<meter>)
        (foulY: float<meter>)
        (dir: AttackDir)
        (fouledTeam: ClubSide)
        (foulerAggression: int)
        (reckless: bool)
        (excessiveForce: bool)
        (homeFrame: TeamFrame)
        (homeRoster: PlayerRoster)
        (awayFrame: TeamFrame)
        (awayRoster: PlayerRoster)
        : FoulContext =

        let goalX =
            match dir with
            | LeftToRight -> PitchLength
            | RightToLeft -> 0.0<meter>

        let distToGoal = distToGoal foulX dir

        let (defFrame, defRoster) =
            if fouledTeam = HomeClub then
                (awayFrame, awayRoster)
            else
                (homeFrame, homeRoster)

        let attackersBetween = countAttackersBetween foulX goalX dir defFrame defRoster

        let zone =
            let effectiveX =
                match dir with
                | LeftToRight -> foulX
                | RightToLeft -> PitchLength - foulX

            if effectiveX < 30.0<meter> then DefensiveZone
            elif effectiveX <= 70.0<meter> then MidfieldZone
            else AttackingZone

        let lastMan = attackersBetween = 0 && zone = AttackingZone

        let promisingAttack =
            zone = AttackingZone || (zone = MidfieldZone && distToGoal < 40.0<meter>)

        let intent =
            if excessiveForce then
                Aggressive
            elif reckless then
                Aggressive
            elif distToGoal < 30.0<meter> && attackersBetween <= 1 then
                Tactical
            else
                Accidental

        { Zone = zone
          DistanceToGoal = distToGoal
          AttackersBetweenFoulAndGoal = attackersBetween
          BallDirection = dir
          FoulPosition = (foulX, foulY)
          Reckless = reckless
          ExcessiveForce = excessiveForce
          Intent = intent
          LastMan = lastMan
          PromisingAttack = promisingAttack
          BallMovingTowardGoal = promisingAttack }

    let classifySeverity (ctx: FoulContext) : FoulSeverity =
        if ctx.ExcessiveForce then
            SeriousFoulPlay
        elif ctx.Reckless && ctx.LastMan && ctx.PromisingAttack then
            DOGSO
        elif ctx.LastMan && ctx.PromisingAttack && ctx.DistanceToGoal < 25.0<meter> then
            DOGSO
        elif
            ctx.PromisingAttack
            && ctx.AttackersBetweenFoulAndGoal <= 1
            && ctx.Intent = Tactical
        then
            ProfessionalFoul
        elif ctx.PromisingAttack && ctx.Intent = Tactical then
            TacticalFoul
        elif ctx.Reckless then
            ProfessionalFoul
        else
            Trivial

    [<Struct>]
    type CardDecision =
        | Yellow
        | Red

    let decideCard (severity: FoulSeverity) (existingYellows: int) : CardDecision option =
        match severity, existingYellows with
        | Trivial, _ -> None
        | TacticalFoul, 0 -> Some Yellow
        | TacticalFoul, _ -> Some Red
        | ProfessionalFoul, 0 -> Some Yellow
        | ProfessionalFoul, _ -> Some Red
        | DOGSO, _ -> Some Red
        | SeriousFoulPlay, _ -> Some Red
        | ViolentConduct, _ -> Some Red

[<Struct>]
type FoulDecision =
    { Card: FoulAnalysis.CardDecision option
      Advantage: bool
      Reason: string }

module FoulAuthority =

    let classifyZone (ballX: float<meter>) (dir: AttackDir) : PitchZone =
        let effectiveX =
            match dir with
            | LeftToRight -> ballX
            | RightToLeft -> PitchLength - ballX

        if effectiveX < 30.0<meter> then DefensiveZone
        elif effectiveX <= 70.0<meter> then MidfieldZone
        else AttackingZone

    let private classifyMatchPhase (minute: float) : MatchPhase =
        if minute < 30.0 then BuildUp
        elif minute < 70.0 then Midfield
        else Attack

    let private profileCardThreshold (profile: RefereeProfile) : int =
        match profile with
        | Permissive -> 2
        | Balanced -> 1
        | Strict -> 0

    let private profileAdvantageWillingness (profile: RefereeProfile) : float =
        match profile with
        | Permissive -> 0.8
        | Balanced -> 0.6
        | Strict -> 0.3

    let private profileTolerance (profile: RefereeProfile) (rw: RefereeWeights) : float =
        let baseTolerance = rw.CardBaseProb

        match profile with
        | Permissive -> baseTolerance * 1.3
        | Balanced -> baseTolerance
        | Strict -> baseTolerance * 0.7

    let decide
        (severity: FoulSeverity)
        (foulerYellows: int)
        (foulerFoulCount: int)
        (zone: PitchZone)
        (phase: MatchPhase)
        (teamFoulCountThisHalf: int)
        (isAdvantagePreserved: bool)
        (profile: RefereeProfile)
        (homeBonus: bool)
        (matchImportance: float)
        (disciplineHistory: int)
        (rw: RefereeWeights)
        : FoulDecision =
        let yellowThreshold = profileCardThreshold profile
        let advantageWillingness = profileAdvantageWillingness profile
        let _tolerance = profileTolerance profile rw

        let adjustedYellows =
            if homeBonus then
                max 0 (foulerYellows - 1)
            else
                foulerYellows

        let importanceMult = 1.0 + (matchImportance - 0.5) * 0.2

        let disciplineMitigation =
            if disciplineHistory <= 2 then 0.1
            elif disciplineHistory >= 6 then -0.1
            else 0.0

        let effectiveAdvantageThreshold = advantageWillingness + disciplineMitigation

        match severity with
        | Trivial ->
            { Card = None
              Advantage = isAdvantagePreserved
              Reason = "" }
        | SeriousFoulPlay ->
            { Card = Some FoulAnalysis.Red
              Advantage = false
              Reason = "Serious foul play" }
        | ViolentConduct ->
            { Card = Some FoulAnalysis.Red
              Advantage = false
              Reason = "Violent conduct" }
        | DOGSO ->
            if zone = AttackingZone && adjustedYellows >= yellowThreshold then
                { Card = Some FoulAnalysis.Red
                  Advantage = false
                  Reason = "DOGSO — second yellow" }
            elif zone = AttackingZone then
                { Card = Some FoulAnalysis.Red
                  Advantage = false
                  Reason = "DOGSO — denying obvious goal-scoring opportunity" }
            else
                { Card = Some FoulAnalysis.Yellow
                  Advantage = false
                  Reason = "DOGSO — denied opportunity outside box" }
        | ProfessionalFoul ->
            if adjustedYellows >= yellowThreshold then
                { Card = Some FoulAnalysis.Red
                  Advantage = false
                  Reason = "Professional foul — second yellow" }
            elif zone = AttackingZone then
                { Card = Some FoulAnalysis.Yellow
                  Advantage = false
                  Reason = "Professional foul in attacking zone" }
            elif isAdvantagePreserved && importanceMult < 1.1 then
                { Card = None
                  Advantage = true
                  Reason = "Advantage played — professional foul" }
            else
                { Card = Some FoulAnalysis.Yellow
                  Advantage = false
                  Reason = "Professional foul" }
        | TacticalFoul ->
            if adjustedYellows >= yellowThreshold then
                { Card = Some FoulAnalysis.Red
                  Advantage = false
                  Reason = "Tactical foul — second yellow" }
            elif zone = AttackingZone && teamFoulCountThisHalf >= 4 then
                { Card = Some FoulAnalysis.Yellow
                  Advantage = false
                  Reason = "Tactical foul — team accumulation warning" }
            elif isAdvantagePreserved && float importanceMult * effectiveAdvantageThreshold > 0.8 then
                { Card = None
                  Advantage = true
                  Reason = "Advantage played — tactical foul" }
            elif isAdvantagePreserved then
                { Card = None
                  Advantage = true
                  Reason = "Advantage played — tactical foul" }
            else
                { Card = Some FoulAnalysis.Yellow
                  Advantage = false
                  Reason = "Tactical foul" }

    let decideFromContext
        (severity: FoulSeverity)
        (foulerYellows: int)
        (foulerFoulCount: int)
        (ballX: float<meter>)
        (dir: AttackDir)
        (minute: float)
        (teamFoulCountThisHalf: int)
        (isAdvantagePreserved: bool)
        (profile: RefereeProfile)
        (homeBonus: bool)
        (matchImportance: float)
        (disciplineHistory: int)
        (rw: RefereeWeights)
        : FoulDecision =
        let zone = classifyZone ballX dir
        let phase = classifyMatchPhase minute

        decide
            severity
            foulerYellows
            foulerFoulCount
            zone
            phase
            teamFoulCountThisHalf
            isAdvantagePreserved
            profile
            homeBonus
            matchImportance
            disciplineHistory
            rw
