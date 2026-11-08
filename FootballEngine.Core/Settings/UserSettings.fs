namespace FootballEngine.Settings

open System
open System.Text.Json
open FootballEngine.Core.Match

// ── Enumerations ──────────────────────────────────────────────────────────
// Simple DUs serialized as strings so JSON stays human-readable and
// diffable without a numeric mapping table.

type Density =
    | Comfortable
    | Compact

module Density =

    let toString =
        function
        | Comfortable -> "Comfortable"
        | Compact -> "Compact"

    let tryParse (s: string) : Result<Density, string> =
        match s with
        | s when String.Equals(s, "Comfortable", StringComparison.OrdinalIgnoreCase) -> Ok Comfortable
        | s when String.Equals(s, "Compact", StringComparison.OrdinalIgnoreCase) -> Ok Compact
        | _ -> Error $"Unknown Density '{s}'"

type HighlightFilter =
    | All
    | GoalsOnly
    | KeyMoments

module HighlightFilter =

    let toString =
        function
        | All -> "All"
        | GoalsOnly -> "GoalsOnly"
        | KeyMoments -> "KeyMoments"

    let tryParse (s: string) : Result<HighlightFilter, string> =
        match s with
        | s when String.Equals(s, "All", StringComparison.OrdinalIgnoreCase) -> Ok All
        | s when String.Equals(s, "GoalsOnly", StringComparison.OrdinalIgnoreCase) -> Ok GoalsOnly
        | s when String.Equals(s, "KeyMoments", StringComparison.OrdinalIgnoreCase) -> Ok KeyMoments
        | _ -> Error $"Unknown HighlightFilter '{s}'"

type NewsFreq =
    | Low
    | Normal
    | High

module NewsFreq =

    let toString =
        function
        | Low -> "Low"
        | Normal -> "Normal"
        | High -> "High"

    let tryParse (s: string) : Result<NewsFreq, string> =
        match s with
        | s when String.Equals(s, "Low", StringComparison.OrdinalIgnoreCase) -> Ok Low
        | s when String.Equals(s, "Normal", StringComparison.OrdinalIgnoreCase) -> Ok Normal
        | s when String.Equals(s, "High", StringComparison.OrdinalIgnoreCase) -> Ok High
        | _ -> Error $"Unknown NewsFreq '{s}'"

type LogLevel =
    | Silent
    | Normal
    | Verbose

module LogLevel =

    let toString =
        function
        | Silent -> "Silent"
        | Normal -> "Normal"
        | Verbose -> "Verbose"

    let tryParse (s: string) : Result<LogLevel, string> =
        match s with
        | s when String.Equals(s, "Silent", StringComparison.OrdinalIgnoreCase) -> Ok Silent
        | s when String.Equals(s, "Normal", StringComparison.OrdinalIgnoreCase) -> Ok Normal
        | s when String.Equals(s, "Verbose", StringComparison.OrdinalIgnoreCase) -> Ok Verbose
        | _ -> Error $"Unknown LogLevel '{s}'"

type DifficultyPreset =
    | Classic
    | Realistic
    | Arcade
    | Custom

module DifficultyPreset =

    let toString =
        function
        | Classic -> "Classic"
        | Realistic -> "Realistic"
        | Arcade -> "Arcade"
        | Custom -> "Custom"

    let tryParse (s: string) : Result<DifficultyPreset, string> =
        match s with
        | s when String.Equals(s, "Classic", StringComparison.OrdinalIgnoreCase) -> Ok Classic
        | s when String.Equals(s, "Realistic", StringComparison.OrdinalIgnoreCase) -> Ok Realistic
        | s when String.Equals(s, "Arcade", StringComparison.OrdinalIgnoreCase) -> Ok Arcade
        | s when String.Equals(s, "Custom", StringComparison.OrdinalIgnoreCase) -> Ok Custom
        | _ -> Error $"Unknown DifficultyPreset '{s}'"

// ── Appearance ──────────────────────────────────────────────────────────

type AppearanceSettings =
    { Accent: string
      Density: Density
      FontScale: float
      Language: string
      Animations: bool
      HighContrast: bool
      PitchPalette: string }

module AppearanceSettings =

    let defaults: AppearanceSettings =
        { Accent = "#C8F031"
          Density = Comfortable
          FontScale = 1.0
          Language = "es"
          Animations = true
          HighContrast = false
          PitchPalette = "default" }

    let validate (a: AppearanceSettings) : string list =
        [ if a.FontScale < 0.7 || a.FontScale > 1.5 then
              yield "Appearance.FontScale must be in [0.7, 1.5]"
          if String.IsNullOrWhiteSpace a.Accent then
              yield "Appearance.Accent must be non-empty"
          elif not (a.Accent.StartsWith "#") then
              yield "Appearance.Accent must be hex like '#00d492'"
          elif a.Accent.Length <> 7 && a.Accent.Length <> 9 then
              // #RRGGBB or #RRGGBBAA — common hex forms, reject malformed strings
              yield "Appearance.Accent must be 7 or 9 characters (#RRGGBB[AA])"
          if String.IsNullOrWhiteSpace a.Language then
              yield "Appearance.Language must be non-empty"
          if String.IsNullOrWhiteSpace a.PitchPalette then
              yield "Appearance.PitchPalette must be non-empty" ]

