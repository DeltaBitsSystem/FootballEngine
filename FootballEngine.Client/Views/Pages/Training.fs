namespace FootballEngine.Pages

open System
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open FootballEngine
open FootballEngine.Domain
open FootballEngine.AppMsgs
open FootballEngine.AppTypes
open FootballEngine.Icons
open FootballEngine.Components
open FootballEngine.World.Phases

module TrainingPresenter =

    type FocusOption =
        { Label: string
          Focus: TrainingFocus
          Description: string
          Icon: Material.Icons.MaterialIconKind
          Color: string }

    type IntensityOption =
        { Label: string
          Intensity: TrainingIntensity
          Description: string
          Color: string
          Badge: string
          ConditionCost: int
          GrowthMult: float
          InjuryRisk: float }

    let focusOptions: FocusOption list =
        [ { Label = "Physical"
            Focus = TrainingFocus.TrainingPhysical
            Description = "Pace, strength & stamina"
            Icon = Material.Icons.MaterialIconKind.RunFast
            Color = Theme.Accent }
          { Label = "Technical"
            Focus = TrainingFocus.TrainingTechnical
            Description = "Ball skills & execution"
            Icon = Material.Icons.MaterialIconKind.Soccer
            Color = Theme.Accent }
          { Label = "Mental"
            Focus = TrainingFocus.TrainingMental
            Description = "Decisions & awareness"
            Icon = Material.Icons.MaterialIconKind.Brain
            Color = Theme.AccentAlt }
          { Label = "Goalkeeping"
            Focus = TrainingFocus.TrainingGoalkeeping
            Description = "Specialized keeper work"
            Icon = Material.Icons.MaterialIconKind.Glove
            Color = Theme.Warning }
          { Label = "All Round"
            Focus = TrainingFocus.TrainingAllRound
            Description = "Balanced development"
            Icon = Material.Icons.MaterialIconKind.ScaleBalance
            Color = Theme.Danger } ]

    let intensityOptions: IntensityOption list =
        [ TrainingIntensity.TrainingLight
          TrainingIntensity.TrainingNormal
          TrainingIntensity.TrainingHeavy ]
        |> List.map (fun intensity ->
            let data = TrainingIntensityData.get intensity

            { Label =
                match intensity with
                | TrainingIntensity.TrainingLight -> "Light"
                | TrainingIntensity.TrainingNormal -> "Normal"
                | TrainingIntensity.TrainingHeavy -> "Heavy"
              Intensity = intensity
              Description =
                match intensity with
                | TrainingIntensity.TrainingLight -> "Minimal fatigue"
                | TrainingIntensity.TrainingNormal -> "Standard pace"
                | TrainingIntensity.TrainingHeavy -> "Max growth, higher risk"
              Color =
                match intensity with
                | TrainingIntensity.TrainingLight -> Theme.Success
                | TrainingIntensity.TrainingNormal -> Theme.Warning
                | TrainingIntensity.TrainingHeavy -> Theme.Danger
              Badge =
                match intensity with
                | TrainingIntensity.TrainingLight -> "SAFE"
                | TrainingIntensity.TrainingNormal -> "BALANCED"
                | TrainingIntensity.TrainingHeavy -> "INTENSE"
              ConditionCost = data.ConditionCost
              GrowthMult = data.DeltaMultiplier
              InjuryRisk = data.InjuryRisk })

    let formatMult (mult: float) : string = TrainingIntensityData.formatMult mult

    let private defaultProfileFor (pos: Position) : BehavioralProfile =
        let baseLT =
            match pos with
            | DL | ML | AML -> -0.5
            | DR | MR | AMR -> 0.5
            | WBL -> -0.6
            | WBR -> 0.6
            | _ -> 0.0

        let baseAD =
            match pos with
            | ST | AMR | AML | AMC -> 0.7
            | MR | ML -> 0.5
            | MC -> 0.4
            | DM | DC | DR | DL -> 0.3
            | WBR | WBL -> 0.5
            | GK -> 0.1

        let baseDH =
            match pos with
            | DC | DM -> 0.7
            | DL | DR | WBL | WBR -> 0.5
            | MC -> 0.4
            | _ -> 0.2

        let baseCW =
            match pos with
            | MC | AMC -> 0.7
            | AMR | AML -> 0.6
            | DM -> 0.4
            | _ -> 0.2

        { PositionalFreedom = 0.5
          AttackingDepth = baseAD
          LateralTendency = baseLT
          DefensiveHeight = baseDH
          PressingIntensity = 0.5
          RiskAppetite = 0.4
          Directness = 0.5
          CreativityWeight = baseCW
          AerialThreat = 0.4
          HoldUpPlay = 0.4 }

    let getPositionImpact (focus: TrainingFocus) (intensity: TrainingIntensity) (pos: Position) : float =
        let intensityMult = (TrainingIntensityData.get intensity).DeltaMultiplier
        let profile = defaultProfileFor pos

        TrainingEngine.focusMultiplier focus profile * intensityMult


