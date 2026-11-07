namespace Avalonia.FuncUI.DSL

[<AutoOpen>]
module MaterialIcon =
    open System
    open Material.Icons
    open Material.Icons.Avalonia
    open Avalonia.FuncUI.Types
    open Avalonia.FuncUI.Builder

    let create (attrs: IAttr<MaterialIcon> list) : IView<MaterialIcon> = ViewBuilder.Create<MaterialIcon>(attrs)

    type MaterialIcon with

        static member kind<'t when 't :> MaterialIcon>(value: MaterialIconKind) : IAttr<'t> =
            AttrBuilder<'t>
                .CreateProperty<Nullable<MaterialIconKind>>(MaterialIcon.KindProperty, Nullable(value), ValueNone)


namespace FootballEngine.Icons

open Material.Icons

module MatchEvent =
    let goal = MaterialIconKind.Soccer
    let ownGoal = MaterialIconKind.SoccerField
    let assist = MaterialIconKind.ShoeCleat
    let yellowCard = MaterialIconKind.CardOutline
    let redCard = MaterialIconKind.Card
    let injury = MaterialIconKind.Ambulance
    let subOn = MaterialIconKind.ArrowUpCircleOutline
    let subOff = MaterialIconKind.ArrowDownCircleOutline
    let penaltyMiss = MaterialIconKind.Target
    let freeKickMiss = MaterialIconKind.FlagOutline
    let corner = MaterialIconKind.Flag
    let pass = MaterialIconKind.SwapHorizontal
    let passIncomplete = MaterialIconKind.CloseNetwork
    let dribble = MaterialIconKind.Run
    let dribbleFail = MaterialIconKind.RunFast
    let tackle = MaterialIconKind.ShoeCleat
    let tackleFail = MaterialIconKind.ShoeSneaker
    let cross = MaterialIconKind.FlagVariant
    let longBall = MaterialIconKind.ChartBellCurve
    let shotBlocked = MaterialIconKind.Shield
    let shotOffTarget = MaterialIconKind.ArrowRight
    let save = MaterialIconKind.HandBackLeft
    let foul = MaterialIconKind.Whistle

module Nav =
    let skipFirst = MaterialIconKind.SkipPrevious
    let rewind = MaterialIconKind.Rewind
    let prev = MaterialIconKind.ChevronLeft
    let next = MaterialIconKind.ChevronRight
    let fastForward = MaterialIconKind.FastForward
    let skipLast = MaterialIconKind.SkipNext
    let play = MaterialIconKind.Play
    let pause = MaterialIconKind.Pause
    let stop = MaterialIconKind.Stop

module IconName =
    let home = MaterialIconKind.HomeOutline
    let squad = MaterialIconKind.AccountGroupOutline
    let tactics = MaterialIconKind.FootballPitch
    let calendar = MaterialIconKind.CalendarMonthOutline
    let league = MaterialIconKind.TrophyOutline
    let settings = MaterialIconKind.CogOutline
    let simulate = MaterialIconKind.PlayCircleOutline
    let search = MaterialIconKind.Magnify
    let close = MaterialIconKind.Close
    let add = MaterialIconKind.Plus
    let remove = MaterialIconKind.Minus
    let edit = MaterialIconKind.PencilOutline
    let save = MaterialIconKind.ContentSaveOutline
    let refresh = MaterialIconKind.Refresh
    let info = MaterialIconKind.InformationOutline
    let warning = MaterialIconKind.AlertOutline
    let success = MaterialIconKind.CheckCircleOutline
    let error = MaterialIconKind.CloseCircleOutline
    let sort = MaterialIconKind.SortVariant
    let filter = MaterialIconKind.FilterOutline
    let expand = MaterialIconKind.ChevronDown
    let collapse = MaterialIconKind.ChevronUp
    let menu = MaterialIconKind.Menu
    let back = MaterialIconKind.ArrowLeft
    let next = MaterialIconKind.ArrowRight
    let soccer = MaterialIconKind.Soccer
    let lightning = MaterialIconKind.LightningBolt
    let training = MaterialIconKind.Dumbbell
    let chart = MaterialIconKind.ChartBar
    let inbox = MaterialIconKind.EmailOutline
    let inboxArrowDown = MaterialIconKind.InboxArrowDown
    let mailOpen = MaterialIconKind.MailOutline
    let school = MaterialIconKind.School
    let bullhorn = MaterialIconKind.Bullhorn
    let fileDocumentEdit = MaterialIconKind.FileDocumentEditOutline
    let ambulance = MaterialIconKind.Ambulance
    let hourglass = MaterialIconKind.Hourglass

module PlayerIcon =
    let condition = MaterialIconKind.HeartPulse
    let morale = MaterialIconKind.EmoticonHappyOutline
    let speed = MaterialIconKind.LightningBolt
    let stamina = MaterialIconKind.Run
    let skill = MaterialIconKind.StarOutline
    let age = MaterialIconKind.CakeVariantOutline
    let contract = MaterialIconKind.FileDocumentOutline
    let value = MaterialIconKind.CurrencyUsd
    let position = MaterialIconKind.Soccer
    let injured = MaterialIconKind.Bandage

module ClubIcon =
    let stadium = MaterialIconKind.Stadium
    let finances = MaterialIconKind.BankOutline
    let transfer = MaterialIconKind.SwapHorizontal
    let scouting = MaterialIconKind.Binoculars
    let staff = MaterialIconKind.AccountTieOutline

module Icons =
    open Avalonia.FuncUI.DSL
    open Avalonia.FuncUI.Types
    open Avalonia.Layout
    open Material.Icons.Avalonia

    let icon (kind: MaterialIconKind) (size: float) (color: string) : IView =
        create
            [ MaterialIcon.kind kind
              MaterialIcon.width size
              MaterialIcon.height size
              MaterialIcon.foreground color
              MaterialIcon.verticalAlignment VerticalAlignment.Center ]

    let iconInherit (kind: MaterialIconKind) (size: float) : IView =
        create
            [ MaterialIcon.kind kind
              MaterialIcon.width size
              MaterialIcon.height size
              MaterialIcon.verticalAlignment VerticalAlignment.Center ]

    let iconSm (kind: MaterialIconKind) (color: string) : IView = icon kind 14.0 color
    let iconMd (kind: MaterialIconKind) (color: string) : IView = icon kind 16.0 color
    let iconLg (kind: MaterialIconKind) (color: string) : IView = icon kind 24.0 color
    let iconXl (kind: MaterialIconKind) (color: string) : IView = icon kind 48.0 color

module NotificationIcons =
    let win = MaterialIconKind.Trophy
    let loss = MaterialIconKind.EmoticonSadOutline
    let draw = MaterialIconKind.HandshakeOutline
    let promotion = MaterialIconKind.ArrowUpBoldCircleOutline
    let relegation = MaterialIconKind.ArrowDownBoldCircleOutline
    let seasonComplete = MaterialIconKind.FlagCheckered
    let swap = MaterialIconKind.SwapHorizontalCircleOutline
    let newCareer = MaterialIconKind.Soccer
    let loadGame = MaterialIconKind.ContentSave
    let error = MaterialIconKind.AlertCircleOutline
