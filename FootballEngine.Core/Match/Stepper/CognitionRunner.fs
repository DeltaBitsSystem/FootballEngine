namespace FootballEngine.Core.Match

open System.Threading

/// A thread of its own for one match's cognition. A tick's halves last microseconds, so each side spins a
/// bounded time for the other before blocking: a wake through the kernel costs more than the work, and a
/// paused or real-time match must not keep a core busy.
type CognitionThread(spinIterations: int) =
    let ready = new ManualResetEventSlim(false, 0)
    let finished = new ManualResetEventSlim(false, 0)
    let mutable work: unit -> unit = ignore
    let mutable failure: System.Runtime.ExceptionServices.ExceptionDispatchInfo option = None
    let mutable stopping = false
    // 0 idle, 1 work posted, 2 work done; the handoff itself never needs the events.
    let mutable phase = 0

    let await (target: int) (signal: ManualResetEventSlim) =
        let mutable spin = SpinWait()
        let mutable count = 0

        while Volatile.Read &phase <> target && count < spinIterations do
            spin.SpinOnce -1
            count <- count + 1

        while Volatile.Read &phase <> target do
            signal.Wait()
            signal.Reset()

    let loop () =
        while not (Volatile.Read &stopping) do
            await 1 ready

            if not (Volatile.Read &stopping) then
                try
                    work ()
                with error ->
                    failure <- Some(System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture error)

                Volatile.Write(&phase, 2)
                finished.Set()

    let thread =
        Thread(loop, IsBackground = true, Name = "FootballEngine cognition", Priority = ThreadPriority.AboveNormal)

    do thread.Start()

    interface ICognitionRunner with
        member _.Start(next: unit -> unit) =
            work <- next
            Volatile.Write(&phase, 1)
            ready.Set()

        member _.Join() =
            await 2 finished
            Volatile.Write(&phase, 0)
            work <- ignore

            match failure with
            | Some error ->
                failure <- None
                error.Throw()
            | None -> ()

        member _.Dispose() =
            Volatile.Write(&stopping, true)
            Volatile.Write(&phase, 1)
            ready.Set()
            thread.Join()
            ready.Dispose()
            finished.Dispose()
