namespace FootballEngine.Client.Views.MatchPitch

open System
open System.Diagnostics
open Avalonia
open Avalonia.Controls
open Avalonia.FuncUI.Builder
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open FootballEngine
open FootballEngine.AppTypes
open FootballEngine.Client.Views.Components
open FootballEngine.Client.Match3D
open FootballEngine.Core.Match
open FootballEngine.LiveMatch
open FootballEngine.Client.Animation

type private PitchBindingState =
    { Binding: MatchPitchBinding
      Stream: MatchPresentationStream
      HudStats: MatchHudStats
      HomeNames: string array
      AwayNames: string array
      HomeIds: FootballEngine.Core.Match.PlayerId array
      AwayIds: FootballEngine.Core.Match.PlayerId array
      HomeStarterCount: int
      AwayStarterCount: int }

module private PitchBindingState =
    let create (binding: MatchPitchBinding) (stream: MatchPresentationStream) : PitchBindingState =
        let home = binding.Fixture.HomeLineup.Players
        let away = binding.Fixture.AwayLineup.Players

        { Binding = binding
          Stream = stream
          HudStats = binding.HudStats
          HomeNames = home |> Array.map _.Name
          AwayNames = away |> Array.map _.Name
          HomeIds = home |> Array.map (fun player -> FootballEngine.Core.Match.PlayerId player.Id)
          AwayIds = away |> Array.map (fun player -> FootballEngine.Core.Match.PlayerId player.Id)
          HomeStarterCount = binding.Fixture.HomeLineup.StarterCount
          AwayStarterCount = binding.Fixture.AwayLineup.StarterCount }

    let project (state: PitchBindingState) (snapshot: FootballEngine.Core.Match.Replay.ReplaySnapshot) : RenderFrame =
        let time =
            float snapshot.Tick / float SimulationClock.defaultClock.SubTicksPerSecond

        MatchProjection.project
            state.HomeNames
            state.AwayNames
            state.HomeIds
            state.AwayIds
            state.HomeStarterCount
            state.AwayStarterCount
            state.Binding.Fixture.Home.ClubId
            state.Binding.Fixture.Away.ClubId
            state.Binding.Fixture.Home.Name
            state.Binding.Fixture.Away.Name
            (Some state.Binding.Fixture.CompetitionName)
            snapshot
            time

type private PitchProjectionCache() =
    let mutable first = None
    let mutable second = None
    let mutable fixture = None

    member _.Project(state: PitchBindingState, snapshot: FootballEngine.Core.Match.Replay.ReplaySnapshot) =
        if
            not (
                fixture
                |> Option.exists (fun source -> obj.ReferenceEquals(source, state.Binding.Fixture))
            )
        then
            first <- None
            second <- None
            fixture <- Some state.Binding.Fixture

        match first, second with
        | Some(source, frame), _ when obj.ReferenceEquals(source, snapshot) -> frame
        | _, Some(source, frame) when obj.ReferenceEquals(source, snapshot) -> frame
        | _ ->
            let frame = PitchBindingState.project state snapshot
            first <- second
            second <- Some(snapshot, frame)
            frame

