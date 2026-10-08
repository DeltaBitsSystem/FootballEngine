module FootballEngine.Client.Tests.RenderWorkerTests

open System
open System.Threading
open Expecto
open FootballEngine.Client.Match3D

type private TestRenderer(dispose: unit -> unit) =
    interface IDisposable with
        member _.Dispose() = dispose ()

let tests =
    testList
        "RenderWorker"
        [ testCase "renderer creation rendering and disposal have one non-UI owner" (fun _ ->
              let caller = Environment.CurrentManagedThreadId
              let owners = Collections.Concurrent.ConcurrentQueue<int>()
              use rendered = new ManualResetEventSlim(false)
              let mutable errors = 0

              let create _ =
                  owners.Enqueue Environment.CurrentManagedThreadId
                  new TestRenderer(fun () -> owners.Enqueue Environment.CurrentManagedThreadId)

              let render _ _ =
                  owners.Enqueue Environment.CurrentManagedThreadId
                  rendered.Set()
                  false

              let worker =
                  new RenderWorker<int, TestRenderer>(
                      create,
                      render,
                      fun _ -> Interlocked.Increment(&errors) |> ignore
                  )

              worker.Publish 1
              Expect.isTrue (rendered.Wait 5000) "worker renders without a UI frame callback"
              (worker :> IDisposable).Dispose()
              (worker :> IDisposable).Dispose()
              let threads = owners.ToArray()

              Expect.isTrue
                  (threads |> Array.forall (fun id -> id = threads[0] && id <> caller))
                  "native resources never cross threads"

              Expect.equal errors 0 "no error on idempotent shutdown")
          testCase "idle worker wakes for newest input and stops without an initialized renderer" (fun _ ->
              let mutable created = 0

              let worker =
                  new RenderWorker<int, TestRenderer>(
                      (fun _ ->
                          created <- created + 1
                          new TestRenderer(ignore)),
                      (fun _ _ -> false),
                      ignore
                  )

              (worker :> IDisposable).Dispose()
              worker.Publish 99
              Expect.equal created 0 "stopped idle worker cannot initialize late")
          testCase "latest publication replaces stale queued layouts" (fun _ ->
              use entered = new ManualResetEventSlim(false)
              use proceed = new ManualResetEventSlim(false)
              use finished = new ManualResetEventSlim(false)
              let mutable observed = 0

              let create _ =
                  entered.Set()
                  proceed.Wait()
                  new TestRenderer(ignore)

              let render _ input =
                  observed <- input
                  finished.Set()
                  false

              use worker = new RenderWorker<int, TestRenderer>(create, render, ignore)
              worker.Publish 1
              Expect.isTrue (entered.Wait 5000) "initialization starts"
              worker.Publish 2
              worker.Publish 3
              proceed.Set()
              Expect.isTrue (finished.Wait 5000) "renderer receives input"
              Expect.equal observed 3 "no stale-frame FIFO between UI and renderer")
          testCase "failure disposes native owner before reporting and exits" (fun _ ->
              use failed = new ManualResetEventSlim(false)
              let mutable disposed = false

              use worker =
                  new RenderWorker<int, TestRenderer>(
                      (fun _ -> new TestRenderer(fun () -> disposed <- true)),
                      (fun _ _ -> raise (InvalidOperationException "render failed")),
                      (fun _ -> failed.Set())
                  )

              worker.Publish 1
              Expect.isTrue (failed.Wait 5000) "error callback fires"
              Expect.isTrue disposed "resources released before error notification") ]
