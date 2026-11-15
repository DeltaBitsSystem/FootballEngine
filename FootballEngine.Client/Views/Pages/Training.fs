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

    type EmphasisOption =
        { Emphasis: TeamEmphasis
          Icon: Material.Icons.MaterialIconKind
          Description: string }

    let emphasisOptions: EmphasisOption list =
        [ { Emphasis = EmHighPress
            Icon = Material.Icons.MaterialIconKind.LightningBolt
            Description = "Pressing + fitness tilt" }
          { Emphasis = EmPossession
            Icon = Material.Icons.MaterialIconKind.Refresh
            Description = "Creation tilt" }
          { Emphasis = EmAttack
            Icon = Material.Icons.MaterialIconKind.TrendingUp
            Description = "Finishing tilt" }
          { Emphasis = EmDefense
            Icon = Material.Icons.MaterialIconKind.ShieldOutline
            Description = "Defending tilt" }
          { Emphasis = EmSetPieces
            Icon = Material.Icons.MaterialIconKind.FlagOutline
            Description = "Technique tilt" }
          { Emphasis = EmRecovery
            Icon = Material.Icons.MaterialIconKind.BatteryCharging
            Description = "Whole squad recovers" } ]

    let groupIcon (g: AttributeGroup) : Material.Icons.MaterialIconKind =
        match g with
        | AttributeGroup.Finishing -> Material.Icons.MaterialIconKind.Soccer
        | AttributeGroup.Creation -> Material.Icons.MaterialIconKind.Brain
        | AttributeGroup.Technique -> Material.Icons.MaterialIconKind.StarOutline
        | AttributeGroup.Defending -> Material.Icons.MaterialIconKind.ShieldOutline
        | AttributeGroup.Physical -> Material.Icons.MaterialIconKind.RunFast
        | AttributeGroup.Mental -> Material.Icons.MaterialIconKind.HeadCogOutline
        | AttributeGroup.Goalkeeping -> Material.Icons.MaterialIconKind.Glove

    let planIcon (plan: IndividualPlan) : Material.Icons.MaterialIconKind =
        match plan with
        | Develop _ -> Material.Icons.MaterialIconKind.TrendingUp
        | Perform -> Material.Icons.MaterialIconKind.PlayCircleOutline
        | Rest -> Material.Icons.MaterialIconKind.BatteryCharging

    let planColor (plan: IndividualPlan) : string =
        match plan with
        | Develop _ -> Theme.Accent
        | Perform -> Theme.AccentAlt
        | Rest -> Theme.Success

    let planDescription (plan: IndividualPlan) : string =
        match plan with
        | Develop _ -> "Grow a target area"
        | Perform -> "Stay sharp for matches"
        | Rest -> "Recover condition"

    let batteryColor (v: int) : string =
        if v >= 70 then Theme.Success
        elif v >= 45 then Theme.Warning
        else Theme.Danger

    let private unitOf (pos: Position) : int * string =
        match pos with
        | GK -> 0, "GOALKEEPERS"
        | DR
        | DC
        | DL
        | WBR
        | WBL -> 1, "DEFENDERS"
        | DM
        | MC
        | MR
        | ML
        | AMR
        | AMC
        | AML -> 2, "MIDFIELDERS"
        | ST -> 3, "ATTACKERS"

    let groupedByUnit (squad: Player list) : (string * Player list) list =
        squad
        |> List.groupBy (fun p -> unitOf p.Position)
        |> List.sortBy fst
        |> List.map (fun ((_, label), ps) -> label, ps |> List.sortByDescending _.CurrentSkill)

    let planChipText (p: Player) : string * string =
        match TrainingPlans.effectivePlan p with
        | None -> "Rehab", Theme.Warning
        | Some plan -> Training.planLabel plan, planColor plan

    type RowCtx =
        { Cfg: Core.Match.TrainingConfig
          Date: DateTime
          Tilt: AttributeGroup option
          Recovery: bool
          Ratings: TrainingPhaseEngine.CoachRatings
          Xi: Set<PlayerId> }

    let lineupIds (gs: GameState) : Set<PlayerId> =
        GameState.headCoach gs.UserClubId gs
        |> Option.bind (fun c -> c.Attributes.Coaching.Lineup)
        |> Option.map (fun lu -> lu.Slots |> List.choose _.PlayerId |> Set.ofList)
        |> Option.defaultValue Set.empty

    let congestedFixtures (gs: GameState) : int =
        let horizon = gs.CurrentDate.Date.AddDays(7.0)

        gs.Competitions
        |> Map.toSeq
        |> Seq.collect (fun (_, comp) -> comp.Fixtures |> Map.toSeq |> Seq.map snd)
        |> Seq.filter (fun f ->
            MatchFixture.involves gs.UserClubId f
            && MatchFixture.isPending f
            && f.ScheduledDate.Date >= gs.CurrentDate.Date
            && f.ScheduledDate.Date <= horizon)
        |> Seq.length


