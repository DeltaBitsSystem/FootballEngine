namespace FootballEngine

open System
open FootballEngine.Domain


module Stats =

    let clamp (lo: int) hi v = Math.Clamp(v, lo, hi)

    let rollProbability () : float = RNG.roll ()

    let uniformSample (lo: float) (hi: float) : float = RNG.uniform lo hi

    let normalSample (mean: float) (stdDev: float) : float = Distributions.normal mean stdDev

    let normalInt (mean: float) (stdDev: float) (lo: int) (hi: int) =
        Distributions.normal mean stdDev
        |> Math.Round
        |> int
        |> fun n -> Math.Clamp(n, lo, hi)

    let normalFloat (mean: float) (stdDev: float) (lo: float) (hi: float) =
        Distributions.normal mean stdDev |> fun n -> Math.Clamp(n, lo, hi)

    let normalFloatUnbounded (mean: float) (stdDev: float) = Distributions.normal mean stdDev

    let betaInt (alpha: float) (beta: float) (lo: int) (hi: int) =
        Distributions.beta alpha beta
        |> fun s -> lo + int (Math.Round(s * float (hi - lo)))
        |> fun n -> Math.Clamp(n, lo, hi)

    let bernoulli (p: float) : bool =
        let clampedP = if Double.IsNaN p then 0.0 else Math.Clamp(p, 0.0, 1.0)
        Distributions.bernoulli clampedP

    let logistic (x: float) : float = 1.0 / (1.0 + exp (-x))

    let logisticBernoulli (score: float) (steepness: float) : bool =
        bernoulli (logistic (score * steepness))

    let betaSample (mean: float) (concentration: float) : float =
        let clampedMean = Math.Clamp(mean, 0.01, 0.99)
        let alpha = Math.Max(0.01, clampedMean * concentration)
        let betaVal = Math.Max(0.01, (1.0 - clampedMean) * concentration)
        Distributions.beta alpha betaVal

    let poissonSample (lambda: float) : int = Distributions.poisson lambda

    let exponentialSample (mean: float) : float = Distributions.exponential (1.0 / mean)

    let pickWeighted (weights: (float * 'a) list) : 'a =
        let total = weights |> List.sumBy fst
        let roll = RNG.uniform 0.0 total

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


    let physicalVariation (condition: int) : float =
        let base' = float condition / 100.0
        betaSample base' (5.0 + base' * 10.0)


    let pressureNoise (composure: int) (urgencyMultiplier: float) : float =
        let resistance = float composure / 100.0
        let noise = (urgencyMultiplier - 1.0) * (1.0 - resistance) * 15.0
        normalSample 0.0 (Math.Max(0.5, noise))
