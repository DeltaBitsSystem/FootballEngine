namespace FootballEngine

open System
open FootballEngine.Domain
open FSharp.Stats
open FSharp.Stats.Distributions


module Stats =

    let clamp (lo: int) hi v = Math.Clamp(v, lo, hi)

    let rollProbability () : float = Continuous.Uniform.Sample 0.0 1.0

    let uniformSample (lo: float) (hi: float) : float = Continuous.Uniform.Sample lo hi

    let normalSample (mean: float) (stdDev: float) : float = Continuous.Normal.Sample mean stdDev

    let normalInt (mean: float) (stdDev: float) (lo: int) (hi: int) =
        Continuous.Normal.Sample mean stdDev
        |> Math.Round
        |> int
        |> fun n -> Math.Clamp(n, lo, hi)

    let normalFloat (mean: float) (stdDev: float) (lo: float) (hi: float) =
        Continuous.Normal.Sample mean stdDev |> fun n -> Math.Clamp(n, lo, hi)

    let normalFloatUnbounded (mean: float) (stdDev: float) = Continuous.Normal.Sample mean stdDev

    let betaInt (alpha: float) (beta: float) (lo: int) (hi: int) =
        Continuous.Beta.Sample alpha beta
        |> fun s -> lo + int (Math.Round(s * float (hi - lo)))
        |> fun n -> Math.Clamp(n, lo, hi)

    let bernoulli (p: float) : bool =
        let clampedP = if Double.IsNaN p then 0.0 else Math.Clamp(p, 0.0, 1.0)
        let dist = Discrete.Bernoulli.Init clampedP
        dist.Sample() = 1

    let logistic (x: float) : float = 1.0 / (1.0 + exp (-x))

    let logisticBernoulli (score: float) (steepness: float) : bool =
        bernoulli (logistic (score * steepness))

    let betaSample (mean: float) (concentration: float) : float =
        let clampedMean = Math.Clamp(mean, 0.01, 0.99)
        let alpha = Math.Max(0.01, clampedMean * concentration)
        let betaVal = Math.Max(0.01, (1.0 - clampedMean) * concentration)
        let dist = Continuous.Beta.Init alpha betaVal
        dist.Sample()

    let poissonSample (lambda: float) : int = Discrete.Poisson.Sample lambda

    let exponentialSample (mean: float) : float =
        Continuous.Exponential.Sample(1.0 / mean)

    let pickWeighted (weights: (float * 'a) list) : 'a =
        let total = weights |> List.sumBy fst
        let roll = Continuous.Uniform.Sample 0.0 total

        weights
        |> List.fold
            (fun (acc, chosen) (w, v) ->
                match chosen with
                | Some _ -> acc, chosen
                | None ->
                    let acc' = acc + w
                    if roll <= acc' then acc', Some v else acc', None)
            (0.0, None)
        |> snd
        |> Option.defaultWith (fun () -> weights |> List.last |> snd)



    let setSeed (seed: int) =
        Random.SetSampleGenerator(Random.RandThreadSafe(seed))

    let clearSeed () =
        Random.SetSampleGenerator(Random.RandThreadSafe())

    let humanPerformance (stat: int) (condition: int) (morale: int) : float =
        let normStat = float stat / 20.0
        let base' = normStat * (float condition / 100.0) * (0.8 + float morale / 500.0)
        let variability = 1.0 + (1.0 - float condition / 100.0) * 0.4
        normalSample base' (variability * 0.1) // Reduced sigma to avoid negative spikes

    let physicalVariation (condition: int) : float =
        let base' = float condition / 100.0
        betaSample base' (5.0 + base' * 10.0)


    let pressureNoise (composure: int) (urgencyMultiplier: float) : float =
        let resistance = float composure / 100.0
        let noise = (urgencyMultiplier - 1.0) * (1.0 - resistance) * 15.0
        normalSample 0.0 (Math.Max(0.5, noise))


    let positionalConsistency (profile: BehavioralProfile) : float =
        let freedomFactor = profile.PositionalFreedom
        let baseVal = 0.85 - freedomFactor * 0.25
        let concentration = 5.0 + freedomFactor * 8.0
        betaSample baseVal concentration


    let fatigueVariation (stamina: int) (isPressing: bool) : float =
        let base' = float stamina / 100.0
        let pressingPenalty = if isPressing then betaSample 0.3 5.0 else 0.0
        normalSample (base' - pressingPenalty) 0.05 |> fun v -> Math.Clamp(v, 0.0, 1.0)

    let momentumEffect (momentum: float) (isAttacking: bool) : float =
        let m = if isAttacking then momentum else -momentum
        normalSample (m * 0.5) (3.0 + Math.Abs(m) * 0.5)
