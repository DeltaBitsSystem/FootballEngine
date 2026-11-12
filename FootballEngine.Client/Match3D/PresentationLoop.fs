namespace FootballEngine.Client.Match3D

open System

/// One attachment owns one callback chain; stale callbacks cannot restart a detached renderer.
[<Sealed>]
type PresentationLoop(request: Action<TimeSpan> -> unit, render: unit -> unit, onError: exn -> unit) =
    let mutable active = false
    let mutable generation = 0

    member _.Start() =
        if not active then
            active <- true
            generation <- generation + 1
            let attachment = generation

            // A recursive function, not a self-referencing delegate value, which F# can only check at runtime.
            let rec frame (_: TimeSpan) =
                if active && generation = attachment then
                    try
                        render ()
                    with error ->
                        active <- false
                        onError error

                    if active && generation = attachment then
                        request (Action<TimeSpan> frame)

            request (Action<TimeSpan> frame)

    member _.Stop() =
        active <- false
        generation <- generation + 1
