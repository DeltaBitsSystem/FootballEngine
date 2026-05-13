namespace FootballEngine.Player

open FootballEngine.Types
open FootballEngine.Types.IntentPhaseTypes
open FootballEngine.Types.PhysicsContract


module FrameMutate =

    let setPos (frame: PhysicsFrame) (idx: int) (x: float<meter>) (y: float<meter>) : unit =
        frame.PosX[idx] <- float32 x
        frame.PosY[idx] <- float32 y

    let setVel (frame: PhysicsFrame) (idx: int) (vx: float<meter / second>) (vy: float<meter / second>) : unit =
        frame.VelX[idx] <- float32 vx
        frame.VelY[idx] <- float32 vy

    let setCondition (frame: TeamFrame) (idx: int) (cond: float32) : unit =
        frame.Condition[idx] <- cond

    let setIntent
        (frame: IntentDataFrame)
        (idx: int)
        (kind: IntentKind)
        (tx: float32)
        (ty: float32)
        (tpid: int)
        : unit =
        frame.Kind[idx] <- kind
        frame.TargetX[idx] <- tx
        frame.TargetY[idx] <- ty
        frame.TargetPid[idx] <- tpid

    let setOccupancy (frame: TeamFrame) (idx: int) (occ: OccupancyKind) : unit = frame.Physics.Occupancy[idx] <- occ

    let setCachedTarget (frame: TeamFrame) (idx: int) (x: float<meter>) (y: float<meter>) : unit =
        frame.CachedTargetX[idx] <- float32 x
        frame.CachedTargetY[idx] <- float32 y

    let setCachedExecution (frame: TeamFrame) (idx: int) (exec: float) : unit =
        frame.CachedExecution[idx] <- float32 exec

    let setMental
        (frame: TeamFrame)
        (idx: int)
        (composure: float)
        (confidence: float)
        (aggression: float)
        (focus: float)
        (riskTolerance: float)
        : unit =
        frame.ComposureLevel[idx] <- float32 composure
        frame.ConfidenceLevel[idx] <- float32 confidence
        frame.AggressionLevel[idx] <- float32 aggression
        frame.FocusLevel[idx] <- float32 focus
        frame.RiskTolerance[idx] <- float32 riskTolerance

    let commitIntent (frame: IntentDataFrame) (i: int) (until: int) (trigger: ExitTrigger) : unit =
        frame.Phase[i] <- Executing
        frame.CommittedUntil[i] <- until
        frame.ExitTrigger[i] <- byte trigger
