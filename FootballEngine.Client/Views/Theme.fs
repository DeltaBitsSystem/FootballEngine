namespace FootballEngine

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Media
open FootballEngine.Settings

/// "Stadium at night": a navy night sky, cards lit from above, one electric accent for actions,
/// and broadcast-style condensed numerals. Every view reads colours from the values below, and
/// `usePalette` swaps them at runtime, so an accent or contrast change reaches the whole app.
module Theme =

    type ThemePalette =
        { BgMain: string
          BgSidebar: string
          BgCard: string
          BgCardHover: string
          BgElevated: string
          BgSubtle: string
          Border: string
          BorderLight: string
          BorderStrong: string
          TextMain: string
          TextSub: string
          TextMuted: string
          TextDim: string
          TextFaint: string
          Accent: string
          AccentHover: string
          AccentPressed: string
          OnAccent: string
          AccentAlt: string
          Success: string
          Warning: string
          Danger: string
          Hot: string
          FocusRing: string
          DisabledSurface: string
          DisabledText: string }

    // Avalonia parses 8-digit hex as AARRGGBB, not CSS RRGGBBAA: appending an alpha
    // suffix ("#rrggbb" + "18") used to render as an opaque muddy tone. Centralize the
    // conversion so call sites can't get the order wrong.
    let withAlpha (alphaHex: string) (hex: string) : string =
        let value = hex.TrimStart('#')
        let rgb = if value.Length = 8 then value.Substring(2, 6) else value
        $"#{alphaHex.TrimStart('#')}{rgb}"

    let private relativeLuminance (color: Color) =
        let channel (value: byte) =
            let normalized = float value / 255.0

            if normalized <= 0.03928 then
                normalized / 12.92
            else
                ((normalized + 0.055) / 1.055) ** 2.4

        0.2126 * channel color.R + 0.7152 * channel color.G + 0.0722 * channel color.B

    let contrastRatio (foreground: string) (background: string) : float =
        let foregroundLuminance = foreground |> Color.Parse |> relativeLuminance
        let backgroundLuminance = background |> Color.Parse |> relativeLuminance
        let lighter = max foregroundLuminance backgroundLuminance
        let darker = min foregroundLuminance backgroundLuminance
        (lighter + 0.05) / (darker + 0.05)

    let meetsAaTextContrast (foreground: string) (background: string) =
        contrastRatio foreground background >= 4.5

    let night: ThemePalette =
        { BgMain = "#0A1020"
          BgSidebar = "#0C1428"
          BgCard = "#121C33"
          BgCardHover = "#1C2A4A"
          BgElevated = "#182442"
          BgSubtle = "#0F1830"
          Border = "#22304F"
          BorderLight = "#2A3A5E"
          BorderStrong = "#3D5078"
          TextMain = "#EEF2F6"
          TextSub = "#CDD7EA"
          TextMuted = "#A3B1CC"
          TextDim = "#93A3C4"
          TextFaint = "#5F6F92"
          Accent = "#C8F031"
          AccentHover = "#D8FF5A"
          AccentPressed = "#A9D016"
          OnAccent = "#0A1020"
          AccentAlt = "#4DA3FF"
          Success = "#2EE6A6"
          Warning = "#FFC83D"
          Danger = "#FF5A6A"
          Hot = "#FF7A2F"
          FocusRing = "#EEF2F6"
          DisabledSurface = "#182442"
          DisabledText = "#5F6F92" }

    // Accents shipped before the redesign were defaults, not choices; they map to the new default.
    let private retiredDefaultAccents = set [ "#00d492"; "#19d3a2" ]

    let paletteForSettings (settings: AppearanceSettings) : ThemePalette =
        let configured = if isNull settings.Accent then "" else settings.Accent.Trim()

        let accent =
            let valid =
                configured.StartsWith "#"
                && (try
                        Color.Parse configured |> ignore
                        true
                    with _ ->
                        false)

            if not valid || retiredDefaultAccents.Contains(configured.ToLowerInvariant()) then
                night.Accent
            else
                configured

        let custom = accent <> night.Accent

        // A custom accent keeps text on it readable: dark ink on light accents, white on dark ones.
        let onAccent =
            if meetsAaTextContrast night.OnAccent accent then
                night.OnAccent
            else
                "#FFFFFF"

        let palette =
            { night with
                Accent = accent
                AccentHover = if custom then accent else night.AccentHover
                AccentPressed = if custom then accent else night.AccentPressed
                OnAccent = onAccent }

        if settings.HighContrast then
            { palette with
                Border = "#5C7099"
                BorderLight = "#7487B0"
                BorderStrong = "#A3B1CC"
                TextMain = "#FFFFFF"
                TextSub = "#EEF2F6"
                TextMuted = "#CDD7EA"
                TextDim = "#BCC8DE"
                TextFaint = "#93A3C4"
                FocusRing = "#FFFFFF"
                DisabledText = "#A3B1CC" }
        else
            palette

    // Runtime colour values. Views read these names at render time; `usePalette` rewrites them.
    let mutable BgMain = night.BgMain
    let mutable BgSidebar = night.BgSidebar
    let mutable BgCard = night.BgCard
    let mutable BgCardHover = night.BgCardHover
    let mutable Surface3 = night.BgCardHover
    let mutable BgElevated = night.BgElevated
    let mutable BgSubtle = night.BgSubtle
    let mutable Border = night.Border
    let mutable BorderLight = night.BorderLight
    let mutable BorderStrong = night.BorderStrong
    let mutable TextMain = night.TextMain
    let mutable TextSub = night.TextSub
    let mutable TextMuted = night.TextMuted
    let mutable TextDim = night.TextDim
    let mutable TextFaint = night.TextFaint
    let mutable Accent = night.Accent
    let mutable AccentHover = night.AccentHover
    let mutable AccentPressed = night.AccentPressed
    let mutable AccentLight = withAlpha "24" night.Accent
    let mutable OnAccent = night.OnAccent
    let mutable AccentAlt = night.AccentAlt
    let mutable Info = night.AccentAlt
    let mutable Success = night.Success
    let mutable Positive = night.Success
    let mutable Warning = night.Warning
    let mutable WarningLight = withAlpha "24" night.Warning
    let mutable Danger = night.Danger
    let mutable Negative = night.Danger
    let mutable Hot = night.Hot
    let mutable DragBorder = night.AccentAlt
    let mutable DragOverlay = withAlpha "22" night.AccentAlt
    let mutable FocusRing = night.FocusRing

    // Radii follow the mockup: soft controls, generous cards, pill chips.
    let RadiusXs = 6.0
    let RadiusSm = 8.0
    let RadiusMd = 12.0
    let RadiusLg = 16.0
    let RadiusXl = 20.0
    let RadiusPill = 999.0

    /// Inter for reading, Barlow Condensed for scoreboard numerals and headings.
    let TextFontName = "fonts:Inter#Inter, $Default"

    let DisplayFont =
        FontFamily("avares://FootballEngine.Client/Assets/Fonts#Barlow Condensed")

    type ColorTokens =
        { Canvas: string
          Surface1: string
          Surface2: string
          Surface3: string
          BorderSubtle: string
          TextPrimary: string
          TextSecondary: string
          TextMuted: string
          Accent: string
          AccentStrong: string
          Positive: string
          Warning: string
          Negative: string
          Info: string
          FocusRing: string
          OnAccent: string
          DisabledSurface: string
          DisabledText: string }

    type TypographyTokens =
        { Family: string
          Detail: float
          Body: float
          Emphasis: float
          Section: float
          Page: float
          Hero: float
          LineDetail: float
          LineBody: float
          LineHeading: float
          TabularNumerals: string }

    type SpacingTokens =
        { Xs: float
          Sm: float
          Md: float
          Lg: float
          Xl: float
          Xxl: float
          Xxxl: float }

    type RadiusTokens =
        { Control: float
          Panel: float
          Hero: float
          Pill: float }

    type ControlTokens =
        { CompactHeight: float
          StandardHeight: float
          ActionHeight: float
          MinimumTarget: float }

    type FocusTokens =
        { RingColor: string
          RingWidth: float
          RingOffset: float }

    type LayoutMode =
        | Compact
        | Standard
        | Wide

    type DensityTokens =
        { Mode: LayoutMode
          RowHeight: float
          PanelPadding: float
          ControlGap: float }

    type MotionTokens =
        { FastMs: float
          StandardMs: float
          SlowMs: float
          Reduced: bool }

    type ThemeTokens =
        { Colors: ColorTokens
          Typography: TypographyTokens
          Spacing: SpacingTokens
          Radii: RadiusTokens
          Controls: ControlTokens
          Focus: FocusTokens
          Density: DensityTokens
          Motion: MotionTokens }

    let private colorsFromPalette (palette: ThemePalette) : ColorTokens =
        { Canvas = palette.BgMain
          Surface1 = palette.BgSidebar
          Surface2 = palette.BgCard
          Surface3 = palette.BgCardHover
          BorderSubtle = palette.Border
          TextPrimary = palette.TextMain
          TextSecondary = palette.TextSub
          TextMuted = palette.TextMuted
          Accent = palette.Accent
          AccentStrong = palette.AccentPressed
          Positive = palette.Success
          Warning = palette.Warning
          Negative = palette.Danger
          Info = palette.AccentAlt
          FocusRing = palette.FocusRing
          OnAccent = palette.OnAccent
          DisabledSurface = palette.DisabledSurface
          DisabledText = palette.DisabledText }

    let private typographyFor (fontScale: float) : TypographyTokens =
        let scale =
            if Double.IsNaN fontScale then
                1.0
            else
                max 0.7 (min 1.5 fontScale)

        let readable minimum baseSize = max minimum (baseSize * scale)

        { Family = TextFontName
          Detail = readable 12.0 12.0
          Body = readable 12.0 14.0
          Emphasis = readable 12.0 16.0
          Section = readable 20.0 22.0
          Page = readable 30.0 40.0
          Hero = readable 40.0 56.0
          LineDetail = readable 16.0 16.0
          LineBody = readable 20.0 20.0
          LineHeading = readable 26.0 28.0
          TabularNumerals = "tnum" }

    let private spacingFor (mode: LayoutMode) : SpacingTokens =
        match mode with
        | LayoutMode.Compact ->
            { Xs = 4.0
              Sm = 8.0
              Md = 12.0
              Lg = 16.0
              Xl = 20.0
              Xxl = 24.0
              Xxxl = 32.0 }
        | LayoutMode.Standard
        | LayoutMode.Wide ->
            { Xs = 4.0
              Sm = 8.0
              Md = 12.0
              Lg = 16.0
              Xl = 24.0
              Xxl = 32.0
              Xxxl = 48.0 }

    let private densityFor (mode: LayoutMode) : DensityTokens =
        match mode with
        | LayoutMode.Compact ->
            { Mode = mode
              RowHeight = 40.0
              PanelPadding = 18.0
              ControlGap = 8.0 }
        | LayoutMode.Standard ->
            { Mode = mode
              RowHeight = 48.0
              PanelPadding = 22.0
              ControlGap = 12.0 }
        | LayoutMode.Wide ->
            { Mode = mode
              RowHeight = 52.0
              PanelPadding = 28.0
              ControlGap = 16.0 }

    let layoutModeForWidth (width: float) : LayoutMode =
        if Double.IsNaN width || width < 1280.0 then
            LayoutMode.Compact
        elif width >= 1680.0 then
            LayoutMode.Wide
        else
            LayoutMode.Standard

    let private tokensForMode (settings: AppearanceSettings) (mode: LayoutMode) : ThemeTokens =
        let palette = paletteForSettings settings
        let reduced = not settings.Animations

        { Colors = colorsFromPalette palette
          Typography = typographyFor settings.FontScale
          Spacing = spacingFor mode
          Radii =
            { Control = RadiusMd
              Panel = RadiusLg + 2.0
              Hero = RadiusXl + 2.0
              Pill = RadiusPill }
          Controls =
            { CompactHeight = 36.0
              StandardHeight = 44.0
              ActionHeight = 52.0
              MinimumTarget = 44.0 }
          Focus =
            { RingColor = palette.FocusRing
              RingWidth = 2.0
              RingOffset = 2.0 }
          Density = densityFor mode
          Motion =
            { FastMs = if reduced then 0.0 else 120.0
              StandardMs = if reduced then 0.0 else 180.0
              SlowMs = if reduced then 0.0 else 240.0
              Reduced = reduced } }

    let tokensForSettings (settings: AppearanceSettings) : ThemeTokens =
        let mode =
            match settings.Density with
            | Density.Compact -> LayoutMode.Compact
            | Density.Comfortable -> LayoutMode.Standard

        tokensForMode settings mode

    let tokensForWidth (settings: AppearanceSettings) (width: float) : ThemeTokens =
        let mode =
            if settings.Density = Density.Compact then
                LayoutMode.Compact
            else
                layoutModeForWidth width

        tokensForMode settings mode

    let defaultTokens = tokensForSettings AppearanceSettings.defaults
    let currentTokens: ThemeTokens ref = ref defaultTokens
    let currentPalette: ThemePalette ref = ref (paletteForSettings AppearanceSettings.defaults)

    let colorsForSettings (settings: AppearanceSettings) = (tokensForSettings settings).Colors
    let currentColors () = currentTokens.Value.Colors

    /// Makes the palette current everywhere: the named colours views read, the token record and
    /// the Fluent resources native controls (sliders, checkboxes, combo highlights) draw with.
    let usePalette (palette: ThemePalette) =
        currentPalette.Value <- palette

        currentTokens.Value <-
            { currentTokens.Value with
                Colors = colorsFromPalette palette }

        BgMain <- palette.BgMain
        BgSidebar <- palette.BgSidebar
        BgCard <- palette.BgCard
        BgCardHover <- palette.BgCardHover
        Surface3 <- palette.BgCardHover
        BgElevated <- palette.BgElevated
        BgSubtle <- palette.BgSubtle
        Border <- palette.Border
        BorderLight <- palette.BorderLight
        BorderStrong <- palette.BorderStrong
        TextMain <- palette.TextMain
        TextSub <- palette.TextSub
        TextMuted <- palette.TextMuted
        TextDim <- palette.TextDim
        TextFaint <- palette.TextFaint
        Accent <- palette.Accent
        AccentHover <- palette.AccentHover
        AccentPressed <- palette.AccentPressed
        AccentLight <- withAlpha "24" palette.Accent
        OnAccent <- palette.OnAccent
        AccentAlt <- palette.AccentAlt
        Info <- palette.AccentAlt
        Success <- palette.Success
        Positive <- palette.Success
        Warning <- palette.Warning
        WarningLight <- withAlpha "24" palette.Warning
        Danger <- palette.Danger
        Negative <- palette.Danger
        Hot <- palette.Hot
        DragBorder <- palette.AccentAlt
        DragOverlay <- withAlpha "22" palette.AccentAlt
        FocusRing <- palette.FocusRing

        match Application.Current with
        | null -> ()
        | app ->
            app.Resources.["SystemAccentColor"] <- Color.Parse palette.Accent
            app.Resources.["SystemAccentColorLight1"] <- Color.Parse palette.AccentHover
            app.Resources.["SystemAccentColorDark1"] <- Color.Parse palette.AccentPressed

    let applyToApp (settings: AppearanceSettings) (window: Window) : unit =
        currentTokens.Value <- tokensForSettings settings
        usePalette (paletteForSettings settings)
        window.Background <- SolidColorBrush(Color.Parse BgMain)

    let private brush (hex: string) = SolidColorBrush(Color.Parse hex) :> IBrush

    let private stop (hex: string) (offset: float) = GradientStop(Color.Parse hex, offset)

    /// Stadium floodlights behind every page: a cool glow from the stands, a hint of accent low left.
    let floodlightBrushes () : IBrush list =
        let glow (centerX: float) (centerY: float) (radius: float) (color: string) =
            let gradient = RadialGradientBrush()
            gradient.Center <- RelativePoint(centerX, centerY, RelativeUnit.Relative)
            gradient.GradientOrigin <- RelativePoint(centerX, centerY, RelativeUnit.Relative)
            gradient.RadiusX <- RelativeScalar(radius, RelativeUnit.Relative)
            gradient.RadiusY <- RelativeScalar(radius * 0.75, RelativeUnit.Relative)
            gradient.GradientStops.Add(stop color 0.0)
            gradient.GradientStops.Add(stop (withAlpha "00" color) 1.0)
            gradient :> IBrush

        [ brush BgMain
          glow 0.78 -0.08 0.55 (withAlpha "38" AccentAlt)
          glow -0.04 1.06 0.45 (withAlpha "1A" Accent) ]

    /// A lit card: vivid at the top-left corner, fading into the night.
    let spotlightBrush (from: string) (middle: string) (until: string) : IBrush =
        let gradient = RadialGradientBrush()
        gradient.Center <- RelativePoint(0.0, 0.0, RelativeUnit.Relative)
        gradient.GradientOrigin <- RelativePoint(0.0, 0.0, RelativeUnit.Relative)
        gradient.RadiusX <- RelativeScalar(1.25, RelativeUnit.Relative)
        gradient.RadiusY <- RelativeScalar(1.45, RelativeUnit.Relative)
        gradient.GradientStops.Add(stop from 0.0)
        gradient.GradientStops.Add(stop middle 0.42)
        gradient.GradientStops.Add(stop until 1.0)
        gradient :> IBrush

    /// Matches the glow under primary actions in the mockup.
    let accentGlow () =
        let glow = withAlpha "70" Accent
        BoxShadow.Parse $"0 8 26 0 {glow}"

    let cardShadow () =
        BoxShadow.Parse "0 10 30 0 #55000000"

    let moraleColor morale =
        if morale > 72 then Success
        elif morale > 45 then Warning
        else Danger

    /// Position chips: amber keepers, blue defenders, mint midfield, orange attack.
    let positionColor (pos: Domain.Position) =
        match pos with
        | Domain.GK -> Warning
        | Domain.DC
        | Domain.DL
        | Domain.DR
        | Domain.WBL
        | Domain.WBR -> AccentAlt
        | Domain.DM
        | Domain.MC
        | Domain.ML
        | Domain.MR -> Success
        | Domain.AMC
        | Domain.AML
        | Domain.AMR
        | Domain.ST -> Hot
