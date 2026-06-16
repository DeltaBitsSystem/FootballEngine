namespace Training

[<CLIMutable>]
type ExperienceMetrics =
    { MomentumVariance: float
      EventDistributionEntropy: float
      LeadChanges: int
      LateGoalFrequency: float
      ComebackRate: float }
