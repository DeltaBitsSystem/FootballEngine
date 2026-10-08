namespace FootballEngine.Components

open System
open Avalonia.Automation
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.FuncUI.Builder
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open Avalonia.FuncUI.DSL
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Icons


module UI =

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

    let private colorWithState (variant: ButtonVariant) (state: ControlState) =
        let colors = Theme.currentTokens.Value.Colors

        let baseStyle =
            match variant with
            | Primary -> colors.Accent, colors.OnAccent, colors.Accent
            | Secondary -> colors.Surface2, colors.TextPrimary, colors.BorderSubtle
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
        :> IView

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
                          TextBox.watermark placeholder
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
                                        [ TextBlock.create
                                              [ TextBlock.text title
                                                TextBlock.fontSize tokens.Typography.Page
                                                TextBlock.fontWeight FontWeight.SemiBold
                                                TextBlock.foreground tokens.Colors.TextPrimary ]
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
                                        [ TextBlock.create
                                              [ TextBlock.text title
                                                TextBlock.fontSize tokens.Typography.Section
                                                TextBlock.fontWeight FontWeight.SemiBold
                                                TextBlock.foreground tokens.Colors.TextPrimary ]
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
                  [ TextBlock.create
                        [ TextBlock.text valueText
                          TextBlock.fontSize tokens.Typography.Emphasis
                          TextBlock.fontWeight FontWeight.SemiBold
                          TextBlock.foreground tokens.Colors.TextPrimary ]
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
              Border.cornerRadius 8.0
              Border.padding 15.0
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
              Border.cornerRadius 4.0
              Border.padding (6.0, 2.0)
              Border.child (
                  TextBlock.create
                      [ TextBlock.text text
                        TextBlock.fontSize (Theme.currentTokens.Value.Typography.Detail)
                        TextBlock.fontWeight FontWeight.Bold
                        TextBlock.foreground "#ffffff" ]
              ) ]

    let countBadge (count: int) =
        Border.create
            [ Border.background Theme.Accent
              Border.cornerRadius Theme.RadiusPill
              Border.padding (8.0, 2.0)
              Border.child (
                  TextBlock.create
                      [ TextBlock.text (string count)
                        TextBlock.fontSize (Theme.currentTokens.Value.Typography.Detail)
                        TextBlock.fontWeight FontWeight.Black
                        TextBlock.foreground Theme.BgSidebar ]
              ) ]

    let positionBadge (pos: Position) =
        let color = Theme.positionColor pos

        Border.create
            [ Border.background (Theme.withAlpha "1a" color)
              Border.borderBrush (Theme.withAlpha "55" color)
              Border.borderThickness 1.0
              Border.cornerRadius 5.0
              Border.padding (7.0, 4.0)
              Border.child (
                  TextBlock.create
                      [ TextBlock.text $"%A{pos}"
                        TextBlock.fontSize (Theme.currentTokens.Value.Typography.Detail)
                        TextBlock.fontWeight FontWeight.Black
                        TextBlock.foreground color ]
              ) ]

    let statCard (iconKind: Material.Icons.MaterialIconKind) (label: string) (value: string) (sub: string) =
        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusLg
              Border.padding (16.0, 14.0)
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
                                    TextBlock.text label
                                    TextBlock.fontSize (Theme.currentTokens.Value.Typography.Detail)
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground Theme.TextDim
                                    TextBlock.lineSpacing 0.8 ]
                              StackPanel.create
                                  [ Grid.column 1
                                    Grid.row 1
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 6.0
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text value
                                                TextBlock.fontSize 20.0
                                                TextBlock.fontWeight FontWeight.Black
                                                TextBlock.foreground Theme.TextMain ]
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
              Border.cornerRadius Theme.RadiusMd
              Border.padding (14.0, 12.0)
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
                              TextBlock.create
                                  [ TextBlock.text value
                                    TextBlock.foreground Theme.TextMain
                                    TextBlock.fontSize 22.0
                                    TextBlock.fontWeight FontWeight.Black ] ] ]
              ) ]

    let sectionTitle (label: string) (iconKind: Material.Icons.MaterialIconKind) =
        StackPanel.create
            [ StackPanel.orientation Orientation.Horizontal
              StackPanel.spacing 8.0
              StackPanel.margin (0.0, 0.0, 0.0, 10.0)
              StackPanel.children
                  [ Icons.iconSm iconKind Theme.Accent
                    TextBlock.create
                        [ TextBlock.text label
                          TextBlock.fontSize (Theme.currentTokens.Value.Typography.Detail)
                          TextBlock.fontWeight FontWeight.Black
                          TextBlock.foreground Theme.TextMuted
                          TextBlock.lineSpacing 1.2
                          TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]

    let sectionHeader (iconKind: Material.Icons.MaterialIconKind) (label: string) : IView =
        Border.create
            [ Border.background Theme.BgCard
              Border.padding (16.0, 11.0)
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 8.0
                        StackPanel.children
                            [ Icons.iconSm iconKind Theme.Accent
                              TextBlock.create
                                  [ TextBlock.text label
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.fontSize (Theme.currentTokens.Value.Typography.Detail)
                                    TextBlock.fontWeight FontWeight.Black
                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
              ) ]
        :> IView

    let sectionHeaderWithBadge (iconKind: Material.Icons.MaterialIconKind) (label: string) (count: int) : IView =
        Border.create
            [ Border.background Theme.BgCard
              Border.padding (16.0, 11.0)
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 8.0
                        StackPanel.children
                            [ Icons.iconSm iconKind Theme.Accent
                              TextBlock.create
                                  [ TextBlock.text label
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.fontSize (Theme.currentTokens.Value.Typography.Detail)
                                    TextBlock.fontWeight FontWeight.Black
                                    TextBlock.verticalAlignment VerticalAlignment.Center ]
                              countBadge count ] ]
              ) ]
        :> IView

    let panelCard (header: IView) (body: IView) : IView =
        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusLg
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
              Button.background (if isActive then Theme.Accent else "transparent")
              Button.foreground (if isActive then Theme.BgSidebar else Theme.TextMuted)
              Button.borderThickness (if isActive then 0.0 else 1.0)
              Button.borderBrush Theme.Border
              Button.padding (15.0, 5.0)
              Button.minHeight tokens.Controls.StandardHeight
              Button.fontSize tokens.Typography.Body
              Button.fontWeight FontWeight.Bold
              Button.cornerRadius 4.0
              Button.focusable true
              Button.isTabStop true
              automationName label
              Button.onClick onClick
              Button.margin (0.0, 0.0, 10.0, 0.0) ]

    let sectionContainer (title: string) (content: IView) =
        StackPanel.create
            [ StackPanel.spacing 10.0
              StackPanel.children
                  [ TextBlock.create
                        [ TextBlock.text title
                          TextBlock.fontWeight FontWeight.Bold
                          TextBlock.foreground Theme.TextMuted
                          TextBlock.fontSize 12.0 ]
                    Border.create
                        [ Border.background Theme.BgCard
                          Border.borderBrush Theme.Border
                          Border.borderThickness 1.0
                          Border.cornerRadius 8.0
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
                        Border.cornerRadius 12.0
                        Border.padding 30.0
                        Border.borderBrush Theme.Border
                        Border.borderThickness 1.0
                        Border.child (
                            StackPanel.create
                                [ StackPanel.spacing 10.0
                                  StackPanel.children
                                      [ Icons.iconXl icon Theme.TextMain
                                        TextBlock.create
                                            [ TextBlock.text title
                                              TextBlock.fontSize 22.0
                                              TextBlock.fontWeight FontWeight.Black
                                              TextBlock.foreground Theme.Accent
                                              TextBlock.horizontalAlignment HorizontalAlignment.Center ]
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
          Button.foreground (if isActive then Theme.BgSidebar else Theme.TextMain)
          Button.borderThickness 0.0
          Button.cornerRadius 8.0
          Button.margin (10.0, 4.0)
          Button.padding (15.0, 10.0)
          Button.horizontalAlignment HorizontalAlignment.Stretch ]


module PlayerView =

    let tableHeader () =
        let col index label =
            TextBlock.create
                [ Grid.column index
                  TextBlock.text label
                  TextBlock.foreground Theme.TextMuted
                  TextBlock.fontSize Theme.currentTokens.Value.Typography.Detail
                  TextBlock.fontWeight FontWeight.Bold ]

        Border.create
            [ Border.padding (15.0, 8.0)
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions (ColumnDefs.toGridString ColumnDefs.playerListCols)
                        Grid.children [ col 0 "PLAYER"; col 1 "POS"; col 2 "AGE"; col 3 "CA / PA"; col 4 "VALUE" ] ]
              ) ]

    let private statusIndicator (p: Player) =
        match p.Status with
        | Injured(severity, _) ->
            let color =
                match severity with
                | Minor
                | Moderate -> Theme.Warning
                | _ -> Theme.Danger

            UI.badge "INJ" color
        | Suspended days -> UI.badge $"SUS {days}" Theme.Warning
        | Available -> Border.create [ Border.width 0.0 ]

    let row (p: Player) (currentDate: DateTime) (isSelected: bool) (isDragged: bool) onSelect =
        let leftBorderColor =
            if isSelected then Theme.Accent
            elif isDragged then Theme.DragBorder
            else "transparent"

        let bg =
            if isSelected then SelectionStyle.rowBg true
            elif isDragged then Theme.DragOverlay
            else "transparent"

        Border.create
            [ Border.background bg
              Border.borderBrush leftBorderColor
              Border.borderThickness (3.0, 0.0, 0.0, 0.0)
              Border.padding (12.0, 10.0)
              Border.onTapped (fun _ -> onSelect ())
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions (ColumnDefs.toGridString ColumnDefs.playerListCols)
                        Grid.children
                            [ Grid.create
                                  [ Grid.column 0
                                    Grid.columnDefinitions "*, Auto"
                                    Grid.children
                                        [ StackPanel.create
                                              [ Grid.column 0
                                                StackPanel.verticalAlignment VerticalAlignment.Center
                                                StackPanel.spacing 2.0
                                                StackPanel.children
                                                    [ TextBlock.create
                                                          [ TextBlock.text p.Name
                                                            TextBlock.fontWeight FontWeight.SemiBold
                                                            TextBlock.fontSize 13.0 ]
                                                      StackPanel.create
                                                          [ StackPanel.orientation Orientation.Horizontal
                                                            StackPanel.spacing 6.0
                                                            StackPanel.children
                                                                [ TextBlock.create
                                                                      [ TextBlock.text $"Morale {p.Morale}%%"
                                                                        TextBlock.fontSize
                                                                            Theme.currentTokens.Value.Typography.Detail
                                                                        TextBlock.foreground (
                                                                            Theme.moraleColor p.Morale
                                                                        ) ]
                                                                  TextBlock.create
                                                                      [ TextBlock.text
                                                                            $"·  {(Player.contractOf p |> Option.map _.ExpiryYear |> Option.defaultValue 0)}"
                                                                        TextBlock.fontSize
                                                                            Theme.currentTokens.Value.Typography.Detail
                                                                        TextBlock.foreground Theme.TextMuted ] ] ] ] ]
                                          StackPanel.create
                                              [ Grid.column 1
                                                StackPanel.verticalAlignment VerticalAlignment.Center
                                                StackPanel.margin (8.0, 0.0)
                                                StackPanel.children [ statusIndicator p ] ] ] ]

                              TextBlock.create
                                  [ Grid.column 1
                                    TextBlock.text $"%A{p.Position}"
                                    TextBlock.fontSize Theme.currentTokens.Value.Typography.Detail
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground Theme.Accent
                                    TextBlock.verticalAlignment VerticalAlignment.Center ]

                              TextBlock.create
                                  [ Grid.column 2
                                    TextBlock.text (string (Player.age currentDate p))
                                    TextBlock.fontSize 12.0
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.verticalAlignment VerticalAlignment.Center ]

                              StackPanel.create
                                  [ Grid.column 3
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.spacing 3.0
                                    StackPanel.children
                                        [ DockPanel.create
                                              [ DockPanel.children
                                                    [ TextBlock.create
                                                          [ TextBlock.text (string p.CurrentSkill)
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.foreground Theme.Accent
                                                            TextBlock.fontSize 12.0 ]
                                                      TextBlock.create
                                                          [ DockPanel.dock Dock.Right
                                                            TextBlock.text (string p.PotentialSkill)
                                                            TextBlock.fontSize
                                                                Theme.currentTokens.Value.Typography.Detail
                                                            TextBlock.foreground Theme.TextMuted
                                                            TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
                                          ProgressBar.create
                                              [ ProgressBar.minimum 0.0
                                                ProgressBar.maximum 200.0
                                                ProgressBar.value (float p.CurrentSkill)
                                                ProgressBar.height 3.0
                                                ProgressBar.background Theme.BgSidebar
                                                ProgressBar.foreground Theme.Accent ] ] ]

                              TextBlock.create
                                  [ Grid.column 4
                                    TextBlock.text $"€{int (Player.playerValue p.CurrentSkill / 1_000_000m)}M"
                                    TextBlock.fontSize Theme.currentTokens.Value.Typography.Detail
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
              ) ]

    type private StatGroup =
        { Title: string
          Color: string
          Stats: (string * int) list }

    let private physicalGroup (p: Player) =
        { Title = "PHYSICAL"
          Color = Theme.Accent
          Stats =
            [ "Acceleration", p.Physical.Acceleration
              "Pace", p.Physical.Pace
              "Agility", p.Physical.Agility
              "Balance", p.Physical.Balance
              "Jumping Reach", p.Physical.JumpingReach
              "Stamina", p.Physical.Stamina
              "Strength", p.Physical.Strength ] }

    let private mentalGroup (p: Player) =
        { Title = "MENTAL"
          Color = Theme.Accent
          Stats =
            [ "Aggression", p.Mental.Aggression
              "Composure", p.Mental.Composure
              "Concentration", p.Mental.Concentration
              "Vision", p.Mental.Vision
              "Positioning", p.Mental.Positioning
              "Bravery", p.Mental.Bravery
              "Work Rate", p.Mental.WorkRate
              "Leadership", p.Mental.Leadership ] }

    let private technicalGroup (p: Player) =
        { Title = "TECHNICAL"
          Color = Theme.Accent
          Stats =
            [ "Finishing", p.Technical.Finishing
              "Long Shots", p.Technical.LongShots
              "Dribbling", p.Technical.Dribbling
              "Ball Control", p.Technical.BallControl
              "Passing", p.Technical.Passing
              "Crossing", p.Technical.Crossing
              "Tackling", p.Technical.Tackling
              "Marking", p.Technical.Marking
              "Heading", p.Technical.Heading
              "Free Kick", p.Technical.FreeKick
              "Penalty", p.Technical.Penalty ] }

    let private goalkeepingGroup (p: Player) =
        { Title = "GOALKEEPING"
          Color = Theme.DragBorder
          Stats =
            [ "Reflexes", p.Goalkeeping.Reflexes
              "Handling", p.Goalkeeping.Handling
              "Kicking", p.Goalkeeping.Kicking
              "One On One", p.Goalkeeping.OneOnOne
              "Aerial Reach", p.Goalkeeping.AerialReach ] }

    let private statGroupsFor (p: Player) =
        let base' = [ physicalGroup p; mentalGroup p; technicalGroup p ]

        if p.Position = GK then
            base' @ [ goalkeepingGroup p ]
        else
            base'

    let private statRow label value (color: string) =
        StackPanel.create
            [ StackPanel.margin (0.0, 2.0)
              StackPanel.children
                  [ DockPanel.create
                        [ DockPanel.children
                              [ TextBlock.create
                                    [ TextBlock.text label
                                      TextBlock.foreground Theme.TextMuted
                                      TextBlock.fontSize Theme.currentTokens.Value.Typography.Detail ]
                                TextBlock.create
                                    [ TextBlock.text (string value)
                                      TextBlock.fontWeight FontWeight.Bold
                                      TextBlock.fontSize Theme.currentTokens.Value.Typography.Detail
                                      DockPanel.dock Dock.Right ] ] ]
                    ProgressBar.create
                        [ ProgressBar.minimum 0.0
                          ProgressBar.maximum 20.0
                          ProgressBar.value (float value)
                          ProgressBar.height 3.0
                          ProgressBar.background Theme.BgMain
                          ProgressBar.foreground color ] ] ]

    let private renderGroup (g: StatGroup) : IView list =
        [ TextBlock.create
              [ TextBlock.text g.Title
                TextBlock.fontSize Theme.currentTokens.Value.Typography.Detail
                TextBlock.fontWeight FontWeight.Black
                TextBlock.foreground g.Color
                TextBlock.lineSpacing 1.5
                TextBlock.margin (0, 14, 0, 6) ]
          :> IView
          yield! g.Stats |> List.map (fun (label, value) -> statRow label value g.Color :> IView) ]

    let private miniBar label value (color: string) =
        Grid.create
            [ Grid.rowDefinitions "Auto, 4"
              Grid.children
                  [ DockPanel.create
                        [ Grid.row 0
                          DockPanel.children
                              [ TextBlock.create
                                    [ TextBlock.text label
                                      TextBlock.fontSize Theme.currentTokens.Value.Typography.Detail
                                      TextBlock.foreground Theme.TextMuted ]
                                TextBlock.create
                                    [ TextBlock.text (string value)
                                      TextBlock.fontSize Theme.currentTokens.Value.Typography.Detail
                                      TextBlock.fontWeight FontWeight.Bold
                                      DockPanel.dock Dock.Right ] ] ]
                    ProgressBar.create
                        [ Grid.row 1
                          ProgressBar.minimum 0.0
                          ProgressBar.maximum 100.0
                          ProgressBar.value (float value)
                          ProgressBar.height 4.0
                          ProgressBar.background Theme.BgMain
                          ProgressBar.foreground color ] ] ]

    let detail (player: Player option) (currentDate: DateTime) =
        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness (1.0, 0.0, 0.0, 0.0)
              Border.child (
                  match player with
                  | None -> UI.emptyState IconName.squad "Select a player" ""
                  | Some p ->
                      Grid.create
                          [ Grid.rowDefinitions "Auto, *"
                            Grid.children
                                [ Border.create
                                      [ Grid.row 0
                                        Border.padding (20.0, 20.0, 20.0, 16.0)
                                        Border.borderBrush Theme.Border
                                        Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                        Border.child (
                                            StackPanel.create
                                                [ StackPanel.spacing 12.0
                                                  StackPanel.children
                                                      [ DockPanel.create
                                                            [ DockPanel.children
                                                                  [ UI.badge $"%A{p.Position}" Theme.Accent
                                                                    TextBlock.create
                                                                        [ TextBlock.text p.Name
                                                                          TextBlock.fontSize 20.0
                                                                          TextBlock.fontWeight FontWeight.Bold
                                                                          TextBlock.verticalAlignment
                                                                              VerticalAlignment.Center
                                                                          TextBlock.margin (10.0, 0.0, 0.0, 0.0) ] ] ]

                                                        UniformGrid.create
                                                            [ UniformGrid.columns 2
                                                              UniformGrid.rows 2
                                                              UniformGrid.children
                                                                  [ UI.statMiniCard
                                                                        PlayerIcon.age
                                                                        "AGE"
                                                                        (string (Player.age currentDate p))
                                                                        Theme.Accent
                                                                    UI.statMiniCard
                                                                        PlayerIcon.condition
                                                                        "CONDITION"
                                                                        $"{p.Condition}%%"
                                                                        Theme.Accent
                                                                    UI.statMiniCard
                                                                        PlayerIcon.skill
                                                                        "CA / PA"
                                                                        $"{p.CurrentSkill} / {p.PotentialSkill}"
                                                                        Theme.Accent
                                                                    UI.statMiniCard
                                                                        PlayerIcon.contract
                                                                        "CONTRACT"
                                                                        (Player.contractOf p
                                                                         |> Option.map (_.ExpiryYear >> string)
                                                                         |> Option.defaultValue "—")
                                                                        Theme.Accent ] ]

                                                        Grid.create
                                                            [ Grid.columnDefinitions "*, 10, *"
                                                              Grid.children
                                                                  [ miniBar
                                                                        "MORALE"
                                                                        p.Morale
                                                                        (Theme.moraleColor p.Morale)
                                                                    Grid.create
                                                                        [ Grid.column 2
                                                                          Grid.children
                                                                              [ miniBar
                                                                                    "MATCH FIT"
                                                                                    p.MatchFitness
                                                                                    Theme.Accent ] ] ] ] ] ]
                                        ) ]

                                  ScrollViewer.create
                                      [ Grid.row 1
                                        ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Visible
                                        ScrollViewer.padding (20.0, 4.0, 20.0, 20.0)
                                        ScrollViewer.content (
                                            StackPanel.create
                                                [ StackPanel.children
                                                      [ yield! statGroupsFor p |> List.collect renderGroup
                                                        Border.create [ Border.height 40.0 ] ] ]
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
