namespace FootballEngine.Domain

open FootballEngine.Core.Match
open FootballEngine.Domain

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

    let private unwrapPid (FootballEngine.Core.Match.PlayerId n) = n

    let private sideOf (s: FootballEngine.Core.Match.ClubSide) (homeId: ClubId) (awayId: ClubId) : ClubId =
        match s with
        | FootballEngine.Core.Match.ClubSide.Home -> homeId
        | FootballEngine.Core.Match.ClubSide.Away -> awayId

    /// Extracts match statistics from the engine event stream.
    /// `clubOfPlayer` resolves an engine player to its club (from the game state).
    let extract
        (events: MatchEvent list)
        (homeId: ClubId)
        (awayId: ClubId)
        (clubOfPlayer: PlayerId -> ClubId option)
        : MatchMetrics =
        let ofSide side = sideOf side homeId awayId

        let goalsHome, goalsAway =
            events
            |> List.fold
                (fun (gh, ga) ev ->
                    match ev.Data with
                    | EventData.Goal(side, _, _, _) -> if ofSide side = homeId then (gh + 1, ga) else (gh, ga + 1)
                    | _ -> (gh, ga))
                (0, 0)

        let shotsHome, shotsAway, sotHome, sotAway =
            events
            |> List.fold
                (fun (sh, sa, sth, sta) ev ->
                    match ev.Data with
                    | EventData.Shot(shooter, result, _, _, _, _) ->
                        let isHome = clubOfPlayer (unwrapPid shooter) = Some homeId

                        let onTarget =
                            match result with
                            | ShotResult.OnTarget _
                            | ShotResult.Post -> 1
                            | _ -> 0

                        if isHome then
                            (sh + 1, sa, sth + onTarget, sta)
                        else
                            (sh, sa + 1, sth, sta + onTarget)
                    | _ -> (sh, sa, sth, sta))
                (0, 0, 0, 0)

        let passesHome, passesAway, pcHome, pcAway =
            events
            |> List.fold
                (fun (ph, pa, pch, pca) ev ->
                    match ev.Data with
                    | EventData.Pass(passer, _, _, _, completed, _, _, _, _) ->
                        let isHome = clubOfPlayer (unwrapPid passer) = Some homeId
                        let done' = if completed then 1 else 0

                        if isHome then
                            (ph + 1, pa, pch + done', pca)
                        else
                            (ph, pa + 1, pch, pca + done')
                    | _ -> (ph, pa, pch, pca))
                (0, 0, 0, 0)

        let dribblesHome, dribblesAway =
            events
            |> List.fold
                (fun (dh, da) ev ->
                    match ev.Data with
                    | EventData.Dribble(dribbler, _, _, _) ->
                        if clubOfPlayer (unwrapPid dribbler) = Some homeId then
                            (dh + 1, da)
                        else
                            (dh, da + 1)
                    | _ -> (dh, da))
                (0, 0)

        let cornersHome, cornersAway =
            events
            |> List.fold
                (fun (ch, ca) ev ->
                    match ev.Data with
                    | EventData.SetPiece(SetPieceKind.Corner, side) ->
                        if ofSide side = homeId then (ch + 1, ca) else (ch, ca + 1)
                    | _ -> (ch, ca))
                (0, 0)

        let foulsHome, foulsAway =
            events
            |> List.fold
                (fun (fh, fa) ev ->
                    match ev.Data with
                    | EventData.Foul(fouler, _, _, _, _) ->
                        if clubOfPlayer (unwrapPid fouler) = Some homeId then
                            (fh + 1, fa)
                        else
                            (fh, fa + 1)
                    | _ -> (fh, fa))
                (0, 0)

        let possHome, possAway =
            events
            |> List.fold
                (fun (ph, pa) ev ->
                    match ev.Data with
                    | EventData.Possession(side, _) -> if ofSide side = homeId then (ph + 1, pa) else (ph, pa + 1)
                    | _ -> (ph, pa))
                (0, 0)

        let totalPoss = float (possHome + possAway)
        let possessionHome = if totalPoss > 0.0 then float possHome / totalPoss else 0.5

        let passAccuracyHome =
            if passesHome > 0 then
                float pcHome / float passesHome
            else
                0.0

        let passAccuracyAway =
            if passesAway > 0 then
                float pcAway / float passesAway
            else
                0.0

        let isHomeWin = goalsHome > goalsAway
        let isDraw = goalsHome = goalsAway
        let isAwayWin = goalsAway > goalsHome

        { GoalsHome = goalsHome
          GoalsAway = goalsAway
          ShotsHome = shotsHome
          ShotsAway = shotsAway
          ShotsOnTargetHome = sotHome
          ShotsOnTargetAway = sotAway
          PassesHome = passesHome
          PassesAway = passesAway
          PassAccuracyHome = passAccuracyHome
          PassAccuracyAway = passAccuracyAway
          PossessionHome = possessionHome
          PossessionAway = 1.0 - possessionHome
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
