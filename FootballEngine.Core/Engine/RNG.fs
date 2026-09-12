namespace FootballEngine

open System
open System.Threading

[<AutoOpen>]
module RNG =
    let private counter = ref 42

    type private RngState =
        { Generator: Random
          mutable SeededState: uint64 option
          Token: obj }

    let private threadLocalRng =
        new ThreadLocal<RngState>(fun () ->
            { Generator = Random(Interlocked.Increment(counter))
              SeededState = None
              Token = obj () })

    let private scopedRng = new AsyncLocal<RngState option>()

    let private currentState () =
        match scopedRng.Value with
        | Some state -> state
        | None -> threadLocalRng.Value

    let internal sourceToken () = (currentState ()).Token

    let private nextSeededUInt64 (state: RngState) =
        match state.SeededState with
        | None -> invalidOp "The RNG state is not seeded."
        | Some current ->
            let next = current + 0x9E3779B97F4A7C15UL
            state.SeededState <- Some next

            let mutable value = next
            value <- (value ^^^ (value >>> 30)) * 0xBF58476D1CE4E5B9UL
            value <- (value ^^^ (value >>> 27)) * 0x94D049BB133111EBUL
            value ^^^ (value >>> 31)

    let private nextDouble (state: RngState) =
        match state.SeededState with
        | None -> state.Generator.NextDouble()
        | Some _ -> float (nextSeededUInt64 state >>> 11) / 9007199254740992.0

    let private nextIntFromState (state: RngState) (lo: int) (hi: int) =
        match state.SeededState with
        | None -> state.Generator.Next(lo, hi + 1)
        | Some _ ->
            if hi < lo then
                invalidArg "hi" "The upper bound must not be lower than the lower bound."

            let range = uint64 (int64 hi - int64 lo + 1L)
            int (int64 lo + int64 (nextSeededUInt64 state % range))

    let withSeed (seed: uint64) (work: unit -> 'a) : 'a =
        let previous = scopedRng.Value

        scopedRng.Value <-
            Some
                { Generator = Random(0)
                  SeededState = Some seed
                  Token = obj () }

        try
            work ()
        finally
            scopedRng.Value <- previous

    let roll () : float = nextDouble (currentState ())

    let uniform (lo: float) (hi: float) : float =
        lo + (hi - lo) * nextDouble (currentState ())

    let nextInt (lo: int) (hi: int) : int =
        nextIntFromState (currentState ()) lo hi