module Training =

    let private trainingBadges (player: Player) =
        let focusOpt =
            TrainingPresenter.focusOptions
            |> List.tryFind (fun o -> o.Focus = player.TrainingSchedule.Focus)

        let intensityOpt =
            TrainingPresenter.intensityOptions
            |> List.tryFind (fun o -> o.Intensity = player.TrainingSchedule.Intensity)

        let focusLabel = focusOpt |> Option.map _.Label |> Option.defaultValue "—"

        let focusColor =
            focusOpt |> Option.map _.Color |> Option.defaultValue Theme.TextMuted

        let intensityLabel = intensityOpt |> Option.map _.Label |> Option.defaultValue "—"

        let intensityColor =
            intensityOpt |> Option.map _.Color |> Option.defaultValue Theme.TextMuted

        StackPanel.create
            [ StackPanel.orientation Orientation.Horizontal
              StackPanel.spacing 4.0
              StackPanel.children
                  [ Border.create
                        [ Border.background (focusColor + "18")
                          Border.borderBrush (focusColor + "35")
                          Border.borderThickness 1.0
                          Border.cornerRadius 4.0
                          Border.padding (6.0, 3.0)
                          Border.child (
                              TextBlock.create
                                  [ TextBlock.text focusLabel
                                    TextBlock.fontSize 9.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground focusColor ]
                          ) ]
                    Border.create
                        [ Border.background (intensityColor + "18")
                          Border.borderBrush (intensityColor + "35")
                          Border.borderThickness 1.0
                          Border.cornerRadius 4.0
                          Border.padding (6.0, 3.0)
                          Border.child (
                              TextBlock.create
                                  [ TextBlock.text intensityLabel
                                    TextBlock.fontSize 9.0
                                    TextBlock.fontWeight FontWeight.SemiBold
                                    TextBlock.foreground intensityColor ]
                          ) ] ] ]

    let private playerRow (player: Player) (isSelected: bool) (onClick: unit -> unit) : IView =
        let fitCol = PlayerPresenter.fitnessColor player.MatchFitness
        let bgColor = SelectionStyle.rowBg isSelected
        let leftBorderColor = SelectionStyle.rowBorder isSelected

        Border.create
            [ Border.background bgColor
              Border.borderBrush leftBorderColor
              Border.borderThickness (2.0, 0.0, 0.0, 0.0)
              Border.padding (14.0, 10.0)
              Border.cursor Avalonia.Input.Cursor.Default
              Border.onPointerReleased (fun _ -> onClick ())
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions (ColumnDefs.toGridString ColumnDefs.playerListCols)
                        Grid.children
                            [ UI.positionBadge player.Position
                              |> fun b ->
                                  Border.create
                                      [ Grid.column 1
                                        Border.margin (0.0, 0.0, 10.0, 0.0)
                                        Border.verticalAlignment VerticalAlignment.Center
                                        Border.child b ]
                              StackPanel.create
                                  [ Grid.column 2
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.spacing 2.0
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text player.Name
                                                TextBlock.foreground Theme.TextMain
                                                TextBlock.fontSize 12.0
                                                TextBlock.fontWeight FontWeight.SemiBold ]
                                          trainingBadges player ] ]
                              StackPanel.create
                                  [ Grid.column 3
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.horizontalAlignment HorizontalAlignment.Right
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 4.0
                                    StackPanel.children
                                        [ Icons.iconSm PlayerIcon.condition fitCol
                                          TextBlock.create
                                              [ TextBlock.text $"{player.MatchFitness}%%"
                                                TextBlock.foreground fitCol
                                                TextBlock.fontSize 11.0
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.verticalAlignment VerticalAlignment.Center ] ] ] ] ]
              ) ]
        |> View.withKey $"tr-{player.Id}"
        :> IView

    let private focusCard (opt: TrainingPresenter.FocusOption) (isSelected: bool) (onSelect: TrainingFocus -> unit) =
        let bgColor = if isSelected then opt.Color + "15" else Theme.BgSidebar
        let borderColor = if isSelected then opt.Color else Theme.Border

        Border.create
            [ Border.background bgColor
              Border.borderBrush borderColor
              Border.borderThickness (if isSelected then 1.5 else 1.0)
              Border.cornerRadius 8.0
              Border.padding (12.0, 10.0)
              Border.cursor Avalonia.Input.Cursor.Default
              Border.onPointerReleased (fun _ -> onSelect opt.Focus)
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 10.0
                        StackPanel.children
                            [ Border.create
                                  [ Border.background (opt.Color + "18")
                                    Border.cornerRadius 6.0
                                    Border.padding 6.0
                                    Border.child (
                                        Icons.iconSm opt.Icon (if isSelected then opt.Color else Theme.TextMuted)
                                    ) ]
                              StackPanel.create
                                  [ StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text opt.Label
                                                TextBlock.fontSize 12.0
                                                TextBlock.fontWeight FontWeight.SemiBold
                                                TextBlock.foreground (if isSelected then opt.Color else Theme.TextMain) ]
                                          TextBlock.create
                                              [ TextBlock.text opt.Description
                                                TextBlock.fontSize 10.0
                                                TextBlock.foreground Theme.TextMuted ] ] ] ] ]
              ) ]
        |> View.withKey $"focus-{opt.Focus}"
        :> IView

    let private intensityCard
        (opt: TrainingPresenter.IntensityOption)
        (isSelected: bool)
        (onSelect: TrainingIntensity -> unit)
        =
        let conditionColor =
            if opt.ConditionCost >= -5 then Theme.Success
            elif opt.ConditionCost >= -10 then Theme.Warning
            else Theme.Danger

        Border.create
            [ Border.background (if isSelected then opt.Color + "12" else Theme.BgSidebar)
              Border.borderBrush (if isSelected then opt.Color else Theme.Border)
              Border.borderThickness (if isSelected then 1.5 else 1.0)
              Border.cornerRadius 8.0
              Border.padding (12.0, 10.0)
              Border.cursor Avalonia.Input.Cursor.Default
              Border.onPointerReleased (fun _ -> onSelect opt.Intensity)
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions "*, Auto, Auto, Auto"
                        Grid.children
                            [ StackPanel.create
                                  [ Grid.column 0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ StackPanel.create
                                              [ StackPanel.orientation Orientation.Horizontal
                                                StackPanel.spacing 6.0
                                                StackPanel.children
                                                    [ TextBlock.create
                                                          [ TextBlock.text opt.Label
                                                            TextBlock.fontSize 12.0
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.foreground (
                                                                if isSelected then opt.Color else Theme.TextMain
                                                            ) ]
                                                      Border.create
                                                          [ Border.background (opt.Color + "20")
                                                            Border.cornerRadius 4.0
                                                            Border.padding (5.0, 2.0)
                                                            Border.child (
                                                                TextBlock.create
                                                                    [ TextBlock.text opt.Badge
                                                                      TextBlock.fontSize 8.0
                                                                      TextBlock.fontWeight FontWeight.Bold
                                                                      TextBlock.foreground opt.Color
                                                                      TextBlock.lineSpacing 0.5 ]
                                                            ) ] ] ]
                                          TextBlock.create
                                              [ TextBlock.text opt.Description
                                                TextBlock.fontSize 10.0
                                                TextBlock.foreground Theme.TextMuted ] ] ]
                              StackPanel.create
                                  [ Grid.column 1
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.horizontalAlignment HorizontalAlignment.Center
                                    StackPanel.margin (12.0, 0.0, 0.0, 0.0)
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text "COND"
                                                TextBlock.fontSize 8.0
                                                TextBlock.foreground Theme.TextMuted
                                                TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                                          TextBlock.create
                                              [ TextBlock.text (string opt.ConditionCost)
                                                TextBlock.fontSize 11.0
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.foreground conditionColor
                                                TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
                              StackPanel.create
                                  [ Grid.column 2
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.horizontalAlignment HorizontalAlignment.Center
                                    StackPanel.margin (12.0, 0.0, 0.0, 0.0)
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text "GROW"
                                                TextBlock.fontSize 8.0
                                                TextBlock.foreground Theme.TextMuted
                                                TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                                          TextBlock.create
                                              [ TextBlock.text (TrainingPresenter.formatMult opt.GrowthMult)
                                                TextBlock.fontSize 11.0
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.foreground Theme.TextMain
                                                TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
                              StackPanel.create
                                  [ Grid.column 3
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.horizontalAlignment HorizontalAlignment.Center
                                    StackPanel.margin (12.0, 0.0, 0.0, 0.0)
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text "INJ"
                                                TextBlock.fontSize 8.0
                                                TextBlock.foreground Theme.TextMuted
                                                TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                                          TextBlock.create
                                              [ TextBlock.text (sprintf "%d%%" (int (opt.InjuryRisk * 100.0)))
                                                TextBlock.fontSize 11.0
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.foreground (
                                                    if opt.InjuryRisk > 0.0 then Theme.Danger else Theme.Success
                                                )
                                                TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ] ] ]
              ) ]
        |> View.withKey $"intensity-{opt.Intensity}"
        :> IView

    let private positionBadge (pos: Position) (mult: float) =
        let color =
            if mult >= 1.3 then Theme.Success
            elif mult >= 1.1 then Theme.Accent
            elif mult >= 1.0 then Theme.Warning
            elif mult >= 0.9 then Theme.TextMuted
            else Theme.Danger

        Border.create
            [ Border.background (color + "15")
              Border.borderBrush (color + "35")
              Border.borderThickness 1.0
              Border.cornerRadius 5.0
              Border.padding (8.0, 5.0)
              Border.margin (0.0, 0.0, 5.0, 5.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 4.0
                        StackPanel.children
                            [ TextBlock.create
                                  [ TextBlock.text (string pos)
                                    TextBlock.fontSize 9.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground Theme.TextMain ]
                              TextBlock.create
                                  [ TextBlock.text (TrainingPresenter.formatMult mult)
                                    TextBlock.fontSize 9.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground color ] ] ]
              ) ]

    let private emptyState =
        Border.create
            [ Border.verticalAlignment VerticalAlignment.Center
              Border.horizontalAlignment HorizontalAlignment.Center
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 10.0
                        StackPanel.horizontalAlignment HorizontalAlignment.Center
                        StackPanel.verticalAlignment VerticalAlignment.Center
                        StackPanel.children
                            [ Icons.iconXl IconName.squad Theme.TextMuted
                              TextBlock.create
                                  [ TextBlock.text "Select a player"
                                    TextBlock.fontSize 14.0
                                    TextBlock.fontWeight FontWeight.SemiBold
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                              TextBlock.create
                                  [ TextBlock.text "Choose a player from the list\nto manage their training"
                                    TextBlock.fontSize 11.0
                                    TextBlock.foreground (Theme.TextMuted + "99")
                                    TextBlock.textAlignment TextAlignment.Center
                                    TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
              ) ]

    let private playerDetailPanel
        (player: Player)
        (onFocusChange: TrainingFocus -> unit)
        (onIntensityChange: TrainingIntensity -> unit)
        =
        let positions: Position list =
            [ GK; DC; DR; DL; WBR; WBL; DM; MC; MR; ML; AMC; AML; AMR; ST ]

        let schedule = player.TrainingSchedule

        DockPanel.create
            [ DockPanel.lastChildFill true
              DockPanel.children
                  [ UI.sectionHeader IconName.training "TRAINING SCHEDULE"
                    |> fun h -> Border.create [ DockPanel.dock Dock.Top; Border.child h ]

                    ScrollViewer.create
                        [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
                          ScrollViewer.content (
                              StackPanel.create
                                  [ StackPanel.spacing 0.0
                                    StackPanel.children
                                        [ Border.create
                                              [ Border.padding (16.0, 12.0)
                                                Border.borderBrush Theme.Border
                                                Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                                Border.child (
                                                    StackPanel.create
                                                        [ StackPanel.spacing 2.0
                                                          StackPanel.children
                                                              [ TextBlock.create
                                                                    [ TextBlock.text player.Name
                                                                      TextBlock.fontSize 15.0
                                                                      TextBlock.fontWeight FontWeight.Bold
                                                                      TextBlock.foreground Theme.TextMain ]
                                                                TextBlock.create
                                                                    [ TextBlock.text
                                                                          $"%s{string player.Position}  ·  Age %d{Player.age DateTime.Now player}  ·  CS %d{player.CurrentSkill}  ·  PS %d{player.PotentialSkill}"
                                                                      TextBlock.fontSize 11.0
                                                                      TextBlock.foreground Theme.TextMuted ] ] ]
                                                ) ]
                                          UI.sectionHeader IconName.training "FOCUS"
                                          Border.create
                                              [ Border.padding (16.0, 10.0)
                                                Border.borderBrush Theme.Border
                                                Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                                Border.child (
                                                    StackPanel.create
                                                        [ StackPanel.spacing 6.0
                                                          StackPanel.children (
                                                              TrainingPresenter.focusOptions
                                                              |> List.map (fun opt ->
                                                                  focusCard
                                                                      opt
                                                                      (schedule.Focus = opt.Focus)
                                                                      onFocusChange)
                                                          ) ]
                                                ) ]
                                          UI.sectionHeader IconName.training "INTENSITY"
                                          Border.create
                                              [ Border.padding (16.0, 10.0)
                                                Border.borderBrush Theme.Border
                                                Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                                Border.child (
                                                    StackPanel.create
                                                        [ StackPanel.spacing 6.0
                                                          StackPanel.children (
                                                              TrainingPresenter.intensityOptions
                                                              |> List.map (fun opt ->
                                                                  intensityCard
                                                                      opt
                                                                      (schedule.Intensity = opt.Intensity)
                                                                      onIntensityChange)
                                                          ) ]
                                                ) ]
                                          UI.sectionHeader IconName.chart "POSITION IMPACT"
                                          Border.create
                                              [ Border.padding (16.0, 12.0)
                                                Border.child (
                                                    WrapPanel.create
                                                        [ WrapPanel.children (
                                                              positions
                                                              |> List.map (fun pos ->
                                                                  positionBadge
                                                                      pos
                                                                      (TrainingPresenter.getPositionImpact
                                                                          schedule.Focus
                                                                          schedule.Intensity
                                                                          pos))
                                                          ) ]
                                                ) ] ] ]
                          ) ] ] ]

    let trainingView (state: State) (dispatch: Msg -> unit) : IView =
        match state.Mode with
        | InGame(gs, _) ->
            let userClubId = gs.UserClubId
            let selectedPlayerId = state.Training.SelectedPlayer

            let squad =
                GameState.getSquad userClubId gs
                |> List.sortBy (fun p -> PlayerPresenter.positionSortKey p.Position, p.CurrentSkill * -1)

            let setPlayerFocus (playerId: PlayerId) (currentSchedule: TrainingSchedule) (focus: TrainingFocus) =
                dispatch (TrainingMsg (TrainingMsg.SetFocus(playerId, focus)))

            let setPlayerIntensity
                (playerId: PlayerId)
                (currentSchedule: TrainingSchedule)
                (intensity: TrainingIntensity)
                =
                dispatch (
                    TrainingMsg (
                        TrainingMsg.SetIntensity(
                            playerId,
                            intensity
                        )
                    )
                )

            let playerScheduleKey =
                selectedPlayerId
                |> Option.bind (fun id -> gs.Players.TryFind id)
                |> Option.map (fun p -> $"{p.TrainingSchedule.Focus}{p.TrainingSchedule.Intensity}")
                |> Option.defaultValue ""

            Grid.create
                [ Grid.columnDefinitions "*, 272"
                  Grid.children
                      [ Border.create
                            [ Grid.column 0
                              Border.background Theme.BgSidebar
                              Border.borderBrush Theme.Border
                              Border.borderThickness (0.0, 0.0, 1.0, 0.0)
                              Border.child (
                                  match selectedPlayerId with
                                  | Some playerId ->
                                      match gs.Players |> Map.tryFind playerId with
                                      | Some player ->
                                          playerDetailPanel
                                              player
                                              (fun focus -> setPlayerFocus playerId player.TrainingSchedule focus)
                                              (fun intensity ->
                                                  setPlayerIntensity playerId player.TrainingSchedule intensity)
                                      | None -> DockPanel.create [ DockPanel.children [ emptyState ] ]
                                  | None -> DockPanel.create [ DockPanel.children [ emptyState ] ]
                              ) ]
                        Border.create
                            [ Grid.column 1
                              Border.background Theme.BgSidebar
                              Border.borderBrush Theme.Border
                              Border.borderThickness (1.0, 1.0, 1.0, 1.0)
                              Border.child (
                                  DockPanel.create
                                      [ DockPanel.lastChildFill true
                                        DockPanel.children
                                            [ UI.sectionHeaderWithBadge IconName.squad "SQUAD" squad.Length
                                              |> fun h -> Border.create [ DockPanel.dock Dock.Top; Border.child h ]
                                              ScrollViewer.create
                                                  [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
                                                    ScrollViewer.content (
                                                        StackPanel.create
                                                            [ StackPanel.children (
                                                                  squad
                                                                  |> List.map (fun p ->
                                                                      playerRow
                                                                          p
                                                                          (selectedPlayerId = Some p.Id)
                                                                                                                                                     (fun () -> dispatch (TrainingMsg (TrainingMsg.SelectPlayer p.Id))))
                                                              ) ]
                                                    ) ] ] ]
                              ) ] ] ]
            |> View.withKey $"training-{selectedPlayerId}-{playerScheduleKey}"
            :> IView
        | _ -> Border.create [] :> IView