type MatchPitchControl() as this =
    inherit Control()

    static let bindingProperty =
        AvaloniaProperty.Register<MatchPitchControl, MatchPitchBinding option>("Binding", None)

    static let cameraProperty =
        AvaloniaProperty.Register<MatchPitchControl, ViewerCamera>("Camera", ViewerCamera.Broadcast)

    let mutable worker: RenderWorker<PixelRect * ViewerCamera * PitchBindingState option, WebGpuStadiumRenderer> option =
        None

    let mutable state: PitchBindingState option = None
    let mutable presentation: PresentationLoop option = None
    let mutable firstPresented =
        Threading.Tasks.TaskCompletionSource<unit>(Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously)

    let currentFrame (cache: PitchProjectionCache) state =
        match state.Stream.Sample(ViewerDiagnostics.presentationTimestamp ()) with
        | None -> None
        | Some(Hold pose) ->
            Some(
                cache.Project(state, pose.Snapshot),
                { Tick = int pose.Tick
                  Fraction = 0.0f }
            )
        | Some(Between(first, second, t)) ->
            let left = cache.Project(state, first.Snapshot)
            let right = cache.Project(state, second.Snapshot)
            let dt = right.TimeSeconds - left.TimeSeconds

            let time =
                MotionTime.ofTicks (float first.Tick + float (second.Tick - first.Tick) * t)

            Some(MatchInterp.hermite left right t dt, time)

    let publishLayout () =
        let visibleState = if this.IsEffectivelyVisible then state else None

        worker
        |> Option.iter (fun active -> active.Publish(MatchPitchNative.bounds this, this.Camera, visibleState))

    static member BindingProperty = bindingProperty
    static member CameraProperty = cameraProperty

    member this.Camera
        with get () = this.GetValue cameraProperty
        and set value = this.SetValue(cameraProperty, value) |> ignore

    member this.Binding
        with get () = this.GetValue bindingProperty
        and set value = this.SetValue(bindingProperty, value) |> ignore

    override this.OnPropertyChanged change =
        base.OnPropertyChanged change

        if change.Property = cameraProperty then
            publishLayout ()

        if change.Property = bindingProperty then
            state <-
                match this.Binding with
                | Some binding ->
                    match state with
                    | Some previous when
                        previous.Binding.RuntimeId = binding.RuntimeId
                        && obj.ReferenceEquals(previous.Binding.Fixture, binding.Fixture)
                        ->
                        Some
                            { previous with
                                Binding = binding
                                HudStats = binding.HudStats }
                    | _ ->
                        MatchRuntime.presentationStream binding.RuntimeId
                        |> Option.map (PitchBindingState.create binding)
                | None -> None

            publishLayout ()

    override this.OnAttachedToVisualTree args =
        base.OnAttachedToVisualTree args
        firstPresented <- Threading.Tasks.TaskCompletionSource<unit>(Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously)
        let ready = firstPresented

        try
            match TopLevel.GetTopLevel(this), MatchPitchNative.rendererFactory this with
            | null, _
            | _, None -> ()
            | topLevel, Some create ->
                let cache = PitchProjectionCache()

                let mutable evaluator: MotionPaletteEvaluator option = None

                let render (renderer: WebGpuStadiumRenderer) ((bounds: PixelRect), camera, state) =
                    // Reuse the renderer's validated rig instead of decoding the entire player again on the UI thread.
                    let activeEvaluator =
                        match evaluator with
                        | Some value -> value
                        | None ->
                            let matcher =
                                match MotionMatcher.Create(renderer.MotionDatabase, renderer.CharacterRig, AnimationConfig.defaultConfig, MatchingConfig.defaultConfig) with
                                | Ok matcher -> Some matcher
                                | Error message ->
                                    // A database that does not fit the rig leaves the procedural body rather than no players.
                                    Diagnostics.Trace.WriteLine($"Motion matching disabled: {message}")
                                    None

                            let value = MotionPaletteEvaluator(renderer.CharacterRig, AnimationConfig.defaultConfig, matcher)
                            evaluator <- Some value
                            value

                    renderer.SetCamera camera
                    match state with
                    | Some state when bounds.Width > 0 && bounds.Height > 0 ->
                        let home = state.Binding.Fixture.Home
                        // The club id seeds the layout, so a club plays every home match in the same ground.
                        renderer.SetStadium(
                            StadiumIdentity.create
                                home.Reputation
                                (defaultArg home.LeagueLevel 2)
                                (int home.ClubId)
                                home.PrimaryColor
                                home.SecondaryColor
                        )

                        match currentFrame cache state with
                        | Some(frame, time) ->
                            match activeEvaluator with
                            | evaluator when state.Stream.MotionRigMatches evaluator.RigHash && evaluator.Evaluate(state.Stream, frame, time) ->
                                let presented = renderer.Render(bounds, frame, evaluator.Palettes, [||], state.HudStats)
                                if presented && not ready.Task.IsCompleted then
                                    // SurfacePresent queues work; capture readiness must also wait for the first GPU submission.
                                    renderer.WaitForGpu()
                                    ready.TrySetResult(()) |> ignore
                                presented
                            | _ -> false
                        | None -> false
                    | _ -> false

                let onError error =
                    ready.TrySetException(error: exn) |> ignore
                    Diagnostics.Trace.WriteLine($"WebGPU match viewer stopped after a render error: {error}")

                worker <- Some(new RenderWorker<_, _>((fun (bounds, _, _) -> create bounds), render, onError))
                publishLayout ()

                let loop =
                    new PresentationLoop(topLevel.RequestAnimationFrame, publishLayout, onError)

                presentation <- Some loop
                loop.Start()
        with error ->
            ready.TrySetException error |> ignore
            Diagnostics.Trace.WriteLine($"WebGPU match viewer could not initialize: {error}")

    member _.FirstPresented = firstPresented.Task

    interface IPitchPresentationSource with
        member _.FirstPresented = firstPresented.Task

    override this.OnDetachedFromVisualTree args =
        firstPresented.TrySetCanceled() |> ignore
        presentation |> Option.iter _.Stop()
        presentation <- None
        worker |> Option.iter (fun value -> (value :> IDisposable).Dispose())
        worker <- None
        state <- None
        base.OnDetachedFromVisualTree args

module MatchPitch =
    let private hudStats (live: LiveMatchState) =
        let fixture = live.Fixture

        let clubsByPlayer =
            [| yield!
                   fixture.HomeLineup.Players
                   |> Array.map (fun player -> player.Id, fixture.Home.ClubId)
               yield!
                   fixture.AwayLineup.Players
                   |> Array.map (fun player -> player.Id, fixture.Away.ClubId) |]
            |> Map.ofArray

        let metrics =
            FootballEngine.Domain.MatchMetrics.extract
                live.Events
                fixture.Home.ClubId
                fixture.Away.ClubId
                (fun playerId -> Map.tryFind playerId clubsByPlayer)

        { HomeShortName = fixture.Home.ShortName
          AwayShortName = fixture.Away.ShortName
          Period = live.Clock.Period
          HomePossession = int (Math.Round(metrics.PossessionHome * 100.0))
          AwayPossession = int (Math.Round(metrics.PossessionAway * 100.0))
          HomeShots = live.Metrics.Home.Shots
          AwayShots = live.Metrics.Away.Shots
          HomeOnTarget = live.Metrics.Home.OnTarget
          AwayOnTarget = live.Metrics.Away.OnTarget
          HomePasses = live.Metrics.Home.PassesCompleted
          AwayPasses = live.Metrics.Away.PassesCompleted
          HomeColor = fixture.Home.PrimaryColor
          AwayColor = fixture.Away.PrimaryColor }

    let view (runtimeId: MatchRuntimeId) (live: LiveMatchState) (camera: ViewerCamera) : IView =
        let binding =
            { RuntimeId = runtimeId
              Fixture = live.Fixture
              HudStats = hudStats live }

        ViewBuilder.Create<MatchPitchControl>(
            [ AttrBuilder<MatchPitchControl>
                  .CreateProperty<MatchPitchBinding option>(MatchPitchControl.BindingProperty, Some binding, ValueNone)
              AttrBuilder<MatchPitchControl>
                  .CreateProperty<ViewerCamera>(MatchPitchControl.CameraProperty, camera, ValueNone) ]
        )
        :> IView
