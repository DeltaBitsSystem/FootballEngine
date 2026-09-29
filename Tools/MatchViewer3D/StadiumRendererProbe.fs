namespace FootballEngine.MatchViewer3D.Probe

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open FootballEngine.Client.Match3D
open FootballEngine.Client.Views.Components

module private ProbeLifecycle =
    let private configuredCycles =
        match Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_CYCLES") with
        | null -> 0
        | value ->
            match Int32.TryParse value with
            | true, cycles when cycles > 0 -> cycles
            | _ -> 0

    let mutable private attached = 0
    let mutable private detached = 0

    let cycles = configuredCycles
    let attachCount () = attached
    let detachCount () = detached
    let attachedOnce () = attached <- attached + 1
    let detachedOnce () = detached <- detached + 1

[<Sealed>]
type private ProbePitch() as this =
    inherit Control()

    let mutable renderer: WebGpuStadiumRenderer option = None
    let mutable time = 0.0
    let timer = DispatcherTimer(Interval = TimeSpan.FromSeconds(1.0 / 30.0))

    let players =
        let home =
            [| for row, count in [| 0, 1; 1, 4; 2, 3; 3, 3 |] do
                   for index = 0 to count - 1 do
                       let x = -46.0 + float row * 14.0
                       let y = (float index - float (count - 1) / 2.0) * 13.0

                       yield
                           { Id = row * 4 + index + 1
                             Position = { X = x; Y = y }
                             Velocity = Vector2.zero
                             Acceleration = Vector2.zero
                             State = Idle
                             VisualSlot = index
                             ShortName = "HOME"
                             Condition = 90
                             IsHome = true
                             HasBall = false
                             IsSidelined = false } |]

        let away =
            [| for row, count in [| 0, 1; 1, 4; 2, 3; 3, 3 |] do
                   for index = 0 to count - 1 do
                       let x = 46.0 - float row * 14.0
                       let y = (float index - float (count - 1) / 2.0) * 13.0

                       yield
                           { Id = 100 + row * 4 + index + 1
                             Position = { X = x; Y = y }
                             Velocity = Vector2.zero
                             Acceleration = Vector2.zero
                             State = Idle
                             VisualSlot = index
                             ShortName = "AWAY"
                             Condition = 90
                             IsHome = false
                             HasBall = false
                             IsSidelined = false } |]

        Array.append home away
        |> Array.mapi (fun index player ->
            { player with
                VisualSlot = index % 11
                IsHome = index < 11 })

    let frame () =
        time <- time + 1.0 / 30.0

        let movingPlayers =
            players
            |> Array.mapi (fun index player ->
                let phase = time * 0.55 + float index * 0.63

                { player with
                    Position =
                        { X = player.Position.X + sin phase * 2.0
                          Y = player.Position.Y + cos phase * 1.5 }
                    Velocity =
                        { X = cos phase * 1.1
                          Y = -sin phase * 0.8 } })

        { Players = movingPlayers
          Ball =
            { Position =
                { X = 4.0 + sin time * 6.0
                  Y = 2.0 + cos time * 4.0 }
              Velocity = { X = 0.0; Y = 0.0 }
              VelocityZ = 0.0
              Spin = { X = 0.0; Y = 1.0 }
              Height = 0.8 + abs (sin (time * 2.0)) }
          Officials = [||]
          TimeSeconds = time
          HomeScore = 2
          AwayScore = 1
          AttackingClubId = None
          Momentum = 0.0
          Situation = MatchSituation.Live
          HomeName = "HOME"
          AwayName = "AWAY"
          CompetitionName = Some "STADIUM WEBGPU PROBE" }

    do
        timer.Tick.Add(fun _ ->
            match renderer with
            | Some active -> active.Render(MatchPitchNative.bounds this, frame (), [||])
            | None -> ())

    override this.OnAttachedToVisualTree args =
        base.OnAttachedToVisualTree args

        match TopLevel.GetTopLevel(this).TryGetPlatformHandle() with
        | null -> invalidOp "Avalonia did not expose a native top-level handle."
        | handle when handle.HandleDescriptor <> "XID" ->
            invalidOp $"Expected an X11 XID for this probe; found '{handle.HandleDescriptor}'."
        | handle ->
            let parent = unativeint (uint64 handle.Handle)

            let asset =
                IO.Path.GetFullPath("FootballEngine.Client/Assets/MatchViewer/stadium.glb")

            renderer <- Some(new WebGpuStadiumRenderer(parent, MatchPitchNative.bounds this, asset))
            ProbeLifecycle.attachedOnce ()
            timer.Start()

    override this.OnDetachedFromVisualTree args =
        timer.Stop()
        let wasAttached = renderer.IsSome
        renderer |> Option.iter (fun value -> (value :> IDisposable).Dispose())
        renderer <- None

        if wasAttached then
            ProbeLifecycle.detachedOnce ()

        base.OnDetachedFromVisualTree args

