namespace FootballEngine.Domain

type MatchMetrics =
    { GoalsHome: int
      GoalsAway: int
      ShotsHome: int
      ShotsAway: int
      ShotsOnTargetHome: int
      ShotsOnTargetAway: int
      PassesHome: int
      PassesAway: int
      PassAccuracyHome: float
      PassAccuracyAway: float
      PossessionHome: float
      PossessionAway: float
      DribblesHome: int
      DribblesAway: int
      CornersHome: int
      CornersAway: int
      FoulsHome: int
      FoulsAway: int
      IsHomeWin: bool
      IsDraw: bool
      IsAwayWin: bool }

module MatchMetrics =

    let private countByClub (events: MatchEvent list) (clubId: ClubId) (filter: MatchEventType -> bool) : int =
        events
        |> List.filter (fun e -> e.ClubId = clubId && filter e.Type)
        |> List.length

    let private countAll (events: MatchEvent list) (filter: MatchEventType -> bool) : int =
        events |> List.filter (fun e -> filter e.Type) |> List.length

    let extract (events: MatchEvent list) (homeId: ClubId) (awayId: ClubId) : MatchMetrics =
        let goalsHome =
            countByClub events homeId (function
                | MatchEventType.Goal -> true
                | _ -> false)

        let goalsAway =
            countByClub events awayId (function
                | MatchEventType.Goal -> true
                | _ -> false)

        let shotsHome =
            countByClub events homeId (function
                | MatchEventType.ShotOnTarget
                | MatchEventType.ShotOffTarget
                | MatchEventType.ShotBlocked
                | MatchEventType.Save -> true
                | _ -> false)

        let shotsAway =
            countByClub events awayId (function
                | MatchEventType.ShotOnTarget
                | MatchEventType.ShotOffTarget
                | MatchEventType.ShotBlocked
                | MatchEventType.Save -> true
                | _ -> false)

        let shotsOnTargetHome =
            countByClub events homeId (function
                | MatchEventType.ShotOnTarget
                | MatchEventType.Save -> true
                | _ -> false)

        let shotsOnTargetAway =
            countByClub events awayId (function
                | MatchEventType.ShotOnTarget
                | MatchEventType.Save -> true
                | _ -> false)

        let passesHome =
            countByClub events homeId (function
                | MatchEventType.PassCompleted _
                | MatchEventType.PassIncomplete _
                | MatchEventType.PassDeflected _
                | MatchEventType.PassIntercepted _
                | MatchEventType.PassMisplaced _ -> true
                | _ -> false)

        let passesAway =
            countByClub events awayId (function
                | MatchEventType.PassCompleted _
                | MatchEventType.PassIncomplete _
                | MatchEventType.PassDeflected _
                | MatchEventType.PassIntercepted _
                | MatchEventType.PassMisplaced _ -> true
                | _ -> false)

        let passesCompletedHome =
            countByClub events homeId (function
                | MatchEventType.PassCompleted _ -> true
                | _ -> false)

        let passesCompletedAway =
            countByClub events awayId (function
                | MatchEventType.PassCompleted _ -> true
                | _ -> false)

        let passAccuracyHome =
            if passesHome > 0 then
                float passesCompletedHome / float passesHome
            else
                0.0

        let passAccuracyAway =
            if passesAway > 0 then
                float passesCompletedAway / float passesAway
            else
                0.0

        let dribblesHome =
            countByClub events homeId (function
                | MatchEventType.DribbleSuccess
                | MatchEventType.DribbleFail
                | MatchEventType.DribbleKeep -> true
                | _ -> false)

        let dribblesAway =
            countByClub events awayId (function
                | MatchEventType.DribbleSuccess
                | MatchEventType.DribbleFail
                | MatchEventType.DribbleKeep -> true
                | _ -> false)

        let cornersHome =
            countByClub events homeId (function
                | MatchEventType.Corner -> true
                | _ -> false)

        let cornersAway =
            countByClub events awayId (function
                | MatchEventType.Corner -> true
                | _ -> false)

        let foulsHome =
            countByClub events homeId (function
                | MatchEventType.FoulCommitted -> true
                | _ -> false)

        let foulsAway =
            countByClub events awayId (function
                | MatchEventType.FoulCommitted -> true
                | _ -> false)

        let isHomeWin = goalsHome > goalsAway
        let isDraw = goalsHome = goalsAway
        let isAwayWin = goalsAway > goalsHome

        let totalEvents = float (List.length events)

        let possessionHome =
            if totalEvents > 0.0 then
                let homeEvents =
                    events |> List.filter (fun e -> e.ClubId = homeId) |> List.length |> float

                homeEvents / totalEvents
            else
                0.5

        let possessionAway = 1.0 - possessionHome

        { GoalsHome = goalsHome
          GoalsAway = goalsAway
          ShotsHome = shotsHome
          ShotsAway = shotsAway
          ShotsOnTargetHome = shotsOnTargetHome
          ShotsOnTargetAway = shotsOnTargetAway
          PassesHome = passesHome
          PassesAway = passesAway
          PassAccuracyHome = passAccuracyHome
          PassAccuracyAway = passAccuracyAway
          PossessionHome = possessionHome
          PossessionAway = possessionAway
          DribblesHome = dribblesHome
          DribblesAway = dribblesAway
          CornersHome = cornersHome
          CornersAway = cornersAway
          FoulsHome = foulsHome
          FoulsAway = foulsAway
          IsHomeWin = isHomeWin
          IsDraw = isDraw
          IsAwayWin = isAwayWin }

    let empty =
        { GoalsHome = 0
          GoalsAway = 0
          ShotsHome = 0
          ShotsAway = 0
          ShotsOnTargetHome = 0
          ShotsOnTargetAway = 0
          PassesHome = 0
          PassesAway = 0
          PassAccuracyHome = 0.0
          PassAccuracyAway = 0.0
          PossessionHome = 0.5
          PossessionAway = 0.5
          DribblesHome = 0
          DribblesAway = 0
          CornersHome = 0
          CornersAway = 0
          FoulsHome = 0
          FoulsAway = 0
          IsHomeWin = false
          IsDraw = true
          IsAwayWin = false }

    let average (metrics: MatchMetrics list) : MatchMetrics =
        if List.isEmpty metrics then
            empty
        else
            let n = float (List.length metrics)

            let sum =
                List.fold
                    (fun acc m ->
                        { GoalsHome = acc.GoalsHome + m.GoalsHome
                          GoalsAway = acc.GoalsAway + m.GoalsAway
                          ShotsHome = acc.ShotsHome + m.ShotsHome
                          ShotsAway = acc.ShotsAway + m.ShotsAway
                          ShotsOnTargetHome = acc.ShotsOnTargetHome + m.ShotsOnTargetHome
                          ShotsOnTargetAway = acc.ShotsOnTargetAway + m.ShotsOnTargetAway
                          PassesHome = acc.PassesHome + m.PassesHome
                          PassesAway = acc.PassesAway + m.PassesAway
                          PassAccuracyHome = acc.PassAccuracyHome + m.PassAccuracyHome
                          PassAccuracyAway = acc.PassAccuracyAway + m.PassAccuracyAway
                          PossessionHome = acc.PossessionHome + m.PossessionHome
                          PossessionAway = acc.PossessionAway + m.PossessionAway
                          DribblesHome = acc.DribblesHome + m.DribblesHome
                          DribblesAway = acc.DribblesAway + m.DribblesAway
                          CornersHome = acc.CornersHome + m.CornersHome
                          CornersAway = acc.CornersAway + m.CornersAway
                          FoulsHome = acc.FoulsHome + m.FoulsHome
                          FoulsAway = acc.FoulsAway + m.FoulsAway
                          IsHomeWin = acc.IsHomeWin || m.IsHomeWin
                          IsDraw = acc.IsDraw || m.IsDraw
                          IsAwayWin = acc.IsAwayWin || m.IsAwayWin })
                    empty
                    metrics

            let avg (x: float) = x / n

            { GoalsHome = int (avg (float sum.GoalsHome))
              GoalsAway = int (avg (float sum.GoalsAway))
              ShotsHome = int (avg (float sum.ShotsHome))
              ShotsAway = int (avg (float sum.ShotsAway))
              ShotsOnTargetHome = int (avg (float sum.ShotsOnTargetHome))
              ShotsOnTargetAway = int (avg (float sum.ShotsOnTargetAway))
              PassesHome = int (avg (float sum.PassesHome))
              PassesAway = int (avg (float sum.PassesAway))
              PassAccuracyHome = avg sum.PassAccuracyHome
              PassAccuracyAway = avg sum.PassAccuracyAway
              PossessionHome = avg sum.PossessionHome
              PossessionAway = avg sum.PossessionAway
              DribblesHome = int (avg (float sum.DribblesHome))
              DribblesAway = int (avg (float sum.DribblesAway))
              CornersHome = int (avg (float sum.CornersHome))
              CornersAway = int (avg (float sum.CornersAway))
              FoulsHome = int (avg (float sum.FoulsHome))
              FoulsAway = int (avg (float sum.FoulsAway))
              IsHomeWin = false
              IsDraw = false
              IsAwayWin = false }
