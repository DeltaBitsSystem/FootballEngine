namespace FootballEngine

open System
open System.Threading

module Distributions =

    module private NormalCache =
        [<Literal>]
        let private TwoPi = 6.283185307179586

        type private State =
            { Source: obj
              mutable Spare: float
              mutable HasSpare: bool }

        let private state = new AsyncLocal<State option>()

        let sampleBoxMuller (u1: float) (u2: float) =
            Math.Sqrt(-2.0 * Math.Log u1) * Math.Cos(TwoPi * u2)

        let get () =
            let source = RNG.sourceToken ()

            let cache =
                match state.Value with
                | Some current when obj.ReferenceEquals(current.Source, source) -> current
                | _ ->
                    let fresh =
                        { Source = source
                          Spare = 0.0
                          HasSpare = false }

                    state.Value <- Some fresh
                    fresh

            if cache.HasSpare then
                cache.HasSpare <- false
                cache.Spare
            else
                let u1 = RNG.roll ()
                let u2 = RNG.roll ()
                cache.Spare <- sampleBoxMuller u1 u2
                cache.HasSpare <- true
                sampleBoxMuller u1 u2

    let normal (mean: float) (stdDev: float) : float = mean + stdDev * NormalCache.get ()

    let rec private gammaSample (shape: float) : float =
        if shape < 1.0 then
            gammaSample (shape + 1.0) * Math.Pow(RNG.roll (), 1.0 / shape)
        else
            let d = shape - 1.0 / 3.0
            let c = 1.0 / Math.Sqrt(9.0 * d)
            let mutable v = 0.0
            let mutable accept = false

            while not accept do
                let mutable x = 0.0
                let mutable v' = 0.0

                while v' <= 0.0 do
                    x <- normal 0.0 1.0
                    v' <- 1.0 + c * x

                v <- v' * v' * v'
                let u = RNG.roll ()

                if u < 1.0 - 0.0331 * (x * x) * (x * x) then
                    accept <- true
                elif Math.Log(u) < 0.5 * x * x + d * (1.0 - v + Math.Log v) then
                    accept <- true

            d * v

    let beta (alpha: float) (b: float) : float =
        if alpha <= 0.0 || b <= 0.0 then
            invalidArg "alpha/beta" "Parameters must be positive"

        if alpha >= 1.0 && b >= 1.0 then
            let mutable x = 0.0
            let mutable y = 0.0
            let mutable accept = false

            while not accept do
                let u1 = RNG.roll ()
                let u2 = RNG.roll ()
                x <- Math.Pow(u1, 1.0 / alpha)
                y <- Math.Pow(u2, 1.0 / b)

                if x + y <= 1.0 then
                    accept <- true

            x / (x + y)
        else
            let ga = gammaSample alpha
            let gb = gammaSample b
            ga / (ga + gb)

    let bernoulli (p: float) : bool = RNG.roll () < p

    let poisson (lambda: float) : int =
        let L = Math.Exp(-lambda)
        let mutable k = 0
        let mutable p = 1.0

        while p > L do
            k <- k + 1
            p <- p * RNG.roll ()

        k - 1

    let exponential (lambda: float) : float = -Math.Log(1.0 - RNG.roll ()) / lambda

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