module Training =

    let private sectionLabel (text: string) =
        TextBlock.create
            [ TextBlock.text text
              TextBlock.fontSize 10.0
              TextBlock.fontWeight FontWeight.Black
              TextBlock.foreground Theme.TextDim
              TextBlock.margin (0.0, 0.0, 0.0, 8.0) ]

    let private miniBar (value: int) (color: string) (width: float) =
        ProgressBar.create
            [ ProgressBar.minimum 0.0
              ProgressBar.maximum 100.0
              ProgressBar.value (float value)
              ProgressBar.height 4.0
              ProgressBar.width width
              ProgressBar.cornerRadius 2.0
              ProgressBar.background Theme.BgElevated
              ProgressBar.foreground color
              ProgressBar.verticalAlignment VerticalAlignment.Center ]

    let private emphasisChip (opt: TrainingPresenter.EmphasisOption) (isSelected: bool) (onSelect: unit -> unit) =
        let color =
            if opt.Emphasis = EmRecovery then
                Theme.Success
            else
                Theme.Accent

        Border.create
            [ Border.background (
                  if isSelected then
                      Theme.withAlpha "18" color
                  else
                      Theme.BgCard
              )
              Border.borderBrush (if isSelected then color else Theme.Border)
              Border.borderThickness (if isSelected then 1.5 else 1.0)
              Border.cornerRadius 8.0
              Border.padding (12.0, 8.0)
              Border.margin (0.0, 0.0, 8.0, 0.0)
              Border.cursor FootballEngine.Components.UI.handCursor
              Border.onPointerReleased (fun _ -> onSelect ())
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 8.0
                        StackPanel.verticalAlignment VerticalAlignment.Center
                        StackPanel.children
                            [ Icons.iconSm opt.Icon (if isSelected then color else Theme.TextMuted)
                              StackPanel.create
                                  [ StackPanel.spacing 0.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text (Training.emphasisLabel opt.Emphasis)
                                                TextBlock.fontSize 11.0
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.foreground (if isSelected then color else Theme.TextMain) ]
                                          TextBlock.create
                                              [ TextBlock.text opt.Description
                                                TextBlock.fontSize 9.0
                                                TextBlock.foreground Theme.TextDim ] ] ] ] ]
              ) ]
        |> View.withKey $"emph-{opt.Emphasis}"
        :> IView

    let private emphasisStrip (gs: GameState) (squad: Player list) (dispatch: Msg -> unit) : IView =
        let nextText =
            match HomePresenter.getNextMatch gs with
            | None -> "No fixture scheduled"
            | Some m -> $"Next: {m.LocalName} vs {m.VisitName} · " + m.Date.ToString("dd MMM")

        let avgCond =
            if squad.IsEmpty then
                0
            else
                squad |> List.averageBy (fun p -> float p.Condition) |> int

        let avgFit =
            if squad.IsEmpty then
                0
            else
                squad |> List.averageBy (fun p -> float p.MatchFitness) |> int

        let tired = squad |> List.filter (fun p -> p.Condition < 65) |> List.length
        let injured = squad |> List.filter TrainingPlans.isRehab |> List.length
        let ratings = TrainingPhaseEngine.coachingOf gs gs.UserClubId

        let coachCount =
            GameState.getStaff gs.UserClubId gs
            |> List.filter Staff.isCoachingRole
            |> List.length

        let congestion = TrainingPresenter.congestedFixtures gs

        let xi = TrainingPresenter.lineupIds gs

        let xiFresh =
            squad
            |> List.filter (fun p -> xi.Contains p.Id)
            |> function
                | [] -> None
                | starters -> Some(starters |> List.averageBy (fun p -> float p.Condition) |> int)

        let congestionBanner =
            if congestion >= 2 then
                Border.create
                    [ Border.background (Theme.withAlpha "15" Theme.Warning)
                      Border.borderBrush (Theme.withAlpha "40" Theme.Warning)
                      Border.borderThickness 1.0
                      Border.cornerRadius 8.0
                      Border.padding (12.0, 8.0)
                      Border.child (
                          TextBlock.create
                              [ TextBlock.text
                                    $"Congested week: {congestion} matches in 7 days — consider Recovery or resting starters"
                                TextBlock.fontSize 11.0
                                TextBlock.fontWeight FontWeight.SemiBold
                                TextBlock.foreground Theme.Warning ]
                      ) ]
                :> IView
            else
                Border.create [ Border.height 0.0 ] :> IView

        Border.create
            [ Border.background Theme.BgSidebar
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.padding (20.0, 12.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 10.0
                        StackPanel.children
                            [ StackPanel.create
                                  [ StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 12.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text (
                                                    (Localization.resolve TranslationKey.TrainingEmphasis [])
                                                        .ToUpperInvariant()
                                                )
                                                TextBlock.fontSize 14.0
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.foreground Theme.TextMain
                                                TextBlock.verticalAlignment VerticalAlignment.Center ]
                                          TextBlock.create
                                              [ TextBlock.text nextText
                                                TextBlock.fontSize 11.0
                                                TextBlock.foreground Theme.TextDim
                                                TextBlock.verticalAlignment VerticalAlignment.Center ]
                                          TextBlock.create
                                              [ TextBlock.text
                                                    $"COND {avgCond} · FIT {avgFit} · {tired} tired · {injured} out"
                                                TextBlock.fontSize 11.0
                                                TextBlock.fontWeight FontWeight.SemiBold
                                                TextBlock.foreground Theme.TextMuted
                                                TextBlock.verticalAlignment VerticalAlignment.Center ]
                                          TextBlock.create
                                              [ TextBlock.text
                                                    $"Staff: {coachCount} coaches · Youth {ratings.Youngsters} · Fitness {ratings.Fitness} · Physio {ratings.Physio}"
                                                TextBlock.fontSize 11.0
                                                TextBlock.foreground Theme.TextDim
                                                TextBlock.verticalAlignment VerticalAlignment.Center ]
                                          if xiFresh.IsSome then
                                              TextBlock.create
                                                  [ TextBlock.text $"XI freshness {xiFresh.Value}"
                                                    TextBlock.fontSize 11.0
                                                    TextBlock.fontWeight FontWeight.SemiBold
                                                    TextBlock.foreground (TrainingPresenter.batteryColor xiFresh.Value)
                                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
                              congestionBanner
                              ScrollViewer.create
                                  [ ScrollViewer.horizontalScrollBarVisibility ScrollBarVisibility.Auto
                                    ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Disabled
                                    ScrollViewer.content (
                                        StackPanel.create
                                            [ StackPanel.orientation Orientation.Horizontal
                                              StackPanel.children
                                                  [ for opt in TrainingPresenter.emphasisOptions do
                                                        emphasisChip opt (gs.TeamEmphasis = opt.Emphasis) (fun () ->
                                                            dispatch (
                                                                TrainingMsg(TrainingMsg.SetTeamEmphasis opt.Emphasis)
                                                            )) ] ]
                                    ) ] ] ]
              ) ]
        :> IView

    let private playerRow
        (ctx: TrainingPresenter.RowCtx)
        (p: Player)
        (isSelected: bool)
        (onClick: unit -> unit)
        : IView =
        let chipText, chipColor = TrainingPresenter.planChipText p
        let condColor = TrainingPresenter.batteryColor p.Condition
        let isXi = ctx.Xi.Contains p.Id

        let gap = max 0 (p.PotentialSkill - p.CurrentSkill)

        let risk =
            (TrainingPhaseEngine.previewWith ctx.Cfg ctx.Date ctx.Tilt ctx.Recovery ctx.Ratings p).InjuryPct

        Border.create
            [ Border.background (
                  if isSelected then
                      SelectionStyle.rowBg true
                  else
                      "transparent"
              )
              Border.borderBrush (if isSelected then Theme.Accent else "transparent")
              Border.borderThickness (3.0, 0.0, 0.0, 0.0)
              Border.padding (14.0, 9.0)
              Border.cursor FootballEngine.Components.UI.handCursor
              Border.onPointerReleased (fun _ -> onClick ())
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions "Auto, *, Auto, Auto, Auto"
                        Grid.children
                            [ UI.positionBadge p.Position
                              |> fun b ->
                                  Border.create
                                      [ Grid.column 0
                                        Border.margin (0.0, 0.0, 12.0, 0.0)
                                        Border.verticalAlignment VerticalAlignment.Center
                                        Border.child b ]
                              StackPanel.create
                                  [ Grid.column 1
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.spacing 2.0
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text p.Name
                                                TextBlock.foreground Theme.TextMain
                                                TextBlock.fontSize 13.0
                                                TextBlock.fontWeight FontWeight.SemiBold ]
                                          StackPanel.create
                                              [ StackPanel.orientation Orientation.Horizontal
                                                StackPanel.spacing 6.0
                                                StackPanel.children
                                                    [ if isXi then
                                                          Border.create
                                                              [ Border.background (Theme.withAlpha "18" Theme.AccentAlt)
                                                                Border.cornerRadius 4.0
                                                                Border.padding (6.0, 1.0)
                                                                Border.child (
                                                                    TextBlock.create
                                                                        [ TextBlock.text "XI"
                                                                          TextBlock.fontSize 9.0
                                                                          TextBlock.fontWeight FontWeight.Black
                                                                          TextBlock.foreground Theme.AccentAlt ]
                                                                ) ]
                                                      TextBlock.create
                                                          [ TextBlock.text $"Age {Player.age ctx.Date p}"
                                                            TextBlock.fontSize 10.0
                                                            TextBlock.foreground Theme.TextMuted ]
                                                      Border.create
                                                          [ Border.background (Theme.withAlpha "15" chipColor)
                                                            Border.cornerRadius 4.0
                                                            Border.padding (6.0, 1.0)
                                                            Border.child (
                                                                TextBlock.create
                                                                    [ TextBlock.text chipText
                                                                      TextBlock.fontSize 9.0
                                                                      TextBlock.fontWeight FontWeight.Bold
                                                                      TextBlock.foreground chipColor ]
                                                            ) ]
                                                      if risk > 2.0 then
                                                          TextBlock.create
                                                              [ TextBlock.text (sprintf "INJ %.1f%%" risk)
                                                                TextBlock.fontSize 9.0
                                                                TextBlock.fontWeight FontWeight.Bold
                                                                TextBlock.foreground Theme.Danger ] ] ] ] ]
                              StackPanel.create
                                  [ Grid.column 2
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.spacing 3.0
                                    StackPanel.margin (12.0, 0.0, 0.0, 0.0)
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text $"COND {p.Condition}"
                                                TextBlock.fontSize 9.0
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.foreground condColor
                                                TextBlock.horizontalAlignment HorizontalAlignment.Right ]
                                          miniBar p.Condition condColor 90.0 ] ]
                              TextBlock.create
                                  [ Grid.column 3
                                    TextBlock.text (sprintf "%d%%" p.MatchFitness)
                                    TextBlock.fontSize 11.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground (TrainingPresenter.batteryColor p.MatchFitness)
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                    TextBlock.margin (12.0, 0.0, 0.0, 0.0) ]
                              TextBlock.create
                                  [ Grid.column 4
                                    TextBlock.text (if gap = 0 then "MAX" else $"+{gap}")
                                    TextBlock.fontSize 11.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground (if gap = 0 then Theme.TextDim else Theme.Accent)
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                    TextBlock.margin (12.0, 0.0, 0.0, 0.0) ] ] ]
              ) ]
        |> View.withKey $"tr-{p.Id}"
        :> IView

    let private groupHeader (label: string) =
        Grid.create
            [ Grid.columnDefinitions "auto, *"
              Grid.margin (14.0, 10.0, 14.0, 4.0)
              Grid.children
                  [ Border.create
                        [ Grid.column 0
                          Border.background Theme.Accent
                          Border.cornerRadius 3.0
                          Border.width 3.0
                          Border.margin (0.0, 0.0, 8.0, 0.0) ]
                    TextBlock.create
                        [ Grid.column 1
                          TextBlock.text label
                          TextBlock.fontSize 10.0
                          TextBlock.fontWeight FontWeight.Black
                          TextBlock.foreground Theme.Accent
                          TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
        :> IView

    let private planButton (plan: IndividualPlan) (isSelected: bool) (isRehab: bool) (onSelect: unit -> unit) : IView =
        let color = TrainingPresenter.planColor plan

        Border.create
            [ Border.background (
                  if isSelected then
                      Theme.withAlpha "15" color
                  else
                      Theme.BgCard
              )
              Border.borderBrush (if isSelected then color else Theme.Border)
              Border.borderThickness (if isSelected then 1.5 else 1.0)
              Border.cornerRadius 10.0
              Border.padding (12.0, 10.0)
              Border.cursor FootballEngine.Components.UI.handCursor
              Border.opacity (if isRehab then 0.85 else 1.0)
              Border.onPointerReleased (fun _ -> onSelect ())
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 10.0
                        StackPanel.verticalAlignment VerticalAlignment.Center
                        StackPanel.children
                            [ Border.create
                                  [ Border.background (Theme.withAlpha "18" color)
                                    Border.cornerRadius 8.0
                                    Border.padding 8.0
                                    Border.child (Icons.iconSm (TrainingPresenter.planIcon plan) color) ]
                              StackPanel.create
                                  [ StackPanel.spacing 1.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text (Training.planLabel plan)
                                                TextBlock.fontSize 12.0
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.foreground (if isSelected then color else Theme.TextMain) ]
                                          TextBlock.create
                                              [ TextBlock.text (TrainingPresenter.planDescription plan)
                                                TextBlock.fontSize 10.0
                                                TextBlock.foreground Theme.TextMuted ] ] ] ] ]
              ) ]
        |> View.withKey $"plan-{plan}"
        :> IView

    let private groupChip (g: AttributeGroup) (isSelected: bool) (onSelect: unit -> unit) =
        let color = Theme.Accent

        Border.create
            [ Border.background (
                  if isSelected then
                      Theme.withAlpha "18" color
                  else
                      Theme.BgCard
              )
              Border.borderBrush (if isSelected then color else Theme.Border)
              Border.borderThickness (if isSelected then 1.5 else 1.0)
              Border.cornerRadius 8.0
              Border.padding (10.0, 7.0)
              Border.margin (0.0, 0.0, 6.0, 6.0)
              Border.cursor FootballEngine.Components.UI.handCursor
              Border.onPointerReleased (fun _ -> onSelect ())
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 6.0
                        StackPanel.verticalAlignment VerticalAlignment.Center
                        StackPanel.children
                            [ Icons.iconSm
                                  (TrainingPresenter.groupIcon g)
                                  (if isSelected then color else Theme.TextMuted)
                              TextBlock.create
                                  [ TextBlock.text (Training.groupLabel g)
                                    TextBlock.fontSize 11.0
                                    TextBlock.fontWeight FontWeight.SemiBold
                                    TextBlock.foreground (if isSelected then color else Theme.TextMain) ] ] ]
              ) ]
        |> View.withKey $"grp-{g}"
        :> IView

    let private batteryRow (label: string) (value: int) =
        let color = TrainingPresenter.batteryColor value

        StackPanel.create
            [ StackPanel.spacing 4.0
              StackPanel.margin (0.0, 0.0, 0.0, 10.0)
              StackPanel.children
                  [ StackPanel.create
                        [ StackPanel.orientation Orientation.Horizontal
                          StackPanel.children
                              [ TextBlock.create
                                    [ TextBlock.text label
                                      TextBlock.fontSize 10.0
                                      TextBlock.fontWeight FontWeight.Bold
                                      TextBlock.foreground Theme.TextMuted ]
                                TextBlock.create
                                    [ TextBlock.text $"  {value}"
                                      TextBlock.fontSize 12.0
                                      TextBlock.fontWeight FontWeight.Black
                                      TextBlock.foreground color ] ] ]
                    ProgressBar.create
                        [ ProgressBar.minimum 0.0
                          ProgressBar.maximum 100.0
                          ProgressBar.value (float value)
                          ProgressBar.height 6.0
                          ProgressBar.cornerRadius 3.0
                          ProgressBar.background Theme.BgElevated
                          ProgressBar.foreground color ] ] ]
        :> IView

    let private previewStat (label: string) (value: string) (color: string) =
        StackPanel.create
            [ StackPanel.spacing 2.0
              StackPanel.horizontalAlignment HorizontalAlignment.Center
              StackPanel.children
                  [ TextBlock.create
                        [ TextBlock.text value
                          TextBlock.fontSize 15.0
                          TextBlock.fontWeight FontWeight.Black
                          TextBlock.foreground color
                          TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                    TextBlock.create
                        [ TextBlock.text label
                          TextBlock.fontSize 9.0
                          TextBlock.fontWeight FontWeight.Bold
                          TextBlock.foreground Theme.TextDim
                          TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
        :> IView

    let private detailPanel
        (gs: GameState)
        (cfg: Core.Match.TrainingConfig)
        (player: Player)
        (dispatch: Msg -> unit)
        : IView =
        let rehab = TrainingPlans.isRehab player
        let storedPlan = player.TrainingPlan
        let tilt = Training.tiltOf gs.TeamEmphasis
        let recoveryWeek = gs.TeamEmphasis = EmRecovery
        let ratings = TrainingPhaseEngine.coachingOf gs gs.UserClubId

        let prev =
            TrainingPhaseEngine.previewWith cfg gs.CurrentDate tilt recoveryWeek ratings player


        let rehabBanner =
            match player.Status with
            | Injured(sev, until) ->
                let days = max 0 (int (until - gs.CurrentDate).TotalDays)

                Border.create
                    [ Border.background (Theme.withAlpha "15" Theme.Warning)
                      Border.borderBrush (Theme.withAlpha "40" Theme.Warning)
                      Border.borderThickness 1.0
                      Border.cornerRadius 8.0
                      Border.padding (12.0, 10.0)
                      Border.margin (0.0, 0.0, 0.0, 12.0)
                      Border.child (
                          StackPanel.create
                              [ StackPanel.orientation Orientation.Horizontal
                                StackPanel.spacing 10.0
                                StackPanel.verticalAlignment VerticalAlignment.Center
                                StackPanel.children
                                    [ Icons.iconSm IconName.ambulance Theme.Warning
                                      StackPanel.create
                                          [ StackPanel.spacing 1.0
                                            StackPanel.children
                                                [ TextBlock.create
                                                      [ TextBlock.text $"REHAB · {sev} · ~{days} days"
                                                        TextBlock.fontSize 11.0
                                                        TextBlock.fontWeight FontWeight.Bold
                                                        TextBlock.foreground Theme.Warning ]
                                                  TextBlock.create
                                                      [ TextBlock.text "Plan resumes on return"
                                                        TextBlock.fontSize 10.0
                                                        TextBlock.foreground Theme.TextMuted ] ] ] ] ]
                      ) ]
                :> IView
            | _ -> Border.create [ Border.height 0.0 ] :> IView

        let targetChips =
            match storedPlan with
            | Develop _ ->
                StackPanel.create
                    [ StackPanel.spacing 0.0
                      StackPanel.margin (0.0, 12.0, 0.0, 0.0)
                      StackPanel.children
                          [ sectionLabel "TARGET AREA"
                            WrapPanel.create
                                [ WrapPanel.children
                                      [ for g in TrainingPlans.groupsFor player.Position do
                                            groupChip g (storedPlan = Develop g) (fun () ->
                                                dispatch (TrainingMsg(TrainingMsg.SetPlan(player.Id, Develop g)))) ] ] ] ]
                :> IView
            | _ -> Border.create [ Border.height 0.0 ] :> IView

        ScrollViewer.create
            [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
              ScrollViewer.content (
                  StackPanel.create
                      [ StackPanel.margin (20.0, 18.0)
                        StackPanel.spacing 0.0
                        StackPanel.children
                            [ TextBlock.create
                                  [ TextBlock.text player.Name
                                    TextBlock.fontSize 17.0
                                    TextBlock.fontWeight FontWeight.Black
                                    TextBlock.foreground Theme.TextMain ]
                              TextBlock.create
                                  [ TextBlock.text
                                        $"%A{player.Position} · Age {Player.age gs.CurrentDate player} · CA {player.CurrentSkill} → PA {player.PotentialSkill}"
                                    TextBlock.fontSize 11.0
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.margin (0.0, 2.0, 0.0, 14.0) ]
                              rehabBanner
                              batteryRow "CONDITION" player.Condition
                              batteryRow "SHARPNESS" player.MatchFitness
                              batteryRow
                                  "POTENTIAL"
                                  (if player.PotentialSkill = 0 then
                                       0
                                   else
                                       player.CurrentSkill * 100 / player.PotentialSkill)
                              sectionLabel "WEEKLY PLAN"
                              Grid.create
                                  [ Grid.columnDefinitions "*, *"
                                    Grid.rowDefinitions "Auto, Auto"
                                    Grid.children
                                        [ Border.create
                                              [ Grid.column 0
                                                Grid.row 0
                                                Border.margin (0.0, 0.0, 6.0, 6.0)
                                                Border.child (
                                                    planButton
                                                        (Develop(Physical))
                                                        (match storedPlan with
                                                         | Develop _ -> true
                                                         | _ -> false)
                                                        rehab
                                                        (fun () ->
                                                            let g =
                                                                match storedPlan with
                                                                | Develop g -> g
                                                                | _ ->
                                                                    TrainingPlans.groupsFor player.Position
                                                                    |> List.tryHead
                                                                    |> Option.defaultValue Physical

                                                            dispatch (
                                                                TrainingMsg(TrainingMsg.SetPlan(player.Id, Develop g))
                                                            ))
                                                ) ]
                                          Border.create
                                              [ Grid.column 1
                                                Grid.row 0
                                                Border.margin (6.0, 0.0, 0.0, 6.0)
                                                Border.child (
                                                    planButton Perform (storedPlan = Perform) rehab (fun () ->
                                                        dispatch (TrainingMsg(TrainingMsg.SetPlan(player.Id, Perform))))
                                                ) ]
                                          Border.create
                                              [ Grid.column 0
                                                Grid.row 1
                                                Grid.columnSpan 2
                                                Border.child (
                                                    planButton Rest (storedPlan = Rest) rehab (fun () ->
                                                        dispatch (TrainingMsg(TrainingMsg.SetPlan(player.Id, Rest))))
                                                ) ] ] ]
                              targetChips
                              Border.create
                                  [ Border.background Theme.BgCard
                                    Border.borderBrush Theme.Border
                                    Border.borderThickness 1.0
                                    Border.cornerRadius 10.0
                                    Border.padding (14.0, 12.0)
                                    Border.margin (0.0, 14.0, 0.0, 0.0)
                                    Border.child (
                                        StackPanel.create
                                            [ StackPanel.spacing 10.0
                                              StackPanel.children
                                                  [ TextBlock.create
                                                        [ TextBlock.text (
                                                              (Localization.resolve TranslationKey.TrainingThisWeek [])
                                                                  .ToUpperInvariant()
                                                          )
                                                          TextBlock.fontSize 12.0
                                                          TextBlock.fontWeight FontWeight.Black
                                                          TextBlock.foreground Theme.TextDim ]
                                                    Grid.create
                                                        [ Grid.columnDefinitions "*, *, *, *"
                                                          Grid.children
                                                              [ Border.create
                                                                    [ Grid.column 0
                                                                      Border.child (
                                                                          previewStat
                                                                              "GROWTH"
                                                                              (sprintf "%.0f%%" prev.GrowthPct)
                                                                              Theme.Accent
                                                                      ) ]
                                                                Border.create
                                                                    [ Grid.column 1
                                                                      Border.child (
                                                                          previewStat
                                                                              "TARGET"
                                                                              prev.Target
                                                                              Theme.TextMain
                                                                      ) ]
                                                                Border.create
                                                                    [ Grid.column 2
                                                                      Border.child (
                                                                          previewStat
                                                                              "COND"
                                                                              (if prev.ConditionDelta >= 0 then
                                                                                   $"+{prev.ConditionDelta}"
                                                                               else
                                                                                   string prev.ConditionDelta)
                                                                              (TrainingPresenter.batteryColor (
                                                                                  max
                                                                                      0
                                                                                      (min
                                                                                          100
                                                                                          (player.Condition
                                                                                           + prev.ConditionDelta))
                                                                              ))
                                                                      ) ]
                                                                Border.create
                                                                    [ Grid.column 3
                                                                      Border.child (
                                                                          previewStat
                                                                              "INJURY"
                                                                              (sprintf "%.1f%%" prev.InjuryPct)
                                                                              (if prev.InjuryPct > 2.0 then
                                                                                   Theme.Danger
                                                                               else
                                                                                   Theme.TextMuted)
                                                                      ) ] ] ] ] ]
                                    ) ] ] ]
              ) ]
        :> IView

    let private bulkBar (dispatch: Msg -> unit) =
        let bulkButton (label: string) (rule: TrainingBulk) =
            Button.create
                [ Button.content label
                  Button.fontSize 10.0
                  Button.padding (10.0, 6.0)
                  Button.margin (0.0, 0.0, 6.0, 0.0)
                  Button.background Theme.BgCard
                  Button.borderBrush Theme.Border
                  Button.borderThickness 1.0
                  Button.cornerRadius 6.0
                  Button.foreground Theme.TextSub
                  Button.cursor FootballEngine.Components.UI.handCursor
                  Button.onClick (fun _ -> dispatch (TrainingMsg(TrainingMsg.BulkApply rule))) ]
            :> IView

        StackPanel.create
            [ StackPanel.orientation Orientation.Horizontal
              StackPanel.margin (14.0, 12.0, 14.0, 4.0)
              StackPanel.children
                  [ bulkButton "U21 → Develop" U21Develop
                    bulkButton "Tired → Rest" RestTired
                    bulkButton "XI → Rest" RestStarters
                    bulkButton "All → Perform" AllPerform ] ]
        :> IView

    let trainingView (state: State) (dispatch: Msg -> unit) : IView =
        match state.Mode with
        | InGame(gs, _) ->
            let cfg =
                (FootballEngine.Core.Match.WeightMergeSettings.mergeWithOverridesSave (Some state.SaveSettings))
                    .Training

            let squad =
                GameState.getSquad gs.UserClubId gs
                |> List.sortBy (fun p -> PlayerPresenter.positionSortKey p.Position, -p.CurrentSkill)

            let groups = TrainingPresenter.groupedByUnit squad

            let rowCtx: TrainingPresenter.RowCtx =
                { Cfg = cfg
                  Date = gs.CurrentDate
                  Tilt = Training.tiltOf gs.TeamEmphasis
                  Recovery = gs.TeamEmphasis = EmRecovery
                  Ratings = TrainingPhaseEngine.coachingOf gs gs.UserClubId
                  Xi = TrainingPresenter.lineupIds gs }

            Grid.create
                [ Grid.rowDefinitions "Auto, *"
                  Grid.background Theme.BgMain
                  Grid.children
                      [ Border.create [ Grid.row 0; Border.child (emphasisStrip gs squad dispatch) ]
                        Grid.create
                            [ Grid.row 1
                              Grid.columnDefinitions "*, 400"
                              Grid.children
                                  [ Border.create
                                        [ Grid.column 0
                                          Border.borderBrush Theme.Border
                                          Border.borderThickness (0.0, 0.0, 1.0, 0.0)
                                          Border.child (
                                              Grid.create
                                                  [ Grid.rowDefinitions "Auto, *"
                                                    Grid.children
                                                        [ Border.create [ Grid.row 0; Border.child (bulkBar dispatch) ]
                                                          ScrollViewer.create
                                                              [ Grid.row 1
                                                                ScrollViewer.verticalScrollBarVisibility
                                                                    ScrollBarVisibility.Auto
                                                                ScrollViewer.content (
                                                                    StackPanel.create
                                                                        [ StackPanel.spacing 0.0
                                                                          StackPanel.children
                                                                              [ for label, players in groups do
                                                                                    groupHeader label

                                                                                    for p in players do
                                                                                        playerRow
                                                                                            rowCtx
                                                                                            p
                                                                                            (state.Training.SelectedPlayer = Some
                                                                                                p.Id)
                                                                                            (fun () ->
                                                                                                dispatch (
                                                                                                    TrainingMsg(
                                                                                                        TrainingMsg.SelectPlayer
                                                                                                            p.Id
                                                                                                    )
                                                                                                )) ] ]
                                                                ) ] ] ]
                                          ) ]
                                    Border.create
                                        [ Grid.column 1
                                          Border.background Theme.BgSidebar
                                          Border.borderBrush Theme.Border
                                          Border.borderThickness (1.0, 0.0, 0.0, 0.0)
                                          Border.child (
                                              match
                                                  state.Training.SelectedPlayer
                                                  |> Option.bind (fun id -> gs.Players |> Map.tryFind id)
                                              with
                                              | Some player -> detailPanel gs cfg player dispatch
                                              | None ->
                                                  UI.emptyState
                                                      IconName.squad
                                                      "Select a player"
                                                      "Pick a player to set their weekly plan"
                                          ) ] ] ] ] ]
            |> View.withKey $"training-{state.Training.SelectedPlayer}-{gs.TeamEmphasis}"
            :> IView
        | _ -> Border.create [] :> IView
