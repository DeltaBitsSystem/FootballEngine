namespace Training

open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Simulation

module GradientTuner =

    type ParamPerturbation =
        { Name: string
          Getter: BalanceConfig -> float
          Setter: BalanceConfig -> float -> BalanceConfig
          Min: float
          Max: float }

    let private clamp value min max =
        if value < min then min
        elif value > max then max
        else value

    let private setDuelSteepness (w: BalanceConfig) (v: float) =
        { w with
            Duel = { w.Duel with DuelSteepness = v } }

    let private setShotOnTargetBase (w: BalanceConfig) (v: float) =
        { w with
            Shot = { w.Shot with OnTargetBase = v } }

    let private setShotDistDecay (w: BalanceConfig) (v: float) =
        { w with
            Shot =
                { w.Shot with
                    OnTargetDistDecayRate = v } }

    let private setHomeDuelBonus (w: BalanceConfig) (v: float) =
        { w with
            HomeAdvantage =
                { w.HomeAdvantage with
                    DuelAttackBonus = v } }

    let private setHomePassBonus (w: BalanceConfig) (v: float) =
        { w with
            HomeAdvantage =
                { w.HomeAdvantage with
                    PassAccuracyBonus = v } }

    let private setSoftmaxTemp (w: BalanceConfig) (v: float) =
        { w with
            Individual =
                { w.Individual with
                    SoftmaxTemperature = v } }

    let private setDirectnessBlend (w: BalanceConfig) (v: float) =
        { w with
            Individual =
                { w.Individual with
                    DirectnessBlendTactic = v } }

    let private setFinishWeight (w: BalanceConfig) (v: float) =
        { w with
            Individual =
                { w.Individual with
                    Shoot =
                        { w.Individual.Shoot with
                            FinishingWeight = v } } }

    let private setPassWeight (w: BalanceConfig) (v: float) =
        { w with
            Individual =
                { w.Individual with
                    Pass =
                        { w.Individual.Pass with
                            PassingWeight = v } } }

    let private setDribbleWeight (w: BalanceConfig) (v: float) =
        { w with
            Individual =
                { w.Individual with
                    Dribble =
                        { w.Individual.Dribble with
                            DribblingWeight = v } } }

    let private setTackleSteepness (w: BalanceConfig) (v: float) =
        { w with
            Tackle = { w.Tackle with TackleSteepness = v } }

    let private setFoulBeta (w: BalanceConfig) (v: float) =
        { w with
            Tackle = { w.Tackle with FoulShapeBeta = v } }

    let private setCardBase (w: BalanceConfig) (v: float) =
        { w with
            Referee = { w.Referee with CardBaseProb = v } }

    let private setInjuryBase (w: BalanceConfig) (v: float) =
        { w with
            Referee = { w.Referee with InjuryBaseProb = v } }

    let private tunableParams: ParamPerturbation list =
        [ { Name = "DuelSteepness"
            Getter = (fun w -> w.Duel.DuelSteepness)
            Setter = setDuelSteepness
            Min = 0.8
            Max = 2.0 }
          { Name = "ShotOnTargetBase"
            Getter = (fun w -> w.Shot.OnTargetBase)
            Setter = setShotOnTargetBase
            Min = 0.30
            Max = 0.55 }
          { Name = "ShotDistDecay"
            Getter = (fun w -> w.Shot.OnTargetDistDecayRate)
            Setter = setShotDistDecay
            Min = 5.0
            Max = 30.0 }
          { Name = "HomeDuelBonus"
            Getter = (fun w -> w.HomeAdvantage.DuelAttackBonus)
            Setter = setHomeDuelBonus
            Min = 0.0
            Max = 10.0 }
          { Name = "HomePassBonus"
            Getter = (fun w -> w.HomeAdvantage.PassAccuracyBonus)
            Setter = setHomePassBonus
            Min = 0.0
            Max = 0.2 }
          { Name = "SoftmaxTemp"
            Getter = (fun w -> w.Individual.SoftmaxTemperature)
            Setter = setSoftmaxTemp
            Min = 0.01
            Max = 1.0 }
          { Name = "DirectnessBlend"
            Getter = (fun w -> w.Individual.DirectnessBlendTactic)
            Setter = setDirectnessBlend
            Min = 0.0
            Max = 1.0 }
          { Name = "FinishWeight"
            Getter = (fun w -> w.Individual.Shoot.FinishingWeight)
            Setter = setFinishWeight
            Min = 0.0
            Max = 1.0 }
          { Name = "PassWeight"
            Getter = (fun w -> w.Individual.Pass.PassingWeight)
            Setter = setPassWeight
            Min = 0.0
            Max = 1.0 }
          { Name = "DribbleWeight"
            Getter = (fun w -> w.Individual.Dribble.DribblingWeight)
            Setter = setDribbleWeight
            Min = 0.0
            Max = 1.0 }
          { Name = "TackleSteepness"
            Getter = (fun w -> w.Tackle.TackleSteepness)
            Setter = setTackleSteepness
            Min = 0.5
            Max = 3.0 }
          { Name = "FoulBeta"
            Getter = (fun w -> w.Tackle.FoulShapeBeta)
            Setter = setFoulBeta
            Min = 1.0
            Max = 20.0 }
          { Name = "CardBase"
            Getter = (fun w -> w.Referee.CardBaseProb)
            Setter = setCardBase
            Min = 0.0
            Max = 0.05 }
          { Name = "InjuryBase"
            Getter = (fun w -> w.Referee.InjuryBaseProb)
            Setter = setInjuryBase
            Min = 0.0
            Max = 0.005 } ]

    let tune
        (epsilon: float)
        (learningRate: float)
        (currentWeights: BalanceConfig)
        (errorFn: BalanceConfig -> float)
        : BalanceConfig =
        let baseError = errorFn currentWeights

        let adjusted =
            tunableParams
            |> List.fold
                (fun w param ->
                    let current = param.Getter w
                    let perturbed = current + epsilon
                    let clampedPerturbed = clamp perturbed param.Min param.Max
                    let wPlus = param.Setter w clampedPerturbed
                    let errorPlus = errorFn wPlus

                    let gradient = (errorPlus - baseError) / epsilon

                    let newValue = current - learningRate * gradient
                    let clampedNew = clamp newValue param.Min param.Max
                    param.Setter w clampedNew)
                currentWeights

        adjusted
