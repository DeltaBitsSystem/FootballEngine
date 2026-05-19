namespace FootballEngine.Pages

open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open FootballEngine
open FootballEngine.AppMsgs
open FootballEngine.AppTypes
open FootballEngine.Components
open FootballEngine.Domain
open FootballEngine.Domain.TransferNegotiation
open FootballEngine.Icons

module Transfers =

    let private pageSize = 100
    let roundToNearest (step: decimal) (v: decimal) = System.Math.Round(v / step) * step

    let private positionLabel (f: TransferFilter) =
        match f with
        | AllPositions -> "All"
        | Goalkeepers -> "GK"
        | Defenders -> "DEF"
        | Midfielders -> "MID"
        | Attackers -> "ATT"

    let private tabLabel (t: TransferTab) =
        match t with
        | MarketSearch -> "Market"
        | MyWatchlist -> "Watchlist"
        | IncomingOffers -> "Incoming"
        | OutgoingOffers -> "Outgoing"
        | TransferHistory -> "History"

    let private tabIcon (t: TransferTab) =
        match t with
        | MarketSearch -> IconName.search
        | MyWatchlist -> PlayerIcon.skill
        | IncomingOffers -> IconName.add
        | OutgoingOffers -> ClubIcon.transfer
        | TransferHistory -> PlayerIcon.contract

    let private playerAvatar (name: string) (pos: Position) (size: float) =
        let color = Theme.positionColor pos

        Border.create
            [ Border.width size
              Border.height size
              Border.cornerRadius (size / 2.0)
              Border.background (color + "22")
              Border.borderBrush (color + "66")
              Border.borderThickness 1.5
              Border.child (
                  TextBlock.create
                      [ TextBlock.text (string name[0])
                        TextBlock.fontSize (size * 0.4)
                        TextBlock.fontWeight FontWeight.Black
                        TextBlock.foreground color
                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                        TextBlock.verticalAlignment VerticalAlignment.Center ]
              ) ]

    let private caBar (ca: int) =
        let pct = float ca / 200.0

        let color =
            if ca >= 140 then Theme.Accent
            elif ca >= 110 then Theme.AccentAlt
            elif ca >= 85 then Theme.Warning
            else Theme.TextMuted

        Border.create
            [ Border.cornerRadius 3.0
              Border.background Theme.BgSidebar
              Border.height 4.0
              Border.child (
                  Border.create
                      [ Border.cornerRadius 3.0
                        Border.background color
                        Border.width (pct * 64.0)
                        Border.height 4.0
                        Border.horizontalAlignment HorizontalAlignment.Left ]
              ) ]

    let private playerRow
        (p: Player)
        (clubName: string)
        (isSelected: bool)
        (isWatched: bool)
        (onSelect: unit -> unit)
        (onWatch: unit -> unit)
        =
        let posColor = Theme.positionColor p.Position

        Button.create
            [ Button.horizontalAlignment HorizontalAlignment.Stretch
              Button.padding (16.0, 10.0)
              Button.background (SelectionStyle.rowBg isSelected)
              Button.borderThickness (Avalonia.Thickness(0.0, 0.0, 0.0, 1.0))
              Button.borderBrush Theme.Border
              Button.onClick (fun _ -> onSelect ())
              Button.content (
                  Grid.create
                      [ Grid.columnDefinitions (ColumnDefs.toGridString ColumnDefs.transferListCols)
                        Grid.children
                            [ Border.create
                                  [ Grid.column 0
                                    Border.margin (0.0, 0.0, 12.0, 0.0)
                                    Border.verticalAlignment VerticalAlignment.Center
                                    Border.child (playerAvatar p.Name p.Position 36.0) ]
                              StackPanel.create
                                  [ Grid.column 1
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.spacing 2.0
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text p.Name
                                                TextBlock.fontWeight FontWeight.SemiBold
                                                TextBlock.fontSize 13.0
                                                TextBlock.foreground Theme.TextMain ]
                                          TextBlock.create
                                              [ TextBlock.text clubName
                                                TextBlock.fontSize 11.0
                                                TextBlock.foreground Theme.TextMuted ] ] ]
                              Border.create
                                  [ Grid.column 2
                                    Border.margin (0.0, 0.0, 16.0, 0.0)
                                    Border.background (posColor + "22")
                                    Border.borderBrush (posColor + "55")
                                    Border.borderThickness 1.0
                                    Border.cornerRadius 4.0
                                    Border.padding (6.0, 3.0)
                                    Border.verticalAlignment VerticalAlignment.Center
                                    Border.child (
                                        TextBlock.create
                                            [ TextBlock.text $"%A{p.Position}"
                                              TextBlock.fontSize 10.0
                                              TextBlock.fontWeight FontWeight.Black
                                              TextBlock.foreground posColor ]
                                    ) ]
                              StackPanel.create
                                  [ Grid.column 3
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.spacing 2.0
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text (string p.CurrentSkill)
                                                TextBlock.fontSize 13.0
                                                TextBlock.fontWeight FontWeight.Black
                                                TextBlock.foreground Theme.TextMain
                                                TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                                          caBar p.CurrentSkill ] ]
                              TextBlock.create
                                  [ Grid.column 4
                                    TextBlock.text (Formatters.money (Player.playerValue p.CurrentSkill))
                                    TextBlock.fontSize 12.0
                                    TextBlock.fontWeight FontWeight.SemiBold
                                    TextBlock.foreground Theme.TextSub
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                    TextBlock.horizontalAlignment HorizontalAlignment.Right ]
                              Button.create
                                  [ Grid.column 5
                                    Button.margin (8.0, 0.0, 0.0, 0.0)
                                    Button.padding (6.0, 4.0)
                                    Button.cornerRadius 6.0
                                    Button.background (if isWatched then Theme.Warning + "22" else "Transparent")
                                    Button.borderBrush (if isWatched then Theme.Warning + "66" else Theme.Border)
                                    Button.borderThickness 1.0
                                    Button.verticalAlignment VerticalAlignment.Center
                                    Button.onClick (fun e ->
                                        e.Handled <- true
                                        onWatch ())
                                    Button.content (
                                        Icons.iconSm
                                            PlayerIcon.skill
                                            (if isWatched then Theme.Warning else Theme.TextMuted)
                                    ) ] ] ]
              ) ]

    let private statBar (label: string) (value: int) (color: string) =
        Grid.create
            [ Grid.columnDefinitions "90, *, 30"
              Grid.margin (0.0, 3.0)
              Grid.children
                  [ TextBlock.create
                        [ Grid.column 0
                          TextBlock.text label
                          TextBlock.fontSize 11.0
                          TextBlock.foreground Theme.TextMuted
                          TextBlock.verticalAlignment VerticalAlignment.Center ]
                    Border.create
                        [ Grid.column 1
                          Border.cornerRadius 3.0
                          Border.background Theme.BgSidebar
                          Border.height 6.0
                          Border.verticalAlignment VerticalAlignment.Center
                          Border.margin (0.0, 0.0, 8.0, 0.0)
                          Border.child (
                              Border.create
                                  [ Border.cornerRadius 3.0
                                    Border.background color
                                    Border.width (float value / 20.0 * 120.0)
                                    Border.height 6.0
                                    Border.horizontalAlignment HorizontalAlignment.Left ]
                          ) ]
                    TextBlock.create
                        [ Grid.column 2
                          TextBlock.text (string value)
                          TextBlock.fontSize 11.0
                          TextBlock.fontWeight FontWeight.Bold
                          TextBlock.foreground Theme.TextMain
                          TextBlock.horizontalAlignment HorizontalAlignment.Right
                          TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]

    let private statGroup (title: string) (stats: (string * int) list) (color: string) =
        StackPanel.create
            [ StackPanel.spacing 4.0
              StackPanel.margin (0.0, 0.0, 0.0, 14.0)
              StackPanel.children
                  [ TextBlock.create
                        [ TextBlock.text (title.ToUpperInvariant())
                          TextBlock.fontSize 9.0
                          TextBlock.fontWeight FontWeight.Bold
                          TextBlock.foreground Theme.TextMuted
                          TextBlock.lineSpacing 1.5
                          TextBlock.margin (0.0, 0.0, 0.0, 4.0) ]
                    for label, value in stats do
                        statBar label value color ] ]

    let private sectionLabel (text: string) =
        TextBlock.create
            [ TextBlock.text (text.ToUpperInvariant())
              TextBlock.fontSize 9.0
              TextBlock.fontWeight FontWeight.Bold
              TextBlock.foreground Theme.TextMuted
              TextBlock.lineSpacing 1.5
              TextBlock.margin (0.0, 0.0, 0.0, 6.0) ]

    let private infoRow (label: string) (value: string) (valueColor: string) =
        Grid.create
            [ Grid.columnDefinitions "*, Auto"
              Grid.margin (0.0, 4.0)
              Grid.children
                  [ TextBlock.create
                        [ Grid.column 0
                          TextBlock.text label
                          TextBlock.fontSize 12.0
                          TextBlock.foreground Theme.TextMuted
                          TextBlock.verticalAlignment VerticalAlignment.Center ]
                    TextBlock.create
                        [ Grid.column 1
                          TextBlock.text value
                          TextBlock.fontSize 12.0
                          TextBlock.fontWeight FontWeight.SemiBold
                          TextBlock.foreground valueColor
                          TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]

    let private negotiationPanel (p: Player) (buyer: Club) (neg: Negotiation) (dispatch: Msg -> unit) =
        let minFee = Player.playerValue p.CurrentSkill * 0.5m
        let maxFee = Player.playerValue p.CurrentSkill * 2.0m

        let stepContent =
            match neg.Stage with
            | NegotiationStage.OfferMade fee ->
                let canAffordIt = canAfford buyer fee (suggestedSalary p)

                StackPanel.create
                    [ StackPanel.spacing 12.0
                      StackPanel.children
                          [ infoRow "Offered Fee" (Formatters.money fee) Theme.Accent
                            infoRow "Market Value" (Formatters.money (Player.playerValue p.CurrentSkill)) Theme.TextSub
                            infoRow "Your Budget" (Formatters.money buyer.Budget) Theme.Accent
                            Border.create
                                [ Border.height 1.0; Border.background Theme.Border; Border.margin (0.0, 4.0) ]
                            if not canAffordIt then
                                UI.statusBanner Theme.Danger IconName.warning "Insufficient budget — offer may fail"
                            UI.statusBanner Theme.Warning IconName.hourglass "Waiting for club response..."
                            Grid.create
                                [ Grid.columnDefinitions "*, Auto"
                                  Grid.margin 8.0
                                  Grid.children
                                      [ UI.ghostButton "Withdraw" None (fun _ -> dispatch (TransferMsg(WithdrawOffer neg.Id))) ] ] ] ]
                :> IView

            | NegotiationStage.RejectedByClub(_, reason) ->
                StackPanel.create
                    [ StackPanel.spacing 12.0
                      StackPanel.children
                          [ UI.statusBanner Theme.Danger IconName.error reason
                            infoRow "Market Value" (Formatters.money (Player.playerValue p.CurrentSkill)) Theme.TextSub
                            Grid.create
                                [ Grid.columnDefinitions "*, Auto"
                                  Grid.margin 8.0
                                  Grid.children
                                       [ UI.ghostButton "Give Up" None (fun _ -> dispatch (TransferMsg (ClearNegotiation neg.Id)))
                                         Border.create
                                            [ Grid.column 1
                                              Border.child (
                                                  UI.primaryButton "Counter Offer" (Some IconName.add) (fun _ ->
                                                      let higherFee = Player.playerValue p.CurrentSkill * 1.15m
                                                      dispatch (TransferMsg(CounterOffer(neg.Id, higherFee, None)))
                                                  )
                                              ) ] ] ] ] ]
                :> IView

            | NegotiationStage.CounterReceived(fee, counter) ->
                StackPanel.create
                    [ StackPanel.spacing 12.0
                      StackPanel.children
                          [ UI.statusBanner Theme.Accent IconName.info $"Club counter-offered: {Formatters.money counter}"
                            infoRow "Your Fee" (Formatters.money fee) Theme.TextSub
                            infoRow "Counter" (Formatters.money counter) Theme.AccentAlt
                            Grid.create
                                [ Grid.columnDefinitions "*, Auto"
                                  Grid.margin 8.0
                                  Grid.children
                                       [ UI.ghostButton "Reject" None (fun _ -> dispatch (TransferMsg (ClearNegotiation neg.Id)))
                                         Border.create
                                            [ Grid.column 1
                                              Border.child (
                                                  UI.primaryButton "Accept Counter" (Some IconName.success) (fun _ ->
                                                      dispatch (TransferMsg(CounterOffer(neg.Id, counter, None)))
                                                  )
                                              ) ] ] ] ] ]
                :> IView

            | NegotiationStage.AwaitingPlayerResponse(fee, salary) ->
                let currentSalary =
                    Player.contractOf p |> Option.map _.Salary |> Option.defaultValue 0m

                StackPanel.create
                    [ StackPanel.spacing 12.0
                      StackPanel.children
                          [ UI.statusBanner Theme.Accent IconName.hourglass "Club accepted — waiting for player response..."
                            infoRow "Transfer Fee" (Formatters.money fee) Theme.Accent
                            infoRow "Salary" (Formatters.salary salary) Theme.TextSub
                            infoRow "Player's Current" (Formatters.salary currentSalary) Theme.TextMuted ] ]
                :> IView

            | NegotiationStage.RejectedByPlayer(fee, salary, reason) ->
                StackPanel.create
                    [ StackPanel.spacing 12.0
                      StackPanel.children
                          [ UI.statusBanner Theme.Danger IconName.error reason
                            infoRow "Fee" (Formatters.money fee) Theme.TextSub
                            infoRow "Salary Offered" (Formatters.salary salary) Theme.Danger
                            Grid.create
                                [ Grid.columnDefinitions "*, Auto"
                                  Grid.margin 8.0
                                  Grid.children
                                       [ UI.ghostButton "Abandon" None (fun _ -> dispatch (TransferMsg (ClearNegotiation neg.Id)))
                                         Border.create
                                            [ Grid.column 1
                                              Border.child (
                                                  UI.primaryButton "Improve Salary" (Some IconName.add) (fun _ ->
                                                      dispatch (TransferMsg(CounterOffer(neg.Id, fee, Some(salary * 1.2m))))
                                                  )
                                              ) ] ] ] ] ]
                :> IView

            | NegotiationStage.Agreed(fee, salary, years) ->
                StackPanel.create
                    [ StackPanel.spacing 12.0
                      StackPanel.children
                          [ UI.statusBanner Theme.Accent IconName.success "Transfer agreed — processing..."
                            infoRow "Transfer Fee" (Formatters.money fee) Theme.Accent
                            infoRow "Salary" (Formatters.salary salary) Theme.TextSub
                            infoRow "Contract" $"{years} years" Theme.TextSub ] ]
                :> IView

            | NegotiationStage.Collapsed reason ->
                StackPanel.create
                    [ StackPanel.spacing 12.0
                      StackPanel.children
                          [ UI.statusBanner Theme.Danger IconName.error $"Negotiation collapsed: {reason}"
                            UI.primaryButton "Dismiss" (Some IconName.close) (fun _ ->
                                dispatch (TransferMsg (ClearNegotiation neg.Id))) ] ]
                :> IView

            | NegotiationStage.Approaching ->
                StackPanel.create
                    [ StackPanel.spacing 12.0
                      StackPanel.children
                          [ UI.statusBanner Theme.Warning IconName.hourglass "Preparing negotiation..." ] ]
                :> IView

        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush (Theme.Accent + "44")
              Border.borderThickness 1.0
              Border.cornerRadius 10.0
              Border.margin (0.0, 12.0, 0.0, 0.0)
              Border.padding (16.0, 14.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 10.0
                        StackPanel.verticalAlignment VerticalAlignment.Stretch
                        StackPanel.children
                            [ Grid.create
                                  [ Grid.columnDefinitions "Auto, *"
                                    Grid.children
                                        [ Icons.iconMd ClubIcon.transfer Theme.Accent
                                          TextBlock.create
                                              [ Grid.column 1
                                                TextBlock.text "Transfer Negotiation"
                                                TextBlock.fontSize 13.0
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.foreground Theme.TextMain
                                                TextBlock.margin (8.0, 0.0, 0.0, 0.0)
                                                TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
                              stepContent ] ]
              ) ]

    let private playerDetailPanel
        (p: Player)
        (clubName: string)
        (isWatched: bool)
        (buyer: Club)
        (activeNeg: Negotiation option)
        (dispatch: Msg -> unit)
        =
        let posColor = Theme.positionColor p.Position

        let header =
            Border.create
                [ Border.background Theme.BgCard
                  Border.padding (20.0, 20.0, 20.0, 16.0)
                  Border.borderBrush Theme.Border
                  Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                  Border.child (
                      StackPanel.create
                          [ StackPanel.spacing 14.0
                            StackPanel.children
                                [ Grid.create
                                      [ Grid.columnDefinitions "Auto, *"
                                        Grid.children
                                            [ Border.create
                                                  [ Grid.column 0
                                                    Border.margin (0.0, 0.0, 14.0, 0.0)
                                                    Border.child (playerAvatar p.Name p.Position 52.0) ]
                                              StackPanel.create
                                                  [ Grid.column 1
                                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                                    StackPanel.spacing 4.0
                                                    StackPanel.children
                                                        [ TextBlock.create
                                                              [ TextBlock.text p.Name
                                                                TextBlock.fontSize 16.0
                                                                TextBlock.fontWeight FontWeight.Black
                                                                TextBlock.foreground Theme.TextMain ]
                                                          StackPanel.create
                                                              [ StackPanel.orientation Orientation.Horizontal
                                                                StackPanel.spacing 8.0
                                                                StackPanel.children
                                                                    [ Border.create
                                                                          [ Border.background (posColor + "22")
                                                                            Border.borderBrush (posColor + "55")
                                                                            Border.borderThickness 1.0
                                                                            Border.cornerRadius 4.0
                                                                            Border.padding (6.0, 2.0)
                                                                            Border.child (
                                                                                TextBlock.create
                                                                                    [ TextBlock.text $"%A{p.Position}"
                                                                                      TextBlock.fontSize 10.0
                                                                                      TextBlock.fontWeight
                                                                                          FontWeight.Black
                                                                                      TextBlock.foreground posColor ]
                                                                            ) ]
                                                                      TextBlock.create
                                                                          [ TextBlock.text clubName
                                                                            TextBlock.fontSize 11.0
                                                                            TextBlock.foreground Theme.TextMuted ] ] ] ] ] ] ]
                                  Grid.create
                                      [ Grid.columnDefinitions "*, *, *"
                                        Grid.children
                                            [ StackPanel.create
                                                  [ Grid.column 0
                                                    StackPanel.spacing 2.0
                                                    StackPanel.children
                                                        [ TextBlock.create
                                                              [ TextBlock.text "CA / PA"
                                                                TextBlock.fontSize 9.0
                                                                TextBlock.fontWeight FontWeight.Bold
                                                                TextBlock.foreground Theme.TextMuted
                                                                TextBlock.lineSpacing 1.0 ]
                                                          TextBlock.create
                                                              [ TextBlock.text $"{p.CurrentSkill} / {p.PotentialSkill}"
                                                                TextBlock.fontSize 15.0
                                                                TextBlock.fontWeight FontWeight.Black
                                                                TextBlock.foreground Theme.Accent ] ] ]
                                              StackPanel.create
                                                  [ Grid.column 1
                                                    StackPanel.spacing 2.0
                                                    StackPanel.children
                                                        [ TextBlock.create
                                                              [ TextBlock.text "VALUE"
                                                                TextBlock.fontSize 9.0
                                                                TextBlock.fontWeight FontWeight.Bold
                                                                TextBlock.foreground Theme.TextMuted
                                                                TextBlock.lineSpacing 1.0 ]
                                                          TextBlock.create
                                                              [ TextBlock.text (
                                                                    Formatters.money (Player.playerValue p.CurrentSkill)
                                                                )
                                                                TextBlock.fontSize 15.0
                                                                TextBlock.fontWeight FontWeight.Black
                                                                TextBlock.foreground Theme.TextMain ] ] ]
                                              StackPanel.create
                                                  [ Grid.column 2
                                                    StackPanel.spacing 2.0
                                                    StackPanel.children
                                                        [ TextBlock.create
                                                              [ TextBlock.text "WAGE"
                                                                TextBlock.fontSize 9.0
                                                                TextBlock.fontWeight FontWeight.Bold
                                                                TextBlock.foreground Theme.TextMuted
                                                                TextBlock.lineSpacing 1.0 ]
                                                          TextBlock.create
                                                              [ TextBlock.text (
                                                                    Formatters.salary (
                                                                        Player.contractOf p
                                                                        |> Option.map _.Salary
                                                                        |> Option.defaultValue 0m
                                                                    )
                                                                )
                                                                TextBlock.fontSize 15.0
                                                                TextBlock.fontWeight FontWeight.Black
                                                                TextBlock.foreground Theme.TextMain ] ] ] ] ] ] ]
                  ) ]

        let body =
            ScrollViewer.create
                [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
                  ScrollViewer.content (
                      StackPanel.create
                          [ StackPanel.margin (20.0, 16.0)
                            StackPanel.spacing 0.0
                            StackPanel.children
                                [ statGroup
                                      "Technical"
                                      [ "Finishing", p.Technical.Finishing
                                        "Dribbling", p.Technical.Dribbling
                                        "Passing", p.Technical.Passing
                                        "Ball Control", p.Technical.BallControl
                                        "Tackling", p.Technical.Tackling
                                        "Marking", p.Technical.Marking ]
                                      Theme.AccentAlt
                                  statGroup
                                      "Physical"
                                      [ "Pace", p.Physical.Pace
                                        "Stamina", p.Physical.Stamina
                                        "Strength", p.Physical.Strength
                                        "Agility", p.Physical.Agility ]
                                      Theme.Accent
                                  statGroup
                                      "Mental"
                                      [ "Vision", p.Mental.Vision
                                        "Composure", p.Mental.Composure
                                        "Positioning", p.Mental.Positioning
                                        "Work Rate", p.Mental.WorkRate ]
                                      Theme.Warning

                                  match activeNeg with
                                  | Some neg when neg.PlayerId = p.Id -> negotiationPanel p buyer neg dispatch
                                  | _ ->
                                      StackPanel.create
                                          [ StackPanel.margin (0.0, 8.0, 0.0, 0.0)
                                            StackPanel.spacing 8.0
                                            StackPanel.children
                                                [ Button.create
                                                      [ Button.horizontalAlignment HorizontalAlignment.Stretch
                                                        Button.background (
                                                            if isWatched then Theme.Warning + "22" else "Transparent"
                                                        )
                                                        Button.borderBrush (
                                                            if isWatched then Theme.Warning else Theme.Border
                                                        )
                                                        Button.borderThickness 1.0
                                                        Button.cornerRadius 8.0
                                                        Button.padding (0.0, 10.0)
                                                        Button.onClick (fun _ ->
                                                            dispatch (TransferMsg(WatchToggle p.Id)))
                                                        Button.content (
                                                            StackPanel.create
                                                                [ StackPanel.orientation Orientation.Horizontal
                                                                  StackPanel.spacing 6.0
                                                                  StackPanel.horizontalAlignment
                                                                      HorizontalAlignment.Center
                                                                  StackPanel.children
                                                                      [ Icons.iconSm
                                                                            PlayerIcon.skill
                                                                            (if isWatched then
                                                                                 Theme.Warning
                                                                             else
                                                                                 Theme.TextMuted)
                                                                        TextBlock.create
                                                                            [ TextBlock.text (
                                                                                  if isWatched then
                                                                                      "On Watchlist"
                                                                                  else
                                                                                      "Add to Watchlist"
                                                                              )
                                                                              TextBlock.fontSize 12.0
                                                                              TextBlock.fontWeight FontWeight.SemiBold
                                                                              TextBlock.foreground (
                                                                                  if isWatched then
                                                                                      Theme.Warning
                                                                                  else
                                                                                      Theme.TextMuted
                                                                              )
                                                                              TextBlock.verticalAlignment
                                                                                  VerticalAlignment.Center ] ] ]
                                                        ) ]
                                                  UI.primaryButton "Make Offer" (Some ClubIcon.transfer) (fun _ ->
                                                      dispatch (
                                                          TransferMsg(
                                                              MakeOffer(p.Id, suggestedFee p, suggestedSalary p)
                                                          )
                                                      )) ] ] ] ]
                  ) ]

        Grid.create
            [ Grid.rowDefinitions "Auto, *"
              Grid.children
                  [ Border.create [ Grid.row 0; Border.child header ]
                    Border.create [ Grid.row 1; Border.child body ] ] ]

    let private marketTableHeader () =
        Border.create
            [ Border.padding (16.0, 8.0)
              Border.background Theme.BgSidebar
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions (ColumnDefs.toGridString ColumnDefs.transferListCols)
                        Grid.children
                            [ Border.create [ Grid.column 0; Border.width 48.0 ]
                              TextBlock.create
                                  [ Grid.column 1
                                    TextBlock.text "PLAYER"
                                    TextBlock.fontSize 10.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.lineSpacing 1.0 ]
                              TextBlock.create
                                  [ Grid.column 2
                                    TextBlock.text "POS"
                                    TextBlock.fontSize 10.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.lineSpacing 1.0
                                    TextBlock.margin (0.0, 0.0, 16.0, 0.0) ]
                              TextBlock.create
                                  [ Grid.column 3
                                    TextBlock.text "CA"
                                    TextBlock.fontSize 10.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.lineSpacing 1.0
                                    TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                              TextBlock.create
                                  [ Grid.column 4
                                    TextBlock.text "VALUE"
                                    TextBlock.fontSize 10.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.lineSpacing 1.0
                                    TextBlock.horizontalAlignment HorizontalAlignment.Right ]
                              Border.create [ Grid.column 5; Border.width 44.0 ] ] ]
              ) ]

    let private paginationBar (page: int) (total: int) (dispatch: Msg -> unit) =
        let totalPages = max 1 ((total + pageSize - 1) / pageSize)

        if totalPages <= 1 then
            Border.create [ Border.height 0.0 ] :> IView
        else
            Border.create
                [ Border.background Theme.BgSidebar
                  Border.borderBrush Theme.Border
                  Border.borderThickness (0.0, 1.0, 0.0, 0.0)
                  Border.padding (16.0, 10.0)
                  Border.child (
                      StackPanel.create
                          [ StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 8.0
                            StackPanel.horizontalAlignment HorizontalAlignment.Center
                            StackPanel.children
                                [ Button.create
                                      [ Button.content (Icons.iconSm Nav.prev Theme.TextMuted)
                                        Button.background "Transparent"
                                        Button.borderThickness 0.0
                                        Button.isEnabled (page > 0)
                                        Button.onClick (fun e ->
                                            e.Handled <- true
                                            dispatch (TransferMsg(PageChange(page - 1)))) ]
                                  TextBlock.create
                                      [ TextBlock.text $"{page + 1} / {totalPages}"
                                        TextBlock.fontSize 12.0
                                        TextBlock.foreground (Theme.TextMuted: string)
                                        TextBlock.verticalAlignment VerticalAlignment.Center ]
                                  Button.create
                                      [ Button.content (Icons.iconSm Nav.next Theme.TextMuted)
                                        Button.background "Transparent"
                                        Button.borderThickness 0.0
                                        Button.isEnabled (page < totalPages - 1)
                                        Button.onClick (fun e ->
                                            e.Handled <- true
                                            dispatch (TransferMsg(PageChange(page + 1)))) ] ] ]
                  ) ]
            |> View.withKey $"{page}"
            :> IView

    let private offerStatusLabel (status: OfferStatus) =
        match status with
        | Pending -> "Pending", Theme.Warning
        | AcceptedByClub -> "Club Accepted", Theme.Accent
        | RejectedByClub -> "Rejected", Theme.Danger
        | ContractOffered _ -> "Negotiating", Theme.AccentAlt
        | ContractRejectedByPlayer -> "Player Refused", Theme.Danger
        | Completed -> "Completed", Theme.Accent
        | Withdrawn -> "Withdrawn", Theme.TextMuted

    let private offerRow
        (offer: TransferOffer)
        (players: Map<PlayerId, Player>)
        (clubs: Map<ClubId, Club>)
        (dispatch: Msg -> unit)
        =
        let playerName =
            players
            |> Map.tryFind offer.PlayerId
            |> Option.map _.Name
            |> Option.defaultValue "Unknown"

        let sellerName =
            clubs
            |> Map.tryFind offer.SellerClubId
            |> Option.map _.Name
            |> Option.defaultValue "Unknown"

        let label, color = offerStatusLabel offer.Status

        Border.create
            [ Border.padding (16.0, 12.0)
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions "*, Auto, Auto, Auto"
                        Grid.margin 12.0
                        Grid.children
                            [ StackPanel.create
                                  [ Grid.column 0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.spacing 2.0
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text playerName
                                                TextBlock.fontSize 13.0
                                                TextBlock.fontWeight FontWeight.SemiBold
                                                TextBlock.foreground Theme.TextMain ]
                                          TextBlock.create
                                              [ TextBlock.text sellerName
                                                TextBlock.fontSize 11.0
                                                TextBlock.foreground Theme.TextMuted ] ] ]
                              TextBlock.create
                                  [ Grid.column 1
                                    TextBlock.text (Formatters.money offer.Fee)
                                    TextBlock.fontSize 12.0
                                    TextBlock.fontWeight FontWeight.SemiBold
                                    TextBlock.foreground Theme.TextSub
                                    TextBlock.verticalAlignment VerticalAlignment.Center ]
                              Border.create
                                  [ Grid.column 2
                                    Border.background (color + "18")
                                    Border.borderBrush (color + "55")
                                    Border.borderThickness 1.0
                                    Border.cornerRadius 4.0
                                    Border.padding (6.0, 3.0)
                                    Border.verticalAlignment VerticalAlignment.Center
                                    Border.child (
                                        TextBlock.create
                                            [ TextBlock.text label
                                              TextBlock.fontSize 10.0
                                              TextBlock.fontWeight FontWeight.Bold
                                              TextBlock.foreground color ]
                                    ) ]
                              if offer.Status <> Completed && offer.Status <> Withdrawn then
                                  Button.create
                                      [ Grid.column 3
                                        Button.padding (6.0, 4.0)
                                        Button.background "Transparent"
                                        Button.borderBrush Theme.Border
                                        Button.borderThickness 1.0
                                        Button.cornerRadius 6.0
                                        Button.verticalAlignment VerticalAlignment.Center
                                        Button.onClick (fun _ -> dispatch (TransferMsg(WithdrawOffer offer.Id)))
                                        Button.content (Icons.iconSm IconName.close Theme.Danger) ] ] ]
              ) ]

    let private historyRow (r: TransferRecord) =
        Border.create
            [ Border.padding (16.0, 12.0)
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions "*, Auto, Auto"
                        Grid.margin 12.0
                        Grid.children
                            [ StackPanel.create
                                  [ Grid.column 0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.spacing 2.0
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text r.PlayerName
                                                TextBlock.fontSize 13.0
                                                TextBlock.fontWeight FontWeight.SemiBold
                                                TextBlock.foreground Theme.TextMain ]
                                          TextBlock.create
                                              [ TextBlock.text $"{r.FromClubName} → {r.ToClubName}"
                                                TextBlock.fontSize 11.0
                                                TextBlock.foreground Theme.TextMuted ] ] ]
                              TextBlock.create
                                  [ Grid.column 1
                                    TextBlock.text (Formatters.money r.Fee)
                                    TextBlock.fontSize 12.0
                                    TextBlock.fontWeight FontWeight.SemiBold
                                    TextBlock.foreground Theme.Accent
                                    TextBlock.verticalAlignment VerticalAlignment.Center ]
                              TextBlock.create
                                  [ Grid.column 2
                                    TextBlock.text $"Season {r.Season}"
                                    TextBlock.fontSize 11.0
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
              ) ]

    let transfersView (state: State) (dispatch: Msg -> unit) =
        match state.Mode with
        | InGame(gs, _) ->
            let ts = state.Transfer

            let pagedPlayers =
                ts.FilteredPlayers |> List.skip (ts.Page * pageSize) |> List.truncate pageSize

            let getClubName (pid: PlayerId) =
                ts.ClubNameCache |> Map.tryFind pid |> Option.defaultValue "Unknown"

            let buyer = gs.Clubs[gs.UserClubId]
            let userBudget = buyer.Budget

            let topBar =
                Border.create
                    [ Border.background Theme.BgSidebar
                      Border.borderBrush Theme.Border
                      Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                      Border.padding (20.0, 14.0)
                      Border.child (
                          DockPanel.create
                              [ DockPanel.children
                                    [ StackPanel.create
                                          [ DockPanel.dock Dock.Right
                                            StackPanel.orientation Orientation.Horizontal
                                            StackPanel.spacing 6.0
                                            StackPanel.verticalAlignment VerticalAlignment.Center
                                            StackPanel.children
                                                [ Icons.iconSm ClubIcon.finances Theme.Accent
                                                  TextBlock.create
                                                      [ TextBlock.text "BUDGET"
                                                        TextBlock.fontSize 10.0
                                                        TextBlock.fontWeight FontWeight.Bold
                                                        TextBlock.foreground Theme.TextMuted
                                                        TextBlock.verticalAlignment VerticalAlignment.Center ]
                                                  TextBlock.create
                                                      [ TextBlock.text (Formatters.money userBudget)
                                                        TextBlock.fontSize 14.0
                                                        TextBlock.fontWeight FontWeight.Black
                                                        TextBlock.foreground Theme.Accent
                                                        TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
                                      StackPanel.create
                                          [ StackPanel.spacing 4.0
                                            StackPanel.children
                                                [ TextBlock.create
                                                      [ TextBlock.text "TRANSFERS"
                                                        TextBlock.fontSize 18.0
                                                        TextBlock.fontWeight FontWeight.Black
                                                        TextBlock.foreground Theme.TextMain
                                                        TextBlock.lineSpacing 2.0 ]
                                                  TextBlock.create
                                                      [ TextBlock.text "Recruit, negotiate, and build your squad"
                                                        TextBlock.fontSize 11.0
                                                        TextBlock.foreground Theme.TextMuted ] ] ] ] ]
                      ) ]

            let tabBar =
                Border.create
                    [ Border.background Theme.BgCard
                      Border.borderBrush Theme.Border
                      Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                      Border.padding (20.0, 0.0)
                      Border.child (
                          StackPanel.create
                              [ StackPanel.orientation Orientation.Horizontal
                                StackPanel.spacing 2.0
                                StackPanel.children
                                    [ for tab in
                                          [ MarketSearch; MyWatchlist; IncomingOffers; OutgoingOffers; TransferHistory ] do
                                          let isActive = ts.ActiveTab = tab

                                          Button.create
                                              [ Button.padding (14.0, 12.0)
                                                Button.background "Transparent"
                                                Button.borderThickness (
                                                    Avalonia.Thickness(0.0, 0.0, 0.0, if isActive then 2.0 else 0.0)
                                                )
                                                Button.borderBrush (if isActive then Theme.Accent else "Transparent")
                                                Button.cornerRadius 0.0
                                                Button.onClick (fun _ -> dispatch (TransferMsg(TabChange tab)))
                                                Button.content (
                                                    StackPanel.create
                                                        [ StackPanel.orientation Orientation.Horizontal
                                                          StackPanel.spacing 6.0
                                                          StackPanel.children
                                                              [ Icons.iconSm
                                                                    (tabIcon tab)
                                                                    (if isActive then Theme.Accent else Theme.TextMuted)
                                                                TextBlock.create
                                                                    [ TextBlock.text (tabLabel tab)
                                                                      TextBlock.fontSize 12.0
                                                                      TextBlock.fontWeight (
                                                                          if isActive then
                                                                              FontWeight.SemiBold
                                                                          else
                                                                              FontWeight.Normal
                                                                      )
                                                                      TextBlock.foreground (
                                                                          if isActive then
                                                                              Theme.Accent
                                                                          else
                                                                              Theme.TextMuted
                                                                      )
                                                                      TextBlock.verticalAlignment
                                                                          VerticalAlignment.Center ] ] ]
                                                ) ] ] ]
                      ) ]

            let searchAndFilters =
                Border.create
                    [ Border.background Theme.BgSidebar
                      Border.borderBrush Theme.Border
                      Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                      Border.padding (16.0, 10.0)
                      Border.child (
                          Grid.create
                              [ Grid.columnDefinitions "*, Auto"
                                Grid.children
                                    [ Border.create
                                          [ Grid.column 0
                                            Border.background Theme.BgCard
                                            Border.borderBrush Theme.Border
                                            Border.borderThickness 1.0
                                            Border.cornerRadius 8.0
                                            Border.margin (0.0, 0.0, 12.0, 0.0)
                                            Border.child (
                                                Grid.create
                                                    [ Grid.columnDefinitions "Auto, *"
                                                      Grid.children
                                                          [ Border.create
                                                                [ Grid.column 0
                                                                  Border.padding (10.0, 0.0, 4.0, 0.0)
                                                                  Border.verticalAlignment VerticalAlignment.Center
                                                                  Border.child (
                                                                      Icons.iconSm IconName.search Theme.TextMuted
                                                                  ) ]
                                                            TextBox.create
                                                                [ Grid.column 1
                                                                  TextBox.text ts.SearchQuery
                                                                  TextBox.onTextChanged (fun q ->
                                                                      dispatch (TransferMsg(Search q)))
                                                                  TextBox.watermark "Search players..."
                                                                  TextBox.background "Transparent"
                                                                  TextBox.borderThickness 0.0
                                                                  TextBox.padding (4.0, 10.0)
                                                                  TextBox.fontSize 13.0 ] ] ]
                                            ) ]
                                      StackPanel.create
                                          [ Grid.column 1
                                            StackPanel.orientation Orientation.Horizontal
                                            StackPanel.spacing 6.0
                                            StackPanel.children
                                                [ for f in
                                                      [ AllPositions; Goalkeepers; Defenders; Midfielders; Attackers ] do
                                                      let isActive = ts.PositionFilter = f

                                                      Button.create
                                                          [ Button.padding (10.0, 6.0)
                                                            Button.cornerRadius 6.0
                                                            Button.background (
                                                                if isActive then Theme.AccentLight else "Transparent"
                                                            )
                                                            Button.borderBrush (
                                                                if isActive then Theme.Accent else Theme.Border
                                                            )
                                                            Button.borderThickness 1.0
                                                            Button.onClick (fun _ ->
                                                                dispatch (TransferMsg(FilterChange f)))
                                                            Button.content (
                                                                TextBlock.create
                                                                    [ TextBlock.text (positionLabel f)
                                                                      TextBlock.fontSize 11.0
                                                                      TextBlock.fontWeight FontWeight.SemiBold
                                                                      TextBlock.foreground (
                                                                          if isActive then
                                                                              Theme.Accent
                                                                          else
                                                                              Theme.TextMuted
                                                                      ) ]
                                                            ) ] ] ] ] ]
                      ) ]

            let playerListPanel =
                if ts.IsLoading then
                    UI.loadingState ()
                else
                    Grid.create
                        [ Grid.rowDefinitions "Auto, *, Auto"
                          Grid.children
                              [ marketTableHeader () |> fun h -> Border.create [ Grid.row 0; Border.child h ]

                                if pagedPlayers.IsEmpty then
                                    UI.emptyState
                                        IconName.search
                                        "No players found"
                                        "Try adjusting your search or filters"
                                    |> fun e -> Border.create [ Grid.row 1; Border.child e ]
                                else
                                    ScrollViewer.create
                                        [ Grid.row 1
                                          ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
                                          ScrollViewer.content (
                                              StackPanel.create
                                                  [ StackPanel.children
                                                        [ for p in pagedPlayers do
                                                              playerRow
                                                                  p
                                                                  (getClubName p.Id)
                                                                  (ts.SelectedPlayerId = Some p.Id)
                                                                  (List.contains p.Id ts.WatchlistIds)
                                                                  (fun () -> dispatch (TransferMsg(PlayerSelect p.Id)))
                                                                  (fun () -> dispatch (TransferMsg(WatchToggle p.Id))) ] ]
                                          ) ]

                                paginationBar ts.Page ts.FilteredPlayers.Length dispatch
                                |> fun p -> Border.create [ Grid.row 2; Border.child p ] ] ]
                    :> IView

            let detailPanel =
                match ts.SelectedPlayerId with
                | None -> UI.emptyState PlayerIcon.position "No player selected" "Click a player to view details"
                | Some pid ->
                    match ts.CachedPlayers |> List.tryFind (fun p -> p.Id = pid) with
                    | None -> UI.emptyState IconName.error "Player not found" ""
                    | Some p ->
                        let userNeg = ts.ActiveNegotiationId |> Option.bind (fun id -> gs.PendingNegotiations |> Map.tryFind id)

                        playerDetailPanel
                            p
                            (getClubName pid)
                            (List.contains pid ts.WatchlistIds)
                            buyer
                            userNeg
                            dispatch
                        :> IView

            let marketContent =
                Grid.create
                    [ Grid.columnDefinitions "*, 300"
                      Grid.children
                          [ Border.create
                                [ Grid.column 0
                                  Border.borderBrush Theme.Border
                                  Border.borderThickness (0.0, 0.0, 1.0, 0.0)
                                  Border.child playerListPanel ]
                            Border.create [ Grid.column 1; Border.child detailPanel ] ] ]

            let watchlistContent =
                match ts.WatchlistIds with
                | [] ->
                    UI.emptyState
                        PlayerIcon.skill
                        "Your watchlist is empty"
                        "Star players in the market to track them here"
                | ids ->
                    let watched = ts.CachedPlayers |> List.filter (fun p -> List.contains p.Id ids)

                    Grid.create
                        [ Grid.columnDefinitions "*, 300"
                          Grid.children
                              [ Border.create
                                    [ Grid.column 0
                                      Border.borderBrush Theme.Border
                                      Border.borderThickness (0.0, 0.0, 1.0, 0.0)
                                      Border.child (
                                          StackPanel.create
                                              [ StackPanel.children
                                                    [ marketTableHeader ()
                                                      ScrollViewer.create
                                                          [ ScrollViewer.verticalScrollBarVisibility
                                                                ScrollBarVisibility.Auto
                                                            ScrollViewer.content (
                                                                StackPanel.create
                                                                    [ StackPanel.children
                                                                          [ for p in watched do
                                                                                playerRow
                                                                                    p
                                                                                    (getClubName p.Id)
                                                                                    (ts.SelectedPlayerId = Some p.Id)
                                                                                    true
                                                                                    (fun () ->
                                                                                        dispatch (
                                                                                            TransferMsg(
                                                                                                PlayerSelect p.Id
                                                                                            )
                                                                                        ))
                                                                                    (fun () ->
                                                                                        dispatch (
                                                                                            TransferMsg(
                                                                                                WatchToggle p.Id
                                                                                            )
                                                                                        )) ] ]
                                                            ) ] ] ]
                                      ) ]
                                Border.create [ Grid.column 1; Border.child detailPanel ] ] ]
                    :> IView

            let outgoingContent =
                let active =
                    gs.PendingNegotiations
                    |> Map.toList
                    |> List.map snd
                    |> List.filter (fun n -> n.BuyerClubId = gs.UserClubId)
                    |> List.filter (fun n ->
                        match n.Stage with
                        | NegotiationStage.Collapsed _ -> false
                        | _ -> true)

                let negStatusLabel (stage: NegotiationStage) =
                    match stage with
                    | NegotiationStage.Approaching -> "Approaching", Theme.TextMuted
                    | NegotiationStage.OfferMade _ -> "Waiting club response", Theme.Warning
                    | NegotiationStage.RejectedByClub _ -> "Rejected by club", Theme.Danger
                    | NegotiationStage.CounterReceived _ -> "Counter received", Theme.AccentAlt
                    | NegotiationStage.AwaitingPlayerResponse _ -> "Waiting player", Theme.Accent
                    | NegotiationStage.RejectedByPlayer _ -> "Rejected by player", Theme.Danger
                    | NegotiationStage.Agreed _ -> "Agreed", Theme.Accent
                    | NegotiationStage.Collapsed _ -> "Collapsed", Theme.TextMuted

                let feeOf (neg: Negotiation) =
                    match neg.Stage with
                    | NegotiationStage.OfferMade f
                    | NegotiationStage.CounterReceived(f, _)
                    | NegotiationStage.AwaitingPlayerResponse(f, _)
                    | NegotiationStage.RejectedByClub(f, _)
                    | NegotiationStage.RejectedByPlayer(f, _, _)
                    | NegotiationStage.Agreed(f, _, _) -> f
                    | _ -> 0m

                if active.IsEmpty then
                    UI.emptyState ClubIcon.transfer "No outgoing offers" "Make an offer from the market to see it here"
                else
                    StackPanel.create
                        [ StackPanel.children
                              [ Border.create
                                    [ Border.padding (16.0, 8.0)
                                      Border.background Theme.BgSidebar
                                      Border.borderBrush Theme.Border
                                      Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                      Border.child (
                                          Grid.create
                                              [ Grid.columnDefinitions "*, Auto, 120, 32"
                                                Grid.margin 12.0
                                                Grid.children
                                                    [ TextBlock.create
                                                          [ Grid.column 0
                                                            TextBlock.text "PLAYER"
                                                            TextBlock.fontSize 10.0
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.foreground Theme.TextMuted
                                                            TextBlock.lineSpacing 1.0 ]
                                                      TextBlock.create
                                                          [ Grid.column 1
                                                            TextBlock.text "FEE"
                                                            TextBlock.fontSize 10.0
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.foreground Theme.TextMuted
                                                            TextBlock.lineSpacing 1.0 ]
                                                      TextBlock.create
                                                          [ Grid.column 2
                                                            TextBlock.text "STATUS"
                                                            TextBlock.fontSize 10.0
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.foreground Theme.TextMuted
                                                            TextBlock.lineSpacing 1.0 ] ] ]
                                      ) ]
                                ScrollViewer.create
                                    [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
                                      ScrollViewer.content (
                                          StackPanel.create
                                              [ StackPanel.children
                                                    [ for neg in active do
                                                          let playerName =
                                                              gs.Players
                                                              |> Map.tryFind neg.PlayerId
                                                              |> Option.map _.Name
                                                              |> Option.defaultValue "Unknown"

                                                          let label, color = negStatusLabel neg.Stage

                                                          Border.create
                                                              [ Border.padding (16.0, 12.0)
                                                                Border.borderBrush Theme.Border
                                                                Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                                                Border.child (
                                                                    Grid.create
                                                                        [ Grid.columnDefinitions "*, Auto, Auto, Auto"
                                                                          Grid.margin 12.0
                                                                          Grid.children
                                                                              [ TextBlock.create
                                                                                    [ Grid.column 0
                                                                                      TextBlock.text playerName
                                                                                      TextBlock.fontSize 13.0
                                                                                      TextBlock.fontWeight
                                                                                          FontWeight.SemiBold
                                                                                      TextBlock.foreground
                                                                                          Theme.TextMain ]
                                                                                TextBlock.create
                                                                                    [ Grid.column 1
                                                                                      TextBlock.text
                                                                                          (Formatters.money (feeOf neg))
                                                                                      TextBlock.fontSize 12.0
                                                                                      TextBlock.fontWeight
                                                                                          FontWeight.SemiBold
                                                                                      TextBlock.foreground
                                                                                          Theme.TextSub
                                                                                      TextBlock.verticalAlignment
                                                                                          VerticalAlignment.Center ]
                                                                                Border.create
                                                                                    [ Grid.column 2
                                                                                      Border.background (color + "18")
                                                                                      Border.borderBrush (color + "55")
                                                                                      Border.borderThickness 1.0
                                                                                      Border.cornerRadius 4.0
                                                                                      Border.padding (6.0, 3.0)
                                                                                      Border.verticalAlignment
                                                                                          VerticalAlignment.Center
                                                                                      Border.child (
                                                                                          TextBlock.create
                                                                                              [ TextBlock.text label
                                                                                                TextBlock.fontSize 10.0
                                                                                                TextBlock.fontWeight
                                                                                                    FontWeight.Bold
                                                                                                TextBlock.foreground
                                                                                                    color ]
                                                                                      ) ]
                                                                                if neg.BuyerClubId = gs.UserClubId
                                                                                   && color <> Theme.Danger then
                                                                                    Button.create
                                                                                        [ Grid.column 3
                                                                                          Button.padding (6.0, 4.0)
                                                                                          Button.background
                                                                                              "Transparent"
                                                                                          Button.borderBrush
                                                                                              Theme.Border
                                                                                          Button.borderThickness 1.0
                                                                                          Button.cornerRadius 6.0
                                                                                          Button.verticalAlignment
                                                                                              VerticalAlignment.Center
                                                                                          Button.onClick (fun _ ->
                                                                                              dispatch (
                                                                                                  TransferMsg(
                                                                                                      WithdrawOffer
                                                                                                          neg.Id
                                                                                                  )
                                                                                              ))
                                                                                          Button.content (
                                                                                              Icons.iconSm
                                                                                                  IconName.close
                                                                                                  Theme.Danger
                                                                                          ) ] ] ]
                                                                ) ] ] ]
                                      ) ] ] ]
                    :> IView

            let historyContent =
                if ts.TransferHistory.IsEmpty then
                    UI.emptyState PlayerIcon.contract "No transfer history" "Completed deals will appear here"
                else
                    StackPanel.create
                        [ StackPanel.children
                              [ ScrollViewer.create
                                    [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
                                      ScrollViewer.content (
                                          StackPanel.create
                                              [ StackPanel.children
                                                    [ for r in ts.TransferHistory do
                                                          historyRow r ] ]
                                      ) ] ] ]
                    :> IView

            let tabContent =
                match ts.ActiveTab with
                | MarketSearch -> marketContent :> IView
                | MyWatchlist -> watchlistContent
                | IncomingOffers ->
                    UI.emptyState IconName.add "No incoming offers" "Other clubs haven't made any offers yet"
                | OutgoingOffers -> outgoingContent
                | TransferHistory -> historyContent

            Grid.create
                [ Grid.background Theme.BgMain
                  Grid.rowDefinitions "Auto, Auto, Auto, *"
                  Grid.children
                      [ Border.create [ Grid.row 0; Border.child topBar ]
                        Border.create [ Grid.row 1; Border.child tabBar ]
                        Border.create
                            [ Grid.row 2
                              Border.isVisible (ts.ActiveTab = MarketSearch || ts.ActiveTab = MyWatchlist)
                              Border.child searchAndFilters ]
                        Border.create [ Grid.row 3; Border.child tabContent ] ] ]
            :> IView
        | _ -> Border.create [] :> IView