// ── MatchView ───────────────────────────────────────────────────────────

/// Core only stores the player's choice; the Client maps it to concrete render knobs so Core never depends on GPU code.
[<RequireQualifiedAccess>]
type ViewerQualityTier =
    | Low
    | Medium
    | High
    | Ultra

module ViewerQualityTier =

    let all =
        [ ViewerQualityTier.Low
          ViewerQualityTier.Medium
          ViewerQualityTier.High
          ViewerQualityTier.Ultra ]

    let toString =
        function
        | ViewerQualityTier.Low -> "Low"
        | ViewerQualityTier.Medium -> "Medium"
        | ViewerQualityTier.High -> "High"
        | ViewerQualityTier.Ultra -> "Ultra"

    let tryParse (s: string) : Result<ViewerQualityTier, string> =
        match s with
        | s when String.Equals(s, "Low", StringComparison.OrdinalIgnoreCase) -> Ok ViewerQualityTier.Low
        | s when String.Equals(s, "Medium", StringComparison.OrdinalIgnoreCase) -> Ok ViewerQualityTier.Medium
        | s when String.Equals(s, "High", StringComparison.OrdinalIgnoreCase) -> Ok ViewerQualityTier.High
        | s when String.Equals(s, "Ultra", StringComparison.OrdinalIgnoreCase) -> Ok ViewerQualityTier.Ultra
        | _ -> Error $"Unknown ViewerQualityTier '{s}'"

type MatchViewSettings =
    { PlaybackSpeed: int
      AutoPlay: bool
      HighlightFilter: HighlightFilter
      TickRate: int
      ShowXG: bool
      ShowPossession: bool
      ShowOverlays: bool
      SoundEnabled: bool
      PauseOnGoal: bool
      PauseOnRed: bool
      ViewerQuality: ViewerQualityTier }

module MatchViewSettings =

    let defaults: MatchViewSettings =
        { PlaybackSpeed = 20
          AutoPlay = false
          HighlightFilter = All
          TickRate = 16
          ShowXG = true
          ShowPossession = true
          ShowOverlays = true
          SoundEnabled = true
          PauseOnGoal = true
          PauseOnRed = true
          // Development default: quality first, cheaper tiers are derived from it.
          ViewerQuality = ViewerQualityTier.Ultra }

    let validate (m: MatchViewSettings) : string list =
        [ if m.PlaybackSpeed < 5 || m.PlaybackSpeed > 60 then
              yield "MatchView.PlaybackSpeed must be in [5, 60]"
          // 16ms ~60fps, allow 1..100ms so UI can trade smoothness for battery
          if m.TickRate < 1 || m.TickRate > 100 then
              yield "MatchView.TickRate must be in [1, 100]" ]

// ── Simulation ──────────────────────────────────────────────────────────

type SimulationSettings =
    { DifficultyPreset: DifficultyPreset
      BalanceOverrides: BalanceConfig option
      RefStrictness: float
      InjuryFreq: float
      FatigueFactor: float
      WeatherEnabled: bool
      HomeAdvantage: float }

module SimulationSettings =

    let defaults: SimulationSettings =
        { DifficultyPreset = Classic
          BalanceOverrides = None
          RefStrictness = 1.0
          InjuryFreq = 1.0
          FatigueFactor = 1.0
          WeatherEnabled = true
          HomeAdvantage = 1.0 }

    let validate (s: SimulationSettings) : string list =
        [ if s.RefStrictness < 0.0 || s.RefStrictness > 3.0 then
              yield "Simulation.RefStrictness must be in [0.0, 3.0]"
          if s.InjuryFreq < 0.0 || s.InjuryFreq > 3.0 then
              yield "Simulation.InjuryFreq must be in [0.0, 3.0]"
          if s.FatigueFactor < 0.0 || s.FatigueFactor > 3.0 then
              yield "Simulation.FatigueFactor must be in [0.0, 3.0]"
          if s.HomeAdvantage < 0.0 || s.HomeAdvantage > 3.0 then
              yield "Simulation.HomeAdvantage must be in [0.0, 3.0]"
          match s.BalanceOverrides with
          | None -> ()
          | Some cfg ->
              // delegate to ConfigValidation.validateAll so every section is covered
              match ConfigValidation.validateAll cfg with
              | Ok _ -> ()
              | Error errs ->
                  for e in errs do
                      yield $"Simulation.BalanceOverrides: {e}" ]

    // ── Preset mapping ────────────────────────────────────────────────
    // Classic = engine defaults verbatim. Realistic and Arcade must be
    // distinguishable for tests and for the diff panel: they touch
    // disjoint fields so a simple equality check is enough to prove the
    // preset applied.

    let private classicBalance = BalanceConfig.defaultConfig

    let private realisticBalance: BalanceConfig =
        // Realistic slightly reduces shot power so long shots are rarer and
        // xG matches real-world conversion — 0.95 keeps the value close to
        // default so existing calibration is not invalidated.
        { BalanceConfig.defaultConfig with
            Ball =
                { BalanceConfig.defaultConfig.Ball with
                    MaxShotSpeed = BalanceConfig.defaultConfig.Ball.MaxShotSpeed * 0.95 } }

    let private arcadeBalance: BalanceConfig =
        // Arcade favours spectacle with faster shots and sprints. Values are measurably different from
        // Classic/Realistic (see tests) while staying inside
        // ConfigValidation bounds.
        { BalanceConfig.defaultConfig with
            Ball =
                { BalanceConfig.defaultConfig.Ball with
                    MaxShotSpeed = 35.0<meterPerSecond> }
            Movement =
                { BalanceConfig.defaultConfig.Movement with
                    SprintSpeed = BalanceConfig.defaultConfig.Movement.SprintSpeed * 1.05 } }

    let private presetBalance (preset: DifficultyPreset) : BalanceConfig =
        match preset with
        | Classic -> classicBalance
        | Realistic -> realisticBalance
        | Arcade -> arcadeBalance
        | Custom -> classicBalance // never used when preset = Custom

    let applyPreset (preset: DifficultyPreset) (settings: SimulationSettings) : SimulationSettings =
        match preset with
        | Custom ->
            // Custom unlocks manual overrides without clobbering them
            { settings with
                DifficultyPreset = Custom }
        | _ ->
            { settings with
                DifficultyPreset = preset
                BalanceOverrides = Some(presetBalance preset) }

