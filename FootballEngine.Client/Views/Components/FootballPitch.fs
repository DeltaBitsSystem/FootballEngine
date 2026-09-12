namespace FootballEngine.Components

open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open FootballEngine.Domain
open FootballEngine.Domain.FormationData


module FootballPitch =

    type PitchConfig =
        { Width: float
          Height: float
          Margin: float
          LineThickness: float
          PlayerNodeSize: float }

    let defaultCfg =
        { Width = 800.0
          Height = 800.0
          // Wide margin: player nodes (~96 wide) center on slot coords, so edge
          // slots would clip labels against the lines with a tight margin.
          Margin = 56.0
          LineThickness = 2.0
          PlayerNodeSize = 85.0 }

    // Smaller pitch variant for dual-view
    let smallCfg =
        { Width = 400.0
          Height = 500.0
          Margin = 18.0
          LineThickness = 1.5
          PlayerNodeSize = 60.0 }

    module Colors =
        // Premium broadcast green — deep, saturated, broadcast-quality
        let grassDark = SolidColorBrush.Parse "#0d2b15"
        let grassLight = SolidColorBrush.Parse "#113a1d"
        let grassStripe = SolidColorBrush.Parse "#0f3219"
        let lineWhite = SolidColorBrush.Parse "#CCffffff"
        let lineGlow = SolidColorBrush.Parse "#15ffffff"
        let centerDot = SolidColorBrush.Parse "#AAffffff"

    let private stripedGrass () =
        Grid.create
            [ Grid.rowDefinitions "*,*,*,*,*,*,*,*,*,*,*,*"
              Grid.children
                  [ for i in 0..11 do
                        Border.create
                            [ Grid.row i
                              Border.background (if i % 4 < 2 then Colors.grassDark else Colors.grassLight) ] ] ]

    let private penaltyArea (cfg: PitchConfig) (isTop: bool) =
        let centerX = cfg.Width / 2.0
        let smallAreaW = 160.0
        let smallAreaH = 50.0
        let largeAreaW = 320.0
        let largeAreaH = 120.0

        [ Rectangle.create
              [ Canvas.left (centerX - largeAreaW / 2.0)
                Canvas.top (
                    if isTop then
                        cfg.Margin
                    else
                        cfg.Height - cfg.Margin - largeAreaH
                )
                Shape.width largeAreaW
                Shape.height largeAreaH
                Shape.stroke Colors.lineWhite
                Shape.strokeThickness cfg.LineThickness ]
          :> IView

          Rectangle.create
              [ Canvas.left (centerX - smallAreaW / 2.0)
                Canvas.top (
                    if isTop then
                        cfg.Margin
                    else
                        cfg.Height - cfg.Margin - smallAreaH
                )
                Shape.width smallAreaW
                Shape.height smallAreaH
                Shape.stroke Colors.lineWhite
                Shape.strokeThickness cfg.LineThickness ]
          :> IView

          // Penalty spot
          Ellipse.create
              [ let pDotY =
                    if isTop then
                        cfg.Margin + 90.0
                    else
                        cfg.Height - cfg.Margin - 90.0

                Canvas.left (centerX - 3.0)
                Canvas.top (pDotY - 3.0)
                Shape.width 6.0
                Shape.height 6.0
                Shape.fill Colors.centerDot ]
          :> IView

          // Penalty arc (top only for visual balance)
          if isTop then
              Arc.create
                  [ Canvas.left (centerX - 75.0)
                    Canvas.top (cfg.Margin + 30.0)
                    Shape.width 150.0
                    Shape.height 150.0
                    Shape.stroke Colors.lineWhite
                    Shape.strokeThickness cfg.LineThickness ]
              :> IView
          else
              Arc.create
                  [ Canvas.left (centerX - 75.0)
                    Canvas.top (cfg.Height - cfg.Margin - 180.0)
                    Shape.width 150.0
                    Shape.height 150.0
                    Shape.stroke Colors.lineWhite
                    Shape.strokeThickness cfg.LineThickness ]
              :> IView ]

    let private markings (cfg: PitchConfig) =
        let w, h, m = cfg.Width, cfg.Height, cfg.Margin
        let centerX, centerY = w / 2.0, h / 2.0

        Canvas.create
            [ Canvas.width w
              Canvas.height h
              Canvas.isHitTestVisible false
              Canvas.children
                  [ // Outer boundary
                    Rectangle.create
                        [ Canvas.left m
                          Canvas.top m
                          Shape.width (w - m * 2.0)
                          Shape.height (h - m * 2.0)
                          Shape.stroke Colors.lineWhite
                          Shape.strokeThickness cfg.LineThickness ]

                    // Halfway line
                    Rectangle.create
                        [ Canvas.left m
                          Canvas.top centerY
                          Shape.width (w - m * 2.0)
                          Shape.height cfg.LineThickness
                          Shape.fill Colors.lineWhite ]

                    // Center circle
                    Ellipse.create
                        [ Canvas.left (centerX - 75.0)
                          Canvas.top (centerY - 75.0)
                          Shape.width 150.0
                          Shape.height 150.0
                          Shape.stroke Colors.lineWhite
                          Shape.strokeThickness cfg.LineThickness ]

                    // Center spot
                    Ellipse.create
                        [ Canvas.left (centerX - 3.0)
                          Canvas.top (centerY - 3.0)
                          Shape.width 6.0
                          Shape.height 6.0
                          Shape.fill Colors.centerDot ]

                    yield! penaltyArea cfg true
                    yield! penaltyArea cfg false ] ]

    let render formation (renderPlayer: LineupSlot -> IView) =
        let cfg = defaultCfg
        let slots = getFormation formation

        // No fixed size: the Viewbox scales Uniform into whatever the parent
        // gives it. A hardcoded 800x800 overflowed smaller cells and clipped.
        Viewbox.create
            [ Viewbox.stretch Stretch.Uniform
              Viewbox.horizontalAlignment HorizontalAlignment.Center
              Viewbox.verticalAlignment VerticalAlignment.Center
              Viewbox.child (
                  Grid.create
                      [ Grid.children
                            [ stripedGrass ()
                              markings cfg

                              Canvas.create
                                  [ Canvas.width cfg.Width
                                    Canvas.height cfg.Height
                                    Canvas.children
                                        [ for slot in slots do
                                              let xPos = (slot.X * cfg.Width) - (cfg.PlayerNodeSize / 2.0)
                                              let yPos = (slot.Y * cfg.Height) - (cfg.PlayerNodeSize / 2.0)

                                              ContentControl.create
                                                  [ Canvas.left xPos
                                                    Canvas.top yPos
                                                    ContentControl.content (renderPlayer slot) ] ] ] ] ]
              ) ]

    // Smaller pitch for dual-view (In Possession / Out of Possession)
    let renderSmall formation (renderPlayer: LineupSlot -> IView) =
        let cfg = smallCfg
        let slots = getFormation formation

        Border.create
            [ Border.cornerRadius 8.0
              Border.clipToBounds true
              Border.child (
                  Viewbox.create
                      [ Viewbox.stretch Stretch.Uniform
                        Viewbox.width cfg.Width
                        Viewbox.height cfg.Height
                        Viewbox.child (
                            Grid.create
                                [ Grid.children
                                      [ stripedGrass ()
                                        markings cfg

                                        Canvas.create
                                            [ Canvas.width cfg.Width
                                              Canvas.height cfg.Height
                                              Canvas.children
                                                  [ for slot in slots do
                                                        let xPos = (slot.X * cfg.Width) - (cfg.PlayerNodeSize / 2.0)

                                                        let yPos = (slot.Y * cfg.Height) - (cfg.PlayerNodeSize / 2.0)

                                                        ContentControl.create
                                                            [ Canvas.left xPos
                                                              Canvas.top yPos
                                                              ContentControl.content (renderPlayer slot) ] ] ] ] ]
                        ) ]
              ) ]
        :> IView
