namespace FootballEngine

open System
open System.Collections.Generic
open System.Diagnostics
open System.Text.RegularExpressions

type Locale =
    | Es
    | En

module Locale =
    let defaultValue = Es

    let toCode =
        function
        | Es -> "es"
        | En -> "en"

    let tryParse (value: string) : Locale option =
        match value with
        | value when String.Equals(value, "es", StringComparison.OrdinalIgnoreCase) -> Some Es
        | value when String.Equals(value, "en", StringComparison.OrdinalIgnoreCase) -> Some En
        | _ -> None

type TranslationKey =
    | AppTitle
    | Loading
    | Retry
    | Cancel
    | Close
    | Save
    | Continue
    | ErrorTitle
    | DisabledTitle
    | NoResults
    | ContinueToDate
    | GoToMatch
    | Today
    | Inbox
    | Squad
    | Tactics
    | Training
    | Staff
    | Scouting
    | Shortlist
    | Negotiations
    | Finances
    | Board
    | Youth
    | Calendar
    | Competitions
    | News
    | Settings
    | SaveStatusSaving
    | SaveStatusSaved
    | SaveStatusError
    | Back
    | Forward
    | ManualSave
    | NewCareer
    | LoadGame
    | NoSaveSlots
    | ItemsCount

type TranslationCatalog =
    { Es: Map<TranslationKey, string>
      En: Map<TranslationKey, string> }

module Localization =
    let private entries: (TranslationKey * string * string) list =
        [ TranslationKey.AppTitle, "Football Engine", "Football Engine"
          TranslationKey.Loading, "Cargando...", "Loading..."
          TranslationKey.Retry, "Reintentar", "Retry"
          TranslationKey.Cancel, "Cancelar", "Cancel"
          TranslationKey.Close, "Cerrar", "Close"
          TranslationKey.Save, "Guardar", "Save"
          TranslationKey.Continue, "Continuar", "Continue"
          TranslationKey.ErrorTitle, "No se pudo completar la accion", "The action could not be completed"
          TranslationKey.DisabledTitle, "Accion no disponible", "Action unavailable"
          TranslationKey.NoResults, "No hay resultados", "No results"
          TranslationKey.ContinueToDate, "Continuar al {date}", "Continue to {date}"
          TranslationKey.GoToMatch, "Ir al partido", "Go to match"
          TranslationKey.Today, "Hoy", "Today"
          TranslationKey.Inbox, "Bandeja", "Inbox"
          TranslationKey.Squad, "Plantel", "Squad"
          TranslationKey.Tactics, "Táctica", "Tactics"
          TranslationKey.Training, "Entrenamiento", "Training"
          TranslationKey.Staff, "Staff", "Staff"
          TranslationKey.Scouting, "Scouting", "Scouting"
          TranslationKey.Shortlist, "Lista", "Shortlist"
          TranslationKey.Negotiations, "Negociaciones", "Negotiations"
          TranslationKey.Finances, "Finanzas", "Finances"
          TranslationKey.Board, "Directiva", "Board"
          TranslationKey.Youth, "Cantera", "Youth"
          TranslationKey.Calendar, "Calendario", "Calendar"
          TranslationKey.Competitions, "Competiciones", "Competitions"
          TranslationKey.News, "Noticias", "News"
          TranslationKey.Settings, "Ajustes", "Settings"
          TranslationKey.SaveStatusSaving, "Guardando", "Saving"
          TranslationKey.SaveStatusSaved, "Guardado", "Saved"
          TranslationKey.SaveStatusError, "Error de guardado", "Save error"
          TranslationKey.Back, "Atrás", "Back"
          TranslationKey.Forward, "Adelante", "Forward"
          TranslationKey.ManualSave, "Guardar", "Save"
          TranslationKey.NewCareer, "Nueva carrera", "New career"
          TranslationKey.LoadGame, "Cargar partida", "Load game"
          TranslationKey.NoSaveSlots, "No hay partidas guardadas", "No saved careers"
          TranslationKey.ItemsCount, "{count} elementos", "{count} items" ]

    let catalog: TranslationCatalog =
        { Es = entries |> List.map (fun (key, es, _) -> key, es) |> Map.ofList
          En = entries |> List.map (fun (key, _, en) -> key, en) |> Map.ofList }

    let private activeLocale = ref Locale.defaultValue

    let active () = activeLocale.Value

    let setActive (locale: Locale) = activeLocale.Value <- locale

    let setActiveCode (code: string) : Result<unit, string> =
        match Locale.tryParse code with
        | Some locale ->
            setActive locale
            Ok()
        | None -> Error $"Unsupported locale '{code}'"

    let private developmentFallback (key: TranslationKey) =
        let environment = Environment.GetEnvironmentVariable("FOOTBALL_ENGINE_ENV")

        if
            Debugger.IsAttached
            || String.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)
        then
            $"[[missing:{key}]]"
        else
            raise (KeyNotFoundException $"Missing translation for '{key}'")

    let private render (key: TranslationKey) (template: string) (parameters: (string * string) list) =
        let values = parameters |> Map.ofList

        let placeholders =
            Regex.Matches(template, @"\{([A-Za-z0-9_.-]+)\}")
            |> Seq.cast<Match>
            |> Seq.map (fun m -> m.Groups.[1].Value)
            |> Set.ofSeq

        let missing =
            placeholders |> Set.filter (fun name -> not (Map.containsKey name values))

        if not missing.IsEmpty then
            let names = String.concat ", " missing
            invalidArg "parameters" $"Missing parameters for '{key}': {names}"

        (template, values)
        ||> Map.fold (fun text name value -> text.Replace("{" + name + "}", value))

    // The only text lookup used by future views. Templates use named parameters
    // so translated word order never depends on string concatenation.
    let resolve (key: TranslationKey) (parameters: (string * string) list) : string =
        let source =
            match activeLocale.Value with
            | Locale.Es -> catalog.Es
            | Locale.En -> catalog.En

        match Map.tryFind key source with
        | Some template -> render key template parameters
        | None -> developmentFallback key