// ── Career ─────────────────────────────────────────────────────────────

type CareerSettings =
    { AutoSaveDays: int
      NewsFreq: NewsFreq
      InboxGrouping: bool
      Tutorial: bool }

module CareerSettings =

    let defaults: CareerSettings =
        { AutoSaveDays = 7
          NewsFreq = NewsFreq.Normal
          InboxGrouping = true
          Tutorial = true }

    let validate (c: CareerSettings) : string list =
        [ if c.AutoSaveDays < 1 || c.AutoSaveDays > 30 then
              yield "Career.AutoSaveDays must be in [1, 30]" ]

// ── Data ────────────────────────────────────────────────────────────────

type DataSettings =
    { ActiveMods: string list
      ModOrder: string list }

module DataSettings =

    let defaults: DataSettings = { ActiveMods = []; ModOrder = [] }

    let validate (_d: DataSettings) : string list = []

// ── Scouting ────────────────────────────────────────────────────────────

type ScoutingSettings =
    { AiNoise: float
      HalfLifeDays: float
      MinConfidence: float }

module ScoutingSettings =

    let defaults: ScoutingSettings =
        { AiNoise = 0.1
          HalfLifeDays = 7.0
          MinConfidence = 0.5 }

    let validate (s: ScoutingSettings) : string list =
        [ if s.AiNoise < 0.0 || s.AiNoise > 1.0 then
              yield "Scouting.AiNoise must be in [0.0, 1.0]"
          if s.HalfLifeDays <= 0.0 || s.HalfLifeDays > 365.0 then
              yield "Scouting.HalfLifeDays must be in (0, 365]"
          if s.MinConfidence < 0.0 || s.MinConfidence > 1.0 then
              yield "Scouting.MinConfidence must be in [0.0, 1.0]" ]

// ── Advanced ────────────────────────────────────────────────────────────

type AdvancedSettings =
    { ShowDebug: bool
      LogLevel: LogLevel
      UnlockMotorEditor: bool }

module AdvancedSettings =

    let defaults: AdvancedSettings =
        { ShowDebug = false
          LogLevel = LogLevel.Normal
          UnlockMotorEditor = false }

    let validate (_a: AdvancedSettings) : string list = []

// ── Global vs Save ─────────────────────────────────────────────────────

type GlobalSettings =
    { Appearance: AppearanceSettings
      MatchView: MatchViewSettings
      Career: CareerSettings
      Data: DataSettings
      Advanced: AdvancedSettings
      Version: int }

type SaveSettings =
    { Simulation: SimulationSettings
      Scouting: ScoutingSettings
      Version: int }

// ── JSON helpers (private) ────────────────────────────────────────────

