namespace FootballEngine.Components

open System
open Avalonia.Automation
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Shapes
open Avalonia.FuncUI.Builder
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open Avalonia.FuncUI.DSL
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Icons


module UI =

    /// One shared cursor: a view rebuilt on every update would otherwise create a native cursor handle each time.
    let handCursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)

    type ButtonVariant =
        | Primary
        | Secondary
        | Ghost
        | Danger

    type ControlState =
        | Default
        | Hover
        | Pressed
        | Focused
        | Disabled of reason: string
        | Loading
        | Error of message: string

    type ButtonState = ControlState

    type BannerLevel =
        | Info
        | Success
        | Warning
        | Error

    let private automationName<'t when 't :> Control> (name: string) : IAttr<'t> =
        AttrBuilder<'t>.CreateProperty<string>(AutomationProperties.NameProperty, name, ValueNone)

    let private automationHelp<'t when 't :> Control> (help: string) : IAttr<'t> =
        AttrBuilder<'t>.CreateProperty<string>(AutomationProperties.HelpTextProperty, help, ValueNone)

    let private stateHelp =
        function
        | ControlState.Disabled reason -> reason
        | ControlState.Loading -> Localization.resolve TranslationKey.Loading []
        | ControlState.Error message -> message
        | _ -> ""

    let private stateEnabled =
        function
        | ControlState.Disabled _
        | ControlState.Loading -> false
        | _ -> true

    /// Scoreboard type: condensed, upper case, heavy. Headings and the numbers a manager scans.
    let display (text: string) (size: float) (color: string) : IView =
        TextBlock.create
            [ TextBlock.text (text.ToUpperInvariant())
              TextBlock.fontFamily Theme.DisplayFont
              TextBlock.fontSize size
              TextBlock.fontWeight FontWeight.ExtraBold
              TextBlock.foreground color
              TextBlock.verticalAlignment VerticalAlignment.Center ]
        :> IView

    /// Small upper-case label that names a value (CAJA, MORAL, RACHA).
    let eyebrow (text: string) (color: string) : IView =
        TextBlock.create
            [ TextBlock.text (text.ToUpperInvariant())
              TextBlock.fontSize 12.0
              TextBlock.fontWeight FontWeight.SemiBold
              TextBlock.foreground color ]
        :> IView

    /// Ink that stays readable on a solid chip colour: night navy on light fills, white on dark ones.
    let inkOn (background: string) =
        if Theme.meetsAaTextContrast Theme.night.OnAccent background then
            Theme.night.OnAccent
        else
            "#FFFFFF"

    /// Paints a club kit: hard colour stops give a sash, stripes, hoops or halves on any shape.
    let kitBrush (kit: ClubIdentity.Kit) : IBrush =
        let bands (startX, startY) (endX, endY) (stops: (string * float) list) =
            let gradient = LinearGradientBrush()
            gradient.StartPoint <- Avalonia.RelativePoint(startX, startY, Avalonia.RelativeUnit.Relative)
            gradient.EndPoint <- Avalonia.RelativePoint(endX, endY, Avalonia.RelativeUnit.Relative)

            for color, offset in stops do
                gradient.GradientStops.Add(GradientStop(Color.Parse color, offset))

            gradient :> IBrush

        let p, s = kit.Primary, kit.Secondary

        let alternating count =
            [ for band in 0 .. count - 1 do
                  let color = if band % 2 = 0 then p else s
                  yield color, float band / float count
                  yield color, float (band + 1) / float count ]

        match kit.Pattern with
        | Plain -> SolidColorBrush(Color.Parse p) :> IBrush
        | Sash ->
            bands (0.0, 0.0) (1.0, 1.0) [ p, 0.0; p, 0.40; s, 0.40; s, 0.58; p, 0.58; p, 1.0 ]
        | Stripes -> bands (0.0, 0.5) (1.0, 0.5) (alternating 5)
        | Hoops -> bands (0.5, 0.0) (0.5, 1.0) (alternating 5)
        | Band -> bands (0.5, 0.0) (0.5, 1.0) [ p, 0.0; p, 0.38; s, 0.38; s, 0.6; p, 0.6; p, 1.0 ]
        | Halves -> bands (0.0, 0.5) (1.0, 0.5) [ p, 0.0; p, 0.5; s, 0.5; s, 1.0 ]

    let crest (kit: ClubIdentity.Kit) (size: float) : IView =
        Border.create
            [ Border.width size
              Border.height size
              Border.cornerRadius (size * 0.28)
              Border.background (kitBrush kit)
              // A plain kit has no pattern to carry its second colour; without this trim Velez or Huracan read as a
              // blank white tile.
              Border.borderBrush (if kit.Pattern = Plain then kit.Secondary else "#FFFFFF")
              Border.borderThickness (if kit.Pattern = Plain then max 2.0 (size * 0.09) else 2.0)
              Border.boxShadow (BoxShadow.Parse "0 4 14 0 #55000000") ]
        :> IView

    let private shirtGeometry =
        "M16 4 L10 6 L2 13 L7 21 L12 18 L12 44 L36 44 L36 18 L41 21 L46 13 L38 6 L32 4 Q24 10 16 4 Z"

    /// A football shirt in the club's kit with its squad number on the chest.
    let shirt (kit: ClubIdentity.Kit) (number: string) (size: float) (outline: string) (outlineWidth: float) : IView =
        Panel.create
            [ Panel.width size
              Panel.height size
              Panel.children
                  [ Path.create
                        [ Path.data shirtGeometry
                          Path.stretch Stretch.Uniform
                          Path.fill (kitBrush kit)
                          Path.stroke outline
                          Path.strokeThickness outlineWidth
                          Path.strokeJoin PenLineJoin.Round ]
                    // The number sits on a white tag so it reads on stripes, hoops and dark kits alike.
                    if number <> "" then
                        Border.create
                            [ Border.background "#F2FFFFFF"
                              Border.cornerRadius (size * 0.08)
                              Border.padding (size * 0.06, 0.0)
                              Border.minWidth (size * 0.32)
                              Border.horizontalAlignment HorizontalAlignment.Center
                              Border.verticalAlignment VerticalAlignment.Top
                              Border.margin (0.0, size * 0.3, 0.0, 0.0)
                              Border.child (
                                  TextBlock.create
                                      [ TextBlock.text number
                                        TextBlock.fontFamily Theme.DisplayFont
                                        TextBlock.fontWeight FontWeight.ExtraBold
                                        TextBlock.fontSize (size * 0.32)
                                        TextBlock.foreground "#0A1020"
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                              ) ] ] ]
        :> IView

    let private colorWithState (variant: ButtonVariant) (state: ControlState) =
        let colors = Theme.currentTokens.Value.Colors

        let baseStyle =
            match variant with
            | Primary -> colors.Accent, colors.OnAccent, colors.Accent
            | Secondary -> Theme.BgElevated, colors.TextPrimary, Theme.BorderLight
            | Ghost -> "Transparent", colors.TextSecondary, colors.BorderSubtle
            | Danger -> colors.Negative, colors.TextPrimary, colors.Negative

        match state with
        | ControlState.Hover ->
            match variant with
            | Primary -> colors.Accent, colors.OnAccent, colors.Accent
            | Secondary
            | Ghost -> colors.Surface3, colors.TextPrimary, colors.BorderSubtle
            | Danger -> colors.Negative, colors.TextPrimary, colors.Negative
        | ControlState.Pressed ->
            match variant with
            | Primary -> colors.AccentStrong, colors.OnAccent, colors.AccentStrong
            | Secondary
            | Ghost -> colors.Surface3, colors.TextPrimary, colors.BorderSubtle
            | Danger -> colors.Negative, colors.TextPrimary, colors.Negative
        | ControlState.Focused ->
            let background, foreground, _ = baseStyle
            background, foreground, colors.FocusRing
        | ControlState.Disabled _
        | ControlState.Loading -> colors.DisabledSurface, colors.DisabledText, colors.BorderSubtle
        | ControlState.Error _ -> colors.Negative, colors.TextPrimary, colors.Negative
        | ControlState.Default -> baseStyle

    let private buttonContent
        (label: string)
        (icon: Material.Icons.MaterialIconKind option)
        (state: ControlState)
        (foreground: string)
        =
        let text =
            if state = ControlState.Loading then
                Localization.resolve TranslationKey.Loading []
            else
                label

        StackPanel.create
            [ StackPanel.orientation Orientation.Horizontal
              StackPanel.spacing Theme.currentTokens.Value.Spacing.Sm
              StackPanel.children
                  [ if state = ControlState.Loading then
                        Icons.iconSm IconName.refresh foreground
                    else
                        match icon with
                        | Some kind -> Icons.iconSm kind foreground
                        | None -> ()
                    TextBlock.create
                        [ TextBlock.text text
                          TextBlock.fontSize Theme.currentTokens.Value.Typography.Body
                          TextBlock.fontWeight FontWeight.SemiBold
                          TextBlock.foreground foreground
                          TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]

    let button
        (variant: ButtonVariant)
        (state: ControlState)
        (label: string)
        (icon: Material.Icons.MaterialIconKind option)
        (accessibleName: string)
        onClick
        : IView =
        let background, foreground, border = colorWithState variant state
        let tokens = Theme.currentTokens.Value
        let help = stateHelp state

        Button.create
            [ Button.background background
              Button.foreground foreground
              Button.borderBrush border
              Button.borderThickness (
                  if state = ControlState.Focused then
                      tokens.Focus.RingWidth
                  else
                      1.0
              )
              Button.padding (tokens.Spacing.Lg, tokens.Spacing.Sm)
              Button.minHeight (
                  if variant = Primary then
                      tokens.Controls.ActionHeight
                  else
                      tokens.Controls.StandardHeight
              )
              Button.cornerRadius tokens.Radii.Control
              Button.fontSize tokens.Typography.Body
              Button.fontWeight FontWeight.SemiBold
              Button.focusable true
              Button.isTabStop true
              Button.isEnabled (stateEnabled state)
              Button.onClick onClick
              automationName accessibleName
              automationHelp help
              Button.content (buttonContent label icon state foreground) ]
        |> fun view ->
            // The lit primary action is the one thing on a page that should pull the eye.
            if variant = Primary && stateEnabled state then
                Border.create
                    [ Border.cornerRadius tokens.Radii.Control
                      Border.boxShadow (Theme.accentGlow ())
                      Border.child view ]
                :> IView
            else
                view :> IView

    let primaryButtonWithState
        (label: string)
        (icon: Material.Icons.MaterialIconKind option)
        (state: ControlState)
        onClick
        =
        button Primary state label icon label onClick

    let secondaryButton (label: string) (icon: Material.Icons.MaterialIconKind option) (state: ControlState) onClick =
        button Secondary state label icon label onClick

    let ghostButtonWithState
        (label: string)
        (icon: Material.Icons.MaterialIconKind option)
        (state: ControlState)
        onClick
        =
        button Ghost state label icon label onClick

    let dangerButton (label: string) (icon: Material.Icons.MaterialIconKind option) (state: ControlState) onClick =
        button Danger state label icon label onClick

    let iconOnlyButton
        (accessibleName: string)
        (icon: Material.Icons.MaterialIconKind)
        (variant: ButtonVariant)
        (state: ControlState)
        onClick
        =
        let background, foreground, border = colorWithState variant state
        let tokens = Theme.currentTokens.Value
        let help = stateHelp state

        Button.create
            [ Button.background background
              Button.foreground foreground
              Button.borderBrush border
              Button.borderThickness (
                  if state = ControlState.Focused then
                      tokens.Focus.RingWidth
                  else
                      1.0
              )
              Button.padding tokens.Spacing.Sm
              Button.minWidth tokens.Controls.MinimumTarget
              Button.minHeight tokens.Controls.MinimumTarget
              Button.cornerRadius tokens.Radii.Control
              Button.focusable true
              Button.isTabStop true
              Button.isEnabled (stateEnabled state)
              Button.onClick onClick
              ToolTip.tip accessibleName
              automationName accessibleName
              automationHelp help
              Button.content (
                  if state = ControlState.Loading then
                      Icons.iconSm IconName.refresh foreground
                  else
                      Icons.iconMd icon foreground
              ) ]
        :> IView

    let private bannerColor =
        function
        | BannerLevel.Info -> Theme.currentTokens.Value.Colors.Info
        | BannerLevel.Success -> Theme.currentTokens.Value.Colors.Positive
        | BannerLevel.Warning -> Theme.currentTokens.Value.Colors.Warning
        | BannerLevel.Error -> Theme.currentTokens.Value.Colors.Negative

    let banner (level: BannerLevel) (title: string) (message: string) (icon: Material.Icons.MaterialIconKind) : IView =
        let color = bannerColor level
        let tokens = Theme.currentTokens.Value

        let content =
            StackPanel.create
                [ StackPanel.orientation Orientation.Horizontal
                  StackPanel.spacing tokens.Spacing.Md
                  StackPanel.children
                      [ Icons.iconSm icon color
                        StackPanel.create
                            [ StackPanel.spacing tokens.Spacing.Xs
                              StackPanel.children
                                  [ TextBlock.create
                                        [ TextBlock.text title
                                          TextBlock.foreground tokens.Colors.TextPrimary
                                          TextBlock.fontSize tokens.Typography.Emphasis
                                          TextBlock.fontWeight FontWeight.SemiBold ]
                                    TextBlock.create
                                        [ TextBlock.text message
                                          TextBlock.foreground tokens.Colors.TextSecondary
                                          TextBlock.fontSize tokens.Typography.Body
                                          TextBlock.textWrapping TextWrapping.Wrap ] ] ] ] ]

        Border.create
            [ Border.background (Theme.withAlpha "18" color)
              Border.borderBrush color
              Border.borderThickness 1.0
              Border.cornerRadius tokens.Radii.Control
              Border.padding tokens.Spacing.Lg
              automationName title
              automationHelp message
              Border.child content ]
        :> IView

    let toast (level: BannerLevel) (message: string) : IView =
        banner level "Notification" message IconName.info

    let dialog (title: string) (message: string) (actions: IView list) : IView =
        let tokens = Theme.currentTokens.Value

        let content =
            StackPanel.create
                [ StackPanel.spacing tokens.Spacing.Lg
                  StackPanel.children
                      [ TextBlock.create
                            [ TextBlock.text title
                              TextBlock.fontSize tokens.Typography.Section
                              TextBlock.fontWeight FontWeight.SemiBold
                              TextBlock.foreground tokens.Colors.TextPrimary ]
                        TextBlock.create
                            [ TextBlock.text message
                              TextBlock.fontSize tokens.Typography.Body
                              TextBlock.foreground tokens.Colors.TextSecondary
                              TextBlock.textWrapping TextWrapping.Wrap ]
                        StackPanel.create
                            [ StackPanel.orientation Orientation.Horizontal
                              StackPanel.spacing tokens.Spacing.Sm
                              StackPanel.children actions ] ] ]

        Border.create
            [ Border.background tokens.Colors.Surface2
              Border.borderBrush tokens.Colors.BorderSubtle
              Border.borderThickness 1.0
              Border.cornerRadius tokens.Radii.Hero
              Border.padding tokens.Spacing.Xxl
              automationName title
              automationHelp message
              Border.child content ]
        :> IView

    let private fieldFeedback (help: string option) (error: string option) : IView list =
        [ match help with
          | Some text when not (String.IsNullOrWhiteSpace text) ->
              TextBlock.create
                  [ TextBlock.text text
                    TextBlock.fontSize (Theme.currentTokens.Value.Typography.Detail)
                    TextBlock.foreground Theme.currentTokens.Value.Colors.TextMuted
                    TextBlock.textWrapping TextWrapping.Wrap ]
              :> IView
          | _ -> ()
          match error with
          | Some text when not (String.IsNullOrWhiteSpace text) ->
              TextBlock.create
                  [ TextBlock.text text
                    TextBlock.fontSize (Theme.currentTokens.Value.Typography.Detail)
                    TextBlock.foreground Theme.currentTokens.Value.Colors.Negative
                    TextBlock.textWrapping TextWrapping.Wrap ]
              :> IView
          | _ -> () ]

    let textField
        (label: string)
        (value: string)
        (placeholder: string)
        (help: string option)
        (error: string option)
        (isEnabled: bool)
        onChanged
        : IView =
        let tokens = Theme.currentTokens.Value
        let feedback = error |> Option.orElse help

        StackPanel.create
            [ StackPanel.spacing tokens.Spacing.Sm
              StackPanel.children
                  [ TextBlock.create
                        [ TextBlock.text label
                          TextBlock.fontSize tokens.Typography.Body
                          TextBlock.fontWeight FontWeight.SemiBold
                          TextBlock.foreground tokens.Colors.TextPrimary ]
                    TextBox.create
                        [ TextBox.text value
                          TextBox.placeholderText placeholder
                          TextBox.fontSize tokens.Typography.Body
                          TextBox.minHeight tokens.Controls.StandardHeight
                          TextBox.padding (tokens.Spacing.Md, tokens.Spacing.Sm)
                          TextBox.borderBrush (
                              if error.IsSome then
                                  tokens.Colors.Negative
                              else
                                  tokens.Colors.BorderSubtle
                          )
                          TextBox.borderThickness 1.0
                          TextBox.isEnabled isEnabled
                          TextBox.onTextChanged onChanged
                          automationName label
                          automationHelp (feedback |> Option.defaultValue "") ]
                    yield! fieldFeedback help error ] ]
        :> IView

    let selectField
        (label: string)
        (items: 'a list)
        (selected: 'a)
        (help: string option)
        (error: string option)
        (isEnabled: bool)
        onChanged
        : IView =
        let tokens = Theme.currentTokens.Value

        StackPanel.create
            [ StackPanel.spacing tokens.Spacing.Sm
              StackPanel.children
                  [ TextBlock.create
                        [ TextBlock.text label
                          TextBlock.fontSize tokens.Typography.Body
                          TextBlock.fontWeight FontWeight.SemiBold
                          TextBlock.foreground tokens.Colors.TextPrimary ]
                    ComboBox.create
                        [ ComboBox.dataItems items
                          ComboBox.selectedItem selected
                          ComboBox.minHeight tokens.Controls.StandardHeight
                          ComboBox.isEnabled isEnabled
                          ComboBox.onSelectedItemChanged (fun value ->
                              if not (isNull value) then
                                  onChanged (value :?> 'a))
                          automationName label
                          automationHelp (error |> Option.orElse help |> Option.defaultValue "") ]
                    yield! fieldFeedback help error ] ]
        :> IView

    let checkBox (label: string) (isChecked: bool) (help: string option) (isEnabled: bool) onChanged : IView =
        let tokens = Theme.currentTokens.Value

        StackPanel.create
            [ StackPanel.spacing tokens.Spacing.Xs
              StackPanel.children
                  [ CheckBox.create
                        [ CheckBox.isChecked isChecked
                          CheckBox.content label
                          CheckBox.fontSize tokens.Typography.Body
                          CheckBox.minHeight tokens.Controls.MinimumTarget
                          CheckBox.isEnabled isEnabled
                          CheckBox.onIsCheckedChanged (fun args ->
                              match args.Source with
                              | :? CheckBox as checkBox -> onChanged (checkBox.IsChecked = Nullable true)
                              | _ -> ())
                          automationName label
                          automationHelp (help |> Option.defaultValue "") ]
                    yield!
                        help
                        |> Option.map (fun text ->
                            TextBlock.create
                                [ TextBlock.text text
                                  TextBlock.fontSize tokens.Typography.Detail
                                  TextBlock.foreground tokens.Colors.TextMuted
                                  TextBlock.margin (tokens.Spacing.Xl, 0.0, 0.0, 0.0) ]
                            :> IView)
                        |> Option.toList ] ]
        :> IView

    let toggle = checkBox

    let segmentedControl (options: ('a * string) list) (selected: 'a) (isEnabled: bool) onChanged : IView =
        let tokens = Theme.currentTokens.Value

        StackPanel.create
            [ StackPanel.orientation Orientation.Horizontal
              StackPanel.spacing tokens.Spacing.Xs
              StackPanel.children
                  [ for value, label in options do
                        let state =
                            if value = selected then
                                ControlState.Focused
                            else
                                ControlState.Default

                        button Secondary state label None label (fun _ -> onChanged value)
                        |> fun view -> Border.create [ Border.isEnabled isEnabled; Border.child view ] ] ]
        :> IView

    let pageHeader
        (title: string)
        (context: string option)
        (primaryAction: IView option)
        (secondaryActions: IView list)
        : IView =
        let tokens = Theme.currentTokens.Value

        let actions =
            [ yield! secondaryActions
              match primaryAction with
              | Some action -> yield action
              | None -> () ]

        Border.create
            [ Border.padding (0.0, 0.0, 0.0, tokens.Spacing.Xl)
              automationName title
              Border.child (
                  DockPanel.create
                      [ DockPanel.lastChildFill false
                        DockPanel.children
                            [ StackPanel.create
                                  [ StackPanel.spacing tokens.Spacing.Xs
                                    StackPanel.children
                                        [ display title tokens.Typography.Page tokens.Colors.TextPrimary
                                          match context with
                                          | Some text ->
                                              TextBlock.create
                                                  [ TextBlock.text text
                                                    TextBlock.fontSize tokens.Typography.Body
                                                    TextBlock.foreground tokens.Colors.TextSecondary ]
                                          | None -> () ] ]
                              StackPanel.create
                                  [ DockPanel.dock Dock.Right
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing tokens.Spacing.Sm
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children actions ] ] ]
              ) ]
        :> IView

    let sectionHeaderContent (title: string) (description: string option) (action: IView option) : IView =
        let tokens = Theme.currentTokens.Value

        Border.create
            [ Border.padding (0.0, tokens.Spacing.Lg)
              Border.borderBrush tokens.Colors.BorderSubtle
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              automationName title
              Border.child (
                  DockPanel.create
                      [ DockPanel.lastChildFill false
                        DockPanel.children
                            [ StackPanel.create
                                  [ StackPanel.spacing tokens.Spacing.Xs
                                    StackPanel.children
                                        [ display title tokens.Typography.Section tokens.Colors.TextPrimary
                                          match description with
                                          | Some text ->
                                              TextBlock.create
                                                  [ TextBlock.text text
                                                    TextBlock.fontSize tokens.Typography.Body
                                                    TextBlock.foreground tokens.Colors.TextSecondary ]
                                          | None -> () ] ]
                              match action with
                              | Some view ->
                                  Border.create
                                      [ DockPanel.dock Dock.Right
                                        Border.verticalAlignment VerticalAlignment.Center
                                        Border.child view ]
                              | None -> () ] ]
              ) ]
        :> IView

    type MetricData =
        { Value: string
          Unit: string option
          Trend: string option
          Context: string option }

    let metric (data: MetricData) : IView =
        let tokens = Theme.currentTokens.Value

        let valueText =
            match data.Unit with
            | Some unit -> $"{data.Value} {unit}"
            | None -> data.Value

        StackPanel.create
            [ StackPanel.spacing tokens.Spacing.Xs
              StackPanel.children
                  [ display valueText 30.0 tokens.Colors.TextPrimary
                    match data.Trend with
                    | Some trend ->
                        TextBlock.create
                            [ TextBlock.text trend
                              TextBlock.fontSize tokens.Typography.Body
                              TextBlock.foreground tokens.Colors.Positive ]
                    | None -> ()
                    match data.Context with
                    | Some context ->
                        TextBlock.create
                            [ TextBlock.text context
                              TextBlock.fontSize tokens.Typography.Detail
                              TextBlock.foreground tokens.Colors.TextMuted ]
                    | None -> () ] ]
        :> IView

    let statusBanner (color: string) (icon: Material.Icons.MaterialIconKind) (text: string) =
        Border.create
            [ Border.background (Theme.withAlpha "18" color)
              Border.borderBrush color
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusLg
              Border.padding 16.0
              automationName text
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 12.0
                        StackPanel.children
                            [ Icons.iconSm icon color
                              TextBlock.create
                                  [ TextBlock.text text
                                    TextBlock.foreground Theme.TextMain
                                    TextBlock.fontSize 12.0
                                    TextBlock.fontWeight FontWeight.Medium
                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
              ) ]

    let primaryButton (label: string) (icon: Material.Icons.MaterialIconKind option) onClick =
        primaryButtonWithState label icon Default onClick

    let ghostButton (label: string) (icon: Material.Icons.MaterialIconKind option) onClick =
        ghostButtonWithState label icon Default onClick

    let iconToggleButton (label: string) (iconKind: Material.Icons.MaterialIconKind) (isActive: bool) onClick =
        let tokens = Theme.currentTokens.Value

        Button.create
            [ Button.onClick onClick
              Button.padding (10.0, 6.0)
              Button.minHeight tokens.Controls.StandardHeight
              Button.cornerRadius 6.0
              Button.cursor Avalonia.Input.Cursor.Default
              Button.background (if isActive then Theme.AccentLight else "Transparent")
              Button.borderBrush (if isActive then Theme.Accent else "Transparent")
              Button.borderThickness (if isActive then 1.0 else 0.0)
              Button.focusable true
              Button.isTabStop true
              automationName label
              Button.content (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 4.0
                        StackPanel.children
                            [ Icons.iconSm iconKind (if isActive then Theme.Accent else Theme.TextMuted)
                              TextBlock.create
                                  [ TextBlock.text label
                                    TextBlock.fontSize tokens.Typography.Body
                                    TextBlock.fontWeight (if isActive then FontWeight.SemiBold else FontWeight.Normal)
                                    TextBlock.foreground (if isActive then Theme.Accent else Theme.TextMuted)
                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
              ) ]

    let badge (text: string) (color: string) =
        Border.create
            [ Border.background color
              Border.cornerRadius Theme.RadiusSm
              Border.padding (8.0, 3.0)
              Border.child (
                  TextBlock.create
                      [ TextBlock.text text
                        TextBlock.fontSize (Theme.currentTokens.Value.Typography.Detail)
                        TextBlock.fontWeight FontWeight.Bold
                        TextBlock.foreground (inkOn color) ]
              ) ]

    let countBadge (count: int) =
        Border.create
            [ Border.background Theme.Hot
              Border.cornerRadius Theme.RadiusPill
              Border.minWidth 22.0
              Border.padding (7.0, 2.0)
              Border.child (
                  TextBlock.create
                      [ TextBlock.text (string count)
                        TextBlock.fontSize (Theme.currentTokens.Value.Typography.Detail)
                        TextBlock.fontWeight FontWeight.Black
                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                        TextBlock.foreground (inkOn Theme.Hot) ]
              ) ]

    let positionBadge (pos: Position) =
        let color = Theme.positionColor pos

        Border.create
            [ Border.background color
              Border.cornerRadius Theme.RadiusSm
              Border.minWidth 44.0
              Border.padding (7.0, 4.0)
              Border.child (
                  TextBlock.create
                      [ TextBlock.text $"%A{pos}"
                        TextBlock.fontSize (Theme.currentTokens.Value.Typography.Detail)
                        TextBlock.fontWeight FontWeight.Black
                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                        TextBlock.foreground (inkOn color) ]
              ) ]

    let statCard (iconKind: Material.Icons.MaterialIconKind) (label: string) (value: string) (sub: string) =
        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius (Theme.RadiusLg + 2.0)
              Border.padding (18.0, 16.0)
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions "auto, *"
                        Grid.rowDefinitions "auto, auto"
                        Grid.children
                            [ Border.create
                                  [ Grid.column 0
                                    Grid.rowSpan 2
                                    Border.background Theme.AccentLight
                                    Border.cornerRadius Theme.RadiusMd
                                    Border.padding 9.0
                                    Border.margin (0.0, 0.0, 14.0, 0.0)
                                    Border.verticalAlignment VerticalAlignment.Center
                                    Border.child (Icons.iconMd iconKind Theme.Accent) ]
                              TextBlock.create
                                  [ Grid.column 1
                                    Grid.row 0
                                    TextBlock.text (label.ToUpperInvariant())
                                    TextBlock.fontSize (Theme.currentTokens.Value.Typography.Detail)
                                    TextBlock.fontWeight FontWeight.SemiBold
                                    TextBlock.foreground Theme.TextMuted ]
                              StackPanel.create
                                  [ Grid.column 1
                                    Grid.row 1
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 6.0
                                    StackPanel.children
                                        [ display value 34.0 Theme.TextMain
                                          if sub <> "" then
                                              TextBlock.create
                                                  [ TextBlock.text sub
                                                    TextBlock.fontSize (Theme.currentTokens.Value.Typography.Detail)
                                                    TextBlock.foreground Theme.TextMuted
                                                    TextBlock.verticalAlignment VerticalAlignment.Bottom
                                                    TextBlock.margin (0.0, 0.0, 0.0, 3.0) ] ] ] ] ]
              ) ]

    let iconStatCard (label: string) (value: string) (iconKind: Material.Icons.MaterialIconKind) (sub: string) =
        statCard iconKind label value sub

    let statMiniCard (iconKind: Material.Icons.MaterialIconKind) (label: string) (value: string) (color: string) =
        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusLg
              Border.padding (16.0, 14.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 4.0
                        StackPanel.children
                            [ StackPanel.create
                                  [ StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 6.0
                                    StackPanel.children
                                        [ Border.create
                                              [ Border.background (Theme.withAlpha "15" color)
                                                Border.cornerRadius 6.0
                                                Border.padding 4.0
                                                Border.child (Icons.iconSm iconKind color) ]
                                          TextBlock.create
                                              [ TextBlock.text label
                                                TextBlock.foreground Theme.TextDim
                                                TextBlock.fontSize (Theme.currentTokens.Value.Typography.Detail)
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
                              display value 30.0 Theme.TextMain ] ]
              ) ]

    let sectionTitle (label: string) (iconKind: Material.Icons.MaterialIconKind) =
        StackPanel.create
            [ StackPanel.orientation Orientation.Horizontal
              StackPanel.spacing 8.0
              StackPanel.margin (0.0, 0.0, 0.0, 10.0)
              StackPanel.children
                  [ Icons.iconSm iconKind Theme.Accent
                    display label 22.0 Theme.TextMain ] ]

    let sectionHeader (iconKind: Material.Icons.MaterialIconKind) (label: string) : IView =
        Border.create
            [ Border.background "Transparent"
              Border.padding (20.0, 16.0, 20.0, 10.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 10.0
                        StackPanel.children [ Icons.iconSm iconKind Theme.Accent; display label 22.0 Theme.TextMain ] ]
              ) ]
        :> IView

    let sectionHeaderWithBadge (iconKind: Material.Icons.MaterialIconKind) (label: string) (count: int) : IView =
        Border.create
            [ Border.background "Transparent"
              Border.padding (20.0, 16.0, 20.0, 10.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 10.0
                        StackPanel.children
                            [ Icons.iconSm iconKind Theme.Accent
                              display label 22.0 Theme.TextMain
                              Border.create [ Border.verticalAlignment VerticalAlignment.Center; Border.child (countBadge count) ] ] ]
              ) ]
        :> IView

    let panelCard (header: IView) (body: IView) : IView =
        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius (Theme.RadiusLg + 2.0)
              Border.clipToBounds true
              Border.child (StackPanel.create [ StackPanel.spacing 0.0; StackPanel.children [ header; body ] ]) ]
        :> IView

    let emptyState (iconKind: Material.Icons.MaterialIconKind) (title: string) (sub: string) : IView =
        StackPanel.create
            [ StackPanel.verticalAlignment VerticalAlignment.Center
              StackPanel.horizontalAlignment HorizontalAlignment.Center
              StackPanel.spacing 10.0
              StackPanel.margin (0.0, 40.0)
              StackPanel.children
                  [ Border.create
                        [ Border.background (Theme.withAlpha "12" Theme.TextMuted)
                          Border.cornerRadius 999.0
                          Border.padding 16.0
                          Border.horizontalAlignment HorizontalAlignment.Center
                          Border.child (Icons.iconXl iconKind Theme.TextDim) ]
                    TextBlock.create
                        [ TextBlock.text title
                          TextBlock.fontSize 14.0
                          TextBlock.fontWeight FontWeight.SemiBold
                          TextBlock.foreground Theme.TextSub
                          TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                    if sub <> "" then
                        TextBlock.create
                            [ TextBlock.text sub
                              TextBlock.fontSize (Theme.currentTokens.Value.Typography.Detail)
                              TextBlock.foreground Theme.TextDim
                              TextBlock.textAlignment TextAlignment.Center
                              TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
        :> IView

    let loadingState () : IView =
        StackPanel.create
            [ StackPanel.verticalAlignment VerticalAlignment.Center
              StackPanel.horizontalAlignment HorizontalAlignment.Center
              StackPanel.spacing 12.0
              StackPanel.margin (0.0, 40.0)
              StackPanel.children
                  [ Icons.iconXl IconName.refresh Theme.TextMuted
                    TextBlock.create
                        [ TextBlock.text "Loading..."
                          TextBlock.fontSize 13.0
                          TextBlock.foreground Theme.TextMuted
                          TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
        :> IView

    let loadingStateWith (message: string) : IView =
        StackPanel.create
            [ StackPanel.verticalAlignment VerticalAlignment.Center
              StackPanel.horizontalAlignment HorizontalAlignment.Center
              StackPanel.spacing Theme.currentTokens.Value.Spacing.Md
              StackPanel.children
                  [ Icons.iconXl IconName.refresh Theme.currentTokens.Value.Colors.TextMuted
                    TextBlock.create
                        [ TextBlock.text message
                          TextBlock.fontSize Theme.currentTokens.Value.Typography.Body
                          TextBlock.foreground Theme.currentTokens.Value.Colors.TextMuted
                          TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
        :> IView

    let errorState
        (iconKind: Material.Icons.MaterialIconKind)
        (title: string)
        (message: string)
        (retry: (unit -> unit) option)
        : IView =
        let tokens = Theme.currentTokens.Value

        let action =
            match retry with
            | Some onRetry ->
                [ primaryButtonWithState
                      (Localization.resolve TranslationKey.Retry [])
                      (Some IconName.refresh)
                      ControlState.Default
                      (fun _ -> onRetry ()) ]
            | None -> []

        StackPanel.create
            [ StackPanel.verticalAlignment VerticalAlignment.Center
              StackPanel.horizontalAlignment HorizontalAlignment.Center
              StackPanel.spacing tokens.Spacing.Md
              StackPanel.children
                  [ Border.create
                        [ Border.background (Theme.withAlpha "24" tokens.Colors.Negative)
                          Border.cornerRadius tokens.Radii.Pill
                          Border.padding tokens.Spacing.Md
                          Border.horizontalAlignment HorizontalAlignment.Center
                          Border.child (Icons.iconLg iconKind tokens.Colors.Negative) ]
                    TextBlock.create
                        [ TextBlock.text title
                          TextBlock.fontSize tokens.Typography.Emphasis
                          TextBlock.fontWeight FontWeight.SemiBold
                          TextBlock.foreground tokens.Colors.TextPrimary
                          TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                    TextBlock.create
                        [ TextBlock.text message
                          TextBlock.fontSize tokens.Typography.Body
                          TextBlock.foreground tokens.Colors.TextSecondary
                          TextBlock.textWrapping TextWrapping.Wrap
                          TextBlock.textAlignment TextAlignment.Center
                          TextBlock.maxWidth 480.0 ]
                    yield! action ] ]
        :> IView

    let disabledState
        (iconKind: Material.Icons.MaterialIconKind)
        (title: string)
        (reason: string)
        (action: IView option)
        : IView =
        let tokens = Theme.currentTokens.Value

        StackPanel.create
            [ StackPanel.verticalAlignment VerticalAlignment.Center
              StackPanel.horizontalAlignment HorizontalAlignment.Center
              StackPanel.spacing tokens.Spacing.Md
              StackPanel.children
                  [ Border.create
                        [ Border.background (Theme.withAlpha "18" tokens.Colors.DisabledText)
                          Border.cornerRadius tokens.Radii.Pill
                          Border.padding tokens.Spacing.Md
                          Border.horizontalAlignment HorizontalAlignment.Center
                          Border.child (Icons.iconLg iconKind tokens.Colors.DisabledText) ]
                    TextBlock.create
                        [ TextBlock.text title
                          TextBlock.fontSize tokens.Typography.Emphasis
                          TextBlock.fontWeight FontWeight.SemiBold
                          TextBlock.foreground tokens.Colors.TextPrimary
                          TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                    TextBlock.create
                        [ TextBlock.text reason
                          TextBlock.fontSize tokens.Typography.Body
                          TextBlock.foreground tokens.Colors.TextSecondary
                          TextBlock.textWrapping TextWrapping.Wrap
                          TextBlock.textAlignment TextAlignment.Center
                          TextBlock.maxWidth 480.0 ]
                    match action with
                    | Some view -> view
                    | None -> () ] ]
        :> IView

    type DataTableColumn = { Header: string; Width: string }

    let dataTable (columns: DataTableColumn list) (rows: IView list) (state: ControlState) : IView =
        let tokens = Theme.currentTokens.Value

        let header =
            Border.create
                [ Border.background tokens.Colors.Surface1
                  Border.borderBrush tokens.Colors.BorderSubtle
                  Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                  Border.padding (tokens.Spacing.Lg, tokens.Spacing.Sm)
                  Border.child (
                      Grid.create
                          [ Grid.columnDefinitions (columns |> List.map _.Width |> String.concat ",")
                            Grid.children
                                [ for index, column in columns |> List.indexed do
                                      TextBlock.create
                                          [ Grid.column index
                                            TextBlock.text column.Header
                                            TextBlock.fontSize tokens.Typography.Detail
                                            TextBlock.fontWeight FontWeight.SemiBold
                                            TextBlock.foreground tokens.Colors.TextMuted
                                            automationName column.Header ] ] ]
                  ) ]

        let body =
            match state with
            | ControlState.Loading -> loadingStateWith (Localization.resolve TranslationKey.Loading [])
            | ControlState.Error message ->
                errorState IconName.error (Localization.resolve TranslationKey.ErrorTitle []) message None
            | ControlState.Disabled reason ->
                disabledState IconName.info (Localization.resolve TranslationKey.DisabledTitle []) reason None
            | _ -> StackPanel.create [ StackPanel.children rows ] :> IView

        Border.create
            [ Border.background tokens.Colors.Surface2
              Border.borderBrush tokens.Colors.BorderSubtle
              Border.borderThickness 1.0
              Border.cornerRadius tokens.Radii.Panel
              Border.clipToBounds true
              Border.child (StackPanel.create [ StackPanel.children [ header; body ] ]) ]
        :> IView

    let inspector (title: string) (isOpen: bool) (body: IView) (onToggle: unit -> unit) : IView =
        let tokens = Theme.currentTokens.Value

        let header =
            secondaryButton
                (if isOpen then "Hide " + title else "Show " + title)
                (Some(if isOpen then IconName.collapse else IconName.expand))
                ControlState.Default
                (fun _ -> onToggle ())

        Border.create
            [ Border.background tokens.Colors.Surface2
              Border.borderBrush tokens.Colors.BorderSubtle
              Border.borderThickness 1.0
              Border.cornerRadius tokens.Radii.Panel
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing tokens.Spacing.Md
                        StackPanel.children
                            [ header
                              if isOpen then body else () ] ]
              ) ]
        :> IView

    type DecisionCardData =
        { Situation: string
          Evidence: string
          Proposal: string
          Cost: string
          Action: IView }

    let decisionCard (data: DecisionCardData) : IView =
        let tokens = Theme.currentTokens.Value

        let paragraph (label: string) (text: string) (color: string) =
            StackPanel.create
                [ StackPanel.spacing tokens.Spacing.Xs
                  StackPanel.children
                      [ TextBlock.create
                            [ TextBlock.text label
                              TextBlock.fontSize tokens.Typography.Detail
                              TextBlock.fontWeight FontWeight.SemiBold
                              TextBlock.foreground color ]
                        TextBlock.create
                            [ TextBlock.text text
                              TextBlock.fontSize tokens.Typography.Body
                              TextBlock.foreground tokens.Colors.TextSecondary
                              TextBlock.textWrapping TextWrapping.Wrap ] ] ]

        Border.create
            [ Border.background tokens.Colors.Surface2
              Border.borderBrush tokens.Colors.BorderSubtle
              Border.borderThickness 1.0
              Border.cornerRadius tokens.Radii.Panel
              Border.padding tokens.Spacing.Xl
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing tokens.Spacing.Lg
                        StackPanel.children
                            [ paragraph "Situation" data.Situation tokens.Colors.TextPrimary
                              paragraph "Evidence" data.Evidence tokens.Colors.Info
                              paragraph "Proposal" data.Proposal tokens.Colors.Accent
                              paragraph "Cost / uncertainty" data.Cost tokens.Colors.Warning
                              data.Action ] ]
              ) ]
        :> IView

    let confidenceIndicator (label: string) (range: string) (context: string option) : IView =
        let tokens = Theme.currentTokens.Value

        Border.create
            [ Border.background (Theme.withAlpha "18" tokens.Colors.Info)
              Border.borderBrush (Theme.withAlpha "66" tokens.Colors.Info)
              Border.borderThickness 1.0
              Border.cornerRadius tokens.Radii.Control
              Border.padding (tokens.Spacing.Md, tokens.Spacing.Sm)
              automationName label
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing tokens.Spacing.Xs
                        StackPanel.children
                            [ TextBlock.create
                                  [ TextBlock.text $"{label}: {range}"
                                    TextBlock.fontSize tokens.Typography.Body
                                    TextBlock.fontWeight FontWeight.SemiBold
                                    TextBlock.foreground tokens.Colors.Info ]
                              match context with
                              | Some text ->
                                  TextBlock.create
                                      [ TextBlock.text text
                                        TextBlock.fontSize tokens.Typography.Detail
                                        TextBlock.foreground tokens.Colors.TextSecondary ]
                              | None -> () ] ]
              ) ]
        :> IView

    let iconRow (iconKind: Material.Icons.MaterialIconKind) (text: string) =
        Border.create
            [ Border.background Theme.BgCard
              Border.cornerRadius 6.0
              Border.padding (10.0, 8.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 8.0
                        StackPanel.children
                            [ Icons.iconSm iconKind Theme.TextMuted
                              TextBlock.create
                                  [ TextBlock.text text
                                    TextBlock.fontSize Theme.currentTokens.Value.Typography.Detail
                                    TextBlock.foreground Theme.TextSub
                                    TextBlock.textWrapping TextWrapping.Wrap
                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
              ) ]

    let tabButton (label: string) isActive onClick =
        let tokens = Theme.currentTokens.Value

        Button.create
            [ Button.content label
              Button.background (if isActive then Theme.Accent else Theme.BgCard)
              Button.foreground (if isActive then Theme.OnAccent else Theme.TextSub)
              Button.borderThickness 1.0
              Button.borderBrush (if isActive then Theme.Accent else Theme.BorderLight)
              Button.padding (16.0, 8.0)
              Button.minHeight tokens.Controls.StandardHeight
              Button.fontSize tokens.Typography.Body
              Button.fontWeight FontWeight.SemiBold
              Button.cornerRadius Theme.RadiusPill
              Button.focusable true
              Button.isTabStop true
              automationName label
              Button.onClick onClick
              Button.margin (0.0, 0.0, 10.0, 0.0) ]

    let sectionContainer (title: string) (content: IView) =
        StackPanel.create
            [ StackPanel.spacing 10.0
              StackPanel.children
                  [ display title 22.0 Theme.TextMain
                    Border.create
                        [ Border.background Theme.BgCard
                          Border.borderBrush Theme.Border
                          Border.borderThickness 1.0
                          Border.cornerRadius (Theme.RadiusLg + 2.0)
                          Border.clipToBounds true
                          Border.child content ] ] ]

    let menuButton (title: string) (icon: Material.Icons.MaterialIconKind) (description: string) onClick =
        Button.create
            [ Button.onClick onClick
              Button.focusable true
              Button.isTabStop true
              automationName title
              automationHelp description
              Button.padding 0.0
              Button.background "transparent"
              Button.content (
                  Border.create
                      [ Border.background Theme.BgCard
                        Border.cornerRadius Theme.RadiusXl
                        Border.padding 30.0
                        Border.borderBrush Theme.Border
                        Border.borderThickness 1.0
                        Border.child (
                            StackPanel.create
                                [ StackPanel.spacing 10.0
                                  StackPanel.children
                                      [ Icons.iconXl icon Theme.TextMain
                                        Border.create
                                            [ Border.horizontalAlignment HorizontalAlignment.Center
                                              Border.child (display title 26.0 Theme.Accent) ]
                                        TextBlock.create
                                            [ TextBlock.text description
                                              TextBlock.fontSize 12.0
                                              TextBlock.foreground Theme.TextMuted
                                              TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
                        ) ]
              ) ]

    let countrySelectionCard name flag isPrimary isSecondary onClickPrimary onClickSecondary =
        let borderColor =
            if isPrimary then Theme.Accent
            elif isSecondary then Theme.AccentAlt
            else "transparent"

        let statusText, statusColor =
            if isPrimary then "PRIMARY LEAGUE", Theme.Accent
            elif isSecondary then "SIMULATED", Theme.AccentAlt
            else "NOT ACTIVE", Theme.TextMuted

        Border.create
            [ Border.background Theme.BgCard
              Border.cornerRadius 10.0
              Border.borderThickness 2.0
              Border.borderBrush borderColor
              Border.padding 15.0
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions (ColumnDefs.toGridString ColumnDefs.inboxListCols)
                        Grid.children
                            [ TextBlock.create
                                  [ Grid.column 0
                                    TextBlock.text flag
                                    TextBlock.fontSize 32.0
                                    TextBlock.verticalAlignment VerticalAlignment.Center ]
                              StackPanel.create
                                  [ Grid.column 1
                                    StackPanel.margin (15.0, 0.0)
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text name
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.fontSize 16.0 ]
                                          TextBlock.create
                                              [ TextBlock.text statusText
                                                TextBlock.fontSize Theme.currentTokens.Value.Typography.Detail
                                                TextBlock.foreground statusColor ] ] ]
                              StackPanel.create
                                  [ Grid.column 2
                                    StackPanel.spacing 5.0
                                    StackPanel.children
                                        [ Button.create
                                              [ Button.content (if isPrimary then "✓ Main" else "Set Main")
                                                Button.fontSize Theme.currentTokens.Value.Typography.Detail
                                                Button.minHeight Theme.currentTokens.Value.Controls.CompactHeight
                                                automationName (if isPrimary then "Main league" else "Set main league")
                                                Button.onClick onClickPrimary
                                                Button.background (if isPrimary then Theme.Accent else Theme.BgSidebar) ]
                                          Button.create
                                              [ Button.content (if isSecondary then "✕ Remove" else "+ Add")
                                                Button.fontSize Theme.currentTokens.Value.Typography.Detail
                                                Button.minHeight Theme.currentTokens.Value.Controls.CompactHeight
                                                automationName (
                                                    if isSecondary then
                                                        "Remove simulated league"
                                                    else
                                                        "Add simulated league"
                                                )
                                                Button.onClick onClickSecondary
                                                Button.isEnabled (not isPrimary) ] ] ] ] ]
              ) ]

    let roleBadge (role: string) (duty: string) =
        StackPanel.create
            [ StackPanel.orientation Orientation.Horizontal
              StackPanel.spacing 2.0
              StackPanel.horizontalAlignment HorizontalAlignment.Center
              StackPanel.children
                  [ TextBlock.create
                        [ TextBlock.text role
                          TextBlock.fontWeight FontWeight.Bold
                          TextBlock.fontSize (Theme.currentTokens.Value.Typography.Detail)
                          TextBlock.foreground "#ffffff" ]
                    TextBlock.create
                        [ TextBlock.text $"- {duty}"
                          TextBlock.fontSize (Theme.currentTokens.Value.Typography.Detail)
                          TextBlock.foreground "#fbbf24" ] ] ]

    let sidebarButton (isActive: bool) =
        [ Button.background (if isActive then Theme.Accent else "transparent")
          Button.foreground (if isActive then Theme.OnAccent else Theme.TextMain)
          Button.borderThickness 0.0
          Button.cornerRadius Theme.RadiusMd
          Button.margin (10.0, 4.0)
          Button.padding (15.0, 10.0)
          Button.horizontalAlignment HorizontalAlignment.Stretch ]


module PlayerView =

    let private tr key = Localization.resolve key []

    let private statusBadge (p: Player) : IView list =
        match p.Status with
        | Injured(severity, _) ->
            let color =
                match severity with
                | Minor
                | Moderate -> Theme.Warning
                | _ -> Theme.Danger

            [ UI.badge (tr TranslationKey.StatusInjured) color ]
        | Suspended days ->
            [ UI.badge (Localization.resolve TranslationKey.StatusSuspended [ "days", string days ]) Theme.Warning ]
        | Available -> []

    let private bar (value: float) (maximum: float) (color: string) (height: float) : IView =
        ProgressBar.create
            [ ProgressBar.minimum 0.0
              ProgressBar.maximum maximum
              ProgressBar.value value
              ProgressBar.height height
              ProgressBar.minHeight height
              ProgressBar.cornerRadius (height / 2.0)
              ProgressBar.background Theme.Border
              ProgressBar.foreground color ]
        :> IView

    let private conditionColor (value: int) =
        if value >= 75 then Theme.Accent
        elif value >= 55 then Theme.Warning
        else Theme.Hot

    /// A squad row is a card: position chip, who, how fit, and the level a manager scans first.
    let row (p: Player) (currentDate: DateTime) (isSelected: bool) (isDragged: bool) onSelect =
        Border.create
            [ Border.background (if isSelected then Theme.BgCardHover else Theme.BgCard)
              Border.borderBrush (
                  if isSelected then Theme.Accent
                  elif isDragged then Theme.DragBorder
                  else Theme.Border
              )
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusLg
              Border.padding (16.0, 12.0)
              Border.margin (0.0, 0.0, 0.0, 8.0)
              Border.cursor UI.handCursor
              Border.onTapped (fun _ -> onSelect ())
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions "Auto, 16, *, 16, 120, 16, 64, 12, 64"
                        Grid.children
                            [ Border.create
                                  [ Grid.column 0
                                    Border.verticalAlignment VerticalAlignment.Center
                                    Border.child (UI.positionBadge p.Position) ]
                              StackPanel.create
                                  [ Grid.column 2
                                    StackPanel.spacing 3.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ StackPanel.create
                                              [ StackPanel.orientation Orientation.Horizontal
                                                StackPanel.spacing 8.0
                                                StackPanel.children
                                                    [ TextBlock.create
                                                          [ TextBlock.text p.Name
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.fontSize 15.0
                                                            TextBlock.foreground Theme.TextMain
                                                            TextBlock.textTrimming TextTrimming.CharacterEllipsis ]
                                                      yield! statusBadge p ] ]
                                          TextBlock.create
                                              [ TextBlock.text (
                                                    Localization.resolve
                                                        TranslationKey.PlayerAgeMorale
                                                        [ "age", string (Player.age currentDate p); "morale", string p.Morale ]
                                                )
                                                TextBlock.fontSize 13.0
                                                TextBlock.foreground Theme.TextMuted ] ] ]
                              StackPanel.create
                                  [ Grid.column 4
                                    StackPanel.spacing 6.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text (tr TranslationKey.PlayerCondition)
                                                TextBlock.fontSize 12.0
                                                TextBlock.foreground Theme.TextMuted ]
                                          bar (float p.Condition) 100.0 (conditionColor p.Condition) 6.0 ] ]
                              TextBlock.create
                                  [ Grid.column 6
                                    TextBlock.text $"€{int (Player.playerValue p.CurrentSkill / 1_000_000m)}M"
                                    TextBlock.fontSize 13.0
                                    TextBlock.foreground Theme.TextSub
                                    TextBlock.horizontalAlignment HorizontalAlignment.Right
                                    TextBlock.verticalAlignment VerticalAlignment.Center ]
                              Border.create
                                  [ Grid.column 8
                                    Border.horizontalAlignment HorizontalAlignment.Right
                                    Border.child (UI.display (string p.CurrentSkill) 32.0 Theme.TextMain) ] ] ]
              ) ]

    type private StatGroup =
        { Title: string
          Color: string
          Stats: (TranslationKey * int) list }

    let private statGroupsFor (p: Player) =
        let physical =
            { Title = tr TranslationKey.AttrGroupPhysical
              Color = Theme.AccentAlt
              Stats =
                [ TranslationKey.AttrAcceleration, p.Physical.Acceleration
                  TranslationKey.AttrPace, p.Physical.Pace
                  TranslationKey.AttrAgility, p.Physical.Agility
                  TranslationKey.AttrBalance, p.Physical.Balance
                  TranslationKey.AttrJumpingReach, p.Physical.JumpingReach
                  TranslationKey.AttrStamina, p.Physical.Stamina
                  TranslationKey.AttrStrength, p.Physical.Strength ] }

        let mental =
            { Title = tr TranslationKey.AttrGroupMental
              Color = Theme.Success
              Stats =
                [ TranslationKey.AttrAggression, p.Mental.Aggression
                  TranslationKey.AttrComposure, p.Mental.Composure
                  TranslationKey.AttrConcentration, p.Mental.Concentration
                  TranslationKey.AttrVision, p.Mental.Vision
                  TranslationKey.AttrPositioning, p.Mental.Positioning
                  TranslationKey.AttrBravery, p.Mental.Bravery
                  TranslationKey.AttrWorkRate, p.Mental.WorkRate
                  TranslationKey.AttrLeadership, p.Mental.Leadership ] }

        let technical =
            { Title = tr TranslationKey.AttrGroupTechnical
              Color = Theme.Hot
              Stats =
                [ TranslationKey.AttrFinishing, p.Technical.Finishing
                  TranslationKey.AttrLongShots, p.Technical.LongShots
                  TranslationKey.AttrDribbling, p.Technical.Dribbling
                  TranslationKey.AttrBallControl, p.Technical.BallControl
                  TranslationKey.AttrPassing, p.Technical.Passing
                  TranslationKey.AttrCrossing, p.Technical.Crossing
                  TranslationKey.AttrTackling, p.Technical.Tackling
                  TranslationKey.AttrMarking, p.Technical.Marking
                  TranslationKey.AttrHeading, p.Technical.Heading
                  TranslationKey.AttrFreeKick, p.Technical.FreeKick
                  TranslationKey.AttrPenalty, p.Technical.Penalty ] }

        let goalkeeping =
            { Title = tr TranslationKey.AttrGroupGoalkeeping
              Color = Theme.Warning
              Stats =
                [ TranslationKey.AttrReflexes, p.Goalkeeping.Reflexes
                  TranslationKey.AttrHandling, p.Goalkeeping.Handling
                  TranslationKey.AttrKicking, p.Goalkeeping.Kicking
                  TranslationKey.AttrOneOnOne, p.Goalkeeping.OneOnOne
                  TranslationKey.AttrAerialReach, p.Goalkeeping.AerialReach ] }

        if p.Position = GK then
            [ goalkeeping; physical; mental ]
        else
            [ technical; physical; mental ]

    let private statRow (label: TranslationKey) (value: int) (color: string) : IView =
        Grid.create
            [ Grid.columnDefinitions "120, 12, *, 12, 28"
              Grid.margin (0.0, 4.0)
              Grid.children
                  [ TextBlock.create
                        [ Grid.column 0
                          TextBlock.text (tr label)
                          TextBlock.fontSize 14.0
                          TextBlock.foreground Theme.TextSub
                          TextBlock.verticalAlignment VerticalAlignment.Center ]
                    Border.create
                        [ Grid.column 2
                          Border.verticalAlignment VerticalAlignment.Center
                          Border.child (bar (float value) 20.0 (if value >= 16 then Theme.Accent else color) 8.0) ]
                    Border.create
                        [ Grid.column 4
                          Border.horizontalAlignment HorizontalAlignment.Right
                          Border.child (UI.display (string value) 20.0 Theme.TextMain) ] ] ]
        :> IView

    let private figure (label: TranslationKey) (value: string) (color: string) : IView =
        Border.create
            [ Border.background Theme.BgElevated
              Border.cornerRadius Theme.RadiusMd
              Border.padding (12.0, 10.0)
              Border.margin (0.0, 0.0, 8.0, 8.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 2.0
                        StackPanel.children
                            [ TextBlock.create
                                  [ TextBlock.text (tr label)
                                    TextBlock.fontSize 12.0
                                    TextBlock.foreground Theme.TextMuted ]
                              UI.display value 28.0 color ] ]
              ) ]
        :> IView

    let detail (player: Player option) (currentDate: DateTime) (clubKit: ClubIdentity.Kit) =
        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius (Theme.RadiusXl + 2.0)
              Border.clipToBounds true
              Border.child (
                  match player with
                  | None -> UI.emptyState IconName.squad (tr TranslationKey.SelectPlayer) (tr TranslationKey.SelectPlayerHint)
                  | Some p ->
                      let kit = if p.Position = GK then ClubIdentity.keeperKit else clubKit

                      let tint = Theme.positionColor p.Position

                      Grid.create
                          [ Grid.rowDefinitions "Auto, *"
                            Grid.children
                                [ Border.create
                                      [ Grid.row 0
                                        Border.background (
                                            Theme.spotlightBrush (Theme.withAlpha "66" tint) (Theme.withAlpha "22" tint) Theme.BgCard
                                        )
                                        Border.padding (24.0, 24.0, 24.0, 18.0)
                                        Border.child (
                                            StackPanel.create
                                                [ StackPanel.spacing 16.0
                                                  StackPanel.children
                                                      [ StackPanel.create
                                                            [ StackPanel.orientation Orientation.Horizontal
                                                              StackPanel.spacing 18.0
                                                              StackPanel.children
                                                                  [ UI.shirt kit "" 84.0 "#0A1020" 1.5
                                                                    StackPanel.create
                                                                        [ StackPanel.spacing 6.0
                                                                          StackPanel.verticalAlignment VerticalAlignment.Center
                                                                          StackPanel.children
                                                                              [ UI.display p.Name 30.0 Theme.TextMain
                                                                                StackPanel.create
                                                                                    [ StackPanel.orientation Orientation.Horizontal
                                                                                      StackPanel.spacing 8.0
                                                                                      StackPanel.children
                                                                                          [ UI.positionBadge p.Position
                                                                                            TextBlock.create
                                                                                                [ TextBlock.text (
                                                                                                      Localization.resolve
                                                                                                          TranslationKey.PlayerAgeMorale
                                                                                                          [ "age", string (Player.age currentDate p)
                                                                                                            "morale", string p.Morale ]
                                                                                                  )
                                                                                                  TextBlock.fontSize 14.0
                                                                                                  TextBlock.foreground Theme.TextSub
                                                                                                  TextBlock.verticalAlignment
                                                                                                      VerticalAlignment.Center ] ] ] ] ] ] ]
                                                        WrapPanel.create
                                                            [ WrapPanel.children
                                                                  [ figure TranslationKey.PlayerLevel (string p.CurrentSkill) Theme.TextMain
                                                                    figure TranslationKey.PlayerPotential (string p.PotentialSkill) Theme.Accent
                                                                    figure TranslationKey.PlayerCondition $"{p.Condition}%%" (conditionColor p.Condition)
                                                                    figure TranslationKey.PlayerMatchFitness $"{p.MatchFitness}%%" Theme.TextMain
                                                                    figure
                                                                        TranslationKey.PlayerContract
                                                                        (Player.contractOf p
                                                                         |> Option.map (_.ExpiryYear >> string)
                                                                         |> Option.defaultValue "—")
                                                                        Theme.TextMain ] ] ] ]
                                        ) ]
                                  ScrollViewer.create
                                      [ Grid.row 1
                                        ScrollViewer.padding (24.0, 8.0, 24.0, 24.0)
                                        ScrollViewer.content (
                                            StackPanel.create
                                                [ StackPanel.spacing 4.0
                                                  StackPanel.children
                                                      [ for group in statGroupsFor p do
                                                            Border.create
                                                                [ Border.margin (0.0, 14.0, 0.0, 4.0)
                                                                  Border.child (UI.eyebrow group.Title group.Color) ]
                                                            :> IView

                                                            for label, value in group.Stats do
                                                                statRow label value group.Color ] ]
                                        ) ] ] ]
                      :> IView
              ) ]

module Display =

    module Matches =

        let private teamBlock (name: string) initial align =
            StackPanel.create
                [ StackPanel.orientation Orientation.Horizontal
                  StackPanel.spacing 14.0
                  StackPanel.horizontalAlignment align
                  StackPanel.verticalAlignment VerticalAlignment.Center
                  StackPanel.children
                      [ if align = HorizontalAlignment.Right then
                            TextBlock.create
                                [ TextBlock.text (name.ToUpper())
                                  TextBlock.fontSize 20.0
                                  TextBlock.fontWeight FontWeight.Black
                                  TextBlock.foreground Theme.TextMain
                                  TextBlock.verticalAlignment VerticalAlignment.Center ]

                        Border.create
                            [ Border.width 56.0
                              Border.height 56.0
                              Border.cornerRadius 28.0
                              Border.background (
                                  if align = HorizontalAlignment.Right then
                                      Theme.TextMain
                                  else
                                      Theme.BgElevated
                              )
                              Border.borderBrush (
                                  if align = HorizontalAlignment.Right then
                                      Theme.TextMain
                                  else
                                      Theme.BorderLight
                              )
                              Border.borderThickness 1.0
                              Border.child (
                                  TextBlock.create
                                      [ TextBlock.text initial
                                        TextBlock.fontSize 24.0
                                        TextBlock.fontWeight FontWeight.Black
                                        TextBlock.foreground (
                                            if align = HorizontalAlignment.Right then
                                                "#0f172a"
                                            else
                                                Theme.TextSub
                                        )
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                                        TextBlock.verticalAlignment VerticalAlignment.Center ]
                              ) ]

                        if align = HorizontalAlignment.Left then
                            TextBlock.create
                                [ TextBlock.text (name.ToUpper())
                                  TextBlock.fontSize 20.0
                                  TextBlock.fontWeight FontWeight.Black
                                  TextBlock.foreground Theme.TextSub
                                  TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]

        let nextMatchBanner (homeName: string) (awayName: string) (date: DateTime) (location: string) =
            Border.create
                [ Border.cornerRadius Theme.RadiusXl
                  Border.margin (0.0, 0.0, 0.0, 20.0)
                  Border.background Theme.BgCard
                  Border.borderBrush Theme.Border
                  Border.borderThickness 1.0
                  Border.clipToBounds true
                  Border.child (
                      Grid.create
                          [ Grid.columnDefinitions "*, Auto, *"
                            Grid.children
                                [ Border.create
                                      [ Grid.columnSpan 3
                                        Border.height 3.0
                                        Border.background Theme.Accent
                                        Border.verticalAlignment VerticalAlignment.Top ]

                                  StackPanel.create
                                      [ Grid.column 0
                                        StackPanel.verticalAlignment VerticalAlignment.Center
                                        StackPanel.margin (28.0, 20.0, 0.0, 20.0)
                                        StackPanel.children
                                            [ teamBlock homeName (homeName.Substring(0, 1)) HorizontalAlignment.Right ] ]

                                  Border.create
                                      [ Grid.column 1
                                        Border.background Theme.BgElevated
                                        Border.cornerRadius Theme.RadiusLg
                                        Border.borderBrush Theme.BorderLight
                                        Border.borderThickness 1.0
                                        Border.padding (24.0, 14.0)
                                        Border.margin (28.0, 0.0)
                                        Border.verticalAlignment VerticalAlignment.Center
                                        Border.child (
                                            StackPanel.create
                                                [ StackPanel.spacing 2.0
                                                  StackPanel.horizontalAlignment HorizontalAlignment.Center
                                                  StackPanel.children
                                                      [ Border.create
                                                            [ Border.background (Theme.withAlpha "18" Theme.Accent)
                                                              Border.cornerRadius Theme.RadiusPill
                                                              Border.padding (8.0, 3.0)
                                                              Border.horizontalAlignment HorizontalAlignment.Center
                                                              Border.child (
                                                                  TextBlock.create
                                                                      [ TextBlock.text $"NEXT MATCH  •  {location}"
                                                                        TextBlock.foreground Theme.Accent
                                                                        TextBlock.fontSize
                                                                            Theme.currentTokens.Value.Typography.Detail
                                                                        TextBlock.fontWeight FontWeight.Bold
                                                                        TextBlock.horizontalAlignment
                                                                            HorizontalAlignment.Center ]
                                                              ) ]
                                                        TextBlock.create
                                                            [ TextBlock.text "VS"
                                                              TextBlock.fontSize
                                                                  Theme.currentTokens.Value.Typography.Detail
                                                              TextBlock.fontWeight FontWeight.Black
                                                              TextBlock.foreground Theme.TextDim
                                                              TextBlock.horizontalAlignment HorizontalAlignment.Center
                                                              TextBlock.margin (0.0, 6.0, 0.0, 2.0) ]
                                                        TextBlock.create
                                                            [ TextBlock.text (date.ToString("dddd").ToUpper())
                                                              TextBlock.fontSize 28.0
                                                              TextBlock.fontWeight FontWeight.Black
                                                              TextBlock.foreground Theme.TextMain
                                                              TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                                                        TextBlock.create
                                                            [ TextBlock.text (date.ToString("dd MMMM yyyy").ToUpper())
                                                              TextBlock.foreground Theme.TextMuted
                                                              TextBlock.horizontalAlignment HorizontalAlignment.Center
                                                              TextBlock.fontSize
                                                                  Theme.currentTokens.Value.Typography.Detail ] ] ]
                                        ) ]

                                  StackPanel.create
                                      [ Grid.column 2
                                        StackPanel.verticalAlignment VerticalAlignment.Center
                                        StackPanel.margin (0.0, 20.0, 28.0, 20.0)
                                        StackPanel.children
                                            [ teamBlock awayName (awayName.Substring(0, 1)) HorizontalAlignment.Left ] ] ] ]
                  ) ]

        let nextMatchBannerCompact (homeName: string) (awayName: string) (date: DateTime) (location: string) =
            Border.create
                [ Border.cornerRadius Theme.RadiusXl
                  Border.margin (0.0, 0.0, 0.0, 20.0)
                  Border.background Theme.BgCard
                  Border.borderBrush Theme.Border
                  Border.borderThickness 1.0
                  Border.clipToBounds true
                  Border.child (
                      StackPanel.create
                          [ StackPanel.spacing 6.0
                            StackPanel.horizontalAlignment HorizontalAlignment.Center
                            StackPanel.margin (16.0, 18.0)
                            StackPanel.children
                                [ Border.create
                                      [ Border.background (Theme.withAlpha "18" Theme.Accent)
                                        Border.cornerRadius Theme.RadiusPill
                                        Border.padding (8.0, 3.0)
                                        Border.horizontalAlignment HorizontalAlignment.Center
                                        Border.child (
                                            TextBlock.create
                                                [ TextBlock.text $"NEXT MATCH  |  {location}"
                                                  TextBlock.foreground Theme.Accent
                                                  TextBlock.fontSize Theme.currentTokens.Value.Typography.Detail
                                                  TextBlock.fontWeight FontWeight.Bold ]
                                        ) ]
                                  TextBlock.create
                                      [ TextBlock.text $"{homeName.ToUpper()}  VS  {awayName.ToUpper()}"
                                        TextBlock.fontSize 18.0
                                        TextBlock.fontWeight FontWeight.Black
                                        TextBlock.foreground Theme.TextMain
                                        TextBlock.textAlignment TextAlignment.Center
                                        TextBlock.textWrapping TextWrapping.Wrap ]
                                  TextBlock.create
                                      [ TextBlock.text (date.ToString("dddd, dd MMMM yyyy").ToUpper())
                                        TextBlock.fontSize Theme.currentTokens.Value.Typography.Detail
                                        TextBlock.foreground Theme.TextMuted
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
                  ) ]

        let emptyBanner () =
            Border.create
                [ Border.cornerRadius Theme.RadiusLg
                  Border.margin (0.0, 0.0, 0.0, 20.0)
                  Border.padding 32.0
                  Border.background Theme.BgCard
                  Border.borderBrush Theme.Border
                  Border.borderThickness 1.0
                  Border.child (
                      StackPanel.create
                          [ StackPanel.horizontalAlignment HorizontalAlignment.Center
                            StackPanel.spacing 8.0
                            StackPanel.children
                                [ Border.create
                                      [ Border.background (Theme.withAlpha "14" Theme.TextMuted)
                                        Border.cornerRadius 999.0
                                        Border.padding 12.0
                                        Border.horizontalAlignment HorizontalAlignment.Center
                                        Border.child (Icons.iconSm IconName.calendar Theme.TextDim) ]
                                  TextBlock.create
                                      [ TextBlock.text "No upcoming matches"
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                                        TextBlock.foreground Theme.TextDim
                                        TextBlock.fontSize 13.0 ] ] ]
                  ) ]

    module Tables =

        type RowViewModel =
            { Pos: int
              TeamName: string
              Points: int
              Stats: string
              IsUser: bool
              PosColor: string }

        let leagueTable
            (leagues: (int * string) list)
            (selectedId: int)
            (onSelect: int -> unit)
            (rows: RowViewModel list)
            =
            StackPanel.create
                [ StackPanel.children
                      [ Border.create
                            [ Border.padding 15.0
                              Border.borderBrush Theme.Border
                              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                              Border.child (
                                  DockPanel.create
                                      [ DockPanel.children
                                            [ StackPanel.create
                                                  [ StackPanel.dock Dock.Right
                                                    StackPanel.orientation Orientation.Horizontal
                                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                                    StackPanel.children
                                                        [ TextBlock.create
                                                              [ TextBlock.text "W-D-L"
                                                                TextBlock.width 60.0
                                                                TextBlock.textAlignment TextAlignment.Right
                                                                TextBlock.fontSize
                                                                    Theme.currentTokens.Value.Typography.Detail
                                                                TextBlock.foreground Theme.TextMuted ]
                                                          TextBlock.create
                                                              [ TextBlock.text "PTS"
                                                                TextBlock.width 40.0
                                                                TextBlock.textAlignment TextAlignment.Right
                                                                TextBlock.fontSize
                                                                    Theme.currentTokens.Value.Typography.Detail
                                                                TextBlock.foreground Theme.TextMuted
                                                                TextBlock.fontWeight FontWeight.Bold ] ] ]
                                              StackPanel.create
                                                  [ StackPanel.orientation Orientation.Horizontal
                                                    StackPanel.children
                                                        [ for id, name in leagues do
                                                              UI.tabButton name (id = selectedId) (fun _ -> onSelect id) ] ] ] ]
                              ) ]

                        ScrollViewer.create
                            [ ScrollViewer.maxHeight 450.0
                              ScrollViewer.content (
                                  StackPanel.create
                                      [ StackPanel.children
                                            [ for row in rows do
                                                  Border.create
                                                      [ Border.background (
                                                            (if row.IsUser then
                                                                 Theme.withAlpha "30" Theme.AccentAlt
                                                             else
                                                                 "transparent")
                                                        )
                                                        Border.padding (15.0, 8.0)
                                                        Border.borderBrush (
                                                            if row.IsUser then "transparent" else Theme.BgCard
                                                        )
                                                        Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                                        Border.child (
                                                            DockPanel.create
                                                                [ DockPanel.children
                                                                      [ TextBlock.create
                                                                            [ TextBlock.dock Dock.Right
                                                                              TextBlock.width 40.0
                                                                              TextBlock.textAlignment
                                                                                  TextAlignment.Right
                                                                              TextBlock.text (string row.Points)
                                                                              TextBlock.fontWeight FontWeight.Bold
                                                                              TextBlock.foreground (
                                                                                  if row.IsUser then
                                                                                      "White"
                                                                                  else
                                                                                      Theme.TextMain
                                                                              ) ]
                                                                        TextBlock.create
                                                                            [ TextBlock.dock Dock.Right
                                                                              TextBlock.width 60.0
                                                                              TextBlock.textAlignment
                                                                                  TextAlignment.Right
                                                                              TextBlock.text row.Stats
                                                                              TextBlock.fontSize
                                                                                  Theme.currentTokens.Value.Typography.Detail
                                                                              TextBlock.foreground (
                                                                                  if row.IsUser then
                                                                                      Theme.TextSub
                                                                                  else
                                                                                      Theme.TextMuted
                                                                              ) ]
                                                                        TextBlock.create
                                                                            [ TextBlock.text (string row.Pos)
                                                                              TextBlock.width 30.0
                                                                              TextBlock.foreground row.PosColor
                                                                              TextBlock.fontWeight FontWeight.Bold ]
                                                                        TextBlock.create
                                                                            [ TextBlock.text row.TeamName
                                                                              TextBlock.fontWeight (
                                                                                  if row.IsUser then
                                                                                      FontWeight.Bold
                                                                                  else
                                                                                      FontWeight.Normal
                                                                              )
                                                                              TextBlock.foreground (
                                                                                  if row.IsUser then
                                                                                      "White"
                                                                                  else
                                                                                      Theme.TextMain
                                                                              ) ] ] ]
                                                        ) ] ] ]
                              ) ] ] ]