[<Sealed>]
type private ProbeApp() =
    inherit Application()

    override _.Initialize() = ()

    override this.OnFrameworkInitializationCompleted() =
        match base.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            let asset =
                IO.Path.GetFullPath("FootballEngine.Client/Assets/MatchViewer/stadium.glb")

            let scene = StadiumAsset.load asset

            let staticTriangles =
                scene.StaticBatches |> Array.sumBy (fun batch -> batch.Indices.Length / 3)

            let liveTriangles =
                scene.DynamicBatches |> Array.sumBy (fun batch -> batch.Indices.Length / 3)

            Console.Error.WriteLine(
                $"Loaded stadium batches={scene.StaticBatches.Length}, triangles={staticTriangles}, live batches={scene.DynamicBatches.Length}, live triangles={liveTriangles}, camera={scene.Camera.World}"
            )

            let createWindow () =
                let root = Grid()
                root.RowDefinitions.Add(RowDefinition(52.0, GridUnitType.Pixel))
                root.RowDefinitions.Add(RowDefinition(1.0, GridUnitType.Star))
                root.RowDefinitions.Add(RowDefinition(40.0, GridUnitType.Pixel))
                root.Background <- SolidColorBrush(Color.Parse("#0b1020"))
                let header = Grid()
                header.ColumnDefinitions.Add(ColumnDefinition(1.0, GridUnitType.Star))
                header.ColumnDefinitions.Add(ColumnDefinition(170.0, GridUnitType.Pixel))

                let title =
                    TextBlock(
                        Text = "FOOTBALL ENGINE  ·  HOME 2–1 AWAY",
                        Foreground = Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = Thickness(20.0, 0.0)
                    )

                header.Children.Add(title) |> ignore

                let hudButton =
                    Button(
                        Content = "HUD CONTROL",
                        Background = SolidColorBrush(Color.Parse("#18243a")),
                        Foreground = Brushes.White,
                        Margin = Thickness(8.0),
                        VerticalAlignment = VerticalAlignment.Center
                    )

                hudButton.Click.Add(fun _ -> hudButton.Content <- "HUD CLICK OK")
                Grid.SetColumn(hudButton, 1)
                header.Children.Add(hudButton) |> ignore
                Grid.SetRow(header, 0)
                root.Children.Add(header) |> ignore
                let pitch = ProbePitch()
                Grid.SetRow(pitch, 1)
                root.Children.Add(pitch) |> ignore

                let footer =
                    TextBlock(
                        Text = "LIVE MATCH  ·  WEBGPU BLEND SCENE",
                        Foreground = Brushes.LightGreen,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = Thickness(20.0, 0.0)
                    )

                Grid.SetRow(footer, 2)
                root.Children.Add(footer) |> ignore

                Window(Title = "FootballEngine stadium renderer probe", Width = 1160.0, Height = 790.0, Content = root)

            if ProbeLifecycle.cycles = 0 then
                let window = createWindow ()
                desktop.MainWindow <- window
                window.Show()
            else
                desktop.ShutdownMode <- ShutdownMode.OnExplicitShutdown
                let mutable completed = 0
                let mutable activeWindow: Window option = None
                let cycleTimer = DispatcherTimer(Interval = TimeSpan.FromMilliseconds 350.0)

                cycleTimer.Tick.Add(fun _ ->
                    cycleTimer.Stop()
                    activeWindow |> Option.iter (fun window -> window.Close()))

                let rec openNext () =
                    let window = createWindow ()
                    activeWindow <- Some window
                    desktop.MainWindow <- window

                    window.Closed.Add(fun _ ->
                        completed <- completed + 1

                        if completed >= ProbeLifecycle.cycles then
                            let attached = ProbeLifecycle.attachCount ()
                            let detached = ProbeLifecycle.detachCount ()

                            Console.WriteLine(
                                $"Lifecycle cycles={completed}, renderer attaches={attached}, detaches={detached}"
                            )

                            desktop.Shutdown(
                                if attached = completed && detached = completed then
                                    0
                                else
                                    1
                            )
                        else
                            Dispatcher.UIThread.Post(fun () -> openNext ()))

                    window.Show()
                    cycleTimer.Start()

                openNext ()
        | _ -> ()

        base.OnFrameworkInitializationCompleted()

module Program =
    [<EntryPoint>]
    let main args =
        AppBuilder.Configure<ProbeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args)