module private JsonHelpers =

    open System.IO
    open Microsoft.FSharp.Reflection

    let currentVersion = 1

    // ── camelCase ─────────────────────────────────────────────────────
    let toCamelCase (name: string) =
        if String.IsNullOrEmpty name then
            name
        else
            Char.ToLowerInvariant(name[0]).ToString() + name.Substring(1)

    // ── property lookup (case-insensitive, no mutable defaultof) ─────
    let tryGetProperty (name: string) (elem: JsonElement) : JsonElement option =
        elem.EnumerateObject()
        |> Seq.tryFind (fun p -> String.Equals(p.Name, name, StringComparison.Ordinal))
        |> Option.map (fun p -> p.Value)

    // Fallback case-insensitive search for backward compatibility
    let tryGetPropertyCI (name: string) (elem: JsonElement) : JsonElement option =
        elem.EnumerateObject()
        |> Seq.tryFind (fun p -> String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
        |> Option.map (fun p -> p.Value)

    let getRequired (name: string) (elem: JsonElement) : Result<JsonElement, string> =
        match tryGetProperty name elem with
        | Some v -> Ok v
        | None -> Error $"Missing property '{name}'"

    let getString (name: string) (elem: JsonElement) : Result<string, string> =
        match getRequired name elem with
        | Error e -> Error e
        | Ok v ->
            if v.ValueKind = JsonValueKind.String then
                match v.GetString() with
                | null -> Error $"Property '{name}' is null"
                | s -> Ok s
            else
                Error $"Property '{name}' must be a string"

    let getInt (name: string) (elem: JsonElement) : Result<int, string> =
        match getRequired name elem with
        | Error e -> Error e
        | Ok v ->
            if v.ValueKind = JsonValueKind.Number then
                Ok(v.GetInt32())
            else
                Error $"Property '{name}' must be a number"

    let getFloat (name: string) (elem: JsonElement) : Result<float, string> =
        match getRequired name elem with
        | Error e -> Error e
        | Ok v ->
            if v.ValueKind = JsonValueKind.Number then
                Ok(v.GetDouble())
            else
                Error $"Property '{name}' must be a number"

    let getBool (name: string) (elem: JsonElement) : Result<bool, string> =
        match getRequired name elem with
        | Error e -> Error e
        | Ok v ->
            match v.ValueKind with
            | JsonValueKind.True -> Ok true
            | JsonValueKind.False -> Ok false
            | _ -> Error $"Property '{name}' must be a boolean"

    let getVersion (elem: JsonElement) : int =
        match tryGetProperty "version" elem with
        | Some v when v.ValueKind = JsonValueKind.Number -> v.GetInt32()
        | _ ->
            match tryGetPropertyCI "version" elem with
            | Some v when v.ValueKind = JsonValueKind.Number -> v.GetInt32()
            | _ -> currentVersion

    // ── list helpers ──────────────────────────────────────────────────
    let writeStringList (writer: Utf8JsonWriter) (name: string) (values: string list) =
        writer.WritePropertyName(name)
        writer.WriteStartArray()

        for v in values do
            writer.WriteStringValue(v)

        writer.WriteEndArray()

    let readStringList (name: string) (elem: JsonElement) : Result<string list, string> =
        match getRequired name elem with
        | Error e -> Error e
        | Ok v ->
            if v.ValueKind <> JsonValueKind.Array then
                Error $"Property '{name}' must be an array"
            else
                let mutable error: string option = None
                let acc = ResizeArray<string>()

                for item in v.EnumerateArray() do
                    if error.IsNone then
                        if item.ValueKind = JsonValueKind.String then
                            match item.GetString() with
                            | null -> error <- Some $"Property '{name}' contains null"
                            | s -> acc.Add(s)
                        else
                            error <- Some $"Property '{name}' must be an array of strings"

                match error with
                | Some e -> Error e
                | None -> Ok(List.ofSeq acc)

    // ── BalanceConfig (reflection) ────────────────────────────────────
    // Measures are erased at runtime, so FSharpType reports the underlying
    // float/int. We mirror WeightMerge's field lookup but write instead of
    // merging. Keeping write and WeightMerge symmetric guarantees round-trip.

    let rec private writeValue (writer: Utf8JsonWriter) (value: obj) (t: Type) =
        if FSharpType.IsRecord(t, true) then
            writer.WriteStartObject()

            for field in FSharpType.GetRecordFields(t, true) do
                let fieldValue = field.GetValue(value)
                let propName = toCamelCase field.Name
                writer.WritePropertyName(propName)
                writeValue writer fieldValue field.PropertyType

            writer.WriteEndObject()
        elif t.IsArray then
            writer.WriteStartArray()
            let arr = value :?> Array
            let elemType = t.GetElementType()

            for i = 0 to arr.Length - 1 do
                writeValue writer (arr.GetValue(i)) elemType

            writer.WriteEndArray()
        elif t = typeof<string> then
            match value with
            | null -> writer.WriteNullValue()
            | _ -> writer.WriteStringValue(unbox<string> value)
        elif t = typeof<bool> then
            writer.WriteBooleanValue(unbox<bool> value)
        elif t = typeof<int> then
            writer.WriteNumberValue(unbox<int> value)
        elif t = typeof<float> then
            writer.WriteNumberValue(unbox<float> value)
        elif t = typeof<decimal> then
            writer.WriteNumberValue(unbox<decimal> value)
        elif t = typeof<float32> then
            writer.WriteNumberValue(float (unbox<float32> value))
        elif t.IsValueType && not t.IsPrimitive then
            // measure-erased scalar fallback — runtime type is the underlying
            // primitive so we inspect the boxed value
            match value with
            | :? float as f -> writer.WriteNumberValue(f)
            | :? int as i -> writer.WriteNumberValue(i)
            | :? decimal as d -> writer.WriteNumberValue(d)
            | :? float32 as f32 -> writer.WriteNumberValue(float f32)
            | _ -> writer.WriteStringValue(value.ToString())
        else
            writer.WriteStringValue(value.ToString())

    let writeBalanceConfig (writer: Utf8JsonWriter) (name: string) (config: BalanceConfig) =
        writer.WritePropertyName(name)
        writeValue writer (box config) typeof<BalanceConfig>

    let writeBalanceConfigOption (writer: Utf8JsonWriter) (name: string) (opt: BalanceConfig option) =
        writer.WritePropertyName(name)

        match opt with
        | None -> writer.WriteNullValue()
        | Some cfg -> writeValue writer (box cfg) typeof<BalanceConfig>

    let readBalanceConfig (elem: JsonElement) : Result<BalanceConfig, string> =
        // Reuse WeightMerge so validation stays single-sourced and future
        // BalanceConfig fields are handled without duplicating reflection.
        let json = elem.GetRawText()
        WeightMerge.mergeWithDefaults json BalanceConfig.defaultConfig

    let readBalanceConfigOption (elem: JsonElement) (name: string) : Result<BalanceConfig option, string> =
        match tryGetProperty name elem with
        | None -> Ok None
        | Some v ->
            if v.ValueKind = JsonValueKind.Null then
                Ok None
            elif v.ValueKind = JsonValueKind.Object then
                match readBalanceConfig v with
                | Ok cfg -> Ok(Some cfg)
                | Error e -> Error e
            else
                Error $"Property '{name}' must be object or null"

    // ── section writers / readers ─────────────────────────────────────

    let writeAppearance (writer: Utf8JsonWriter) (a: AppearanceSettings) =
        writer.WriteStartObject()
        writer.WriteString("accent", a.Accent)
        writer.WriteString("density", Density.toString a.Density)
        writer.WriteNumber("fontScale", a.FontScale)
        writer.WriteString("language", a.Language)
        writer.WriteBoolean("animations", a.Animations)
        writer.WriteBoolean("highContrast", a.HighContrast)
        writer.WriteString("pitchPalette", a.PitchPalette)
        writer.WriteEndObject()

    let readAppearance (elem: JsonElement) : Result<AppearanceSettings, string> =
        let accentR = getString "accent" elem

        let densityR = getString "density" elem |> Result.bind Density.tryParse

        let fontScaleR = getFloat "fontScale" elem
        let languageR = getString "language" elem
        let animationsR = getBool "animations" elem
        let highContrastR = getBool "highContrast" elem
        let pitchPaletteR = getString "pitchPalette" elem

        match accentR, densityR, fontScaleR, languageR, animationsR, highContrastR, pitchPaletteR with
        | Ok accent, Ok dens, Ok fs, Ok lang, Ok anim, Ok hc, Ok pitch ->
            Ok
                { Accent = accent
                  Density = dens
                  FontScale = fs
                  Language = lang
                  Animations = anim
                  HighContrast = hc
                  PitchPalette = pitch }
        | _ ->
            let errs =
                [ match accentR with
                  | Error e -> yield e
                  | _ -> ()
                  match densityR with
                  | Error e -> yield e
                  | _ -> ()
                  match fontScaleR with
                  | Error e -> yield e
                  | _ -> ()
                  match languageR with
                  | Error e -> yield e
                  | _ -> ()
                  match animationsR with
                  | Error e -> yield e
                  | _ -> ()
                  match highContrastR with
                  | Error e -> yield e
                  | _ -> ()
                  match pitchPaletteR with
                  | Error e -> yield e
                  | _ -> () ]

            Error(String.concat "; " errs)

    let writeMatchView (writer: Utf8JsonWriter) (m: MatchViewSettings) =
        writer.WriteStartObject()
        writer.WriteNumber("playbackSpeed", m.PlaybackSpeed)
        writer.WriteBoolean("autoPlay", m.AutoPlay)
        writer.WriteString("highlightFilter", HighlightFilter.toString m.HighlightFilter)
        writer.WriteNumber("tickRate", m.TickRate)
        writer.WriteBoolean("showXG", m.ShowXG)
        writer.WriteBoolean("showPossession", m.ShowPossession)
        writer.WriteBoolean("showOverlays", m.ShowOverlays)
        writer.WriteBoolean("soundEnabled", m.SoundEnabled)
        writer.WriteBoolean("pauseOnGoal", m.PauseOnGoal)
        writer.WriteBoolean("pauseOnRed", m.PauseOnRed)
        writer.WriteString("viewerQuality", ViewerQualityTier.toString m.ViewerQuality)
        writer.WriteEndObject()

    let readMatchView (elem: JsonElement) : Result<MatchViewSettings, string> =
        let playbackR = getInt "playbackSpeed" elem
        let autoPlayR = getBool "autoPlay" elem

        let highlightR =
            getString "highlightFilter" elem |> Result.bind HighlightFilter.tryParse

        let tickRateR = getInt "tickRate" elem
        let showXGR = getBool "showXG" elem
        let showPossR = getBool "showPossession" elem
        let showOverR = getBool "showOverlays" elem
        let soundR = getBool "soundEnabled" elem
        let pauseGoalR = getBool "pauseOnGoal" elem
        let pauseRedR = getBool "pauseOnRed" elem

        // Settings files written before the quality tier existed must keep loading; only a present-but-invalid value is an error.
        let qualityR =
            match elem.TryGetProperty "viewerQuality" with
            | true, _ -> getString "viewerQuality" elem |> Result.bind ViewerQualityTier.tryParse
            | _ -> Ok MatchViewSettings.defaults.ViewerQuality

        match
            playbackR, autoPlayR, highlightR, tickRateR, showXGR, showPossR, showOverR, soundR, pauseGoalR, pauseRedR
        with
        | Ok ps, Ok ap, Ok hf, Ok tr, Ok xg, Ok poss, Ok over, Ok sound, Ok pg, Ok pr when qualityR |> Result.isOk ->
            Ok
                { PlaybackSpeed = ps
                  AutoPlay = ap
                  HighlightFilter = hf
                  TickRate = tr
                  ShowXG = xg
                  ShowPossession = poss
                  ShowOverlays = over
                  SoundEnabled = sound
                  PauseOnGoal = pg
                  PauseOnRed = pr
                  ViewerQuality = qualityR |> Result.defaultValue MatchViewSettings.defaults.ViewerQuality }
        | _ ->
            let errs =
                [ match playbackR with
                  | Error e -> yield e
                  | _ -> ()
                  match autoPlayR with
                  | Error e -> yield e
                  | _ -> ()
                  match highlightR with
                  | Error e -> yield e
                  | _ -> ()
                  match tickRateR with
                  | Error e -> yield e
                  | _ -> ()
                  match showXGR with
                  | Error e -> yield e
                  | _ -> ()
                  match showPossR with
                  | Error e -> yield e
                  | _ -> ()
                  match showOverR with
                  | Error e -> yield e
                  | _ -> ()
                  match soundR with
                  | Error e -> yield e
                  | _ -> ()
                  match pauseGoalR with
                  | Error e -> yield e
                  | _ -> ()
                  match pauseRedR with
                  | Error e -> yield e
                  | _ -> ()
                  match qualityR with
                  | Error e -> yield e
                  | _ -> () ]

            Error(String.concat "; " errs)

    let writeSimulation (writer: Utf8JsonWriter) (s: SimulationSettings) =
        writer.WriteStartObject()
        writer.WriteString("difficultyPreset", DifficultyPreset.toString s.DifficultyPreset)
        writeBalanceConfigOption writer "balanceOverrides" s.BalanceOverrides
        writer.WriteNumber("refStrictness", s.RefStrictness)
        writer.WriteNumber("injuryFreq", s.InjuryFreq)
        writer.WriteNumber("fatigueFactor", s.FatigueFactor)
        writer.WriteBoolean("weatherEnabled", s.WeatherEnabled)
        writer.WriteNumber("homeAdvantage", s.HomeAdvantage)
        writer.WriteEndObject()

    let readSimulation (elem: JsonElement) : Result<SimulationSettings, string> =
        let presetR =
            getString "difficultyPreset" elem |> Result.bind DifficultyPreset.tryParse

        let balanceR = readBalanceConfigOption elem "balanceOverrides"
        let refStrictR = getFloat "refStrictness" elem
        let injuryR = getFloat "injuryFreq" elem
        let fatigueR = getFloat "fatigueFactor" elem
        let weatherR = getBool "weatherEnabled" elem
        let homeAdvR = getFloat "homeAdvantage" elem

        match presetR, balanceR, refStrictR, injuryR, fatigueR, weatherR, homeAdvR with
        | Ok preset, Ok bal, Ok rs, Ok inj, Ok fat, Ok weather, Ok ha ->
            Ok
                { DifficultyPreset = preset
                  BalanceOverrides = bal
                  RefStrictness = rs
                  InjuryFreq = inj
                  FatigueFactor = fat
                  WeatherEnabled = weather
                  HomeAdvantage = ha }
        | _ ->
            let errs =
                [ match presetR with
                  | Error e -> yield e
                  | _ -> ()
                  match balanceR with
                  | Error e -> yield e
                  | _ -> ()
                  match refStrictR with
                  | Error e -> yield e
                  | _ -> ()
                  match injuryR with
                  | Error e -> yield e
                  | _ -> ()
                  match fatigueR with
                  | Error e -> yield e
                  | _ -> ()
                  match weatherR with
                  | Error e -> yield e
                  | _ -> ()
                  match homeAdvR with
                  | Error e -> yield e
                  | _ -> () ]

            Error(String.concat "; " errs)

    let writeCareer (writer: Utf8JsonWriter) (c: CareerSettings) =
        writer.WriteStartObject()
        writer.WriteNumber("autoSaveDays", c.AutoSaveDays)
        writer.WriteString("newsFreq", NewsFreq.toString c.NewsFreq)
        writer.WriteBoolean("inboxGrouping", c.InboxGrouping)
        writer.WriteBoolean("tutorial", c.Tutorial)
        writer.WriteEndObject()

    let readCareer (elem: JsonElement) : Result<CareerSettings, string> =
        let autoSaveR = getInt "autoSaveDays" elem

        let newsFreqR = getString "newsFreq" elem |> Result.bind NewsFreq.tryParse

        let inboxR = getBool "inboxGrouping" elem
        let tutorialR = getBool "tutorial" elem

        match autoSaveR, newsFreqR, inboxR, tutorialR with
        | Ok days, Ok nf, Ok inbox, Ok tut ->
            Ok
                { AutoSaveDays = days
                  NewsFreq = nf
                  InboxGrouping = inbox
                  Tutorial = tut }
        | _ ->
            let errs =
                [ match autoSaveR with
                  | Error e -> yield e
                  | _ -> ()
                  match newsFreqR with
                  | Error e -> yield e
                  | _ -> ()
                  match inboxR with
                  | Error e -> yield e
                  | _ -> ()
                  match tutorialR with
                  | Error e -> yield e
                  | _ -> () ]

            Error(String.concat "; " errs)

    let writeData (writer: Utf8JsonWriter) (d: DataSettings) =
        writer.WriteStartObject()
        writeStringList writer "activeMods" d.ActiveMods
        writeStringList writer "modOrder" d.ModOrder
        writer.WriteEndObject()

    let readData (elem: JsonElement) : Result<DataSettings, string> =
        let activeR = readStringList "activeMods" elem
        let orderR = readStringList "modOrder" elem

        match activeR, orderR with
        | Ok active, Ok order ->
            Ok
                { ActiveMods = active
                  ModOrder = order }
        | _ ->
            let errs =
                [ match activeR with
                  | Error e -> yield e
                  | _ -> ()
                  match orderR with
                  | Error e -> yield e
                  | _ -> () ]

            Error(String.concat "; " errs)

    let writeScouting (writer: Utf8JsonWriter) (s: ScoutingSettings) =
        writer.WriteStartObject()
        writer.WriteNumber("aiNoise", s.AiNoise)
        writer.WriteNumber("halfLifeDays", s.HalfLifeDays)
        writer.WriteNumber("minConfidence", s.MinConfidence)
        writer.WriteEndObject()

    let readScouting (elem: JsonElement) : Result<ScoutingSettings, string> =
        let aiNoiseR = getFloat "aiNoise" elem
        let halfLifeR = getFloat "halfLifeDays" elem
        let minConfR = getFloat "minConfidence" elem

        match aiNoiseR, halfLifeR, minConfR with
        | Ok ai, Ok half, Ok minC ->
            Ok
                { AiNoise = ai
                  HalfLifeDays = half
                  MinConfidence = minC }
        | _ ->
            let errs =
                [ match aiNoiseR with
                  | Error e -> yield e
                  | _ -> ()
                  match halfLifeR with
                  | Error e -> yield e
                  | _ -> ()
                  match minConfR with
                  | Error e -> yield e
                  | _ -> () ]

            Error(String.concat "; " errs)

    let writeAdvanced (writer: Utf8JsonWriter) (a: AdvancedSettings) =
        writer.WriteStartObject()
        writer.WriteBoolean("showDebug", a.ShowDebug)
        writer.WriteString("logLevel", LogLevel.toString a.LogLevel)
        writer.WriteBoolean("unlockMotorEditor", a.UnlockMotorEditor)
        writer.WriteEndObject()

    let readAdvanced (elem: JsonElement) : Result<AdvancedSettings, string> =
        let showDebugR = getBool "showDebug" elem

        let logLevelR = getString "logLevel" elem |> Result.bind LogLevel.tryParse

        let unlockR = getBool "unlockMotorEditor" elem

        match showDebugR, logLevelR, unlockR with
        | Ok sd, Ok ll, Ok unlock ->
            Ok
                { ShowDebug = sd
                  LogLevel = ll
                  UnlockMotorEditor = unlock }
        | _ ->
            let errs =
                [ match showDebugR with
                  | Error e -> yield e
                  | _ -> ()
                  match logLevelR with
                  | Error e -> yield e
                  | _ -> ()
                  match unlockR with
                  | Error e -> yield e
                  | _ -> () ]

            Error(String.concat "; " errs)

module GlobalSettings =

    let currentVersion = JsonHelpers.currentVersion

    let defaults: GlobalSettings =
        { Appearance = AppearanceSettings.defaults
          MatchView = MatchViewSettings.defaults
          Career = CareerSettings.defaults
          Data = DataSettings.defaults
          Advanced = AdvancedSettings.defaults
          Version = currentVersion }

    let validate (g: GlobalSettings) : Result<GlobalSettings, string list> =
        let errs =
            (AppearanceSettings.validate g.Appearance
             @ MatchViewSettings.validate g.MatchView
             @ CareerSettings.validate g.Career
             @ DataSettings.validate g.Data
             @ AdvancedSettings.validate g.Advanced
             @ (if g.Version < 1 then
                    [ "GlobalSettings.Version must be >= 1" ]
                else
                    []))

        if List.isEmpty errs then Ok g else Error errs

    let toJson (settings: GlobalSettings) : string =
        use stream = new System.IO.MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
        writer.WriteStartObject()
        writer.WriteNumber("version", settings.Version)
        writer.WritePropertyName("appearance")
        JsonHelpers.writeAppearance writer settings.Appearance
        writer.WritePropertyName("matchView")
        JsonHelpers.writeMatchView writer settings.MatchView
        writer.WritePropertyName("career")
        JsonHelpers.writeCareer writer settings.Career
        writer.WritePropertyName("data")
        JsonHelpers.writeData writer settings.Data
        writer.WritePropertyName("advanced")
        JsonHelpers.writeAdvanced writer settings.Advanced
        writer.WriteEndObject()
        writer.Flush()
        System.Text.Encoding.UTF8.GetString(stream.ToArray())

    let fromJson (json: string) : Result<GlobalSettings, string> =
        try
            use doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            if root.ValueKind <> JsonValueKind.Object then
                Error "GlobalSettings JSON must be an object"
            else
                let version = JsonHelpers.getVersion root

                let appearanceR =
                    match JsonHelpers.tryGetProperty "appearance" root with
                    | None -> Error "Missing property 'appearance'"
                    | Some v -> JsonHelpers.readAppearance v

                let matchViewR =
                    match JsonHelpers.tryGetProperty "matchView" root with
                    | None -> Error "Missing property 'matchView'"
                    | Some v -> JsonHelpers.readMatchView v

                let careerR =
                    match JsonHelpers.tryGetProperty "career" root with
                    | None -> Error "Missing property 'career'"
                    | Some v -> JsonHelpers.readCareer v

                let dataR =
                    match JsonHelpers.tryGetProperty "data" root with
                    | None -> Error "Missing property 'data'"
                    | Some v -> JsonHelpers.readData v

                let advancedR =
                    match JsonHelpers.tryGetProperty "advanced" root with
                    | None -> Error "Missing property 'advanced'"
                    | Some v -> JsonHelpers.readAdvanced v

                match appearanceR, matchViewR, careerR, dataR, advancedR with
                | Ok a, Ok m, Ok c, Ok d, Ok adv ->
                    Ok
                        { Appearance = a
                          MatchView = m
                          Career = c
                          Data = d
                          Advanced = adv
                          Version = version }
                | _ ->
                    let errs =
                        [ match appearanceR with
                          | Error e -> yield e
                          | _ -> ()
                          match matchViewR with
                          | Error e -> yield e
                          | _ -> ()
                          match careerR with
                          | Error e -> yield e
                          | _ -> ()
                          match dataR with
                          | Error e -> yield e
                          | _ -> ()
                          match advancedR with
                          | Error e -> yield e
                          | _ -> () ]

                    Error(String.concat "; " errs)
        with ex ->
            Error $"Invalid JSON: {ex.Message}"

module SaveSettings =

    let currentVersion = JsonHelpers.currentVersion

    let defaults: SaveSettings =
        { Simulation = SimulationSettings.defaults
          Scouting = ScoutingSettings.defaults
          Version = currentVersion }

    let validate (s: SaveSettings) : Result<SaveSettings, string list> =
        let errs =
            (SimulationSettings.validate s.Simulation
             @ ScoutingSettings.validate s.Scouting
             @ (if s.Version < 1 then
                    [ "SaveSettings.Version must be >= 1" ]
                else
                    []))

        if List.isEmpty errs then Ok s else Error errs

    let toJson (settings: SaveSettings) : string =
        use stream = new System.IO.MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
        writer.WriteStartObject()
        writer.WriteNumber("version", settings.Version)
        writer.WritePropertyName("simulation")
        JsonHelpers.writeSimulation writer settings.Simulation
        writer.WritePropertyName("scouting")
        JsonHelpers.writeScouting writer settings.Scouting
        writer.WriteEndObject()
        writer.Flush()
        System.Text.Encoding.UTF8.GetString(stream.ToArray())

    let fromJson (json: string) : Result<SaveSettings, string> =
        try
            use doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            if root.ValueKind <> JsonValueKind.Object then
                Error "SaveSettings JSON must be an object"
            else
                let version = JsonHelpers.getVersion root

                let simR =
                    match JsonHelpers.tryGetProperty "simulation" root with
                    | None -> Error "Missing property 'simulation'"
                    | Some v -> JsonHelpers.readSimulation v

                let scoutingR =
                    match JsonHelpers.tryGetProperty "scouting" root with
                    | None -> Error "Missing property 'scouting'"
                    | Some v -> JsonHelpers.readScouting v

                match simR, scoutingR with
                | Ok sim, Ok scout ->
                    Ok
                        { Simulation = sim
                          Scouting = scout
                          Version = version }
                | _ ->
                    let errs =
                        [ match simR with
                          | Error e -> yield e
                          | _ -> ()
                          match scoutingR with
                          | Error e -> yield e
                          | _ -> () ]

                    Error(String.concat "; " errs)
        with ex ->
            Error $"Invalid JSON: {ex.Message}"

// ── Balance resolver ────────────────────────────────────────────────────
// Thin Settings → BalanceConfig projection that respects Save > Global >
// default priority and validates without throwing. Delegates to WeightMerge
// so priority logic stays single-sourced.
module BalanceResolver =

    let effectiveBalance (saveOpt: SaveSettings option) : BalanceConfig =
        let saveCfg = saveOpt |> Option.bind (fun s -> s.Simulation.BalanceOverrides)
        WeightMerge.mergeWithOverridesSingle saveCfg

    let effectiveBalanceWithGlobal (globalOpt: GlobalSettings option) (saveOpt: SaveSettings option) : BalanceConfig =
        // Global currently has no BalanceOverrides — placeholder for forward compat
        let _globalCfg: BalanceConfig option = None
        let saveCfg = saveOpt |> Option.bind (fun s -> s.Simulation.BalanceOverrides)
        let _ = globalOpt
        WeightMerge.mergeWithOverrides _globalCfg saveCfg

// ── Aliases for spec shorthand (Appearance.defaults etc.) ────────────
// Spec lists short module names; we keep canonical *Settings modules above
// and expose aliases so both `AppearanceSettings.defaults` and
// `Appearance.defaults` compile.

module Appearance =
    let defaults = AppearanceSettings.defaults
    let validate = AppearanceSettings.validate

module MatchView =
    let defaults = MatchViewSettings.defaults
    let validate = MatchViewSettings.validate

module Simulation =
    let defaults = SimulationSettings.defaults
    let validate = SimulationSettings.validate
    let applyPreset = SimulationSettings.applyPreset

module Career =
    let defaults = CareerSettings.defaults
    let validate = CareerSettings.validate

module Data =
    let defaults = DataSettings.defaults
    let validate = DataSettings.validate

module Scouting =
    let defaults = ScoutingSettings.defaults
    let validate = ScoutingSettings.validate

module Advanced =
    let defaults = AdvancedSettings.defaults
    let validate = AdvancedSettings.validate
