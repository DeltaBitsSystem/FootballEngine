module FootballEngine.Client.Tests.PresentationLoopTests

open System
open Expecto
open FootballEngine.Client.Match3D

let tests =
    testList
        "PresentationLoop"
        [ testCase "one pending callback follows presentation without a fixed FPS timer" (fun _ ->
              let callbacks = Collections.Generic.Queue<Action<TimeSpan>>()
              let mutable frames = 0

              let loop =
                  new PresentationLoop(
                      callbacks.Enqueue,
                      (fun () -> frames <- frames + 1),
                      fun error -> failtest error.Message
                  )

              loop.Start()
              loop.Start()
              Expect.equal callbacks.Count 1 "starting twice cannot double the frame rate"

              for index = 1 to 10 do
                  callbacks.Dequeue().Invoke(TimeSpan.FromMilliseconds(float index))
                  Expect.equal callbacks.Count 1 "exactly one next frame"

              Expect.equal frames 10 "every requested frame renders"
              loop.Stop()
              callbacks.Dequeue().Invoke(TimeSpan.Zero)
              Expect.equal frames 10 "detached callback does nothing"
              Expect.equal callbacks.Count 0 "detached callback cannot requeue")
          testCase "callbacks from an older attachment cannot revive on reattach" (fun _ ->
              let callbacks = Collections.Generic.Queue<Action<TimeSpan>>()
              let mutable frames = 0

              let loop =
                  new PresentationLoop(callbacks.Enqueue, (fun () -> frames <- frames + 1), ignore)

              loop.Start()
              let stale = callbacks.Dequeue()
              loop.Stop()
              loop.Start()
              stale.Invoke(TimeSpan.Zero)
              Expect.equal frames 0 "generation isolates renderer lifetime"
              Expect.equal callbacks.Count 1 "only new attachment remains pending"
              callbacks.Dequeue().Invoke(TimeSpan.Zero)
              Expect.equal frames 1 "new attachment renders"
              loop.Stop())
          testCase "a failed render stops the chain and reports the error once" (fun _ ->
              let callbacks = Collections.Generic.Queue<Action<TimeSpan>>()
              let mutable errors = 0

              let loop =
                  new PresentationLoop(
                      callbacks.Enqueue,
                      (fun () -> raise (InvalidOperationException "render failed")),
                      fun _ -> errors <- errors + 1
                  )

              loop.Start()
              callbacks.Dequeue().Invoke(TimeSpan.Zero)
              Expect.equal errors 1 "failure is reported"
              Expect.equal callbacks.Count 0 "failed renderer does not keep rendering") ]
