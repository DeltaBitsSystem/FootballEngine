namespace FootballEngine.Pages

open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.FuncUI.Types
open Avalonia.FuncUI.Builder
open Avalonia.Input
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open FootballEngine
open FootballEngine.AppTypes
open FootballEngine.AppMsgs
open FootballEngine.Domain
open FootballEngine.Components
open FootballEngine.Icons
open FootballEngine.Routing

module Tactics =

    let private draggedId = ref<PlayerId option> None

    let private startDrag (playerId: int) (_dispatch: AppMsgs.Msg -> unit) (args: PointerPressedEventArgs) =
        draggedId.Value <- Some playerId

        task {
            let! _ = DragDrop.DoDragDropAsync(args, new DataTransfer(), DragDropEffects.Move)
            draggedId.Value <- None
        }
        |> ignore

    let private getCurrentLineup (gs: GameState) : Lineup option =
        gs
        |> GameState.headCoach gs.UserClubId
        |> Option.bind (fun coach -> coach.Attributes.Coaching.Lineup)

    // ── Helpers ───────────────────────────────────────────────────────

    let private sliderLabels = [ "Low"; "Mid"; "High" ]

    let private sliderValueToIndex (v: int) =
        if v <= 0 then 0
        elif v <= 2 then 1
        else 2

    let private indexToSliderValue (idx: int) =
        match idx with
        | 0 -> 0
        | 1 -> 2
        | 2 -> 4
        | _ -> 2

    let private positionJerseyNumber (pos: Position) =
        match pos with
        | GK -> 1
        | DR -> 2
        | DL -> 3
        | DC -> 4
        | WBR -> 5
        | WBL -> 5
        | DM -> 6
        | MC -> 8
        | MR -> 7
        | ML -> 11
        | AMR -> 7
        | AMC -> 10
        | AML -> 10
        | ST -> 9

    let private skillStars (skill: int) =
        let full = skill / 20
        let half = if (skill % 20) >= 10 then 1 else 0
        full, half

    // ── Player node on pitch (FM26-style) ─────────────────────────────

    let playerNode (player: Player option) (slot: LineupSlot) (state: State) (dispatch: AppMsgs.Msg -> unit) =
        let pIdFijo = player |> Option.map _.Id |> Option.defaultValue -1
        let isEmpty = player.IsNone

        let nodeCol =
            player
            |> Option.map (fun p -> Theme.positionColor p.Position)
            |> Option.defaultValue "#15ffffff"

        let shortName =
            player
            |> Option.map (fun p ->
                let parts = p.Name.Split(' ')

                if parts.Length >= 2 then
                    let last = parts |> Array.last
                    last.Substring(0, min 8 last.Length).ToUpper()
                else
                    p.Name.Substring(0, min 8 p.Name.Length).ToUpper())
            |> Option.defaultValue ""

        let jerseyNumber =
            player
            |> Option.map (fun p -> string (positionJerseyNumber p.Position))
            |> Option.defaultValue ""

        let posLabel =
            player
            |> Option.map (fun p -> $"%A{p.Position}")
            |> Option.defaultValue $"{slot.Role}"

        let conditionColor =
            player
            |> Option.map (fun p ->
                if p.MatchFitness >= 75 then Theme.Success
                elif p.MatchFitness >= 50 then Theme.Warning
                else Theme.Danger)
            |> Option.defaultValue "#10ffffff"

        let conditionText =
            player |> Option.map (fun p -> $"{p.MatchFitness}%%") |> Option.defaultValue ""

        let skillText =
            player |> Option.map (fun p -> string p.CurrentSkill) |> Option.defaultValue ""

        // Jersey shirt
        let shirt =
            Border.create
                [ Border.width 56.0
                  Border.height 46.0
                  Border.background (if isEmpty then "#08ffffff" else nodeCol)
                  Border.borderBrush (
                      if isEmpty then
                          "#12ffffff"
                      else
                          (Theme.withAlpha "66" nodeCol)
                  )
                  Border.borderThickness (if isEmpty then 1.0 else 1.5)
                  Border.cornerRadius (6.0, 6.0, 2.0, 2.0)
                  Border.verticalAlignment VerticalAlignment.Bottom
                  Border.horizontalAlignment HorizontalAlignment.Center
                  Border.child (
                      Grid.create
                          [ Grid.children
                                [ TextBlock.create
                                      [ TextBlock.text jerseyNumber
                                        TextBlock.fontSize 16.0
                                        TextBlock.fontWeight FontWeight.Black
                                        TextBlock.foreground (if isEmpty then "#25ffffff" else "#ffffff")
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                                        TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
                  ) ]

        // Name
        let nameLabel =
            TextBlock.create
                [ TextBlock.text shortName
                  TextBlock.fontSize 9.0
                  TextBlock.fontWeight FontWeight.Bold
                  TextBlock.foreground (if isEmpty then "#30ffffff" else Theme.TextMain)
                  TextBlock.horizontalAlignment HorizontalAlignment.Center ]

        // Position badge
        let posBadge =
            Border.create
                [ Border.background (
                      if isEmpty then
                          "#08ffffff"
                      else
                          (Theme.withAlpha "33" nodeCol)
                  )
                  Border.borderBrush (
                      if isEmpty then
                          "#10ffffff"
                      else
                          (Theme.withAlpha "55" nodeCol)
                  )
                  Border.borderThickness 1.0
                  Border.cornerRadius 3.0
                  Border.padding (5.0, 2.0)
                  Border.horizontalAlignment HorizontalAlignment.Center
                  Border.child (
                      TextBlock.create
                          [ TextBlock.text posLabel
                            TextBlock.fontSize 8.0
                            TextBlock.fontWeight FontWeight.Black
                            TextBlock.foreground (if isEmpty then "#35ffffff" else nodeCol)
                            TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                  ) ]

        // Condition + skill row
        let statsRow: IView =
            if isEmpty then
                Border.create [ Border.height 0.0 ] :> _
            else
                StackPanel.create
                    [ StackPanel.orientation Orientation.Horizontal
                      StackPanel.spacing 4.0
                      StackPanel.horizontalAlignment HorizontalAlignment.Center
                      StackPanel.children
                          [ Border.create
                                [ Border.width 5.0
                                  Border.height 5.0
                                  Border.background conditionColor
                                  Border.cornerRadius 2.5
                                  Border.verticalAlignment VerticalAlignment.Center ]
                            TextBlock.create
                                [ TextBlock.text conditionText
                                  TextBlock.fontSize 8.0
                                  TextBlock.fontWeight FontWeight.Bold
                                  TextBlock.foreground conditionColor
                                  TextBlock.verticalAlignment VerticalAlignment.Center ]
                            TextBlock.create
                                [ TextBlock.text "·"
                                  TextBlock.fontSize 8.0
                                  TextBlock.foreground Theme.TextDim
                                  TextBlock.verticalAlignment VerticalAlignment.Center ]
                            TextBlock.create
                                [ TextBlock.text skillText
                                  TextBlock.fontSize 8.0
                                  TextBlock.fontWeight FontWeight.Bold
                                  TextBlock.foreground Theme.Accent
                                  TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
                :> _

        Border.create
            [ Border.width 96.0
              Border.background "Transparent"
              Border.borderBrush (
                  if state.Tactics.SelectedSlot = Some slot.Index then
                      Theme.Accent
                  else
                      "Transparent"
              )
              Border.borderThickness 2.0
              Border.cornerRadius 6.0
              Control.allowDrop true
              Control.onDragOver (fun e -> e.DragEffects <- DragDropEffects.Move)
              Control.onDrop (fun _ ->
                  match draggedId.Value with
                  | Some pId ->
                      draggedId.Value <- None
                      dispatch (AppMsgs.TacticsMsg(TacticsMsg.DropPlayerInSlot(slot.Index, pId)))
                  | None -> ())
              Border.child (
                  Button.create
                      [ Button.padding 0.0
                        Button.background "Transparent"
                        Button.borderThickness 0.0
                        AttrBuilder<Button>
                            .CreateProperty<string>(
                                Avalonia.Automation.AutomationProperties.NameProperty,
                                Localization.resolve TranslationKey.ChooseForSlot [ "role", string slot.Role ],
                                ValueNone
                            )
                        Button.onClick (fun _ -> dispatch (AppMsgs.TacticsMsg(TacticsMsg.SelectSlot(Some slot.Index))))
                        Button.content (
                            StackPanel.create
                                [ StackPanel.spacing 3.0
                                  StackPanel.horizontalAlignment HorizontalAlignment.Center
                                  StackPanel.children [ shirt; nameLabel; posBadge; statsRow ] ]
                        ) ]
              ) ]
        |> View.withKey (string pIdFijo)

    // ── Pitch container ───────────────────────────────────────────────

    let pitchContainer (state: State) dispatch =
        match state.Mode with
        | InGame(gs, _) ->
            let lineup = getCurrentLineup gs

            let formation =
                lineup |> Option.map _.Formation |> Option.defaultValue state.SelectedTactics

            let slots = lineup |> Option.map _.Slots |> Option.defaultValue []

            FootballPitch.render formation (fun slot ->
                let assignedPlayer =
                    slots
                    |> List.tryFind (fun s -> s.Index = slot.Index)
                    |> Option.bind _.PlayerId
                    |> Option.bind gs.Players.TryFind

                playerNode assignedPlayer slot state dispatch)
            :> IView
        | _ -> Border.create [] :> IView

    // ── Section card wrapper ──────────────────────────────────────────

    let private sectionCard (headerContent: IView) (bodyContent: IView) =
        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusLg
              Border.clipToBounds true
              Border.child (
                  StackPanel.create [ StackPanel.spacing 0.0; StackPanel.children [ headerContent; bodyContent ] ]
              ) ]
        :> IView

    let private sectionHeader (icon: Material.Icons.MaterialIconKind) (color: string) (label: string) =
        Border.create
            [ Border.background Theme.BgElevated
              Border.padding (14.0, 10.0)
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 8.0
                        StackPanel.children
                            [ Icons.iconSm icon color
                              TextBlock.create
                                  [ TextBlock.text label
                                    TextBlock.fontSize 10.0
                                    TextBlock.fontWeight FontWeight.Black
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
              ) ]
        :> IView

    // ── Left panel sections ───────────────────────────────────────────

    let private formationSection (currentFormation: Formation) dispatch =
        let header = sectionHeader IconName.tactics Theme.Accent "FORMATION"

        let body =
            Border.create
                [ Border.padding (12.0, 10.0)
                  Border.child (
                      ComboBox.create
                          [ ComboBox.dataItems FormationLineups.all
                            ComboBox.selectedItem currentFormation
                            ComboBox.onSelectedItemChanged (fun obj ->
                                if obj <> null then
                                    dispatch (AppMsgs.TacticsMsg(TacticsMsg.SetFormation(obj :?> Formation))))
                            ComboBox.horizontalAlignment HorizontalAlignment.Stretch
                            ComboBox.fontSize 13.0
                            ComboBox.fontWeight FontWeight.Bold ]
                  ) ]

        sectionCard header body

    let private playbookIcon situation =
        match situation with
        | TacticalSituation.ProtectLead -> Material.Icons.MaterialIconKind.Shield
        | TacticalSituation.ChaseGoal -> Material.Icons.MaterialIconKind.ArrowUpBold
        | TacticalSituation.ContainFastAttack -> Material.Icons.MaterialIconKind.ShieldOutline
        | TacticalSituation.OpenLowBlock -> Material.Icons.MaterialIconKind.ExpandAll

    let private presetSection (currentInstructions: TacticalInstructions) dispatch =
        let header = sectionHeader IconName.lightning Theme.Warning "PLAYBOOKS CONTEXTUALES"

        let makePlaybookCard (playbook: TacticalPlaybook) =
            let target = TacticalPlaybooks.applyToInstructions playbook currentInstructions
            let changed = target <> currentInstructions
            let effectsText = String.concat " · " playbook.Effects
            let commitmentsText = String.concat " · " playbook.Commitments

            Border.create
                [ Border.padding (8.0, 7.0)
                  Border.margin (0.0, 0.0, 0.0, 6.0)
                  Border.background Theme.BgCard
                  Border.borderBrush Theme.Border
                  Border.borderThickness 1.0
                  Border.cornerRadius 6.0
                  Border.child (
                      StackPanel.create
                          [ StackPanel.spacing 4.0
                            StackPanel.children
                                [ DockPanel.create
                                      [ DockPanel.lastChildFill true
                                        DockPanel.children
                                            [ Button.create
                                                  [ DockPanel.dock Dock.Right
                                                    Button.content (if changed then "Aplicar" else "Activo")
                                                    Button.isEnabled changed
                                                    Button.padding (8.0, 4.0)
                                                    Button.fontSize 10.0
                                                    Button.onClick (fun _ ->
                                                        dispatch (
                                                            AppMsgs.TacticsMsg(
                                                                TacticsMsg.SetInstruction(fun instructions ->
                                                                    TacticalPlaybooks.applyToInstructions
                                                                        playbook
                                                                        instructions)
                                                            )
                                                        )) ]
                                              StackPanel.create
                                                  [ StackPanel.orientation Orientation.Horizontal
                                                    StackPanel.spacing 5.0
                                                    StackPanel.children
                                                        [ Icons.iconSm (playbookIcon playbook.Situation) Theme.Warning
                                                          TextBlock.create
                                                              [ TextBlock.text playbook.Name
                                                                TextBlock.fontWeight FontWeight.Bold
                                                                TextBlock.fontSize 11.0
                                                                TextBlock.foreground Theme.TextMain ] ] ] ] ]
                                  TextBlock.create
                                      [ TextBlock.text playbook.Summary
                                        TextBlock.fontSize 10.0
                                        TextBlock.foreground Theme.TextDim
                                        TextBlock.textWrapping TextWrapping.Wrap ]
                                  TextBlock.create
                                      [ TextBlock.text ($"Efectos: {effectsText}")
                                        TextBlock.fontSize 9.0
                                        TextBlock.foreground Theme.Success
                                        TextBlock.textWrapping TextWrapping.Wrap ]
                                  TextBlock.create
                                      [ TextBlock.text ($"Compromisos: {commitmentsText}")
                                        TextBlock.fontSize 9.0
                                        TextBlock.foreground Theme.Warning
                                        TextBlock.textWrapping TextWrapping.Wrap ]
                                  TextBlock.create
                                      [ TextBlock.text ($"Recomendación basada en: {playbook.RecommendationBasis}")
                                        TextBlock.fontSize 9.0
                                        TextBlock.foreground Theme.TextMuted
                                        TextBlock.textWrapping TextWrapping.Wrap ] ] ]
                  ) ]
            :> IView

        let body =
            StackPanel.create
                [ StackPanel.margin (10.0, 8.0)
                  StackPanel.children (TacticalPlaybooks.all |> List.map makePlaybookCard) ]

        sectionCard header body

    let private instructionSlider (id: string) (currentValue: int) (dispatch: AppMsgs.Msg -> unit) =
        let idx = sliderValueToIndex currentValue
        let label = sliderLabels.[idx]

        // ids are camelCase ("DefensiveLine"); the header needs spaces.
        let title =
            System.Text.RegularExpressions.Regex.Replace(id, "([a-z])([A-Z])", "$1 $2").ToUpperInvariant()

        let registryInfo = TacticalInstructionRegistry.tryFind id

        let desc =
            registryInfo
            |> Option.map (fun (spec, _) -> spec.Description)
            |> Option.defaultValue ""

        let tradeoff = registryInfo |> Option.map snd

        let cost =
            tradeoff
            |> Option.map (fun t -> $"Condición: {t.Condition} · Ventaja: {t.Advantage} · Costo: {t.Cost}")
            |> Option.defaultValue ""

        let accentColor =
            if idx = 0 then Theme.AccentAlt
            elif idx = 1 then Theme.Accent
            else Theme.Danger

        let makeSliderButton (i: int) (lbl: string) =
            let isSelected = i = idx

            Button.create
                [ Button.padding (4.0, 5.0)
                  Button.cornerRadius 4.0
                  Button.background (if isSelected then accentColor else "Transparent")
                  Button.borderThickness 0.0
                  Button.cursor Avalonia.Input.Cursor.Default
                  Button.onClick (fun _ ->
                      let newValue = indexToSliderValue i

                      dispatch (
                          AppMsgs.TacticsMsg(
                              TacticsMsg.SetInstruction(fun instr -> TacticalInstructions.set id newValue instr)
                          )
                      ))
                  Button.content (
                      TextBlock.create
                          [ TextBlock.text lbl
                            TextBlock.fontSize 10.0
                            TextBlock.fontWeight (if isSelected then FontWeight.Bold else FontWeight.Medium)
                            TextBlock.foreground (if isSelected then "#ffffff" else Theme.TextDim)
                            TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                  ) ]
            :> IView

        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusMd
              Border.padding (12.0, 10.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 6.0
                        StackPanel.children
                            [ DockPanel.create
                                  [ DockPanel.lastChildFill true
                                    DockPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text title
                                                TextBlock.fontSize 10.0
                                                TextBlock.fontWeight FontWeight.Black
                                                TextBlock.foreground Theme.TextSub
                                                TextBlock.verticalAlignment VerticalAlignment.Center ]
                                          Border.create
                                              [ DockPanel.dock Dock.Right
                                                Border.background (Theme.withAlpha "22" accentColor)
                                                Border.cornerRadius 4.0
                                                Border.padding (6.0, 2.0)
                                                Border.child (
                                                    TextBlock.create
                                                        [ TextBlock.text label
                                                          TextBlock.fontSize 9.0
                                                          TextBlock.fontWeight FontWeight.Bold
                                                          TextBlock.foreground accentColor ]
                                                ) ] ] ]
                              TextBlock.create
                                  [ TextBlock.text desc
                                    TextBlock.fontSize 10.0
                                    TextBlock.foreground Theme.TextDim
                                    TextBlock.textWrapping TextWrapping.Wrap ]
                              Grid.create
                                  [ Grid.columnDefinitions "*, *, *"
                                    Grid.children (sliderLabels |> List.mapi makeSliderButton) ]
                              if cost <> "" then
                                  Border.create
                                      [ Border.background Theme.BgElevated
                                        Border.cornerRadius 4.0
                                        Border.padding (6.0, 4.0)
                                        Border.child (
                                            TextBlock.create
                                                [ TextBlock.text cost
                                                  TextBlock.fontSize 9.0
                                                  TextBlock.foreground Theme.TextDim
                                                  TextBlock.textWrapping TextWrapping.Wrap ]
                                        ) ] ] ]
              ) ]

    let private instructionsPanel (currentLineup: Lineup option) dispatch =
        let instructions =
            currentLineup
            |> Option.bind _.Instructions
            |> Option.defaultValue TacticalInstructions.defaultInstructions

        let currentTactics =
            currentLineup |> Option.map _.Tactics |> Option.defaultValue Balanced

        let header = sectionHeader IconName.tactics Theme.Accent "INSTRUCTIONS"

        let body =
            StackPanel.create
                [ StackPanel.spacing 0.0
                  StackPanel.children
                      [ Border.create
                            [ Border.padding (12.0, 8.0)
                              Border.borderBrush Theme.Border
                              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                              Border.child (
                                  ComboBox.create
                                      [ ComboBox.dataItems [ Balanced; Attacking; Defensive; Pressing; Counter ]
                                        ComboBox.selectedItem currentTactics
                                        ComboBox.onSelectedItemChanged (fun obj ->
                                            if obj <> null then
                                                dispatch (
                                                    AppMsgs.TacticsMsg(TacticsMsg.SetTeamTactics(obj :?> TeamTactics))
                                                ))
                                        ComboBox.horizontalAlignment HorizontalAlignment.Stretch
                                        ComboBox.fontSize 12.0
                                        ComboBox.fontWeight FontWeight.Bold ]
                              ) ]
                        Border.create
                            [ Border.padding (10.0, 8.0)
                              Border.child (
                                  StackPanel.create
                                      [ StackPanel.spacing 6.0
                                        StackPanel.children
                                            [ instructionSlider "Mentality" instructions.Mentality dispatch
                                              instructionSlider "DefensiveLine" instructions.DefensiveLine dispatch
                                              instructionSlider
                                                  "PressingIntensity"
                                                  instructions.PressingIntensity
                                                  dispatch
                                              instructionSlider "Width" instructions.Width dispatch
                                              instructionSlider "Tempo" instructions.Tempo dispatch
                                              instructionSlider "Directness" instructions.Directness dispatch ] ]
                              ) ] ] ]

        sectionCard header body

    // ── Right panel: Selection Info ────────────────────────────────────

    let private starGlyphs (stars: float) =
        let full = int (round stars) |> max 0 |> min 5
        String.replicate full "★" + String.replicate (5 - full) "☆"

    // Advice: the starter most out of his depth and the bench player who fixes
    // it. Same suitability function as the stars and Quick Pick, so the three
    // can never disagree.
    let private adviceOf (gs: GameState) : (string * string) option =
        match TacticsSlice.Query.currentLineup gs with
        | None -> None
        | Some lu ->
            let squad = GameState.getSquad gs.UserClubId gs

            let starters =
                lu.Slots
                |> List.choose (fun s -> s.PlayerId |> Option.map (fun pid -> pid, s.Role))

            let startersSet = starters |> List.map fst |> Set.ofList

            starters
            |> List.choose (fun (pid, role) ->
                gs.Players
                |> Map.tryFind pid
                |> Option.map (fun p ->
                    let mine = TacticalSuitability.stars p role

                    let best =
                        squad
                        |> List.filter (fun q -> q.Status = Available && not (startersSet.Contains q.Id))
                        |> List.sortByDescending (fun q -> TacticalSuitability.stars q role)
                        |> List.tryHead
                        |> Option.map (fun q -> q, TacticalSuitability.stars q role)

                    mine, role, p, best))
            |> List.choose (fun (mine, role, p, best) ->
                match best with
                | Some(q, theirs) when theirs - mine >= 1.0 -> Some(mine, role, p, (q, theirs))
                | _ -> None)
            |> List.sortByDescending (fun (_, _, _, (_, theirs)) -> theirs)
            |> List.tryHead
            |> Option.map (fun (mine, role, p, (q, theirs)) ->
                let last (n: string) = n.Split(' ') |> Array.last
                let mineText = sprintf "%.1f" mine
                let theirsText = sprintf "%.1f" theirs
                $"{last p.Name} looks off at {role} ({mineText}★)", $"Try {last q.Name} ({theirsText}★)")

    let private selectionInfoRow
        (p: Player)
        (isStarter: bool)
        (slotRole: Position option)
        (dispatch: AppMsgs.Msg -> unit)
        =
        let col = Theme.positionColor p.Position
        let fitCol = PlayerPresenter.fitnessColor p.MatchFitness

        let suitPos = slotRole |> Option.defaultValue p.Position
        let stars = TacticalSuitability.stars p suitPos

        let posText = $"%A{p.Position}"

        Border.create
            [ Border.background "Transparent"
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.padding (8.0, 6.0)
              Border.onPointerPressed (fun e ->
                  let pt = e.GetCurrentPoint(e.Source :?> Control)

                  if pt.Properties.IsLeftButtonPressed then
                      e.Handled <- true
                      startDrag p.Id dispatch e)
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions "30, *, 66, 46"
                        Grid.children
                            [ // Position badge
                              Border.create
                                  [ Grid.column 0
                                    Border.background (Theme.withAlpha "22" col)
                                    Border.borderBrush (Theme.withAlpha "44" col)
                                    Border.borderThickness 1.0
                                    Border.cornerRadius 4.0
                                    Border.padding (4.0, 2.0)
                                    Border.verticalAlignment VerticalAlignment.Center
                                    Border.child (
                                        TextBlock.create
                                            [ TextBlock.text posText
                                              TextBlock.fontSize 8.0
                                              TextBlock.fontWeight FontWeight.Black
                                              TextBlock.foreground col
                                              TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                                    ) ]
                              // Name + skill
                              StackPanel.create
                                  [ Grid.column 1
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.spacing 1.0
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text p.Name
                                                TextBlock.foreground Theme.TextMain
                                                TextBlock.fontSize 11.0
                                                TextBlock.fontWeight FontWeight.SemiBold
                                                TextBlock.textTrimming TextTrimming.CharacterEllipsis ]
                                          TextBlock.create
                                              [ TextBlock.text $"CA {p.CurrentSkill}"
                                                TextBlock.foreground Theme.TextMuted
                                                TextBlock.fontSize 9.0 ] ] ]
                              // Suitability stars for the slot (own position off the pitch)
                              TextBlock.create
                                  [ Grid.column 2
                                    TextBlock.text (starGlyphs stars)
                                    TextBlock.fontSize 10.0
                                    TextBlock.foreground Theme.Warning
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                    TextBlock.margin (6.0, 0.0) ]
                              // Condition
                              StackPanel.create
                                  [ Grid.column 3
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 2.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ Border.create
                                              [ Border.width 5.0
                                                Border.height 5.0
                                                Border.background fitCol
                                                Border.cornerRadius 2.5
                                                Border.verticalAlignment VerticalAlignment.Center ]
                                          TextBlock.create
                                              [ TextBlock.text (sprintf "%d%%" p.MatchFitness)
                                                TextBlock.foreground fitCol
                                                TextBlock.fontSize 9.0
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.verticalAlignment VerticalAlignment.Center ] ] ] ] ]
              ) ]

    let private selectionInfoPanel (starterIds: Set<PlayerId>) (benchPlayers: Player list) (gs: GameState) dispatch =
        let starters = starterIds |> Set.toList |> List.choose gs.Players.TryFind

        let slotRoleOf =
            TacticsSlice.Query.currentLineup gs
            |> Option.map (fun lu ->
                lu.Slots
                |> List.choose (fun s -> s.PlayerId |> Option.map (fun pid -> pid, s.Role))
                |> Map.ofList)
            |> Option.defaultValue Map.empty

        let allPlayers =
            starters
            |> List.map (fun p -> p, true, Map.tryFind p.Id slotRoleOf)
            |> List.append (benchPlayers |> List.map (fun p -> p, false, None))

        let advice =
            match adviceOf gs with
            | None -> Border.create [ Border.height 0.0 ] :> IView
            | Some(title, body) ->
                Border.create
                    [ Border.background (Theme.withAlpha "15" Theme.AccentAlt)
                      Border.borderBrush (Theme.withAlpha "40" Theme.AccentAlt)
                      Border.borderThickness 1.0
                      Border.cornerRadius 8.0
                      Border.padding (10.0, 8.0)
                      Border.margin (8.0, 8.0, 8.0, 0.0)
                      Border.child (
                          StackPanel.create
                              [ StackPanel.spacing 2.0
                                StackPanel.children
                                    [ TextBlock.create
                                          [ TextBlock.text title
                                            TextBlock.fontSize 10.0
                                            TextBlock.fontWeight FontWeight.Bold
                                            TextBlock.foreground Theme.TextMain
                                            TextBlock.textWrapping TextWrapping.Wrap ]
                                      TextBlock.create
                                          [ TextBlock.text body
                                            TextBlock.fontSize 10.0
                                            TextBlock.foreground Theme.AccentAlt
                                            TextBlock.fontWeight FontWeight.SemiBold ] ] ]
                      ) ]
                :> IView

        let header =
            Border.create
                [ Border.background Theme.BgElevated
                  Border.padding (14.0, 10.0)
                  Border.borderBrush Theme.Border
                  Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                  Border.child (
                      StackPanel.create
                          [ StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 8.0
                            StackPanel.children
                                [ Icons.iconSm IconName.squad Theme.Accent
                                  TextBlock.create
                                      [ TextBlock.text "SELECTION INFO"
                                        TextBlock.fontSize 10.0
                                        TextBlock.fontWeight FontWeight.Black
                                        TextBlock.foreground Theme.TextMuted
                                        TextBlock.verticalAlignment VerticalAlignment.Center ]
                                  UI.countBadge (starterIds.Count + benchPlayers.Length) ] ]
                  ) ]

        // Column headers
        let columnHeaders =
            Border.create
                [ Border.padding (8.0, 6.0)
                  Border.borderBrush Theme.Border
                  Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                  Border.child (
                      Grid.create
                          [ Grid.columnDefinitions "30, *, 66, 46"
                            Grid.children
                                [ TextBlock.create
                                      [ Grid.column 0
                                        TextBlock.text "POS"
                                        TextBlock.fontSize 8.0
                                        TextBlock.fontWeight FontWeight.Black
                                        TextBlock.foreground Theme.TextDim ]
                                  TextBlock.create
                                      [ Grid.column 1
                                        TextBlock.text "PLAYER"
                                        TextBlock.fontSize 8.0
                                        TextBlock.fontWeight FontWeight.Black
                                        TextBlock.foreground Theme.TextDim ]
                                  TextBlock.create
                                      [ Grid.column 2
                                        TextBlock.text "SUIT"
                                        TextBlock.fontSize 8.0
                                        TextBlock.fontWeight FontWeight.Black
                                        TextBlock.foreground Theme.TextDim
                                        TextBlock.margin (6.0, 0.0) ]
                                  TextBlock.create
                                      [ Grid.column 3
                                        TextBlock.text "FIT"
                                        TextBlock.fontSize 8.0
                                        TextBlock.fontWeight FontWeight.Black
                                        TextBlock.foreground Theme.TextDim ] ] ]
                  ) ]

        let bodyItems =
            [ yield columnHeaders :> IView
              yield!
                  allPlayers
                  |> List.map (fun (p, isStarter, slotRole) -> selectionInfoRow p isStarter slotRole dispatch :> IView) ]

        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusLg
              Border.clipToBounds true
              Border.child (
                  DockPanel.create
                      [ DockPanel.lastChildFill true
                        DockPanel.children
                            [ Border.create [ DockPanel.dock Dock.Top; Border.child header ]
                              Border.create [ DockPanel.dock Dock.Top; Border.child advice ]
                              ScrollViewer.create
                                  [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
                                    ScrollViewer.content (
                                        StackPanel.create [ StackPanel.spacing 0.0; StackPanel.children bodyItems ]
                                    ) ] ] ]
              ) ]
        :> IView

    // ── Bottom: Substitutes bar ───────────────────────────────────────

    let private substituteSlot (p: Player option) (idx: int) dispatch =
        let bgColor, borderColor, textColor, name, subText =
            match p with
            | Some player ->
                let col = Theme.positionColor player.Position

                Theme.withAlpha "22" col,
                Theme.withAlpha "55" col,
                Theme.TextMain,
                player.Name.Split(' ') |> Array.last,
                $"%A{player.Position}"
            | None -> "#06ffffff", "#12ffffff", Theme.TextDim, $"S{idx + 1}", ""

        Border.create
            [ Border.width 100.0
              Border.height 48.0
              Border.background bgColor
              Border.borderBrush borderColor
              Border.borderThickness 1.0
              Border.cornerRadius 6.0
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 2.0
                        StackPanel.horizontalAlignment HorizontalAlignment.Center
                        StackPanel.verticalAlignment VerticalAlignment.Center
                        StackPanel.children
                            [ TextBlock.create
                                  [ TextBlock.text name
                                    TextBlock.fontSize 9.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground textColor
                                    TextBlock.horizontalAlignment HorizontalAlignment.Center
                                    TextBlock.textTrimming TextTrimming.CharacterEllipsis ]
                              if subText <> "" then
                                  TextBlock.create
                                      [ TextBlock.text subText
                                        TextBlock.fontSize 8.0
                                        TextBlock.foreground Theme.TextDim
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
              ) ]

    let private substitutesBar (benchPlayers: Player list) (dispatch: AppMsgs.Msg -> unit) =
        let slots = [ 0..9 ] |> List.map (fun i -> List.tryItem i benchPlayers)

        Border.create
            [ Border.background Theme.BgSidebar
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 1.0, 0.0, 0.0)
              Border.padding (16.0, 10.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 6.0
                        StackPanel.children
                            [ // Label
                              StackPanel.create
                                  [ StackPanel.orientation Orientation.Vertical
                                    StackPanel.spacing 2.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.margin (0.0, 0.0, 8.0, 0.0)
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text "SUBSTITUTES"
                                                TextBlock.fontSize 9.0
                                                TextBlock.fontWeight FontWeight.Black
                                                TextBlock.foreground Theme.TextMuted ]
                                          TextBlock.create
                                              [ TextBlock.text $"{benchPlayers.Length} available"
                                                TextBlock.fontSize 8.0
                                                TextBlock.foreground Theme.TextDim ] ] ]
                              // Slot grid
                              yield! slots |> List.mapi (fun i p -> substituteSlot p i dispatch :> IView) ] ]
              ) ]

    // ── Top bar ───────────────────────────────────────────────────────

    // ── Main view ─────────────────────────────────────────────────────

    let private slotPicker (state: State) (gs: GameState) dispatch : IView =
        let selected =
            TacticsSlice.Query.currentLineup gs
            |> Option.bind (fun lineup ->
                lineup.Slots
                |> List.tryFind (fun slot -> Some slot.Index = state.Tactics.SelectedSlot))

        let help = Localization.resolve TranslationKey.SelectLineupSlot []

        let controls =
            match selected with
            | None -> []
            | Some slot ->
                let candidates =
                    GameState.getUserSquad gs
                    |> List.filter (fun player -> player.Status = Available)
                    |> List.sortBy (fun player ->
                        -TacticalSuitability.stars player slot.Role, -player.MatchFitness, player.Id)
                    |> List.toArray

                let labels =
                    candidates
                    |> Array.map (fun player ->
                        Localization.resolve
                            TranslationKey.PlayerChoice
                            [ "name", player.Name
                              "position", string player.Position
                              "stars", (TacticalSuitability.stars player slot.Role).ToString("0.0")
                              "fitness", string player.MatchFitness ])

                [ TextBlock.create
                      [ TextBlock.text (Localization.resolve TranslationKey.ChooseForSlot [ "role", string slot.Role ])
                        TextBlock.foreground Theme.Accent
                        TextBlock.fontWeight FontWeight.Bold ]
                  :> IView
                  ComboBox.create
                      [ ComboBox.dataItems labels
                        ComboBox.horizontalAlignment HorizontalAlignment.Stretch
                        ComboBox.selectedIndex (
                            candidates
                            |> Array.tryFindIndex (fun player -> Some player.Id = slot.PlayerId)
                            |> Option.defaultValue -1
                        )
                        ComboBox.onSelectedIndexChanged (fun index ->
                            if
                                index >= 0
                                && index < candidates.Length
                                && Some candidates[index].Id <> slot.PlayerId
                            then
                                dispatch (
                                    AppMsgs.TacticsMsg(TacticsMsg.DropPlayerInSlot(slot.Index, candidates[index].Id))
                                )) ]
                  :> IView
                  UI.ghostButton (Localization.resolve TranslationKey.Cancel []) None (fun _ ->
                      dispatch (AppMsgs.TacticsMsg(TacticsMsg.SelectSlot None))) ]

        Border.create
            [ DockPanel.dock Dock.Top
              Border.padding (12.0, 8.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 6.0
                        StackPanel.children
                            [ TextBlock.create
                                  [ TextBlock.text help
                                    TextBlock.fontSize 12.0
                                    TextBlock.foreground Theme.TextSub
                                    TextBlock.textWrapping TextWrapping.Wrap ]
                              yield! controls ] ]
              ) ]
        :> IView

    // ── Top strip: familiarity, next match, Undo, Quick Pick ────────────

    let private topStrip (gs: GameState) (state: State) (dispatch: AppMsgs.Msg -> unit) : IView =
        let formation =
            TacticsSlice.Query.currentLineup gs
            |> Option.map _.Formation
            |> Option.defaultValue state.SelectedTactics

        let familiarity =
            gs.Clubs
            |> Map.tryFind gs.UserClubId
            |> Option.map (fun c ->
                c.CoordinationMemory.TacticalFamiliarity
                |> Map.tryFind (string formation)
                |> Option.defaultValue 0.5)
            |> Option.defaultValue 0.5

        let nextText =
            match HomePresenter.getNextMatch gs with
            | None -> "No fixture scheduled"
            | Some m -> $"{m.LocalName} vs {m.VisitName} · " + m.Date.ToString("dd MMM")

        let famColor =
            if familiarity >= 0.8 then Theme.Success
            elif familiarity >= 0.6 then Theme.Accent
            else Theme.Warning

        Border.create
            [ Border.background Theme.BgSidebar
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.padding (16.0, 10.0)
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions "Auto, *, Auto"
                        Grid.children
                            [ StackPanel.create
                                  [ Grid.column 0
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 10.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text $"FAMILIARITY · {string formation}"
                                                TextBlock.fontSize 10.0
                                                TextBlock.fontWeight FontWeight.Black
                                                TextBlock.foreground Theme.TextMuted
                                                TextBlock.verticalAlignment VerticalAlignment.Center ]
                                          ProgressBar.create
                                              [ ProgressBar.minimum 0.0
                                                ProgressBar.maximum 1.0
                                                ProgressBar.value familiarity
                                                ProgressBar.width 110.0
                                                ProgressBar.height 6.0
                                                ProgressBar.cornerRadius 3.0
                                                ProgressBar.background Theme.BgElevated
                                                ProgressBar.foreground famColor
                                                ProgressBar.verticalAlignment VerticalAlignment.Center ]
                                          TextBlock.create
                                              [ TextBlock.text (sprintf "%d%%" (int (familiarity * 100.0)))
                                                TextBlock.fontSize 11.0
                                                TextBlock.fontWeight FontWeight.Black
                                                TextBlock.foreground famColor
                                                TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
                              TextBlock.create
                                  [ Grid.column 1
                                    TextBlock.text (
                                        nextText + "\n" + Localization.resolve TranslationKey.ChangesNextMatch []
                                    )
                                    TextBlock.textWrapping TextWrapping.Wrap
                                    TextBlock.maxWidth 380.0
                                    TextBlock.fontSize 11.0
                                    TextBlock.foreground Theme.TextDim
                                    TextBlock.horizontalAlignment HorizontalAlignment.Center
                                    TextBlock.verticalAlignment VerticalAlignment.Center ]
                              StackPanel.create
                                  [ Grid.column 2
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 8.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ UI.secondaryButton
                                              (Localization.resolve
                                                  (if (CareerFlow.todayFixture gs).IsSome then
                                                       TranslationKey.PrepareNextMatch
                                                   else
                                                       TranslationKey.ReturnHome)
                                                  [])
                                              None
                                              UI.ControlState.Default
                                              (fun _ ->
                                                  let route =
                                                      match CareerFlow.continuation state with
                                                      | CareerFlow.ReturnToMatch(id, phase) -> Route.Match(id, phase)
                                                      | CareerFlow.PrepareMatch id ->
                                                          Route.Match(id, FootballEngine.Routing.Prematch)
                                                      | _ -> Route.Home

                                                  dispatch (AppMsgs.Navigate route))
                                          Button.create
                                              [ Button.content (Localization.resolve TranslationKey.UndoLineup [])
                                                Button.fontSize 11.0
                                                Button.padding (12.0, 7.0)
                                                Button.background Theme.BgCard
                                                Button.borderBrush Theme.Border
                                                Button.borderThickness 1.0
                                                Button.cornerRadius 6.0
                                                Button.foreground Theme.TextSub
                                                Button.isEnabled (not state.Tactics.History.IsEmpty)
                                                Button.onClick (fun _ ->
                                                    dispatch (AppMsgs.TacticsMsg(TacticsMsg.UndoTactics))) ]
                                          Button.create
                                              [ Button.content (Localization.resolve TranslationKey.SuggestLineup [])
                                                Button.fontSize 11.0
                                                Button.fontWeight FontWeight.Bold
                                                Button.padding (12.0, 7.0)
                                                Button.background Theme.Accent
                                                Button.borderThickness 0.0
                                                Button.cornerRadius 6.0
                                                Button.foreground Theme.BgSidebar
                                                Button.onClick (fun _ ->
                                                    dispatch (AppMsgs.TacticsMsg(TacticsMsg.QuickPick))) ] ] ] ] ]
              ) ]
        :> IView

    let tacticView (state: State) dispatch : IView =
        match state.Mode with
        | InGame(gs, _) ->
            let lineup = getCurrentLineup gs

            let currentFormation =
                lineup |> Option.map _.Formation |> Option.defaultValue state.SelectedTactics

            let starterIds =
                lineup
                |> Option.map (fun l -> l.Slots |> List.choose _.PlayerId)
                |> Option.defaultValue []
                |> Set.ofList

            let benchPlayers =
                GameState.getSquad gs.UserClubId gs
                |> List.filter (fun p -> not (starterIds.Contains p.Id))
                |> List.sortBy (fun p -> PlayerPresenter.positionSortKey p.Position, p.CurrentSkill * -1)

            let currentInstructions =
                lineup
                |> Option.bind _.Instructions
                |> Option.defaultValue TacticalInstructions.defaultInstructions

            Grid.create
                [ Grid.isEnabled (not state.IsProcessing && state.AdvanceTarget.IsNone)
                  Grid.rowDefinitions "Auto, *, Auto"
                  Grid.children
                      [ Border.create [ Grid.row 0; Border.child (topStrip gs state dispatch) ]
                        Grid.create
                            [ Grid.row 1
                              Grid.columnDefinitions "256, *, 280"
                              Grid.children
                                  [ Border.create
                                        [ Grid.column 0
                                          Border.background Theme.BgSidebar
                                          Border.borderBrush Theme.Border
                                          Border.borderThickness (0.0, 0.0, 1.0, 0.0)
                                          Border.child (
                                              ScrollViewer.create
                                                  [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
                                                    ScrollViewer.content (
                                                        StackPanel.create
                                                            [ StackPanel.spacing 8.0
                                                              StackPanel.margin (8.0, 8.0)
                                                              StackPanel.children
                                                                  [ formationSection currentFormation dispatch
                                                                    presetSection currentInstructions dispatch
                                                                    instructionsPanel lineup dispatch ] ]
                                                    ) ]
                                          ) ]
                                    Border.create
                                        [ Grid.column 1
                                          Border.padding (8.0, 8.0)
                                          Border.child (
                                              Border.create
                                                  [ Border.background Theme.BgSubtle
                                                    Border.borderBrush Theme.Border
                                                    Border.borderThickness 1.0
                                                    Border.cornerRadius 12.0
                                                    Border.clipToBounds true
                                                    Border.child (
                                                        DockPanel.create
                                                            [ DockPanel.lastChildFill true
                                                              DockPanel.children
                                                                  [ slotPicker state gs dispatch
                                                                    pitchContainer state dispatch ] ]
                                                    ) ]
                                          ) ]
                                    Border.create
                                        [ Grid.column 2
                                          Border.background Theme.BgSidebar
                                          Border.borderBrush Theme.Border
                                          Border.borderThickness (1.0, 0.0, 0.0, 0.0)
                                          Border.child (selectionInfoPanel starterIds benchPlayers gs dispatch) ] ] ]
                        Border.create [ Grid.row 2; Border.child (substitutesBar benchPlayers dispatch) ] ] ]
            |> View.withKey $"tactics-{currentFormation}-{starterIds.Count}"
            :> IView

        | _ -> Border.create [] :> IView
