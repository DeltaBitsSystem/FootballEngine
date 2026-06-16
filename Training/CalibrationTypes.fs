namespace Training

open FootballEngine.Domain

type CalibrationTargets =
    { GoalsPerMatch: float * float
      Possession: float * float
      PassAccuracy: float * float
      ShotsOnTargetPct: float * float
      HomeWinRate: float * float
      DribblesPerMatch: float * float
      CornersPerMatch: float * float
      FoulsPerMatch: float * float }

module CalibrationTargets =
    let targetsDefault: CalibrationTargets =
        { GoalsPerMatch = (2.0, 3.5)
          Possession = (0.45, 0.55)
          PassAccuracy = (0.75, 0.85)
          ShotsOnTargetPct = (0.30, 0.45)
          HomeWinRate = (0.40, 0.55)
          DribblesPerMatch = (10.0, 30.0)
          CornersPerMatch = (8.0, 12.0)
          FoulsPerMatch = (30.0, 50.0) }

    let targetMid ((a, b): float * float) = (a + b) / 2.0

    let fromEngineTargets (t: FootballEngine.Types.CalibrationTargets) : CalibrationTargets =
        let range pct v = (v * (1.0 - pct), v * (1.0 + pct))

        { GoalsPerMatch = range 0.2 t.GoalsPerMatch
          Possession = (0.45, 0.55)
          PassAccuracy = range 0.1 t.PassSuccessRate
          ShotsOnTargetPct = (0.30, 0.45)
          HomeWinRate = range 0.15 t.HomeWinPct
          DribblesPerMatch = range 0.3 t.DribblesPerMatch
          CornersPerMatch = (8.0, 12.0)
          FoulsPerMatch = (30.0, 50.0) }

type MetricDelta =
    { Metric: string
      Observed: float
      TargetMin: float
      TargetMax: float
      DeltaMid: float
      DeltaPct: float
      IsHigh: bool
      IsLow: bool }

module MetricDelta =
    let create (name: string) (obs: float) (min: float) (max: float) : MetricDelta =
        let targetMid = (min + max) / 2.0
        let range = max - min
        let deltaMid = obs - targetMid
        let deltaPct = if range > 0.0 then deltaMid / range else 0.0

        { Metric = name
          Observed = obs
          TargetMin = min
          TargetMax = max
          DeltaMid = deltaMid
          DeltaPct = deltaPct
          IsHigh = obs > max
          IsLow = obs < min }

module Calibrator =

    let compare (m: MatchMetrics) (t: CalibrationTargets) : MetricDelta list =
        let goals = float (m.GoalsHome + m.GoalsAway)
        let poss = m.PossessionHome
        let passAcc = (m.PassAccuracyHome + m.PassAccuracyAway) / 2.0
        let shots = float (m.ShotsHome + m.ShotsAway)
        let shotsOnT = float (m.ShotsOnTargetHome + m.ShotsOnTargetAway)
        let shotPct = if shots > 0.0 then shotsOnT / shots else 0.0
        let homeWin = if m.IsHomeWin then 1.0 else 0.0
        let drib = float (m.DribblesHome + m.DribblesAway)
        let corn = float (m.CornersHome + m.CornersAway)
        let foul = float (m.FoulsHome + m.FoulsAway)

        [ MetricDelta.create "GoalsPerMatch" goals (fst t.GoalsPerMatch) (snd t.GoalsPerMatch)
          MetricDelta.create "PassAccuracy" passAcc (fst t.PassAccuracy) (snd t.PassAccuracy)
          MetricDelta.create "PossessionHome" poss (fst t.Possession) (snd t.Possession)
          MetricDelta.create "ShotsOnTargetPct" shotPct (fst t.ShotsOnTargetPct) (snd t.ShotsOnTargetPct)
          MetricDelta.create "HomeWinRate" homeWin (fst t.HomeWinRate) (snd t.HomeWinRate)
          MetricDelta.create "DribblesPerMatch" drib (fst t.DribblesPerMatch) (snd t.DribblesPerMatch)
          MetricDelta.create "CornersPerMatch" corn (fst t.CornersPerMatch) (snd t.CornersPerMatch)
          MetricDelta.create "FoulsPerMatch" foul (fst t.FoulsPerMatch) (snd t.FoulsPerMatch) ]

    let getSignificant (thresh: float) (d: MetricDelta list) : MetricDelta list =
        d |> List.filter (fun x -> abs x.DeltaPct > thresh)
