namespace FootballEngine.Client.Match3D

open System
open System.Threading

/// The renderer and its native connection belong exclusively to this thread.
[<Sealed>]
type RenderWorker<'Input, 'Renderer when 'Renderer :> IDisposable>
    (create: 'Input -> 'Renderer, render: 'Renderer -> 'Input -> bool, onError: exn -> unit) =
    let gate = obj ()
    let changed = new AutoResetEvent(false)
    let mutable latest: 'Input option = None
    let mutable stopping = false
    let mutable disposed = 0

    let read () =
        lock gate (fun () -> if stopping then None else latest)

    let run () =
        try
            let mutable initial = read ()

            while initial.IsNone && not (lock gate (fun () -> stopping)) do
                changed.WaitOne() |> ignore
                initial <- read ()

            match initial with
            | None -> ()
            | Some input ->
                use renderer = create input

                while not (lock gate (fun () -> stopping)) do
                    match read () with
                    | Some input when render renderer input -> ()
                    | _ -> changed.WaitOne() |> ignore
        with error ->
            lock gate (fun () -> stopping <- true)
            onError error

    let thread =
        new Thread(ThreadStart run, IsBackground = true, Name = "Match WebGPU presentation")

    do thread.Start()

    member _.Publish(input: 'Input) =
        lock gate (fun () ->
            if not stopping then
                latest <- Some input
                changed.Set() |> ignore)

    interface IDisposable with
        member _.Dispose() =
            if Interlocked.Exchange(&disposed, 1) = 0 then
                lock gate (fun () ->
                    stopping <- true
                    changed.Set() |> ignore)
                // Joining prevents teardown of the parent window while the GPU child still exists.
                thread.Join()
                changed.Dispose()
