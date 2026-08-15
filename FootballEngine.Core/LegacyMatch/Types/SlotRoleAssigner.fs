namespace FootballEngine.Types

open FootballEngine.Domain

module SlotRoleAssigner =

    let private pressCount (tacticsCfg: TacticsConfig) : int =
        int (2.0 + tacticsCfg.PressingIntensity * 3.0) |> max 2 |> min 5

    let private ballDistances (frame: TeamFrame) (bx: float32) (by: float32) : float32[] =
        Array.init frame.SlotCount (fun i ->
            match frame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                let dx = frame.Physics.PosX[i] - bx
                let dy = frame.Physics.PosY[i] - by
                dx * dx + dy * dy
            | _ -> System.Single.MaxValue)

    let assign
        (frame: TeamFrame)
        (roster: PlayerRoster)
        (kind: DirectiveKind)
        (tacticsCfg: TacticsConfig)
        (bx: float32)
        (by: float32)
        : SlotRole[] =
        let n = frame.SlotCount
        let roles = Array.create n FreeRole

        match kind with

        | ContestBall ->
            let dists = ballDistances frame bx by

            let sortedByDist =
                Array.init n id
                |> Array.filter (fun i ->
                    match frame.Physics.Occupancy[i] with
                    | OccupancyKind.Active _ -> true
                    | _ -> false)
                |> Array.sortBy (fun i -> dists[i])

            for rank = 0 to sortedByDist.Length - 1 do
                let i = sortedByDist[rank]
                roles[i] <- if rank = 0 then PressFirst else HoldShape

        | PressingBlock ->
            let dists = ballDistances frame bx by
            let nPress = pressCount tacticsCfg

            let sortedByDist =
                Array.init n id
                |> Array.filter (fun i ->
                    match frame.Physics.Occupancy[i] with
                    | OccupancyKind.Active _ -> true
                    | _ -> false)
                |> Array.sortBy (fun i -> dists[i])

            for rank = 0 to sortedByDist.Length - 1 do
                let i = sortedByDist[rank]
                let pos = roster.Players[i].Position

                roles[i] <-
                    if pos = GK then AnchorDefense
                    elif pos = DC && rank > nPress then HoldShape
                    elif rank = 0 then PressFirst
                    elif rank < nPress then PressSupport
                    else HoldShape

        | CounterReady ->
            for i = 0 to n - 1 do
                match frame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ ->
                    roles[i] <-
                        match roster.Players[i].Position with
                        | GK
                        | DC -> AnchorDefense
                        | DL
                        | DR
                        | WBL
                        | WBR -> HoldShape
                        | DM
                        | MC -> SupportBuild
                        | ML
                        | MR
                        | AMC
                        | AML
                        | AMR
                        | ST -> MakeRunForward
                | _ -> ()

        | DirectAttack ->
            for i = 0 to n - 1 do
                match frame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ ->
                    roles[i] <-
                        match roster.Players[i].Position with
                        | GK
                        | DC -> AnchorDefense
                        | DL
                        | DR
                        | WBL
                        | WBR
                        | DM
                        | MC -> SupportBuild
                        | _ -> MakeRunForward
                | _ -> ()

        | DefensiveBlock ->
            for i = 0 to n - 1 do
                match frame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ ->
                    roles[i] <-
                        match roster.Players[i].Position with
                        | GK
                        | DC
                        | DL
                        | DR -> AnchorDefense
                        | _ -> HoldShape
                | _ -> ()

        | Structured -> ()

        roles
