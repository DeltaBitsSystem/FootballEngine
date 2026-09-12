namespace FootballEngine

open System
open Avalonia.Controls
open Avalonia.Media
open FootballEngine.Settings

module Theme =

    // Semantic dark palette. Legacy names below intentionally remain as aliases
    // until later gates migrate individual pages to ThemeTokens.
    let Canvas = "#09111D"
    let Surface1 = "#0E1928"
    let Surface2 = "#142338"
    let Surface3 = "#1B2D45"
    let BorderSubtle = "#263A52"
    let TextPrimary = "#F3F7FA"
    let TextSecondary = "#B5C2D0"
    let TextMuted = "#77889D"
    let Accent = "#19D3A2"
    let AccentStrong = "#00B98B"
    let Positive = "#43D18A"
    let Warning = "#F2B84B"
    let Negative = "#F06A72"
    let Info = "#68A9FF"
    let FocusRing = "#F3F7FA"

    let BgMain = Canvas
    let BgSidebar = Surface1
    let BgCard = Surface2
    let BgCardHover = Surface3
    let BgElevated = "#182A40"
    let BgHover = Surface3
    let BgSubtle = "#101D2E"

    let Border = BorderSubtle
    let BorderLight = "#38516C"
    let BorderStrong = "#5B7590"

    let TextMain = TextPrimary
    let TextSub = TextSecondary
    let TextDim = "#91A1B2"
    let TextFaint = "#5B6C80"

    let AccentHover = "#43E0B5"
    let AccentPressed = AccentStrong
    let AccentLight = "#2419D3A2"
    let AccentAlt = Info
    let AccentAltLight = "#2468A9FF"

    let Success = Positive
    let SuccessLight = "#2443D18A"
    let WarningLight = "#24F2B84B"
    let Danger = Negative
    let DangerLight = "#24F06A72"

    let DragBorder = Info
    let DragOverlay = "#2268A9FF"

    // Compatibility aliases used by the current views.
    let RadiusXs = 4.0
    let RadiusSm = 6.0
    let RadiusMd = 8.0
    let RadiusLg = 10.0
    let RadiusXl = 14.0
    let RadiusPill = 999.0

    // Shadows — CSS-like box shadows for Avalonia (use via BoxShadow property where needed)
    let shadowCard = "0 4 16 0 #40000000"
    let shadowElevated = "0 8 24 0 #55000000"
    let shadowSubtle = "0 1 3 0 #30000000"

    // Avalonia parses 8-digit hex as AARRGGBB, not CSS RRGGBBAA: appending an alpha
    // suffix ("#rrggbb" + "18") used to render as an opaque muddy tone. Centralize the
    // conversion so call sites can't get the order wrong.
    let withAlpha (alphaHex: string) (hex: string) : string =
        let value = hex.TrimStart('#')
        let rgb = if value.Length = 8 then value.Substring(0, 6) else value

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

    // ── Dynamic palette — why record: centralized theme so AppearanceSettings can derive full palette without touching every view ──
    type ThemePalette =
        { BgMain: string
          BgSidebar: string
          BgCard: string
          BgCardHover: string
          BgElevated: string
          BgHover: string
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
          AccentLight: string
          AccentAlt: string
          AccentAltLight: string
          Success: string
          SuccessLight: string
          Warning: string
          WarningLight: string
          Danger: string
          DangerLight: string
          Info: string
          DragBorder: string
          DragOverlay: string
          FocusRing: string
          OnAccent: string
          DisabledSurface: string
          DisabledText: string }

    // why defaultPalette mirrors const lets: keeps old Theme.BgMain fallback during transition while new code uses paletteForSettings
    let defaultPalette: ThemePalette =
        { BgMain = BgMain
          BgSidebar = BgSidebar
          BgCard = BgCard
          BgCardHover = BgCardHover
          BgElevated = BgElevated
          BgHover = BgHover
          BgSubtle = BgSubtle
          Border = Border
          BorderLight = BorderLight
          BorderStrong = BorderStrong
          TextMain = TextMain
          TextSub = TextSub
          TextMuted = TextMuted
          TextDim = TextDim
          TextFaint = TextFaint
          Accent = Accent
          AccentHover = AccentHover
          AccentPressed = AccentPressed
          AccentLight = AccentLight
          AccentAlt = AccentAlt
          AccentAltLight = AccentAltLight
          Success = Success
          SuccessLight = SuccessLight
          Warning = Warning
          WarningLight = WarningLight
          Danger = Danger
          DangerLight = DangerLight
          Info = Info
          DragBorder = DragBorder
          DragOverlay = DragOverlay
          FocusRing = FocusRing
          OnAccent = BgSidebar
          DisabledSurface = BgElevated
          DisabledText = TextMuted }

    // why currentPalette ref: allows runtime theme switch without restart; views reading old consts still get fallback until migrated
    let currentPalette: ThemePalette ref = ref defaultPalette

    let private lightPalette (accent: string) : ThemePalette =
        // Light is a separate hierarchy, not an inversion of the dark palette.
        let lightAccent = if accent = Accent then "#007A63" else accent

        { defaultPalette with
            BgMain = "#F7F9FC"
            BgSidebar = "#FFFFFF"
            BgCard = "#ffffff"
            BgCardHover = "#E8EEF5"
            BgElevated = "#FFFFFF"
            BgHover = "#DDE7F0"
            BgSubtle = "#EEF3F8"
            Border = "#C9D5E2"
            BorderLight = "#DCE5EE"
            BorderStrong = "#8EA3B8"
            TextMain = "#17212B"
            TextSub = "#394A59"
            TextMuted = "#536579"
            TextDim = "#687B8F"
            TextFaint = "#9AAABD"
            Accent = lightAccent
            AccentAlt = "#155AA6"
            AccentAltLight = "#18155AA6"
            AccentHover = if accent = Accent then "#00634F" else accent
            AccentPressed = if accent = Accent then "#004E3F" else accent
            AccentLight = withAlpha "18" lightAccent
            Success = "#157347"
            SuccessLight = "#18157347"
            Warning = "#8A5A00"
            WarningLight = "#188A5A00"
            Danger = "#B4232C"
            DangerLight = "#18B4232C"
            Info = "#155AA6"
            DragBorder = "#155AA6"
            DragOverlay = "#18155AA6"
            FocusRing = "#005A9C"
            OnAccent = "#FFFFFF"
            DisabledSurface = "#E1E8F0"
            DisabledText = "#687B8F" }

    let private darkPalette (accent: string) : ThemePalette =
        // why dark reuses defaultPalette: default is already dark premium, only accent may be overridden
        { defaultPalette with
            Accent = accent
            AccentHover = if accent = Accent then AccentHover else accent
            AccentPressed = if accent = Accent then AccentPressed else accent
            AccentLight = withAlpha "18" accent }

    let private effectiveAccent (settings: AppearanceSettings) =
        // The old persisted default is still #00d492. Treat it as the old default,
        // while preserving any genuinely custom accent selected by the user.
        let configured =
            if String.IsNullOrWhiteSpace settings.Accent then
                ""
            else
                settings.Accent.Trim()

        if
            String.IsNullOrWhiteSpace configured
            || String.Equals(configured, "#00d492", StringComparison.OrdinalIgnoreCase)
        then
            defaultPalette.Accent
        else
            configured

    let paletteForSettings (settings: AppearanceSettings) : ThemePalette =
        let accent = effectiveAccent settings

        let isLight =
            match settings.ThemeMode with
            | ThemeMode.Light -> true
            | ThemeMode.Dark -> false
            // why Auto -> Dark: no OS preference detection yet, defaults to dark for consistency with previous behavior
            | ThemeMode.Auto -> false

        let basePalette = if isLight then lightPalette accent else darkPalette accent

        if settings.HighContrast then
            if isLight then
                // why high-contrast light uses near-black borders: increases border contrast against white BgMain
                { basePalette with
                    Border = "#17212B"
                    BorderLight = "#394A59"
                    BorderStrong = "#020617"
                    TextMain = "#020617"
                    TextSub = "#17212B"
                    FocusRing = "#003B73"
                    DisabledText = "#536579" }
            else
                // why high-contrast dark uses lighter borders: lifts border visibility on navy background
                { basePalette with
                    Border = "#475569"
                    BorderLight = "#64748b"
                    BorderStrong = "#94a3b8"
                    TextMain = "#ffffff"
                    TextFaint = "#94a3b8"
                    FocusRing = "#ffffff"
                    DisabledText = "#B5C2D0" }
        else
            basePalette

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
          Info = palette.Info
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

        { Family = "Segoe UI"
          Detail = readable 12.0 12.0
          Body = readable 12.0 14.0
          Emphasis = readable 12.0 16.0
          Section = readable 20.0 20.0
          Page = readable 28.0 28.0
          Hero = readable 36.0 44.0
          LineDetail = readable 16.0 16.0
          LineBody = readable 20.0 20.0
          LineHeading = readable 26.0 26.0
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
        | LayoutMode.Standard ->
            { Xs = 4.0
              Sm = 8.0
              Md = 12.0
              Lg = 16.0
              Xl = 24.0
              Xxl = 32.0
              Xxxl = 48.0 }
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
              RowHeight = 32.0
              PanelPadding = 16.0
              ControlGap = 8.0 }
        | LayoutMode.Standard ->
            { Mode = mode
              RowHeight = 40.0
              PanelPadding = 24.0
              ControlGap = 12.0 }
        | LayoutMode.Wide ->
            { Mode = mode
              RowHeight = 40.0
              PanelPadding = 32.0
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
            { Control = 6.0
              Panel = 10.0
              Hero = 14.0
              Pill = 999.0 }
          Controls =
            { CompactHeight = 32.0
              StandardHeight = 40.0
              ActionHeight = 48.0
              MinimumTarget = 32.0 }
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

    let colorsForSettings (settings: AppearanceSettings) = (tokensForSettings settings).Colors
    let currentColors () = currentTokens.Value.Colors

    // why helpers bgMainFor etc not added per-field: paletteForSettings covers all cases, old consts kept for fallback during migration
    let bgMainFor (settings: AppearanceSettings) = (paletteForSettings settings).BgMain
    let bgSidebarFor (settings: AppearanceSettings) = (paletteForSettings settings).BgSidebar
    let bgCardFor (settings: AppearanceSettings) = (paletteForSettings settings).BgCard
    let textMainFor (settings: AppearanceSettings) = (paletteForSettings settings).TextMain
    let accentFor (settings: AppearanceSettings) = (paletteForSettings settings).Accent

    // Runtime tokens are updated here; legacy constants remain for pages that have
    // not yet moved to the dynamic record.
    let applyToApp (settings: AppearanceSettings) (window: Window) : unit =
        let palette = paletteForSettings settings
        currentTokens.Value <- tokensForSettings settings
        currentPalette.Value <- palette

        // Fluent controls (Slider, CheckBox, ComboBox highlight) read SystemAccentColor,
        // default blue. Point it at the palette accent so native controls match the theme.
        try
            match Avalonia.Application.Current with
            | null -> ()
            | app ->
                app.Resources.["SystemAccentColor"] <- Color.Parse(palette.Accent)
                app.Resources.["FootballEngine.Canvas"] <- palette.BgMain
                app.Resources.["FootballEngine.Surface1"] <- palette.BgSidebar
                app.Resources.["FootballEngine.Surface2"] <- palette.BgCard
                app.Resources.["FootballEngine.Surface3"] <- palette.BgCardHover
                app.Resources.["FootballEngine.BorderSubtle"] <- palette.Border
                app.Resources.["FootballEngine.TextPrimary"] <- palette.TextMain
                app.Resources.["FootballEngine.TextSecondary"] <- palette.TextSub
                app.Resources.["FootballEngine.TextMuted"] <- palette.TextMuted
                app.Resources.["FootballEngine.Accent"] <- palette.Accent
                app.Resources.["FootballEngine.AccentStrong"] <- palette.AccentPressed
                app.Resources.["FootballEngine.FocusRing"] <- palette.FocusRing
        with _ ->
            ()

        try
            // why SolidColorBrush + Color.Parse: Window.Background expects IBrush, hex parsing keeps Theme single-sourced
            window.Background <- SolidColorBrush(Color.Parse(palette.BgMain))
        with _ ->
            // why ignore parse failure: malformed accent or palette hex should not crash startup, fallback to dark BgMain keeps window visible
            try
                window.Background <- SolidColorBrush(Color.Parse(defaultPalette.BgMain))
            with _ ->
                ()

    let moraleColor morale =
        if morale > 72 then Success
        elif morale > 45 then Warning
        else Danger

    let moraleColorFor (palette: ThemePalette) morale =
        if morale > 72 then palette.Success
        elif morale > 45 then palette.Warning
        else palette.Danger

    let positionColor (pos: Domain.Position) =
        match pos with
        | Domain.GK -> Warning // amber — standout for keeper
        | Domain.DC
        | Domain.DL
        | Domain.DR
        | Domain.WBL
        | Domain.WBR -> AccentAlt // blue — defensive line
        | Domain.DM
        | Domain.MC
        | Domain.ML
        | Domain.MR -> Accent // emerald — engine room
        | Domain.AMC
        | Domain.AML
        | Domain.AMR
        | Domain.ST -> Danger // red — attacking threat

    let positionColorFor (palette: ThemePalette) (pos: Domain.Position) =
        match pos with
        | Domain.GK -> palette.Warning
        | Domain.DC
        | Domain.DL
        | Domain.DR
        | Domain.WBL
        | Domain.WBR -> palette.AccentAlt
        | Domain.DM
        | Domain.MC
        | Domain.ML
        | Domain.MR -> palette.Accent
        | Domain.AMC
        | Domain.AML
        | Domain.AMR
        | Domain.ST -> palette.Danger
