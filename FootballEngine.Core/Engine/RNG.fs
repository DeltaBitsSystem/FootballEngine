namespace FootballEngine

open System
open System.Threading

[<AutoOpen>]
module RNG =
    let private counter = ref 42

    let private threadLocalRng =
        new ThreadLocal<Random>(fun () -> Random(Interlocked.Increment(counter)))

    let roll () : float = threadLocalRng.Value.NextDouble()

    let uniform (lo: float) (hi: float) : float =
        lo + (hi - lo) * threadLocalRng.Value.NextDouble()

    let nextInt (lo: int) (hi: int) : int = threadLocalRng.Value.Next(lo, hi + 1)
