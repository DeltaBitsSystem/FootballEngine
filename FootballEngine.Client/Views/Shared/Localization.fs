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
    | MatchStats
    | MatchPossession
    | MatchShots
    | MatchOnTarget
    | MatchPasses
    | MatchBallCarrier
    | MatchFit
    | MatchTired
    | MatchOverview
    | MatchCamera
    | MatchCameraBroadcast
    | MatchCameraTactical
    | MatchCameraBehindGoal
    | MatchCameraTouchline
    | MatchCameraCloseUp
    | MatchPlayers
    | MatchSubstitutions
    | MatchTouchline
    | MatchPause
    | MatchResume
    | MatchLeave
    | MatchPostMatch
    | MatchDefensive
    | MatchBalanced
    | MatchPositive
    | MatchAttacking
    | MatchEncourage
    | MatchDemandMore
    | MatchCalmDown
    | MatchConfirmSubstitution
    | MatchNoEligibleSubstitute
    | MatchStartersAndSubstitutes
    | JourneyTitle
    | PrepareNextMatch
    | AdvanceNextEvent
    | AdvanceNextEventHint
    | JourneyWorking
    | StopAfterDay
    | LineupReady
    | LineupNeedsAttention
    | ReviewLineup
    | PendingDecisions
    | LineupCount
    | LineupEmpty
    | LineupUnavailable
    | LineupDuplicates
    | CurrentPlan
    | ChangesNextMatch
    | StartPreparedMatch
    | BeforeKickoff
    | PreparationHint
    | NoScheduledFixture
    | ResultRecorded
    | ResultAwaitingConfirmation
    | ConfirmMatchResult
    | MatchFacts
    | OpenLastResult
    | ReturnHome
    | ReviewCalendar
    | RecordedEvents
    | MarkReviewed
    | MarkReviewedHint
    | InboxAll
    | InboxDecisions
    | InboxUnread
    | NoMessagesInFilter
    | ClearFilters
    | ReviewArea
    | AdvanceAttention
    | DraftNotSaved
    | SelectLineupSlot
    | ChooseForSlot
    | PlayerChoice
    | UndoLineup
    | SuggestLineup
    | MatchEstimateHint
    | SaveAfterMatch
    | TransferMarket
    | LeaveMatchWarning
    | LeaveMatch
    | KeepPlaying
    | MatchEvidence
    | MatchChangeQueued
    | MatchChangeAccepted
    | MatchChangeApplied
    | MatchChangeRejected
    | MatchChangeHint
    | MatchMentalityHint
    | MatchPauseOnLeave
    | MatchSessionHint
    | AdvanceFailed

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
          TranslationKey.ItemsCount, "{count} elementos", "{count} items"
          TranslationKey.MatchStats, "ESTADÍSTICAS", "MATCH STATS"
          TranslationKey.MatchPossession, "POSESIÓN", "POSSESSION"
          TranslationKey.MatchShots, "TIROS", "SHOTS"
          TranslationKey.MatchOnTarget, "A PUERTA", "ON TARGET"
          TranslationKey.MatchPasses, "PASES", "PASSES"
          TranslationKey.MatchBallCarrier, "CON BALÓN", "BALL CARRIER"
          TranslationKey.MatchFit, "EN FORMA", "FIT"
          TranslationKey.MatchTired, "CANSADO", "TIRED"
          TranslationKey.MatchOverview, "Resumen", "Overview"
          TranslationKey.MatchCamera, "Cámara", "Camera"
          TranslationKey.MatchCameraBroadcast, "Transmisión", "Broadcast"
          TranslationKey.MatchCameraTactical, "Táctica elevada", "Tactical overview"
          TranslationKey.MatchCameraBehindGoal, "Detrás del arco", "Behind goal"
          TranslationKey.MatchCameraTouchline, "Banda", "Touchline"
          TranslationKey.MatchCameraCloseUp, "Primer plano", "Close-up"
          TranslationKey.MatchPlayers, "Jugadores", "Players"
          TranslationKey.MatchSubstitutions, "Cambios", "Subs"
          TranslationKey.MatchTouchline, "Banda", "Touchline"
          TranslationKey.MatchPause, "Pausa", "Pause"
          TranslationKey.MatchResume, "Reanudar", "Resume"
          TranslationKey.MatchLeave, "Salir", "Leave"
          TranslationKey.MatchPostMatch, "Postpartido", "Post-match"
          TranslationKey.MatchDefensive, "Defensiva", "Defensive"
          TranslationKey.MatchBalanced, "Equilibrada", "Balanced"
          TranslationKey.MatchPositive, "Positiva", "Positive"
          TranslationKey.MatchAttacking, "Ofensiva", "Attacking"
          TranslationKey.MatchEncourage, "Animar", "Encourage"
          TranslationKey.MatchDemandMore, "Exigir más", "Demand more"
          TranslationKey.MatchCalmDown, "Calmar", "Calm down"
          TranslationKey.MatchConfirmSubstitution, "Confirmar cambio", "Confirm substitution"
          TranslationKey.MatchNoEligibleSubstitute, "Sin suplentes disponibles", "No eligible substitute"
          TranslationKey.MatchStartersAndSubstitutes,
          "{starters} titulares · {substitutes} suplentes",
          "{starters} starters · {substitutes} substitutes"
          TranslationKey.JourneyTitle, "Tu próxima decisión", "Your next decision"
          TranslationKey.PrepareNextMatch, "Preparar partido", "Prepare match"
          TranslationKey.AdvanceNextEvent, "Hasta el próximo partido", "Until the next match"
          TranslationKey.AdvanceNextEventHint,
          "Avanza día a día. Se detiene ante una nueva decisión, una baja o tu partido. No juega por ti.",
          "Advances day by day. Stops for a new decision, an unavailable player or your match. Never plays for you."
          TranslationKey.JourneyWorking, "Avanzando el día…", "Advancing the day…"
          TranslationKey.StopAfterDay, "Detener al terminar el día", "Stop after this day"
          TranslationKey.LineupReady, "Once disponible", "Lineup available"
          TranslationKey.LineupNeedsAttention, "Revisa el once antes de empezar", "Review the lineup before starting"
          TranslationKey.ReviewLineup, "Revisar once y táctica", "Review lineup and tactics"
          TranslationKey.PendingDecisions, "Mensajes por revisar: {count}", "Messages to review: {count}"
          TranslationKey.LineupCount, "{count}/11 puestos asignados", "{count}/11 positions assigned"
          TranslationKey.LineupEmpty, "Faltan {count} jugadores en el once.", "{count} starting positions are empty."
          TranslationKey.LineupUnavailable, "No disponibles: {names}", "Unavailable: {names}"
          TranslationKey.LineupDuplicates,
          "Un jugador ocupa más de un puesto. Corrige el once.",
          "A player occupies more than one position. Review the lineup."
          TranslationKey.CurrentPlan, "Plan actual: {formation}", "Current plan: {formation}"
          TranslationKey.ChangesNextMatch,
          "Los cambios se usan en el próximo partido. Puedes deshacerlos antes de empezar.",
          "Changes are used in your next match. You can undo them before starting."
          TranslationKey.StartPreparedMatch, "Confirmar once e iniciar partido", "Confirm lineup and start match"
          TranslationKey.BeforeKickoff, "Antes del inicio", "Before kickoff"
          TranslationKey.PreparationHint,
          "El partido aún no empezó. Revisa tu equipo; al iniciar se utilizarán el once y las instrucciones actuales.",
          "The match has not started. Review your team; kickoff uses your current lineup and instructions."
          TranslationKey.NoScheduledFixture,
          "No hay otro partido programado. Puedes revisar el calendario o avanzar un día.",
          "No further match is scheduled. Review the calendar or advance one day."
          TranslationKey.ResultRecorded, "Resultado registrado", "Result recorded"
          TranslationKey.ResultAwaitingConfirmation,
          "Partido terminado · pendiente de registrar",
          "Match finished · awaiting confirmation"
          TranslationKey.ConfirmMatchResult, "Registrar resultado y continuar", "Record result and continue"
          TranslationKey.MatchFacts, "Hechos del partido", "Match facts"
          TranslationKey.OpenLastResult, "Revisar último resultado", "Review last result"
          TranslationKey.ReturnHome, "Volver a Hoy", "Back to Today"
          TranslationKey.ReviewCalendar, "Ver calendario", "View calendar"
          TranslationKey.RecordedEvents,
          "{count} eventos registrados. El marcador no explica por sí solo las causas del resultado.",
          "{count} events recorded. The score alone does not explain what caused the result."
          TranslationKey.MarkReviewed, "Marcar como revisado", "Mark reviewed"
          TranslationKey.MarkReviewedHint,
          "Solo marca el mensaje como revisado. No acepta ofertas ni ejecuta decisiones por ti.",
          "Only marks the message reviewed. Does not accept offers or make decisions for you."
          TranslationKey.InboxAll, "Todos", "All"
          TranslationKey.InboxDecisions, "Por revisar", "To review"
          TranslationKey.InboxUnread, "No leídos", "Unread"
          TranslationKey.NoMessagesInFilter, "No hay mensajes en este filtro.", "No messages match this filter."
          TranslationKey.ClearFilters, "Mostrar todos", "Show all"
          TranslationKey.ReviewArea, "Abrir {area}", "Open {area}"
          TranslationKey.AdvanceAttention,
          "Avance detenido: hay novedades que revisar en tu equipo o en la bandeja.",
          "Advance stopped: review new information about your team or inbox."
          TranslationKey.DraftNotSaved, "Guardar cambios", "Save changes"
          TranslationKey.SelectLineupSlot,
          "Elige un puesto en la cancha para comparar y seleccionar un jugador. También puedes arrastrar desde la lista.",
          "Choose a position on the pitch to compare and select a player. You can also drag from the list."
          TranslationKey.ChooseForSlot, "Seleccionar para {role}", "Select for {role}"
          TranslationKey.PlayerChoice,
          "{name} · {position} · afinidad {stars}★ · estado {fitness}%",
          "{name} · {position} · suitability {stars}★ · fitness {fitness}%"
          TranslationKey.UndoLineup, "Deshacer", "Undo"
          TranslationKey.SuggestLineup, "Elegir once sugerido", "Use suggested lineup"
          TranslationKey.MatchEstimateHint,
          "Remates contados en los eventos; xG estimado por el motor. Indican producción de ocasiones, no una explicación causal completa.",
          "Shots counted from events; xG estimated by the engine. They indicate chance production, not a complete causal explanation."
          TranslationKey.SaveAfterMatch,
          "Registra el resultado antes de guardar la carrera. El partido en curso solo se conserva durante esta sesión.",
          "Record the result before saving the career. The active match is only retained during this session."
          TranslationKey.TransferMarket, "Mercado de fichajes", "Transfer market"
          TranslationKey.LeaveMatchWarning, "El partido en curso o su resultado pendiente no están guardados. Si sales, perderás este partido; tu último guardado se conserva.", "The active match or its pending result is not saved. Leaving loses this match; your last save is retained."
          TranslationKey.LeaveMatch, "Salir sin guardar el partido", "Leave without saving the match"
          TranslationKey.KeepPlaying, "Seguir jugando", "Keep playing"
          TranslationKey.MatchEvidence, "Evidencia", "Evidence"
          TranslationKey.MatchChangeQueued, "Enviada", "Sent"
          TranslationKey.MatchChangeAccepted, "Aceptada; espera el momento de aplicación", "Accepted; waiting for the application point"
          TranslationKey.MatchChangeApplied, "Aplicada por el motor · revisión {revision}", "Applied by the engine · revision {revision}"
          TranslationKey.MatchChangeRejected, "No aplicada: {reason}. Revisa el estado y vuelve a intentarlo.", "Not applied: {reason}. Check the current state and try again."
          TranslationKey.MatchChangeHint, "Aplicada confirma la orden, no su eficacia. Contrasta las jugadas y estadísticas posteriores en Evidencia.", "Applied confirms the instruction, not its effectiveness. Compare later play and statistics in Evidence."
          TranslationKey.MatchMentalityHint, "Una mentalidad más ofensiva aumenta el riesgo; no garantiza ocasiones ni goles.", "A more attacking mentality increases risk; it does not guarantee chances or goals."
          TranslationKey.MatchPauseOnLeave, "Vuelve a Hoy y pausa el partido sin descartarlo. Retómalo con Continuar.", "Return home and pause without discarding the match. Resume it with Continue."
          TranslationKey.MatchSessionHint, "Hay un partido en curso. Se pausa al salir de su pantalla; vuelve al partido para gestionar cambios. Los cambios en Táctica de carrera se usarán en el próximo partido, no en el actual.", "A match is in progress. It pauses when you leave its screen; return to the match to manage changes. Career Tactics edits apply to the next match, not the current one."
          TranslationKey.AdvanceFailed, "El avance se detuvo. No se aplicó el paso fallido. Puedes volver a intentar desde la fecha actual. Detalle: {error}", "Progress stopped. The failed step was not applied. You can retry from the current date. Detail: {error}" ]

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
