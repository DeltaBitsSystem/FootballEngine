namespace FootballEngine.Player.Decision

open FootballEngine.Domain

open FootballEngine.Types
open FootballEngine.Types.PhysicsContract

// ------------------------------------------------------------
// EligibilityMask
//
// Answers one question per action: "Is this physically/spatially
// possible right now?"  It does NOT score quality — that is
// PlayerScorer's job.  It does NOT apply tactical preference —
// that is PlayerDecision's job via BalanceConfig thresholds.
//
// Rule: if the answer is "yes but unlikely", the mask returns true
// and the scorer/config take care of the rest.  The mask only
// returns false when the action is outright impossible or
// self-defeating (e.g. shooting from zero angle, passing when no
// teammate exists, dribbling when the ball is already being taken).
// ------------------------------------------------------------

[<Struct>]
type EligibilityMask =
    { CanShoot: bool // viable shot opportunity exists
      CanCross: bool // position and target allow a cross
      CanLongBall: bool // technical ability + not pressed tight
      CanPass: bool // at least one valid pass target exists
      CanDribble: bool } // not immediately dispossessed on touch

module ActionEligibility =

    // ----------------------------------------------------------
    // Shoot helpers
    // ----------------------------------------------------------

    /// False only when the shooting angle is geometrically negligible
    /// (< ~3 degrees). A tight angle is already penalised by
    /// shootScore's distNorm; we only gate the truly impossible case.
    let private shootAngleOpen (myPos: Spatial) (attackDir: AttackDir) : bool =
        let goalNearY = PostNearY
        let goalFarY = PostFarY
        let goalX = if attackDir = LeftToRight then PitchLength else 0.0<meter>
        let dy1 = float (goalNearY - myPos.Y)
        let dy2 = float (goalFarY - myPos.Y)
        let dx = float (abs (goalX - myPos.X))
        let angle = abs (atan2 dy2 dx - atan2 dy1 dx)
        angle > 0.05

    /// False only when the lane is completely walled (>= 3 defenders
    /// directly in line). Partial obstruction is handled by the scorer.
    let private shootLaneOpen (myPos: Spatial) (attackDir: AttackDir) (oppFrame: TeamFrame) : bool =
        let goalX = if attackDir = LeftToRight then PitchLength else 0.0<meter>
        let goalMY = (PostNearY + PostFarY) / 2.0
        let dx = goalX - myPos.X
        let dy = goalMY - myPos.Y
        let lenSq = dx * dx + dy * dy

        if lenSq < 0.01<meter^2> then
            true
        else
            let mutable blocked = 0

            for i = 0 to oppFrame.SlotCount - 1 do
                match oppFrame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ ->
                    let dpx = float oppFrame.Physics.PosX[i] * 1.0<meter> - myPos.X
                    let dpy = float oppFrame.Physics.PosY[i] * 1.0<meter> - myPos.Y
                    let t = (dpx * dx + dpy * dy) / lenSq

                    if t >= 0.0 && t <= 1.0 then
                        let cross = dpx * dy - dpy * dx
                        let distSq = cross * cross / lenSq

                        if distSq < 4.0<meter^2> then
                            blocked <- blocked + 1
                | _ -> ()

            blocked < 3

    // ----------------------------------------------------------
    // Cross helpers
    // ----------------------------------------------------------

    let private crossPositionValid (ctx: AgentContext) (attackDir: AttackDir) : bool =
        let lateralness =
            float (abs (ctx.MyPos.Y - PitchWidth / 2.0)) / float (PitchWidth / 2.0)

        let inAttackHalf =
            if attackDir = LeftToRight then
                ctx.MyPos.X > PitchLength / 2.0
            else
                ctx.MyPos.X < PitchLength / 2.0

        lateralness > 0.35 && inAttackHalf

    let private crossTargetExists (ctx: AgentContext) (attackDir: AttackDir) : bool =
        let penX =
            if attackDir = LeftToRight then
                PitchLength - PenaltyAreaDepth
            else
                PenaltyAreaDepth

        let mutable count = 0

        for i = 0 to ctx.Team.OwnFrame.SlotCount - 1 do
            if i <> ctx.MeIdx then
                match ctx.Team.OwnFrame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ ->
                    let px = float ctx.Team.OwnFrame.Physics.PosX[i] * 1.0<meter>

                    if (attackDir = LeftToRight && px > penX) || (attackDir = RightToLeft && px < penX) then
                        count <- count + 1
                | _ -> ()

        count > 0

    // ----------------------------------------------------------
    // Long ball
    // ----------------------------------------------------------

    let private longBallFeasible (ctx: AgentContext) : bool =
        let passingOk = ctx.Me.Technical.Passing >= 8

        let notPressed =
            match ctx.NearestOpponentIdx with
            | ValueSome oppIdx ->
                let oppPos =
                    { X = float ctx.Team.OppFrame.Physics.PosX[oppIdx] * 1.0<meter>
                      Y = float ctx.Team.OppFrame.Physics.PosY[oppIdx] * 1.0<meter>
                      Z = 0.0<meter>
                      Vx = 0.0<meter / second>
                      Vy = 0.0<meter / second>
                      Vz = 0.0<meter / second> }

                ctx.MyPos.DistTo2D oppPos > 2.5<meter>
            | ValueNone -> true

        passingOk && notPressed

    // ----------------------------------------------------------
    // Pass — only checks that a target exists.
    // Quality of the target is the scorer's job.
    // ----------------------------------------------------------
    let private passEligible (ctx: AgentContext) : bool = ctx.BestPassTargetIdx.IsSome

    // ----------------------------------------------------------
    // Dribble — only checks the opponent is not so close that any
    // touch is immediately stolen (< 1 m).
    // Tactical preference (zone, tempo, personality) belongs in
    // PlayerDecision via config thresholds.
    // ----------------------------------------------------------
    let private dribbleEligible (ctx: AgentContext) : bool =
        match ctx.NearestOpponentIdx with
        | ValueSome oppIdx ->
            let oppPos =
                { X = float ctx.Team.OppFrame.Physics.PosX[oppIdx] * 1.0<meter>
                  Y = float ctx.Team.OppFrame.Physics.PosY[oppIdx] * 1.0<meter>
                  Z = 0.0<meter>
                  Vx = 0.0<meter / second>
                  Vy = 0.0<meter / second>
                  Vz = 0.0<meter / second> }

            ctx.MyPos.DistTo2D oppPos > 1.0<meter>
        | ValueNone -> true

    let evaluate (ctx: AgentContext) : EligibilityMask =
        let attackDir = ctx.Team.AttackDir

        { CanShoot =
            ctx.Zone <> DefensiveZone
            && shootAngleOpen ctx.MyPos attackDir
            && shootLaneOpen ctx.MyPos attackDir ctx.Team.OppFrame

          CanCross = crossPositionValid ctx attackDir && crossTargetExists ctx attackDir

          CanLongBall = longBallFeasible ctx

          CanPass = passEligible ctx

          CanDribble = dribbleEligible ctx }
