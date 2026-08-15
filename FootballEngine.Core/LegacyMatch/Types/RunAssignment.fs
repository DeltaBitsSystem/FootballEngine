namespace FootballEngine.Types

open FootballEngine.Domain
open FootballEngine.Types.PhysicsContract



type RunAssignment =
    { PlayerId: PlayerId
      RunType: RunType
      Trigger: RunTrigger
      Trajectory: RunTrajectory
      StartSubTick: int<subtick>
      DurationSubTicks: int<tickDelta>
      Intensity: float
      Priority: int }

module RunAssignment =
    let isActive currentSubTick (r: RunAssignment) =
        currentSubTick >= r.StartSubTick
        && currentSubTick < r.StartSubTick + SimulationClock.deltaToSubtick r.DurationSubTicks

    let progress currentSubTick (r: RunAssignment) =
        if not (isActive currentSubTick r) then
            0.0
        else
            let dur = SimulationClock.deltaToSubtick r.DurationSubTicks
            min 1.0 (float (currentSubTick - r.StartSubTick) / float dur)

    let evaluateTrajectory t trajectory =
        match trajectory with
        | Linear(sx, sy, ex, ey) -> (sx + (ex - sx) * t, sy + (ey - sy) * t)
        | Waypoints pts ->
            if Array.isEmpty pts then
                (52.5<meter>, 34.0<meter>)
            elif pts.Length = 1 then
                pts[0]
            else
                let st = t * float (pts.Length - 1)
                let idx = int st
                let frac = st - float idx
                let i = min idx (pts.Length - 2)
                let ax, ay = pts[i]
                let bx, by = pts[i + 1]
                (ax + (bx - ax) * frac, ay + (by - ay) * frac)

    let create
        (runType: RunType)
        (startX: float<meter>)
        (startY: float<meter>)
        (targetX: float<meter>)
        (targetY: float<meter>)
        (playerId: PlayerId)
        (currentSubTick: int<subtick>)
        (durationSubTicks: int<tickDelta>)
        : RunAssignment =
        let trajectory =
            match runType with
            | CheckToBall ->
                Waypoints
                    [| startX, startY
                       (startX + targetX) / 2.0, (startY + targetY) / 2.0
                       targetX, targetY |]
            | _ -> Linear(startX, startY, targetX, targetY)

        { PlayerId = playerId
          RunType = runType
          Trigger = TeammateHasBall
          Trajectory = trajectory
          StartSubTick = currentSubTick
          DurationSubTicks = durationSubTicks
          Intensity = 0.8
          Priority = 1 }
