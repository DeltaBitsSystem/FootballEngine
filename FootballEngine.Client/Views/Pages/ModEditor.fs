namespace FootballEngine.Pages

open System
open System.Text.RegularExpressions
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open FootballEngine
open FootballEngine.AppMsgs
open FootballEngine.AppTypes
open FootballEngine.Data
open FootballEngine.Domain
open FootballEngine.ModEditorTypes
open FootballEngine.Icons
open FootballEngine.Components

module ModEditor =

    // ─── Language and small building blocks ───────────────────────────────────

    let private t (es: string) (en: string) =
        if Localization.active () = Locale.Es then es else en

    let private positions =
        [ "GK"
          "DC"
          "DL"
          "DR"
          "WBL"
          "WBR"
          "DM"
          "MC"
          "ML"
          "MR"
          "AML"
          "AMR"
          "AMC"
          "ST" ]

    // Stored values stay the package's English tokens; only the label follows the language.
    let private feet () =
        [ "Auto", t "Auto" "Auto"
          "Right", t "Diestro" "Right"
          "Left", t "Zurdo" "Left"
          "Both", t "Ambidiestro" "Both" ]

    let private patterns () =
        [ Plain, t "Liso" "Plain"
          Stripes, t "Rayas" "Stripes"
          Hoops, t "Aros" "Hoops"
          Halves, t "Mitades" "Halves"
          Sash, t "Banda" "Sash" ]

    let private legOptions () =
        [ "SingleLeg", t "Partido único" "Single match"
          "TwoLegs", t "Ida y vuelta" "Two legs" ]

    let private tieResolutions () =
        [ "ExtraTimeThenPenalties", t "Alargue y penales" "Extra time, then penalties"
          "PenaltiesOnly", t "Penales" "Penalties"
          "ReplayAtNeutral", t "Desempate en cancha neutral" "Replay at a neutral ground"
          "AwayGoals", t "Gol de visitante" "Away goals" ]

    let private confederations = [ "UEFA"; "CONMEBOL"; "CONCACAF"; "CAF"; "AFC"; "OFC" ]

    // Quick picks cover the colours most real kits use; the hex field stays for anything else.
    let private swatches =
        [ "#FFFFFF"
          "#111111"
          "#E2353B"
          "#8B1E3F"
          "#F28C28"
          "#FFD100"
          "#009639"
          "#0B5D3B"
          "#6CACE4"
          "#0033A0"
          "#5B2C83"
          "#9AA3AE" ]

    // Canonical values per tier sit inside the ranges the label reads back, so a picked tier shows as picked.
    let private reputationTiers () =
        [ 2000, t "Modesto" "Modest"
          4000, t "Establecido" "Established"
          6000, t "Grande nacional" "National force"
          7500, t "Continental" "Continental"
          9000, t "Gigante" "Giant" ]

    let private tierOf (reputation: int) =
        match reputation with
        | r when r >= 8500 -> 9000
        | r when r >= 7000 -> 7500
        | r when r >= 5000 -> 6000
        | r when r >= 3000 -> 4000
        | _ -> 2000

    /// Squad line of a position: goalkeeper, defence, midfield, attack.
    let private lineOf (position: string) =
        match position with
        | "GK" -> 0
        | "DC"
        | "DL"
        | "DR"
        | "WBL"
        | "WBR" -> 1
        | "ST" -> 3
        | _ -> 2

    let private lines () =
        [ t "ARQ" "GK", Theme.Warning
          t "DEF" "DEF", Theme.Info
          t "MED" "MID", Theme.Success
          t "DEL" "ATT", Theme.Danger ]

    let private caption (text: string) =
        TextBlock.create
            [ TextBlock.text (text.ToUpperInvariant())
              TextBlock.fontSize 10.0
              TextBlock.fontWeight FontWeight.SemiBold
              TextBlock.foreground Theme.TextSub ]

    let private hint (text: string) =
        TextBlock.create
            [ TextBlock.text text
              TextBlock.fontSize 11.0
              TextBlock.foreground Theme.TextMuted
              TextBlock.textWrapping TextWrapping.Wrap ]

    let private field (label: string) (input: IView) : IView =
        StackPanel.create [ StackPanel.spacing 4.0; StackPanel.children [ caption label; input ] ]

    let private fieldWithHint (label: string) (help: string) (input: IView) : IView =
        StackPanel.create
            [ StackPanel.spacing 4.0
              StackPanel.children [ caption label; input; hint help ] ]

    let private columns (spec: string) (views: IView list) : IView =
        Grid.create
            [ Grid.columnDefinitions spec
              Grid.children (
                  views
                  |> List.mapi (fun index view -> Border.create [ Grid.column (index * 2); Border.child view ] :> IView)
              ) ]

    // Handlers come before values: when a control is re-bound to another club or player, the new
    // handler must already be attached when the new value lands, or the old entity receives it.
    let private textInput (value: string) (placeholder: string) (onChange: string -> unit) : IView =
        TextBox.create
            [ TextBox.onTextChanged (onChange, SubPatchOptions.Always)
              TextBox.text value
              TextBox.placeholderText placeholder ]

    let private intInput (value: int) (minimum: int) (maximum: int) (onChange: int -> unit) : IView =
        NumericUpDown.create
            [ NumericUpDown.onValueChanged (
                  (fun next ->
                      next
                      |> Option.ofNullable
                      |> Option.map int
                      |> Option.iter (fun value -> onChange (Math.Clamp(value, minimum, maximum)))),
                  SubPatchOptions.Always
              )
              NumericUpDown.minimum (decimal minimum)
              NumericUpDown.maximum (decimal maximum)
              NumericUpDown.formatString "0"
              NumericUpDown.value (decimal value) ]

    let private compactIntInput (value: int) (minimum: int) (maximum: int) (onChange: int -> unit) : IView =
        NumericUpDown.create
            [ NumericUpDown.onValueChanged (
                  (fun next ->
                      next
                      |> Option.ofNullable
                      |> Option.map int
                      |> Option.iter (fun value -> onChange (Math.Clamp(value, minimum, maximum)))),
                  SubPatchOptions.Always
              )
              NumericUpDown.showButtonSpinner false
              NumericUpDown.minimum (decimal minimum)
              NumericUpDown.maximum (decimal maximum)
              NumericUpDown.formatString "0"
              NumericUpDown.value (decimal value) ]

    let private select (items: string list) (selected: string) (onChange: string -> unit) : IView =
        ComboBox.create
            [ ComboBox.onSelectedItemChanged (
                  (fun item ->
                      match item with
                      | :? string as value when value <> selected -> onChange value
                      | _ -> ()),
                  SubPatchOptions.Always
              )
              ComboBox.dataItems items
              ComboBox.selectedItem selected
              ComboBox.horizontalAlignment HorizontalAlignment.Stretch ]

    let private labeledSelect (items: (string * string) list) (selected: string) (onChange: string -> unit) : IView =
        let label =
            items
            |> List.tryFind (fun (value, _) -> value = selected)
            |> Option.map snd
            |> Option.defaultValue selected

        select (items |> List.map snd) label (fun chosen ->
            items
            |> List.tryFind (fun (_, text) -> text = chosen)
            |> Option.iter (fun (value, _) -> onChange value))

    let private iconButton (icon: Material.Icons.MaterialIconKind) (tip: string) (isEnabled: bool) onClick : IView =
        Button.create
            [ Button.onClick onClick
              Button.isEnabled isEnabled
              Button.background "Transparent"
              Button.borderBrush Theme.Border
              Button.borderThickness 1.0
              Button.cornerRadius Theme.RadiusSm
              Button.padding (8.0, 6.0)
              ToolTip.tip tip
              Button.content (Icons.iconSm icon (if isEnabled then Theme.TextSub else Theme.TextDim)) ]

    let private removeButton (tip: string) onClick : IView =
        Button.create
            [ Button.onClick onClick
              Button.background "Transparent"
              Button.borderThickness 0.0
              Button.padding (6.0, 4.0)
              ToolTip.tip tip
              Button.content (Icons.iconSm IconName.close Theme.Danger) ]

    let private actionButton
        (isPrimary: bool)
        (label: string)
        (icon: Material.Icons.MaterialIconKind option)
        onClick
        : IView =
        let foreground = if isPrimary then Theme.BgSidebar else Theme.TextMain

        Button.create
            [ Button.onClick onClick
              Button.background (if isPrimary then Theme.Accent else "Transparent")
              Button.borderBrush (if isPrimary then Theme.Accent else Theme.Border)
              Button.borderThickness 1.0
              Button.cornerRadius Theme.RadiusMd
              Button.padding (12.0, 6.0)
              Button.verticalAlignment VerticalAlignment.Center
              Button.content (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 6.0
                        StackPanel.children
                            [ match icon with
                              | Some kind -> Icons.iconSm kind foreground
                              | None -> ()
                              TextBlock.create
                                  [ TextBlock.text label
                                    TextBlock.fontSize 12.0
                                    TextBlock.fontWeight (if isPrimary then FontWeight.Bold else FontWeight.SemiBold)
                                    TextBlock.foreground foreground ] ] ]
              ) ]

    let private pillButton (label: string) (isActive: bool) onClick : IView =
        Button.create
            [ Button.onClick onClick
              Button.background (if isActive then Theme.AccentLight else "Transparent")
              Button.borderBrush (
                  if isActive then
                      Theme.withAlpha "77" Theme.Accent
                  else
                      Theme.Border
              )
              Button.borderThickness 1.0
              Button.cornerRadius Theme.RadiusPill
              Button.padding (14.0, 5.0)
              Button.margin (0.0, 0.0, 6.0, 0.0)
              Button.content (
                  TextBlock.create
                      [ TextBlock.text label
                        TextBlock.fontSize 12.0
                        TextBlock.fontWeight (if isActive then FontWeight.Bold else FontWeight.Normal)
                        TextBlock.foreground (if isActive then Theme.Accent else Theme.TextSub) ]
              ) ]

    let private card (content: IView) : IView =
        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusLg
              Border.padding 16.0
              Border.child content ]

    let private sectionTitle (title: string) (detail: string) : IView =
        StackPanel.create
            [ StackPanel.spacing 2.0
              StackPanel.children
                  [ TextBlock.create
                        [ TextBlock.text title
                          TextBlock.fontSize 16.0
                          TextBlock.fontWeight FontWeight.Bold
                          TextBlock.foreground Theme.TextMain ]
                    if detail <> "" then
                        hint detail ] ]

    let private chip (label: string) (color: string) : IView =
        Border.create
            [ Border.background (Theme.withAlpha "18" color)
              Border.borderBrush (Theme.withAlpha "55" color)
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusPill
              Border.padding (9.0, 3.0)
              Border.verticalAlignment VerticalAlignment.Center
              Border.child (
                  TextBlock.create
                      [ TextBlock.text label
                        TextBlock.fontSize 10.0
                        TextBlock.fontWeight FontWeight.Bold
                        TextBlock.foreground color ]
              ) ]

    let private count (n: int) (singularEs: string) (pluralEs: string) (singularEn: string) (pluralEn: string) =
        if n = 1 then t $"1 {singularEs}" $"1 {singularEn}" else t $"{n} {pluralEs}" $"{n} {pluralEn}"

    let private isHexColor (value: string) =
        not (isNull value) && Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}$")

    let private kitOf (club: ClubEntryDto) : ClubKit =
        match club.Kit with
        | Some kit when isHexColor kit.Primary && isHexColor kit.Secondary ->
            { Primary = kit.Primary
              Secondary = kit.Secondary
              Pattern =
                match kit.Pattern with
                | "Stripes" -> Stripes
                | "Hoops" -> Hoops
                | "Halves" -> Halves
                | "Sash" -> Sash
                | _ -> Plain }
        | _ -> ClubIdentity.generatedKit club.Name

    let private countBadge (text: string) (isActive: bool) : IView =
        TextBlock.create
            [ DockPanel.dock Dock.Right
              TextBlock.text text
              TextBlock.fontSize 10.0
              TextBlock.fontWeight FontWeight.SemiBold
              TextBlock.foreground (if isActive then Theme.Accent else Theme.TextFaint)
              TextBlock.verticalAlignment VerticalAlignment.Center ]

    /// A full-width row button used by every list in the editor, so selection looks the same everywhere.
    let private rowButton (isActive: bool) ((horizontal, vertical): float * float) (content: IView) onClick : IView =
        Button.create
            [ Button.onClick onClick
              Button.background (if isActive then Theme.AccentLight else "Transparent")
              Button.borderThickness 0.0
              Button.cornerRadius Theme.RadiusSm
              Button.padding (horizontal, vertical)
              Button.horizontalAlignment HorizontalAlignment.Stretch
              Button.horizontalContentAlignment HorizontalAlignment.Stretch
              Button.content content ]

    let private groupCaption (text: string) (action: IView option) : IView =
        DockPanel.create
            [ DockPanel.margin (8.0, 14.0, 4.0, 4.0)
              DockPanel.children
                  [ match action with
                    | Some view -> Border.create [ DockPanel.dock Dock.Right; Border.child view ]
                    | None -> ()
                    TextBlock.create
                        [ TextBlock.text (text.ToUpperInvariant())
                          TextBlock.fontSize 10.0
                          TextBlock.fontWeight FontWeight.Bold
                          TextBlock.foreground Theme.TextFaint
                          TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]

    let private smallAddButton (tip: string) onClick : IView =
        Button.create
            [ Button.onClick onClick
              Button.background "Transparent"
              Button.borderThickness 0.0
              Button.padding (4.0, 2.0)
              ToolTip.tip tip
              Button.content (Icons.iconSm IconName.add Theme.Accent) ]

    let private kitDtoOf (kit: ClubKit) : ClubKitDto =
        { Primary = kit.Primary
          Secondary = kit.Secondary
          Pattern = string kit.Pattern }

    let private leagueName (country: CountryDataDto) (level: int) =
        country.LeagueNames
        |> List.tryItem level
        |> Option.defaultValue (t $"División {level + 1}" $"Division {level + 1}")

    let private newCountryCode (countries: Map<string, CountryDataDto>) =
        [ for first in 'A' .. 'Z' do
              for second in 'A' .. 'Z' do
                  yield $"N{first}{second}" ]
        |> List.tryFind (fun code -> not (countries |> Map.containsKey code))
        |> Option.defaultValue "ZZZ"

    // ─── Start: library ───────────────────────────────────────────────────────

    let private actionCard
        (icon: Material.Icons.MaterialIconKind)
        (title: string)
        (description: string)
        (isPrimary: bool)
        onClick
        : IView =
        Button.create
            [ Button.onClick onClick
              Button.background (if isPrimary then Theme.AccentLight else Theme.BgCard)
              Button.borderBrush (
                  if isPrimary then
                      Theme.withAlpha "77" Theme.Accent
                  else
                      Theme.Border
              )
              Button.borderThickness 1.0
              Button.cornerRadius Theme.RadiusLg
              Button.padding 18.0
              Button.width 300.0
              Button.height 128.0
              Button.margin (0.0, 0.0, 12.0, 0.0)
              Button.horizontalContentAlignment HorizontalAlignment.Left
              Button.verticalContentAlignment VerticalAlignment.Top
              Button.content (
                  StackPanel.create
                      [ StackPanel.spacing 8.0
                        StackPanel.children
                            [ Icons.iconLg icon Theme.Accent
                              TextBlock.create
                                  [ TextBlock.text title
                                    TextBlock.fontSize 15.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground Theme.TextMain ]
                              TextBlock.create
                                  [ TextBlock.text description
                                    TextBlock.fontSize 11.0
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.textWrapping TextWrapping.Wrap ] ] ]
              ) ]

    let private librarySummary (entry: LibraryEntry) =
        if entry.Countries = 0 then
            t $"{entry.Competitions} competiciones internacionales" $"{entry.Competitions} international competitions"
        else
            // A zero player count is the normal case for generated squads and only adds noise.
            [ t $"{entry.Clubs} clubes" $"{entry.Clubs} clubs"
              if entry.Countries > 1 then
                  t $"{entry.Countries} países" $"{entry.Countries} countries"
              if entry.Players > 0 then
                  t $"{entry.Players} jugadores" $"{entry.Players} players"
              if entry.Competitions > 0 then
                  t $"{entry.Competitions} copas" $"{entry.Competitions} cups" ]
            |> String.concat " · "

    /// The whole tile is the action: one click opens the package, with the verb shown on the tile itself.
    let private libraryTile (entry: LibraryEntry) (verb: string) onClick : IView =
        let code =
            match entry.CountryNames with
            | [ (code, _) ] -> code
            | _ when entry.Countries = 0 -> "INT"
            | _ -> "MOD"

        Button.create
            [ Button.onClick onClick
              Button.background Theme.BgCard
              Button.borderBrush Theme.Border
              Button.borderThickness 1.0
              Button.cornerRadius Theme.RadiusMd
              Button.padding (14.0, 12.0)
              Button.width 260.0
              Button.margin (0.0, 0.0, 10.0, 10.0)
              Button.horizontalContentAlignment HorizontalAlignment.Stretch
              ToolTip.tip $"{entry.ModId} · v{entry.Version}"
              Button.content (
                  DockPanel.create
                      [ DockPanel.children
                            [ Border.create
                                  [ DockPanel.dock Dock.Left
                                    Border.width 42.0
                                    Border.height 42.0
                                    Border.cornerRadius Theme.RadiusSm
                                    Border.background Theme.BgElevated
                                    Border.margin (0.0, 0.0, 12.0, 0.0)
                                    Border.child (
                                        TextBlock.create
                                            [ TextBlock.text code
                                              TextBlock.fontSize 11.0
                                              TextBlock.fontWeight FontWeight.Black
                                              TextBlock.foreground Theme.Accent
                                              TextBlock.horizontalAlignment HorizontalAlignment.Center
                                              TextBlock.verticalAlignment VerticalAlignment.Center ]
                                    ) ]
                              StackPanel.create
                                  [ StackPanel.spacing 2.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text entry.Title
                                                TextBlock.fontSize 14.0
                                                TextBlock.fontWeight FontWeight.SemiBold
                                                TextBlock.foreground Theme.TextMain
                                                TextBlock.textTrimming TextTrimming.CharacterEllipsis ]
                                          TextBlock.create
                                              [ TextBlock.text (librarySummary entry)
                                                TextBlock.fontSize 11.0
                                                TextBlock.foreground Theme.TextMuted
                                                TextBlock.textTrimming TextTrimming.CharacterEllipsis ]
                                          TextBlock.create
                                              [ TextBlock.text verb
                                                TextBlock.fontSize 11.0
                                                TextBlock.fontWeight FontWeight.SemiBold
                                                TextBlock.foreground Theme.Accent ] ] ] ] ]
              ) ]

    let private librarySection (title: string) (detail: string) (tiles: IView list) (empty: string) : IView =
        StackPanel.create
            [ StackPanel.spacing 10.0
              StackPanel.children
                  [ sectionTitle title detail
                    if tiles.IsEmpty then
                        hint empty
                    else
                        WrapPanel.create [ WrapPanel.children tiles ] ] ]

    let private startView (state: ModEditorState) dispatch : IView =
        let userMods = state.Library |> List.filter (fun entry -> not entry.IsBuiltin)
        let baseWorld = state.Library |> List.filter _.IsBuiltin

        StackPanel.create
            [ StackPanel.spacing 28.0
              StackPanel.maxWidth 1100.0
              StackPanel.children
                  [ StackPanel.create
                        [ StackPanel.spacing 4.0
                          StackPanel.children
                              [ TextBlock.create
                                    [ TextBlock.text (t "Taller de mods" "Mod workshop")
                                      TextBlock.fontSize 26.0
                                      TextBlock.fontWeight FontWeight.Black
                                      TextBlock.foreground Theme.TextMain ]
                                hint (
                                    t
                                        "Creá tu propio mundo: países, ligas, clubes, camisetas y jugadores. Lo que publiques se juega en tu próxima carrera."
                                        "Build your own world: countries, leagues, clubs, kits and players. What you publish is played in your next career."
                                ) ] ]

                    match state.LibraryError with
                    | Some error -> UI.statusBanner Theme.Warning IconName.warning error
                    | None -> ()

                    StackPanel.create
                        [ StackPanel.orientation Orientation.Horizontal
                          StackPanel.children
                              [ if state.HasDraft then
                                    actionCard
                                        IconName.fileDocumentEdit
                                        (t "Seguir con el borrador" "Continue draft")
                                        (t "Volvé exactamente a donde lo dejaste." "Pick up exactly where you left off.")
                                        true
                                        (fun _ -> dispatch (ModEditorMsg ResumeDraft))
                                actionCard
                                    IconName.add
                                    (t "Mundo nuevo" "New world")
                                    (t
                                        "Empezá de cero y armá países, ligas y clubes propios."
                                        "Start from scratch with your own countries, leagues and clubs.")
                                    (not state.HasDraft)
                                    (fun _ -> dispatch (ModEditorMsg NewMod)) ] ]

                    librarySection
                        (t "Tus mods" "Your mods")
                        (t
                            "Paquetes en la carpeta Mods. Al publicarlos de nuevo se reemplaza la versión instalada."
                            "Packages in the Mods folder. Publishing again replaces the installed version.")
                        [ for entry in userMods do
                              libraryTile entry (t "Editar →" "Edit →") (fun _ ->
                                  dispatch (ModEditorMsg(OpenPackage(entry.Path, false)))) ]
                        (t
                            "Todavía no publicaste ningún mod. Lo que publiques aparece acá."
                            "You have not published a mod yet. What you publish shows up here.")

                    librarySection
                        (t "Editar una copia del mundo base" "Edit a copy of the base world")
                        (t
                            "El original queda intacto: tu mod lo reemplaza mientras esté activo."
                            "The original stays intact: your mod overrides it while active.")
                        [ for entry in baseWorld do
                              libraryTile entry (t "Editar una copia →" "Edit a copy →") (fun _ ->
                                  dispatch (ModEditorMsg(OpenPackage(entry.Path, true)))) ]
                        "" ] ]

    // ─── Header and navigation ────────────────────────────────────────────────

    let private statusOf (state: ModEditorState) =
        match state.PublishStatus with
        | Published _ when not state.IsDirty -> t "Publicado" "Published", Theme.Success
        | PublishFailed _ -> t "Falló la publicación" "Publish failed", Theme.Danger
        | _ when state.IsDirty -> t "Borrador guardado · sin publicar" "Draft saved · not published", Theme.Warning
        | _ -> t "Sin cambios" "No changes", Theme.TextMuted

    let private header (state: ModEditorState) dispatch : IView =
        let manifest = state.Document.Manifest
        let status, statusColor = statusOf state
        let errors = if state.Validated then state.Errors.Length else 0

        Border.create
            [ DockPanel.dock Dock.Top
              Border.background Theme.BgSidebar
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.padding (16.0, 10.0)
              Border.child (
                  DockPanel.create
                      [ DockPanel.children
                            [ StackPanel.create
                                  [ DockPanel.dock Dock.Right
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 6.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ iconButton
                                              Material.Icons.MaterialIconKind.Undo
                                              (t "Deshacer" "Undo")
                                              (not state.Past.IsEmpty)
                                              (fun _ -> dispatch (ModEditorMsg UndoMod))
                                          iconButton
                                              Material.Icons.MaterialIconKind.Redo
                                              (t "Rehacer" "Redo")
                                              (not state.Future.IsEmpty)
                                              (fun _ -> dispatch (ModEditorMsg RedoMod))
                                          Border.create [ Border.width 6.0 ]
                                          Button.create
                                              [ Button.onClick (fun _ -> dispatch (ModEditorMsg(SetTab Publish)))
                                                Button.background Theme.Accent
                                                Button.borderThickness 0.0
                                                Button.cornerRadius Theme.RadiusMd
                                                Button.padding (14.0, 7.0)
                                                Button.content (
                                                    StackPanel.create
                                                        [ StackPanel.orientation Orientation.Horizontal
                                                          StackPanel.spacing 6.0
                                                          StackPanel.children
                                                              [ Icons.iconSm
                                                                    Material.Icons.MaterialIconKind.Upload
                                                                    Theme.OnAccent
                                                                TextBlock.create
                                                                    [ TextBlock.text (t "Publicar" "Publish")
                                                                      TextBlock.fontSize 12.0
                                                                      TextBlock.fontWeight FontWeight.Bold
                                                                      TextBlock.foreground Theme.OnAccent
                                                                      TextBlock.verticalAlignment
                                                                          VerticalAlignment.Center ]
                                                                if errors > 0 then
                                                                    Border.create
                                                                        [ Border.background Theme.Danger
                                                                          Border.cornerRadius Theme.RadiusPill
                                                                          Border.padding (6.0, 0.0)
                                                                          Border.child (
                                                                              TextBlock.create
                                                                                  [ TextBlock.text (string errors)
                                                                                    TextBlock.fontSize 10.0
                                                                                    TextBlock.fontWeight FontWeight.Bold
                                                                                    TextBlock.foreground "#FFFFFF" ]
                                                                          ) ] ] ]
                                                ) ] ] ]
                              StackPanel.create
                                  [ StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 10.0
                                    StackPanel.children
                                        [ actionButton false (t "Biblioteca" "Library") (Some IconName.back) (fun _ ->
                                              dispatch (ModEditorMsg CloseDocument))
                                          TextBlock.create
                                              [ TextBlock.text "/"
                                                TextBlock.fontSize 16.0
                                                TextBlock.foreground Theme.TextFaint
                                                TextBlock.verticalAlignment VerticalAlignment.Center ]
                                          StackPanel.create
                                              [ StackPanel.spacing 1.0
                                                StackPanel.verticalAlignment VerticalAlignment.Center
                                                StackPanel.children
                                                    [ TextBlock.create
                                                          [ TextBlock.text (
                                                                if String.IsNullOrWhiteSpace manifest.Name then
                                                                    t "Mod sin nombre" "Untitled mod"
                                                                else
                                                                    manifest.Name
                                                            )
                                                            TextBlock.fontSize 16.0
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.foreground Theme.TextMain ]
                                                      TextBlock.create
                                                          [ TextBlock.text $"{manifest.Id} · v{manifest.Version}"
                                                            TextBlock.fontSize 10.0
                                                            TextBlock.foreground Theme.TextDim ] ] ]
                                          chip status statusColor ] ] ] ]
              ) ]

    let private navItem (isActive: bool) icon (label: string) (count: string) onClick : IView =
        rowButton
            isActive
            (10.0, 8.0)
            (DockPanel.create
                [ DockPanel.children
                      [ if count <> "" then
                            countBadge count isActive
                        StackPanel.create
                            [ StackPanel.orientation Orientation.Horizontal
                              StackPanel.spacing 10.0
                              StackPanel.children
                                  [ Icons.iconSm icon (if isActive then Theme.Accent else Theme.TextSub)
                                    TextBlock.create
                                        [ TextBlock.text label
                                          TextBlock.fontSize 13.0
                                          TextBlock.fontWeight (if isActive then FontWeight.SemiBold else FontWeight.Normal)
                                          TextBlock.foreground (if isActive then Theme.Accent else Theme.TextMain) ] ] ] ] ])
            onClick

    /// One outline for the whole mod: countries expand into their sections, so there is a single place to navigate.
    let private outline (state: ModEditorState) dispatch : IView =
        let importable =
            state.Library
            |> List.collect (fun entry ->
                entry.CountryNames
                |> List.filter (fun (code, _) -> not (state.Document.Countries |> Map.containsKey code))
                |> List.map (fun (code, name) -> $"{code} · {name} ({entry.Title})", (entry.Path, code)))

        let openSection code subTab =
            dispatch (ModEditorMsg(SetTab World))

            if state.SelectedCountryCode <> Some code then
                dispatch (ModEditorMsg(SelectCountry(Some code)))

            dispatch (ModEditorMsg(SetSubTab subTab))

        let countryItem (country: CountryDataDto) : IView =
            let code = country.Country.Code
            let isOpen = state.SelectedCountryCode = Some code && state.ActiveTab = World
            let isOverride = state.Document.Sources |> Map.containsKey code

            let section subTab (label: string) (count: string) : IView =
                let isActive = isOpen && state.ActiveSubTab = subTab

                rowButton
                    isActive
                    (10.0, 6.0)
                    (DockPanel.create
                        [ DockPanel.children
                              [ if count <> "" then
                                    countBadge count isActive
                                TextBlock.create
                                    [ TextBlock.text label
                                      TextBlock.fontSize 12.0
                                      TextBlock.fontWeight (if isActive then FontWeight.SemiBold else FontWeight.Normal)
                                      TextBlock.foreground (if isActive then Theme.Accent else Theme.TextSub) ] ] ])
                    (fun _ -> openSection code subTab)

            StackPanel.create
                [ StackPanel.spacing 1.0
                  StackPanel.children
                      [ rowButton
                            false
                            (10.0, 7.0)
                            (DockPanel.create
                                [ DockPanel.children
                                      [ if isOverride then
                                            Border.create
                                                [ DockPanel.dock Dock.Right
                                                  ToolTip.tip (t "Copia del mundo base" "Copy of the base world")
                                                  Border.child (Icons.iconSm IconName.fileDocumentEdit Theme.TextFaint) ]
                                        StackPanel.create
                                            [ StackPanel.orientation Orientation.Horizontal
                                              StackPanel.spacing 8.0
                                              StackPanel.children
                                                  [ Icons.iconSm
                                                        (if isOpen then IconName.expand else Nav.next)
                                                        Theme.TextFaint
                                                    TextBlock.create
                                                        [ TextBlock.text code
                                                          TextBlock.fontSize 11.0
                                                          TextBlock.fontWeight FontWeight.Black
                                                          TextBlock.foreground Theme.Accent
                                                          TextBlock.verticalAlignment VerticalAlignment.Center ]
                                                    TextBlock.create
                                                        [ TextBlock.text country.Country.Name
                                                          TextBlock.fontSize 13.0
                                                          TextBlock.fontWeight FontWeight.SemiBold
                                                          TextBlock.foreground Theme.TextMain
                                                          TextBlock.verticalAlignment VerticalAlignment.Center ] ] ] ] ])
                            (fun _ ->
                                if isOpen then
                                    dispatch (ModEditorMsg(SelectCountry None))
                                else
                                    openSection code state.ActiveSubTab)
                        if isOpen then
                            Border.create
                                [ Border.margin (17.0, 0.0, 0.0, 6.0)
                                  Border.padding (6.0, 0.0, 0.0, 0.0)
                                  Border.borderBrush Theme.Border
                                  Border.borderThickness (1.0, 0.0, 0.0, 0.0)
                                  Border.child (
                                      StackPanel.create
                                          [ StackPanel.spacing 1.0
                                            StackPanel.children
                                                [ section Clubs (t "Clubes" "Clubs") (string country.Clubs.Length)
                                                  section Leagues (t "Ligas" "Leagues") (string country.LeagueNames.Length)
                                                  section Cups (t "Copas" "Cups") (string country.Cups.Length)
                                                  section NamePool (t "Nombres" "Names") ""
                                                  section General (t "País" "Country") "" ] ]
                                  ) ] ] ]

        Border.create
            [ DockPanel.dock Dock.Left
              Border.width 236.0
              Border.background Theme.BgSidebar
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 1.0, 0.0)
              Border.child (
                  ScrollViewer.create
                      [ ScrollViewer.content (
                            StackPanel.create
                                [ StackPanel.margin 10.0
                                  StackPanel.spacing 1.0
                                  StackPanel.children
                                      [ navItem
                                            (state.ActiveTab = Details)
                                            IconName.info
                                            (t "Datos del mod" "Mod details")
                                            ""
                                            (fun _ -> dispatch (ModEditorMsg(SetTab Details)))
                                        groupCaption
                                            (t "Países" "Countries")
                                            (Some(
                                                smallAddButton (t "País nuevo" "New country") (fun _ ->
                                                    let code = newCountryCode state.Document.Countries

                                                    dispatch (
                                                        ModEditorMsg(
                                                            AddCountry(
                                                                UpdateModEditor.createEmptyCountry
                                                                    code
                                                                    (t "País nuevo" "New country")
                                                            )
                                                        )
                                                    )

                                                    openSection code General)
                                            ))
                                        if state.Document.Countries.IsEmpty then
                                            TextBlock.create
                                                [ TextBlock.text (
                                                      t
                                                          "Sin países. Creá uno con + o traé uno instalado."
                                                          "No countries. Create one with + or bring in an installed one."
                                                  )
                                                  TextBlock.fontSize 11.0
                                                  TextBlock.foreground Theme.TextMuted
                                                  TextBlock.textWrapping TextWrapping.Wrap
                                                  TextBlock.margin (8.0, 2.0, 8.0, 6.0) ]
                                        for country in state.Document.Countries.Values do
                                            countryItem country
                                        if not importable.IsEmpty then
                                            ComboBox.create
                                                [ ComboBox.onSelectedItemChanged (
                                                      (fun item ->
                                                          match item with
                                                          | :? string as label ->
                                                              importable
                                                              |> List.tryFind (fun (text, _) -> text = label)
                                                              |> Option.iter (fun (_, (path, code)) ->
                                                                  dispatch (ModEditorMsg(ImportCountry(path, code))))
                                                          | _ -> ()),
                                                      SubPatchOptions.Always
                                                  )
                                                  ComboBox.placeholderText (
                                                      t "+ Traer un país instalado" "+ Bring in an installed country"
                                                  )
                                                  ComboBox.dataItems (importable |> List.map fst)
                                                  ComboBox.fontSize 12.0
                                                  ComboBox.margin (0.0, 6.0, 0.0, 0.0)
                                                  ComboBox.horizontalAlignment HorizontalAlignment.Stretch ]
                                        groupCaption (t "Torneos" "Tournaments") None
                                        navItem
                                            (state.ActiveTab = International)
                                            IconName.league
                                            (t "Internacional" "International")
                                            (string state.Document.InternationalComps.Length)
                                            (fun _ -> dispatch (ModEditorMsg(SetTab International))) ] ]
                        ) ]
              ) ]

    // ─── Details ──────────────────────────────────────────────────────────────

    let private detailsTab (state: ModEditorState) dispatch : IView =
        let manifest = state.Document.Manifest

        let edit fieldName (updater: ModManifestDto -> ModManifestDto) =
            dispatch (ModEditorMsg(UpdateManifest(fieldName, updater)))

        let splitLines (text: string) =
            text.Split([| '\n'; '\r'; ',' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.map _.Trim()
            |> Array.filter ((<>) "")
            |> Array.distinct
            |> Array.toList

        StackPanel.create
            [ StackPanel.spacing 16.0
              StackPanel.children
                  [ sectionTitle
                        (t "Datos del mod" "Mod details")
                        (t
                            "Cómo se presenta tu mod en la lista de mods del juego."
                            "How your mod appears in the game's mod list.")
                    card (
                        StackPanel.create
                            [ StackPanel.spacing 14.0
                              StackPanel.children
                                  [ columns
                                        "*, 16, *"
                                        [ field
                                              (t "Nombre" "Name")
                                              (textInput manifest.Name "My World" (fun value ->
                                                  edit "name" (fun m -> { m with Name = value })))
                                          field
                                              (t "Autor" "Author")
                                              (textInput manifest.Author (t "Tu nombre" "Your name") (fun value ->
                                                  edit "author" (fun m -> { m with Author = value }))) ]
                                    columns
                                        "*, 16, *"
                                        [ fieldWithHint
                                              (t "Identificador" "Package ID")
                                              (t
                                                  "Único y estable: si lo cambiás, el juego lo trata como otro mod."
                                                  "Unique and stable: changing it makes the game treat it as another mod.")
                                              (textInput manifest.Id "community.my-world" (fun value ->
                                                  edit "id" (fun m -> { m with Id = value.Trim() })))
                                          fieldWithHint
                                              (t "Versión" "Version")
                                              (t "Subila cuando publiques cambios." "Raise it when you publish changes.")
                                              (textInput manifest.Version "1.0.0" (fun value ->
                                                  edit "version" (fun m -> { m with Version = value.Trim() }))) ]
                                    field
                                        (t "Descripción" "Description")
                                        (TextBox.create
                                            [ TextBox.onTextChanged (
                                                  (fun value ->
                                                      edit "description" (fun m -> { m with Description = value })),
                                                  SubPatchOptions.Always
                                              )
                                              TextBox.text manifest.Description
                                              TextBox.acceptsReturn true
                                              TextBox.textWrapping TextWrapping.Wrap
                                              TextBox.height 90.0 ]) ] ]
                    )
                    Expander.create
                        [ Expander.header (t "Opciones avanzadas" "Advanced options")
                          Expander.horizontalAlignment HorizontalAlignment.Stretch
                          Expander.content (
                              StackPanel.create
                                  [ StackPanel.spacing 14.0
                                    StackPanel.margin (0.0, 8.0, 0.0, 0.0)
                                    StackPanel.children
                                        [ fieldWithHint
                                              (t "Prioridad de carga" "Load priority")
                                              (t
                                                  "Entre mods que no dependen entre sí, el número más alto se carga después."
                                                  "Between mods that do not depend on each other, the higher number loads later.")
                                              (intInput manifest.Priority 0 10000 (fun value ->
                                                  edit "priority" (fun m -> { m with Priority = value })))
                                          fieldWithHint
                                              (t "Dependencias extra" "Extra dependencies")
                                              (t
                                                  "Identificadores de otros mods, uno por línea. Los mods que tu mod modifica se agregan solos."
                                                  "Other mod IDs, one per line. Mods yours overrides are added automatically.")
                                              (TextBox.create
                                                  [ TextBox.onTextChanged (
                                                        (fun text ->
                                                            edit "dependencies" (fun m ->
                                                                { m with
                                                                    Dependencies = splitLines text })),
                                                        SubPatchOptions.Always
                                                    )
                                                    TextBox.text (String.concat "\n" manifest.Dependencies)
                                                    TextBox.acceptsReturn true
                                                    TextBox.height 70.0 ]) ] ]
                          ) ] ] ]

    // ─── World: country sections ──────────────────────────────────────────────

    let private generalSection (state: ModEditorState) (country: CountryDataDto) dispatch : IView =
        let code = country.Country.Code
        let isOverride = state.Document.Sources |> Map.containsKey code

        let edit fieldName updater =
            dispatch (ModEditorMsg(EditCountry(code, fieldName, updater)))

        StackPanel.create
            [ StackPanel.spacing 14.0
              StackPanel.children
                  [ card (
                        columns
                            "2*, 16, *, 16, *"
                            [ field
                                  (t "Nombre" "Name")
                                  (textInput country.Country.Name "" (fun value ->
                                      edit "name" (fun c ->
                                          { c with
                                              Country = { c.Country with Name = value } })))
                              fieldWithHint
                                  (t "Código" "Code")
                                  (if isOverride then
                                       t
                                           "Fijo: identifica al país que reemplazás."
                                           "Fixed: it identifies the country you override."
                                   else
                                       t "Tres letras, único." "Three letters, unique.")
                                  (TextBox.create
                                      [ TextBox.onTextChanged (
                                            (fun value ->
                                                let value = value.Trim().ToUpperInvariant()

                                                if value.Length = 3 then
                                                    edit "code" (fun c ->
                                                        { c with
                                                            Country = { c.Country with Code = value } })),
                                            SubPatchOptions.Always
                                        )
                                        TextBox.text code
                                        TextBox.maxLength 3
                                        TextBox.isEnabled (not isOverride) ])
                              field
                                  (t "Confederación" "Confederation")
                                  (select confederations country.Country.Confederation (fun value ->
                                      edit "confederation" (fun c ->
                                          { c with
                                              Country = { c.Country with Confederation = value } }))) ]
                    )
                    // Removal is kept apart from the fields so it is never hit while editing them.
                    card (
                        DockPanel.create
                            [ DockPanel.children
                                  [ Border.create
                                        [ DockPanel.dock Dock.Right
                                          Border.child (
                                              actionButton
                                                  false
                                                  (t "Quitar país" "Remove country")
                                                  (Some Material.Icons.MaterialIconKind.TrashCanOutline)
                                                  (fun _ -> dispatch (ModEditorMsg(RemoveCountry code)))
                                          ) ]
                                    sectionTitle
                                        (t "Quitar este país del mod" "Remove this country from the mod")
                                        (if isOverride then
                                             t
                                                 "El país original del juego vuelve a usarse."
                                                 "The game's original country is used again."
                                         else
                                             t
                                                 "Se borran sus ligas, clubes y jugadores. Podés deshacerlo."
                                                 "Its leagues, clubs and players are deleted. You can undo it.") ] ]
                    ) ] ]

    let private leaguesSection (country: CountryDataDto) dispatch : IView =
        let code = country.Country.Code

        let update updater =
            dispatch (ModEditorMsg(UpdateCountry(code, updater)))

        let edit index fieldName updater =
            dispatch (ModEditorMsg(EditCountry(code, $"league:{index}:{fieldName}", updater)))

        let setRule index (updater: LeagueRulesDto -> LeagueRulesDto) (c: CountryDataDto) =
            { c with
                LeagueRules =
                    c.LeagueRules
                    |> List.mapi (fun j rule -> if j = index then updater rule else rule) }

        let removeAt index (values: 'a list) =
            values |> List.indexed |> List.filter (fun (j, _) -> j <> index) |> List.map snd

        let spec = "40, 12, *, 12, 96, 12, 96, 12, 80, 12, 32"

        StackPanel.create
            [ StackPanel.spacing 8.0
              StackPanel.children
                  [ hint (
                        t
                            "La pirámide de arriba hacia abajo: el nivel 1 es la máxima categoría. Cada club elige su división en Clubes."
                            "The pyramid from top to bottom: level 1 is the top flight. Each club picks its division under Clubs."
                    )
                    Border.create
                        [ Border.padding (16.0, 4.0)
                          Border.child (
                              columns
                                  spec
                                  [ caption (t "Nivel" "Level")
                                    caption (t "Liga" "League")
                                    caption (t "Pts. victoria" "Win pts")
                                    caption (t "Pts. empate" "Draw pts")
                                    caption (t "Clubes" "Clubs")
                                    TextBlock.create [] ]
                          ) ]
                    for index in 0 .. country.LeagueNames.Length - 1 do
                        let rule =
                            country.LeagueRules
                            |> List.tryItem index
                            |> Option.defaultValue (UpdateModEditor.createEmptyLeague "")

                        let clubs =
                            country.Clubs
                            |> List.filter (fun club -> club.LeagueLevel = index)
                            |> List.length

                        Border.create
                            [ Border.background Theme.BgCard
                              Border.borderBrush Theme.Border
                              Border.borderThickness 1.0
                              Border.cornerRadius Theme.RadiusMd
                              Border.padding (16.0, 10.0)
                              Border.child (
                                  columns
                                      spec
                                      [ Border.create
                                            [ Border.width 30.0
                                              Border.height 30.0
                                              Border.cornerRadius Theme.RadiusPill
                                              Border.background (if index = 0 then Theme.AccentLight else Theme.BgElevated)
                                              Border.horizontalAlignment HorizontalAlignment.Left
                                              Border.child (
                                                  TextBlock.create
                                                      [ TextBlock.text (string (index + 1))
                                                        TextBlock.fontWeight FontWeight.Black
                                                        TextBlock.foreground (
                                                            if index = 0 then Theme.Accent else Theme.TextSub
                                                        )
                                                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                                                        TextBlock.verticalAlignment VerticalAlignment.Center ]
                                              ) ]
                                        textInput country.LeagueNames.[index] "" (fun value ->
                                            edit index "name" (fun c ->
                                                { c with
                                                    LeagueNames =
                                                        c.LeagueNames
                                                        |> List.mapi (fun j n -> if j = index then value else n) }))
                                        compactIntInput rule.PointsForWin 0 10 (fun value ->
                                            edit index "win" (setRule index (fun r -> { r with PointsForWin = value })))
                                        compactIntInput rule.PointsForDraw 0 10 (fun value ->
                                            edit index "draw" (setRule index (fun r -> { r with PointsForDraw = value })))
                                        TextBlock.create
                                            [ TextBlock.text (string clubs)
                                              TextBlock.fontSize 15.0
                                              TextBlock.fontWeight FontWeight.Bold
                                              TextBlock.foreground (if clubs = 0 then Theme.Warning else Theme.TextMain)
                                              TextBlock.verticalAlignment VerticalAlignment.Center ]
                                        removeButton (t "Quitar liga" "Remove league") (fun _ ->
                                            update (fun c ->
                                                { c with
                                                    LeagueNames = removeAt index c.LeagueNames
                                                    LeagueRules = removeAt index c.LeagueRules })) ]
                              ) ]
                    StackPanel.create
                        [ StackPanel.orientation Orientation.Horizontal
                          StackPanel.margin (0.0, 6.0, 0.0, 0.0)
                          StackPanel.children
                              [ actionButton true (t "Agregar división" "Add division") (Some IconName.add) (fun _ ->
                                    update (fun c ->
                                        let name =
                                            t
                                                $"División {c.LeagueNames.Length + 1}"
                                                $"Division {c.LeagueNames.Length + 1}"

                                        { c with
                                            LeagueNames = c.LeagueNames @ [ name ]
                                            LeagueRules = c.LeagueRules @ [ UpdateModEditor.createEmptyLeague name ] })) ] ] ] ]

    // ─── World: clubs and players ─────────────────────────────────────────────

    let private addClub (country: CountryDataDto) (level: int) dispatch =
        dispatch (
            ModEditorMsg(
                UpdateCountry(
                    country.Country.Code,
                    fun c ->
                        { c with
                            Clubs = c.Clubs @ [ UpdateModEditor.createEmptyClub (t "Club nuevo" "New club") level ] }
                )
            )
        )

        dispatch (ModEditorMsg(SelectEditorClub(Some country.Clubs.Length)))

    let private clubList (state: ModEditorState) (country: CountryDataDto) dispatch : IView =
        let query = state.SearchQuery.Trim().ToLowerInvariant()

        let clubButton index (club: ClubEntryDto) : IView =
            let isSelected = state.SelectedClubIndex = Some index

            rowButton
                isSelected
                (8.0, 6.0)
                (DockPanel.create
                    [ DockPanel.children
                          [ if not club.Players.IsEmpty then
                                Border.create
                                    [ DockPanel.dock Dock.Right
                                      ToolTip.tip (t "Jugadores definidos" "Defined players")
                                      Border.child (countBadge (string club.Players.Length) isSelected) ]
                            StackPanel.create
                                [ StackPanel.orientation Orientation.Horizontal
                                  StackPanel.spacing 9.0
                                  StackPanel.children
                                      [ UI.crest (kitOf club) 20.0
                                        TextBlock.create
                                            [ TextBlock.text club.Name
                                              TextBlock.fontSize 13.0
                                              TextBlock.fontWeight (
                                                  if isSelected then FontWeight.SemiBold else FontWeight.Normal
                                              )
                                              TextBlock.foreground (if isSelected then Theme.Accent else Theme.TextMain)
                                              TextBlock.textTrimming TextTrimming.CharacterEllipsis
                                              TextBlock.verticalAlignment VerticalAlignment.Center ] ] ] ] ])
                (fun _ -> dispatch (ModEditorMsg(SelectEditorClub(Some index))))

        let levels =
            country.Clubs
            |> List.map _.LeagueLevel
            |> List.append [ 0 .. country.LeagueNames.Length - 1 ]
            |> List.distinct
            |> List.sort

        Border.create
            [ DockPanel.dock Dock.Left
              Border.width 270.0
              Border.background Theme.BgSubtle
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 1.0, 0.0)
              Border.child (
                  DockPanel.create
                      [ DockPanel.children
                            [ Border.create
                                  [ DockPanel.dock Dock.Top
                                    Border.padding (10.0, 10.0, 10.0, 0.0)
                                    Border.child (
                                        TextBox.create
                                            [ TextBox.onTextChanged (
                                                  (fun text -> dispatch (ModEditorMsg(SearchMod text))),
                                                  SubPatchOptions.Always
                                              )
                                              TextBox.text state.SearchQuery
                                              TextBox.placeholderText (t "Buscar club..." "Search club...") ]
                                    ) ]
                              ScrollViewer.create
                                  [ ScrollViewer.content (
                                        StackPanel.create
                                            [ StackPanel.margin (6.0, 0.0, 6.0, 12.0)
                                              StackPanel.spacing 1.0
                                              StackPanel.children
                                                  [ for level in levels do
                                                        let clubs =
                                                            country.Clubs
                                                            |> List.indexed
                                                            |> List.filter (fun (_, club) ->
                                                                club.LeagueLevel = level
                                                                && (query = "" || club.Name.ToLowerInvariant().Contains query))

                                                        groupCaption
                                                            $"{leagueName country level} · {clubs.Length}"
                                                            (Some(
                                                                smallAddButton
                                                                    (t
                                                                        $"Club nuevo en {leagueName country level}"
                                                                        $"New club in {leagueName country level}")
                                                                    (fun _ -> addClub country level dispatch)
                                                            ))

                                                        if clubs.IsEmpty && query = "" then
                                                            TextBlock.create
                                                                [ TextBlock.text (
                                                                      t "Sin clubes: agregá con +" "No clubs: add with +"
                                                                  )
                                                                  TextBlock.fontSize 11.0
                                                                  TextBlock.foreground Theme.TextFaint
                                                                  TextBlock.margin (10.0, 0.0, 0.0, 4.0) ]
                                                        else
                                                            for index, club in clubs do
                                                                clubButton index club ] ]
                                    ) ] ] ]
              ) ]

    let private swatchButton (color: string) (isSelected: bool) onClick : IView =
        Button.create
            [ Button.onClick onClick
              Button.background color
              Button.width 22.0
              Button.height 22.0
              Button.padding 0.0
              Button.margin (0.0, 0.0, 6.0, 0.0)
              Button.cornerRadius Theme.RadiusXs
              Button.borderBrush (if isSelected then Theme.Accent else Theme.BorderLight)
              Button.borderThickness (if isSelected then 2.0 else 1.0)
              ToolTip.tip color ]

    let private kitEditor (club: ClubEntryDto) (edit: string -> (ClubEntryDto -> ClubEntryDto) -> unit) : IView =
        let kit = kitOf club

        let setKit fieldName (updater: ClubKitDto -> ClubKitDto) =
            edit fieldName (fun c ->
                { c with
                    Kit = Some(updater (kitDtoOf (kitOf c))) })

        let colorRow fieldName (label: string) (value: string) (apply: string -> ClubKitDto -> ClubKitDto) : IView =
            StackPanel.create
                [ StackPanel.spacing 6.0
                  StackPanel.children
                      [ caption label
                        StackPanel.create
                            [ StackPanel.orientation Orientation.Horizontal
                              StackPanel.children
                                  [ for swatch in swatches do
                                        swatchButton swatch (String.Equals(swatch, value, StringComparison.OrdinalIgnoreCase)) (fun _ ->
                                            setKit fieldName (apply swatch))
                                    TextBox.create
                                        [ TextBox.onTextChanged (
                                              (fun text ->
                                                  let text = text.Trim()
                                                  let text = if text.StartsWith "#" then text else "#" + text

                                                  if isHexColor text then
                                                      setKit fieldName (apply (text.ToUpperInvariant()))),
                                              SubPatchOptions.Always
                                          )
                                          TextBox.text value
                                          TextBox.width 92.0
                                          TextBox.fontSize 12.0
                                          TextBox.margin (4.0, 0.0, 0.0, 0.0)
                                          TextBox.maxLength 7 ] ] ] ] ]

        let patternButton (pattern: KitPattern, label: string) : IView =
            let isActive = kit.Pattern = pattern

            Button.create
                [ Button.onClick (fun _ -> setKit "kit-pattern" (fun k -> { k with Pattern = string pattern }))
                  Button.background (if isActive then Theme.AccentLight else "Transparent")
                  Button.borderBrush (
                      if isActive then
                          Theme.withAlpha "77" Theme.Accent
                      else
                          Theme.Border
                  )
                  Button.borderThickness 1.0
                  Button.cornerRadius Theme.RadiusMd
                  Button.padding (10.0, 6.0)
                  Button.margin (0.0, 0.0, 8.0, 0.0)
                  Button.content (
                      StackPanel.create
                          [ StackPanel.spacing 4.0
                            StackPanel.children
                                [ Border.create
                                      [ Border.horizontalAlignment HorizontalAlignment.Center
                                        Border.child (UI.shirt { kit with Pattern = pattern } "" 34.0 Theme.Border 1.0) ]
                                  TextBlock.create
                                      [ TextBlock.text label
                                        TextBlock.fontSize 11.0
                                        TextBlock.fontWeight (if isActive then FontWeight.Bold else FontWeight.Normal)
                                        TextBlock.foreground (if isActive then Theme.Accent else Theme.TextSub)
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
                  ) ]

        StackPanel.create
            [ StackPanel.spacing 14.0
              StackPanel.children
                  [ sectionTitle
                        (t "Camiseta" "Kit")
                        (if club.Kit.IsNone then
                             t
                                 "Automática según el nombre. Elegí un color o un diseño para fijarla."
                                 "Automatic from the name. Pick a colour or a pattern to fix it."
                         else
                             "")
                    colorRow "kit-primary" (t "Color principal" "Main colour") kit.Primary (fun value k ->
                        { k with Primary = value })
                    colorRow "kit-secondary" (t "Color secundario" "Second colour") kit.Secondary (fun value k ->
                        { k with Secondary = value })
                    StackPanel.create
                        [ StackPanel.spacing 6.0
                          StackPanel.children
                              [ caption (t "Diseño" "Pattern")
                                StackPanel.create
                                    [ StackPanel.orientation Orientation.Horizontal
                                      StackPanel.children [ for option in patterns () do patternButton option ] ] ] ] ] ]

    let private lineBadge (position: string) : IView =
        let label, color = lines () |> List.item (lineOf position)

        Border.create
            [ Border.background (Theme.withAlpha "26" color)
              Border.cornerRadius Theme.RadiusXs
              Border.height 26.0
              Border.verticalAlignment VerticalAlignment.Center
              Border.child (
                  TextBlock.create
                      [ TextBlock.text label
                        TextBlock.fontSize 10.0
                        TextBlock.fontWeight FontWeight.Black
                        TextBlock.foreground color
                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                        TextBlock.verticalAlignment VerticalAlignment.Center ]
              ) ]

    let private playerTable
        (state: ModEditorState)
        (code: string)
        (clubIndex: int)
        (club: ClubEntryDto)
        (edit: string -> (ClubEntryDto -> ClubEntryDto) -> unit)
        (update: (ClubEntryDto -> ClubEntryDto) -> unit)
        dispatch
        : IView =
        let query = state.PlayerQuery.Trim().ToLowerInvariant()
        let spec = "40, 8, 3*, 8, 84, 8, 56, 8, 56, 8, 56, 8, 116, 8, 60, 8, 32"

        let setPlayer index (updater: PlayerEntryDto -> PlayerEntryDto) (c: ClubEntryDto) =
            { c with
                Players = c.Players |> List.mapi (fun j p -> if j = index then updater p else p) }

        let editPlayer index fieldName updater =
            edit $"player:{index}:{fieldName}" (setPlayer index updater)

        let ratingInput (value: int) onChange : IView =
            let color =
                match value with
                | v when v >= 80 -> Theme.Success
                | v when v >= 65 -> Theme.TextMain
                | _ -> Theme.TextMuted

            NumericUpDown.create
                [ NumericUpDown.onValueChanged (
                      (fun next ->
                          next
                          |> Option.ofNullable
                          |> Option.map int
                          |> Option.iter (fun value -> onChange (Math.Clamp(value, 1, 100)))),
                      SubPatchOptions.Always
                  )
                  NumericUpDown.showButtonSpinner false
                  NumericUpDown.minimum 1M
                  NumericUpDown.maximum 100M
                  NumericUpDown.formatString "0"
                  NumericUpDown.foreground color
                  NumericUpDown.fontWeight FontWeight.Bold
                  NumericUpDown.value (decimal value) ]

        let row index (player: PlayerEntryDto) : IView =
            Border.create
                [ Border.borderBrush Theme.Border
                  Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                  Border.padding (0.0, 5.0)
                  Border.child (
                      columns
                          spec
                          [ lineBadge player.Position
                            textInput player.Name (t "Nombre y apellido" "Full name") (fun value ->
                                editPlayer index "name" (fun p -> { p with Name = value }))
                            select positions player.Position (fun value ->
                                editPlayer index "position" (fun p -> { p with Position = value }))
                            compactIntInput player.Age 15 45 (fun value ->
                                editPlayer index "age" (fun p -> { p with Age = value }))
                            ratingInput player.Rating (fun value ->
                                editPlayer index "rating" (fun p ->
                                    { p with
                                        Rating = value
                                        Potential = max value p.Potential }))
                            ratingInput player.Potential (fun value ->
                                editPlayer index "potential" (fun p ->
                                    { p with
                                        Potential = max value p.Rating }))
                            labeledSelect (feet ()) (player.PreferredFoot |> Option.defaultValue "Auto") (fun value ->
                                editPlayer index "foot" (fun p ->
                                    { p with
                                        PreferredFoot = (if value = "Auto" then None else Some value) }))
                            TextBox.create
                                [ TextBox.onTextChanged (
                                      (fun value ->
                                          let value = value.Trim().ToUpperInvariant()

                                          editPlayer index "nationality" (fun p ->
                                              { p with
                                                  Nationality = (if value = "" then None else Some value) })),
                                      SubPatchOptions.Always
                                  )
                                  TextBox.text (player.Nationality |> Option.defaultValue "")
                                  TextBox.placeholderText code
                                  TextBox.maxLength 3 ]
                            removeButton (t "Quitar jugador" "Remove player") (fun _ ->
                                update (fun c ->
                                    { c with
                                        Players =
                                            c.Players
                                            |> List.indexed
                                            |> List.filter (fun (j, _) -> j <> index)
                                            |> List.map snd })) ]
                  ) ]

        let visible =
            club.Players
            |> List.indexed
            |> List.filter (fun (_, player) -> query = "" || player.Name.ToLowerInvariant().Contains query)

        let lineCounts =
            lines ()
            |> List.mapi (fun line (label, color) ->
                let count = club.Players |> List.filter (fun p -> lineOf p.Position = line) |> List.length
                chip $"{label} {count}" (if count = 0 then Theme.TextFaint else color))

        StackPanel.create
            [ StackPanel.spacing 10.0
              StackPanel.children
                  [ DockPanel.create
                        [ DockPanel.children
                              [ StackPanel.create
                                    [ DockPanel.dock Dock.Right
                                      StackPanel.orientation Orientation.Horizontal
                                      StackPanel.spacing 8.0
                                      StackPanel.verticalAlignment VerticalAlignment.Top
                                      StackPanel.children
                                          [ if club.Players.Length > 6 then
                                                TextBox.create
                                                    [ TextBox.onTextChanged (
                                                          (fun text -> dispatch (ModEditorMsg(SearchPlayers text))),
                                                          SubPatchOptions.Always
                                                      )
                                                      TextBox.text state.PlayerQuery
                                                      TextBox.width 180.0
                                                      TextBox.placeholderText (t "Buscar jugador..." "Search player...") ]
                                            actionButton true (t "Jugador" "Player") (Some IconName.add) (fun _ ->
                                                let missing =
                                                    positions
                                                    |> List.tryFind (fun position ->
                                                        not (
                                                            club.Players
                                                            |> List.exists (fun p -> p.Position = position)
                                                        ))
                                                    |> Option.defaultValue "MC"

                                                update (fun c ->
                                                    { c with
                                                        Players =
                                                            c.Players
                                                            @ [ UpdateModEditor.createEmptyPlayer
                                                                    (t "Jugador nuevo" "New player")
                                                                    missing ] })) ] ]
                                StackPanel.create
                                    [ StackPanel.spacing 6.0
                                      StackPanel.children
                                          [ sectionTitle
                                                (t "Plantel" "Squad")
                                                (t
                                                    $"{club.Players.Length} definidos a mano. Lo que falte se genera al empezar la carrera."
                                                    $"{club.Players.Length} defined by hand. Whatever is missing is generated when a career starts.")
                                            StackPanel.create
                                                [ StackPanel.orientation Orientation.Horizontal
                                                  StackPanel.spacing 6.0
                                                  StackPanel.children lineCounts ] ] ] ] ]
                    if not club.Players.IsEmpty then
                        columns
                            spec
                            [ TextBlock.create []
                              caption (t "Nombre" "Name")
                              caption (t "Puesto" "Pos")
                              caption (t "Edad" "Age")
                              caption (t "Media" "Rating")
                              caption (t "Potencial" "Potential")
                              caption (t "Pie" "Foot")
                              caption (t "Nac." "Nat.")
                              TextBlock.create [] ]
                    for index, player in visible do
                        row index player

                    Expander.create
                        [ Expander.header (
                              StackPanel.create
                                  [ StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 8.0
                                    StackPanel.children
                                        [ Icons.iconSm Material.Icons.MaterialIconKind.ContentPaste Theme.TextSub
                                          TextBlock.create
                                              [ TextBlock.text (
                                                    t
                                                        "Pegar un plantel desde una planilla"
                                                        "Paste a squad from a spreadsheet"
                                                )
                                                TextBlock.fontSize 12.0
                                                TextBlock.foreground Theme.TextSub ] ] ]
                          )
                          Expander.horizontalAlignment HorizontalAlignment.Stretch
                          Expander.content (
                              StackPanel.create
                                  [ StackPanel.spacing 8.0
                                    StackPanel.children
                                        [ hint (
                                              t
                                                  "Un jugador por línea: nombre, puesto, edad, media, potencial, pie, nacionalidad. Separá con tabulación, punto y coma o coma; los últimos tres son opcionales."
                                                  "One player per line: name, position, age, rating, potential, foot, nationality. Separate with tab, semicolon or comma; the last three are optional."
                                          )
                                          TextBox.create
                                              [ TextBox.onTextChanged (
                                                    (fun text -> dispatch (ModEditorMsg(SetSquadPaste text))),
                                                    SubPatchOptions.Always
                                                )
                                                TextBox.text state.SquadPaste
                                                TextBox.acceptsReturn true
                                                TextBox.height 120.0
                                                TextBox.fontFamily "monospace"
                                                TextBox.placeholderText "Lionel Ejemplo; ST; 24; 82; 90; Zurdo; ARG" ]
                                          StackPanel.create
                                              [ StackPanel.orientation Orientation.Horizontal
                                                StackPanel.spacing 10.0
                                                StackPanel.children
                                                    [ actionButton true (t "Agregar al plantel" "Add to squad") (Some IconName.add) (fun _ ->
                                                          dispatch (ModEditorMsg(ImportSquad(code, clubIndex))))
                                                      match state.SquadPasteReport with
                                                      | Some report ->
                                                          TextBlock.create
                                                              [ TextBlock.text (
                                                                    t
                                                                        $"Jugadores agregados: {report}. Las líneas sin nombre, puesto válido, edad y media se saltean."
                                                                        $"Players added: {report}. Lines without a name, valid position, age and rating are skipped."
                                                                )
                                                                TextBlock.fontSize 11.0
                                                                TextBlock.foreground Theme.TextMuted
                                                                TextBlock.verticalAlignment VerticalAlignment.Center ]
                                                      | None -> () ] ] ] ]
                          ) ] ] ]

    let private clubEditor
        (country: CountryDataDto)
        (index: int)
        (club: ClubEntryDto)
        (state: ModEditorState)
        dispatch
        : IView =
        let code = country.Country.Code

        let setClub (updater: ClubEntryDto -> ClubEntryDto) (c: CountryDataDto) =
            { c with
                Clubs = c.Clubs |> List.mapi (fun j cl -> if j = index then updater cl else cl) }

        let edit fieldName updater =
            dispatch (ModEditorMsg(EditCountry(code, $"club:{index}:{fieldName}", setClub updater)))

        let update updater =
            dispatch (ModEditorMsg(UpdateCountry(code, setClub updater)))

        let divisions =
            [ 0 .. max (country.LeagueNames.Length - 1) club.LeagueLevel ]
            |> List.map (fun level -> leagueName country level)

        let reputationPill (value: int option) (label: string) : IView =
            let isActive =
                match club.Reputation, value with
                | None, None -> true
                | Some current, Some tier -> tierOf current = tier
                | _ -> false

            pillButton label isActive (fun _ -> edit "reputation" (fun c -> { c with Reputation = value }))

        StackPanel.create
            [ StackPanel.spacing 14.0
              StackPanel.maxWidth 1100.0
              StackPanel.children
                  [ card (
                        DockPanel.create
                            [ DockPanel.children
                                  [ Border.create
                                        [ DockPanel.dock Dock.Left
                                          Border.margin (0.0, 0.0, 20.0, 0.0)
                                          Border.verticalAlignment VerticalAlignment.Top
                                          Border.child (UI.shirt (kitOf club) "" 104.0 Theme.Border 1.0) ]
                                    Border.create
                                        [ DockPanel.dock Dock.Right
                                          Border.verticalAlignment VerticalAlignment.Top
                                          Border.child (
                                              iconButton
                                                  Material.Icons.MaterialIconKind.TrashCanOutline
                                                  (t "Borrar club (se puede deshacer)" "Delete club (can be undone)")
                                                  true
                                                  (fun _ ->
                                                      dispatch (ModEditorMsg(SelectEditorClub None))

                                                      dispatch (
                                                          ModEditorMsg(
                                                              UpdateCountry(
                                                                  code,
                                                                  fun c ->
                                                                      { c with
                                                                          Clubs =
                                                                              c.Clubs
                                                                              |> List.indexed
                                                                              |> List.filter (fun (j, _) -> j <> index)
                                                                              |> List.map snd }
                                                              )
                                                          )
                                                      ))
                                          ) ]
                                    StackPanel.create
                                        [ StackPanel.spacing 12.0
                                          StackPanel.margin (0.0, 0.0, 12.0, 0.0)
                                          StackPanel.children
                                              [ columns
                                                    "2*, 16, *"
                                                    [ field
                                                          (t "Nombre" "Name")
                                                          (TextBox.create
                                                              [ TextBox.onTextChanged (
                                                                    (fun value ->
                                                                        edit "name" (fun c -> { c with Name = value })),
                                                                    SubPatchOptions.Always
                                                                )
                                                                TextBox.text club.Name
                                                                TextBox.fontSize 16.0
                                                                TextBox.fontWeight FontWeight.Bold ])
                                                      field
                                                          (t "División" "Division")
                                                          (select divisions (leagueName country club.LeagueLevel) (fun value ->
                                                              match divisions |> List.tryFindIndex ((=) value) with
                                                              | Some level ->
                                                                  edit "division" (fun c -> { c with LeagueLevel = level })
                                                              | None -> ())) ]
                                                StackPanel.create
                                                    [ StackPanel.spacing 6.0
                                                      StackPanel.children
                                                          [ caption (t "Reputación" "Reputation")
                                                            WrapPanel.create
                                                                [ WrapPanel.children
                                                                      [ reputationPill None (t "Automática" "Automatic")
                                                                        for value, label in reputationTiers () do
                                                                            reputationPill (Some value) label ] ]
                                                            hint (
                                                                if club.Reputation.IsNone then
                                                                    t
                                                                        "Automática: sale de su división y su lugar en la tabla."
                                                                        "Automatic: it follows its division and league standing."
                                                                else
                                                                    t
                                                                        "Pesa en fichajes, presupuesto y lo que esperan los hinchas."
                                                                        "It weighs on transfers, budget and what fans expect."
                                                            ) ] ] ] ] ] ]
                    )
                    card (kitEditor club edit)
                    card (playerTable state code index club edit update dispatch) ] ]

    let private cupsSection (country: CountryDataDto) dispatch : IView =
        let code = country.Country.Code

        // Knockout cups are the common case and get plain controls; any other format string written
        // by hand stays editable as text instead of being silently rewritten.
        let knockout (format: string) =
            let m = Regex.Match(format, "^SK:(SingleLeg|TwoLegs):(\\w+)$")

            if m.Success then
                Some(m.Groups.[1].Value, m.Groups.[2].Value)
            else
                None

        StackPanel.create
            [ StackPanel.spacing 10.0
              StackPanel.children
                  [ hint (
                        t
                            "Copas por eliminación directa entre los clubes del país."
                            "Knockout cups between the country's clubs."
                    )
                    for index in 0 .. country.Cups.Length - 1 do
                        let cup = country.Cups.[index]

                        let editCup updater =
                            dispatch (ModEditorMsg(UpdateCup(code, index, updater)))

                        card (
                            DockPanel.create
                                [ DockPanel.children
                                      [ Border.create
                                            [ DockPanel.dock Dock.Right
                                              Border.verticalAlignment VerticalAlignment.Bottom
                                              Border.margin (8.0, 0.0, 0.0, 0.0)
                                              Border.child (
                                                  removeButton (t "Quitar copa" "Remove cup") (fun _ ->
                                                      dispatch (ModEditorMsg(RemoveCup(code, index))))
                                              ) ]
                                        match knockout cup.Format with
                                        | Some(legs, resolution) ->
                                            columns
                                                "2*, 16, *, 16, *"
                                                [ field
                                                      (t "Copa" "Cup")
                                                      (textInput cup.Name "" (fun value ->
                                                          editCup (fun c -> { c with Name = value })))
                                                  field
                                                      (t "Llaves" "Ties")
                                                      (labeledSelect (legOptions ()) legs (fun value ->
                                                          editCup (fun c ->
                                                              { c with
                                                                  Format = $"SK:{value}:{resolution}" })))
                                                  field
                                                      (t "Si hay empate" "If level")
                                                      (labeledSelect (tieResolutions ()) resolution (fun value ->
                                                          editCup (fun c -> { c with Format = $"SK:{legs}:{value}" }))) ]
                                        | None ->
                                            columns
                                                "2*, 16, 2*"
                                                [ field
                                                      (t "Copa" "Cup")
                                                      (textInput cup.Name "" (fun value ->
                                                          editCup (fun c -> { c with Name = value })))
                                                  fieldWithHint
                                                      (t "Formato" "Format")
                                                      (t
                                                          "Formato avanzado escrito a mano."
                                                          "Hand-written advanced format.")
                                                      (textInput cup.Format "" (fun value ->
                                                          editCup (fun c -> { c with Format = value }))) ] ] ]
                        )
                    StackPanel.create
                        [ StackPanel.orientation Orientation.Horizontal
                          StackPanel.children
                              [ actionButton true (t "Agregar copa" "Add cup") (Some IconName.add) (fun _ ->
                                    dispatch (
                                        ModEditorMsg(
                                            AddCup(
                                                code,
                                                { Name = t "Copa nacional" "National cup"
                                                  Format = "SK:SingleLeg:ExtraTimeThenPenalties" }
                                            )
                                        )
                                    )) ] ] ] ]

    let private namesSection (country: CountryDataDto) dispatch : IView =
        let code = country.Country.Code

        let lines (text: string) =
            text.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.map _.Trim()
            |> Array.filter ((<>) "")
            |> Array.toList

        let column
            fieldName
            (title: string)
            (values: string list)
            (apply: string list -> NamePoolDto -> NamePoolDto)
            : IView =
            card (
                StackPanel.create
                    [ StackPanel.spacing 8.0
                      StackPanel.children
                          [ sectionTitle $"{title} · {values.Length}" ""
                            TextBox.create
                                [ TextBox.onTextChanged (
                                      (fun text ->
                                          dispatch (
                                              ModEditorMsg(
                                                  EditCountry(
                                                      code,
                                                      fieldName,
                                                      fun c ->
                                                          { c with
                                                              Names = apply (lines text) c.Names }
                                                  )
                                              )
                                          )),
                                      SubPatchOptions.Always
                                  )
                                  TextBox.text (String.concat "\n" values)
                                  TextBox.acceptsReturn true
                                  TextBox.height 420.0 ] ] ]
            )

        StackPanel.create
            [ StackPanel.spacing 10.0
              StackPanel.children
                  [ hint (
                        t
                            "Con estos nombres se generan los jugadores que no definiste a mano. Uno por línea."
                            "Players you did not define by hand are generated from these names. One per line."
                    )
                    columns
                        "*, 16, *"
                        [ column "first-names" (t "Nombres" "First names") country.Names.FirstNames (fun names pool ->
                              { pool with FirstNames = names })
                          column "last-names" (t "Apellidos" "Last names") country.Names.LastNames (fun names pool ->
                              { pool with LastNames = names }) ] ] ]

    let private countryHeader (state: ModEditorState) (country: CountryDataDto) : IView =
        let source = state.Document.Sources |> Map.tryFind country.Country.Code
        let players = country.Clubs |> List.sumBy _.Players.Length

        let section =
            match state.ActiveSubTab with
            | Clubs -> t "Clubes" "Clubs"
            | Leagues -> t "Ligas" "Leagues"
            | Cups -> t "Copas" "Cups"
            | NamePool -> t "Nombres" "Names"
            | General -> t "País" "Country"

        Border.create
            [ DockPanel.dock Dock.Top
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.padding (24.0, 14.0)
              Border.child (
                  DockPanel.create
                      [ DockPanel.children
                            [ match source with
                              | Some source ->
                                  Border.create
                                      [ DockPanel.dock Dock.Right
                                        Border.verticalAlignment VerticalAlignment.Center
                                        Border.child (
                                            chip (t $"Copia de {source.ModId}" $"Copy of {source.ModId}") Theme.Info
                                        ) ]
                              | None -> ()
                              StackPanel.create
                                  [ StackPanel.spacing 2.0
                                    StackPanel.children
                                        [ StackPanel.create
                                              [ StackPanel.orientation Orientation.Horizontal
                                                StackPanel.spacing 8.0
                                                StackPanel.children
                                                    [ TextBlock.create
                                                          [ TextBlock.text country.Country.Name
                                                            TextBlock.fontSize 18.0
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.foreground Theme.TextMain ]
                                                      TextBlock.create
                                                          [ TextBlock.text $"› {section}"
                                                            TextBlock.fontSize 18.0
                                                            TextBlock.foreground Theme.TextFaint ] ] ]
                                          hint (
                                              [ count country.LeagueNames.Length "división" "divisiones" "division" "divisions"
                                                count country.Clubs.Length "club" "clubes" "club" "clubs"
                                                count players "jugador definido" "jugadores definidos" "defined player" "defined players"
                                                count country.Cups.Length "copa" "copas" "cup" "cups" ]
                                              |> String.concat " · "
                                          ) ] ] ] ]
              ) ]

    let private worldTab (state: ModEditorState) dispatch : IView =
        let selected =
            state.SelectedCountryCode
            |> Option.bind (fun code -> state.Document.Countries |> Map.tryFind code)

        let scroll (content: IView) : IView =
            ScrollViewer.create [ ScrollViewer.padding (24.0, 20.0); ScrollViewer.content content ]

        match selected with
        | None ->
            scroll (
                UI.emptyState
                    IconName.home
                    (t "Elegí o creá un país" "Pick or create a country")
                    (t
                        "Un país tiene divisiones, clubes, copas y los nombres de sus jugadores."
                        "A country holds divisions, clubs, cups and its players' names.")
            )
        | Some country ->
            let body: IView =
                match state.ActiveSubTab with
                | Clubs ->
                    let club =
                        state.SelectedClubIndex
                        |> Option.bind (fun index ->
                            country.Clubs |> List.tryItem index |> Option.map (fun club -> index, club))

                    DockPanel.create
                        [ DockPanel.children
                              [ clubList state country dispatch
                                scroll (
                                    match club with
                                    | Some(index, club) -> clubEditor country index club state dispatch
                                    | None ->
                                        UI.emptyState
                                            IconName.squad
                                            (t "Elegí un club" "Pick a club")
                                            (t
                                                "Editá su nombre, división, camiseta y plantel."
                                                "Edit its name, division, kit and squad.")
                                ) ] ]
                | Leagues -> scroll (leaguesSection country dispatch)
                | Cups -> scroll (cupsSection country dispatch)
                | NamePool -> scroll (namesSection country dispatch)
                | General -> scroll (generalSection state country dispatch)

            DockPanel.create [ DockPanel.children [ countryHeader state country; body ] ]

    // ─── International ────────────────────────────────────────────────────────

    let private internationalTab (state: ModEditorState) dispatch : IView =
        let comps = state.Document.InternationalComps

        StackPanel.create
            [ StackPanel.spacing 12.0
              StackPanel.children
                  [ sectionTitle
                        (t "Competiciones internacionales" "International competitions")
                        (t
                            "Torneos entre clubes de distintos países. El formato sigue la sintaxis del juego; Publicar te avisa si algo no es válido."
                            "Tournaments between clubs from different countries. The format follows the game's syntax; Publish tells you if something is invalid.")
                    for index in 0 .. comps.Length - 1 do
                        let comp = comps.[index]

                        let edit updater =
                            dispatch (ModEditorMsg(UpdateInternationalComp(index, updater)))

                        card (
                            DockPanel.create
                                [ DockPanel.children
                                      [ Border.create
                                            [ DockPanel.dock Dock.Right
                                              Border.verticalAlignment VerticalAlignment.Bottom
                                              Border.child (
                                                  removeButton (t "Quitar" "Remove") (fun _ ->
                                                      dispatch (ModEditorMsg(RemoveInternationalComp index)))
                                              ) ]
                                        columns
                                            "2*, 16, *, 16, 2*"
                                            [ field
                                                  (t "Nombre" "Name")
                                                  (textInput comp.Name "" (fun value ->
                                                      edit (fun c -> { c with Name = value })))
                                              field
                                                  (t "Confederación" "Confederation")
                                                  (select
                                                      confederations
                                                      (comp.Confederation |> Option.defaultValue "UEFA")
                                                      (fun value ->
                                                          edit (fun c -> { c with Confederation = Some value })))
                                              field
                                                  (t "Formato" "Format")
                                                  (textInput comp.Format "" (fun value ->
                                                      edit (fun c -> { c with Format = value }))) ] ] ]
                        )
                    actionButton true (t "Agregar competición" "Add competition") (Some IconName.add) (fun _ ->
                        dispatch (
                            ModEditorMsg(
                                AddInternationalComp
                                    { Name = t "Copa continental" "Continental cup"
                                      Confederation = Some "UEFA"
                                      Format = "SK:TwoLegs:ExtraTimeThenPenalties"
                                      Qualification = [] }
                            )
                        )) ] ]

    // ─── Publish ──────────────────────────────────────────────────────────────

    let private diagnosticTarget (state: ModEditorState) (diagnostic: ModDiagnostic) =
        let text =
            [ diagnostic.EntityId; diagnostic.JsonPath; diagnostic.RelativePath ]
            |> List.choose id
            |> String.concat " "

        let countryMatch = Regex.Match(text, "country[/-]([a-zA-Z0-9]+)")

        let code =
            if countryMatch.Success then
                state.Document.Countries
                |> Map.tryFindKey (fun key _ ->
                    String.Equals(key, countryMatch.Groups.[1].Value, StringComparison.OrdinalIgnoreCase))
            else
                None

        let clubMatch = Regex.Match(text, "Clubs/(\\d+)")

        let club =
            if clubMatch.Success then
                Some(int clubMatch.Groups.[1].Value)
            else
                None

        code |> Option.map (fun code -> code, club)

    let private diagnosticCard (state: ModEditorState) (diagnostic: ModDiagnostic) dispatch : IView =
        let target = diagnosticTarget state diagnostic

        let location =
            [ diagnostic.JsonPath; diagnostic.EntityId ]
            |> List.choose id
            |> String.concat " · "

        Border.create
            [ Border.background (Theme.withAlpha "14" Theme.Danger)
              Border.borderBrush (Theme.withAlpha "66" Theme.Danger)
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusMd
              Border.padding (14.0, 10.0)
              Border.child (
                  DockPanel.create
                      [ DockPanel.children
                            [ match target with
                              | Some(code, club) ->
                                  Border.create
                                      [ DockPanel.dock Dock.Right
                                        Border.verticalAlignment VerticalAlignment.Center
                                        Border.child (
                                            actionButton false (t "Ir" "Go to") (Some IconName.next) (fun _ ->
                                                dispatch (ModEditorMsg(SetTab World))
                                                dispatch (ModEditorMsg(SelectCountry(Some code)))

                                                match club with
                                                | Some index ->
                                                    dispatch (ModEditorMsg(SetSubTab Clubs))
                                                    dispatch (ModEditorMsg(SelectEditorClub(Some index)))
                                                | None -> dispatch (ModEditorMsg(SetSubTab General)))
                                        ) ]
                              | None -> ()
                              StackPanel.create
                                  [ StackPanel.spacing 3.0
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text diagnostic.Message
                                                TextBlock.foreground Theme.TextMain
                                                TextBlock.fontWeight FontWeight.SemiBold
                                                TextBlock.textWrapping TextWrapping.Wrap ]
                                          if diagnostic.SuggestedAction <> "" then
                                              hint diagnostic.SuggestedAction
                                          if location <> "" then
                                              TextBlock.create
                                                  [ TextBlock.text location
                                                    TextBlock.fontSize 10.0
                                                    TextBlock.foreground Theme.TextDim ] ] ] ] ]
              ) ]

    let private checkRow (icon: Material.Icons.MaterialIconKind) (color: string) (title: string) (detail: string) : IView =
        DockPanel.create
            [ DockPanel.margin (0.0, 4.0)
              DockPanel.children
                  [ Border.create
                        [ DockPanel.dock Dock.Left
                          Border.margin (0.0, 1.0, 12.0, 0.0)
                          Border.verticalAlignment VerticalAlignment.Top
                          Border.child (Icons.iconMd icon color) ]
                    StackPanel.create
                        [ StackPanel.spacing 2.0
                          StackPanel.children
                              [ TextBlock.create
                                    [ TextBlock.text title
                                      TextBlock.fontSize 13.0
                                      TextBlock.fontWeight FontWeight.SemiBold
                                      TextBlock.foreground Theme.TextMain ]
                                if detail <> "" then
                                    hint detail ] ] ] ]

    let private publishTab (state: ModEditorState) dispatch : IView =
        let document = state.Document
        let manifest = document.Manifest
        let clubs = document.Countries.Values |> Seq.sumBy _.Clubs.Length

        let players =
            document.Countries.Values
            |> Seq.sumBy (fun country -> country.Clubs |> List.sumBy _.Players.Length)

        let overrides = document.Sources.Count + document.CompetitionSources.Count
        let canPublish = state.Validated && state.Errors.IsEmpty

        let detailsReady =
            [ manifest.Name; manifest.Id; manifest.Version ]
            |> List.forall (String.IsNullOrWhiteSpace >> not)

        let ok = Material.Icons.MaterialIconKind.CheckCircle
        let bad = Material.Icons.MaterialIconKind.AlertCircle

        let outcome: IView option =
            match state.PublishStatus with
            | Published(path, true) when not state.IsDirty ->
                Some(
                    UI.statusBanner
                        Theme.Success
                        IconName.success
                        (t
                            $"Publicado y activado: tu próxima carrera ya juega con este mundo. ({IO.Path.GetFileName path})"
                            $"Published and enabled: your next career already plays with this world. ({IO.Path.GetFileName path})")
                )
            | Published(path, false) when not state.IsDirty ->
                Some(
                    UI.statusBanner
                        Theme.Success
                        IconName.success
                        (t
                            $"Publicado en la carpeta Mods ({IO.Path.GetFileName path}). Activalo en Ajustes › Datos para jugarlo."
                            $"Published to the Mods folder ({IO.Path.GetFileName path}). Enable it under Settings › Data to play it.")
                )
            | PublishFailed error -> Some(UI.statusBanner Theme.Danger IconName.error error)
            | _ -> None

        let publishButton (label: string) (isPrimary: bool) enable : IView =
            Button.create
                [ Button.onClick (fun _ -> dispatch (ModEditorMsg(PublishMod enable)))
                  Button.isEnabled canPublish
                  Button.background (
                      if not canPublish then Theme.BgElevated
                      elif isPrimary then Theme.Accent
                      else "Transparent"
                  )
                  Button.borderBrush (if isPrimary && canPublish then Theme.Accent else Theme.Border)
                  Button.borderThickness 1.0
                  Button.foreground (
                      if not canPublish then Theme.TextDim
                      elif isPrimary then Theme.OnAccent
                      else Theme.TextMain
                  )
                  Button.fontWeight FontWeight.Bold
                  Button.cornerRadius Theme.RadiusMd
                  Button.padding (18.0, 9.0)
                  Button.content label ]

        StackPanel.create
            [ StackPanel.spacing 14.0
              StackPanel.children
                  [ sectionTitle
                        (t "Publicar" "Publish")
                        (t
                            "Se revisa igual que cuando el juego carga los mods, así no hay sorpresas al empezar una carrera."
                            "Checked exactly as the game loads mods, so starting a career holds no surprises.")
                    match outcome with
                    | Some banner -> banner
                    | None -> ()
                    card (
                        StackPanel.create
                            [ StackPanel.spacing 6.0
                              StackPanel.children
                                  [ if detailsReady then
                                        checkRow
                                            ok
                                            Theme.Success
                                            (t "Datos del mod completos" "Mod details complete")
                                            $"{manifest.Name} · {manifest.Id} · v{manifest.Version}"
                                    else
                                        checkRow
                                            bad
                                            Theme.Warning
                                            (t "Faltan datos del mod" "Mod details missing")
                                            (t
                                                "Completá nombre, identificador y versión en Datos del mod."
                                                "Fill in name, ID and version under Mod details.")
                                    checkRow
                                        (if document.Countries.IsEmpty && document.InternationalComps.IsEmpty then
                                             bad
                                         else
                                             ok)
                                        (if document.Countries.IsEmpty && document.InternationalComps.IsEmpty then
                                             Theme.Warning
                                         else
                                             Theme.Success)
                                        (t "Contenido" "Content")
                                        ([ count document.Countries.Count "país" "países" "country" "countries"
                                           count clubs "club" "clubes" "club" "clubs"
                                           count players "jugador" "jugadores" "player" "players"
                                           count document.InternationalComps.Length "competición" "competiciones" "competition" "competitions"
                                           count overrides "copia del mundo base" "copias del mundo base" "base-world copy" "base-world copies" ]
                                         |> String.concat " · ")
                                    if not state.Validated then
                                        checkRow
                                            Material.Icons.MaterialIconKind.ProgressClock
                                            Theme.TextMuted
                                            (t "Revisando como lo carga el juego..." "Checking as the game loads it...")
                                            ""
                                    elif state.Errors.IsEmpty then
                                        checkRow
                                            ok
                                            Theme.Success
                                            (t "Se carga sin problemas" "Loads without problems")
                                            (t
                                                "El juego lo puede cargar y generar un mundo con él."
                                                "The game can load it and build a world from it.")
                                    else
                                        checkRow
                                            bad
                                            Theme.Danger
                                            (t
                                                $"{state.Errors.Length} problema(s) para resolver"
                                                $"{state.Errors.Length} issue(s) to fix")
                                            (t
                                                "Cada uno lleva al lugar donde se corrige."
                                                "Each one takes you to where it is fixed.")

                                        StackPanel.create
                                            [ StackPanel.spacing 8.0
                                              StackPanel.margin (32.0, 4.0, 0.0, 0.0)
                                              StackPanel.children
                                                  [ for diagnostic in state.Errors do
                                                        diagnosticCard state diagnostic dispatch ] ] ] ]
                    )
                    card (
                        DockPanel.create
                            [ DockPanel.children
                                  [ StackPanel.create
                                        [ DockPanel.dock Dock.Right
                                          StackPanel.orientation Orientation.Horizontal
                                          StackPanel.spacing 8.0
                                          StackPanel.verticalAlignment VerticalAlignment.Center
                                          StackPanel.children
                                              [ publishButton (t "Solo publicar" "Publish only") false false
                                                publishButton (t "Publicar y jugar" "Publish and play") true true ] ]
                                    StackPanel.create
                                        [ StackPanel.spacing 3.0
                                          StackPanel.verticalAlignment VerticalAlignment.Center
                                          StackPanel.children
                                              [ TextBlock.create
                                                    [ TextBlock.text (ModEditorPackage.fileNameFor manifest)
                                                      TextBlock.fontSize 14.0
                                                      TextBlock.fontWeight FontWeight.Bold
                                                      TextBlock.foreground Theme.TextMain ]
                                                hint (
                                                    t
                                                        "\"Publicar y jugar\" lo activa y recarga el contenido."
                                                        "\"Publish and play\" enables it and reloads content."
                                                ) ] ] ] ]
                    ) ] ]

    // ─── Root ─────────────────────────────────────────────────────────────────

    let view (state: State) dispatch =
        let editor = state.ModEditor

        if not editor.HasDocument || editor.ActiveTab = Start then
            DockPanel.create
                [ DockPanel.children
                      [ Border.create
                            [ DockPanel.dock Dock.Top
                              Border.background Theme.BgSidebar
                              Border.borderBrush Theme.Border
                              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                              Border.padding (20.0, 10.0)
                              Border.child (
                                  StackPanel.create
                                      [ StackPanel.horizontalAlignment HorizontalAlignment.Left
                                        StackPanel.children
                                            [ actionButton
                                                  false
                                                  (t "Menú principal" "Main menu")
                                                  (Some IconName.back)
                                                  (fun _ -> dispatch GoBack) ] ]
                              ) ]
                        ScrollViewer.create
                            [ ScrollViewer.padding (32.0, 28.0)
                              ScrollViewer.content (startView editor dispatch) ] ] ]
        else
            let content: IView =
                match editor.ActiveTab with
                | World -> worldTab editor dispatch
                | tab ->
                    ScrollViewer.create
                        [ ScrollViewer.padding (28.0, 24.0)
                          ScrollViewer.content (
                              match tab with
                              | International -> internationalTab editor dispatch
                              | Publish -> publishTab editor dispatch
                              | _ -> detailsTab editor dispatch
                          ) ]

            DockPanel.create [ DockPanel.children [ header editor dispatch; outline editor dispatch; content ] ]
