#r "nuget: Avalonia, 12.1.3"
#r "nuget: Avalonia.Desktop, 12.1.3"

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Layout
open Avalonia.Media

type private ProbeApp() =
  inherit Application()

  override _.Initialize() = ()

  override _.OnFrameworkInitializationCompleted() =
    printfn "Avalonia host probe initialized"
    let root = Grid()
    root.Background <- SolidColorBrush(Color.Parse("#0B1020"))
    root.ColumnDefinitions.Add(ColumnDefinition(180.0, GridUnitType.Pixel))
    root.ColumnDefinitions.Add(ColumnDefinition(1.0, GridUnitType.Star))
    root.ColumnDefinitions.Add(ColumnDefinition(180.0, GridUnitType.Pixel))
    root.RowDefinitions.Add(RowDefinition(60.0, GridUnitType.Pixel))
    root.RowDefinitions.Add(RowDefinition(1.0, GridUnitType.Star))
    root.RowDefinitions.Add(RowDefinition(70.0, GridUnitType.Pixel))

    let header =
      TextBlock(
        Text = "AVALONIA HUD  ·  HOME 2–1 AWAY",
        Foreground = Brushes.White,
        VerticalAlignment = VerticalAlignment.Center
      )

    Grid.SetRow(header, 0)
    Grid.SetColumnSpan(header, 3)
    root.Children.Add(header) |> ignore

    let left =
      TextBlock(Text = "MANAGER\n\nTACTICS\nSQUAD\nSUBS", Foreground = Brushes.LightGreen, Margin = Thickness(16.0))

    Grid.SetRow(left, 1)
    Grid.SetColumn(left, 0)
    root.Children.Add(left) |> ignore

    let right =
      TextBlock(
        Text = "MATCH INTELLIGENCE\n\nShots  7   —   4\nPasses 82 — 77",
        Foreground = Brushes.White,
        Margin = Thickness(16.0)
      )

    Grid.SetRow(right, 1)
    Grid.SetColumn(right, 2)
    root.Children.Add(right) |> ignore

    let footer =
      TextBlock(
        Text = "PAUSE       1×       2×       4×       POSITIVE",
        Foreground = Brushes.White,
        VerticalAlignment = VerticalAlignment.Center
      )

    Grid.SetRow(footer, 2)
    Grid.SetColumnSpan(footer, 3)
    root.Children.Add(footer) |> ignore

    let window =
      Window(Title = "Avalonia + WebGPU child-surface composition probe", Width = 900.0, Height = 580.0, Content = root)

    match base.ApplicationLifetime with
    | :? IClassicDesktopStyleApplicationLifetime as desktop ->
      printfn "Creating Avalonia parent window"
      desktop.MainWindow <- window
      window.Show()
    | _ -> ()

    base.OnFrameworkInitializationCompleted()

AppBuilder.Configure<ProbeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime([||])
