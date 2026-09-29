namespace FootballEngine.Client.Views.MatchPitch

open System
open System.Diagnostics
open Avalonia
open Avalonia.Controls
open Avalonia.FuncUI.Builder
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Threading
open FootballEngine
open FootballEngine.AppTypes
open FootballEngine.Client.Views.Components
open FootballEngine.Client.Match3D
open FootballEngine.Core.Match
open FootballEngine.LiveMatch

type private PitchBindingState =
    { Binding: MatchPitchBinding
      Stream: MatchPresentationStream
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

type MatchPitchControl() as this =
    inherit Control()

    static let bindingProperty =
        AvaloniaProperty.Register<MatchPitchControl, MatchPitchBinding option>("Binding", None)

    let mutable renderer: WebGpuStadiumRenderer option = None
    let mutable state: PitchBindingState option = None
    let timer = DispatcherTimer(Interval = TimeSpan.FromSeconds(1.0 / 60.0))

    let currentFrame state =
        match state.Stream.Sample(Stopwatch.GetTimestamp()) with
        | None -> None
        | Some(Hold pose) -> Some(PitchBindingState.project state pose.Snapshot)
        | Some(Between(first, second, t)) ->
            let left = PitchBindingState.project state first.Snapshot
            let right = PitchBindingState.project state second.Snapshot
            let dt = right.TimeSeconds - left.TimeSeconds
            Some(MatchInterp.hermite left right t dt)

    do
        timer.Tick.Add(fun _ ->
            match renderer, state with
            | Some activeRenderer, Some state ->
                try
                    currentFrame state
                    |> Option.iter (fun frame ->
                        activeRenderer.Render(
                            MatchPitchNative.bounds this,
                            frame,
                            MatchPitchNative.overlayCutouts this
                        ))
                with error ->
                    Diagnostics.Trace.WriteLine($"WebGPU match viewer stopped after a render error: {error}")
                    (activeRenderer :> IDisposable).Dispose()
                    renderer <- None
            | _ -> ())

    static member BindingProperty = bindingProperty

    member this.Binding
        with get () = this.GetValue bindingProperty
        and set value = this.SetValue(bindingProperty, value) |> ignore

    override this.OnPropertyChanged change =
        base.OnPropertyChanged change

        if change.Property = bindingProperty then
            state <-
                match this.Binding with
                | Some binding ->
                    MatchRuntime.presentationStream binding.RuntimeId
                    |> Option.map (PitchBindingState.create binding)
                | None -> None

    override this.OnAttachedToVisualTree args =
        base.OnAttachedToVisualTree args

        try
            renderer <- MatchPitchNative.createRenderer this
        with error ->
            Diagnostics.Trace.WriteLine($"WebGPU match viewer could not initialize: {error}")

        timer.Start()

    override this.OnDetachedFromVisualTree args =
        timer.Stop()
        renderer |> Option.iter (fun value -> (value :> IDisposable).Dispose())
        renderer <- None
        state <- None
        base.OnDetachedFromVisualTree args

module MatchPitch =
    let view (runtimeId: MatchRuntimeId) (fixture: FixtureContext) : IView =
        let binding =
            { RuntimeId = runtimeId
              Fixture = fixture }

        ViewBuilder.Create<MatchPitchControl>(
            [ AttrBuilder<MatchPitchControl>
                  .CreateProperty<MatchPitchBinding option>(MatchPitchControl.BindingProperty, Some binding, ValueNone) ]
        )
        :> IView
