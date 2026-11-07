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
    | SearchPlaceholder
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
    | TransfersTitle
    | TransfersSubtitle
    | InboxTitle
    | TrainingEmphasis
    | TrainingThisWeek
    | ColValue
    | ColWage
    | ColStatus
    | TransfersBudget
    | FinanceTitle
    | FinanceSubtitle
    | FinanceCash
    | FinanceCashSub
    | FinanceTransfers
    | FinanceTransfersSub
    | FinanceWages
    | FinanceWagesSub
    | FinanceForecast
    | FinanceForecastSub
    | FinanceReservations
    | FinanceObligations
    | FinanceDebt
    | FinanceLedger
    | FinanceLedgerEmpty
    | LedgerIncome
    | LedgerExpense
    | LedgerCommitment
    | CalendarTitle
    | CalendarSummary
    | FixturePending
    | FixtureNext
    | NoCareerLoaded
    | TacticsFamiliarity
    | TacticsSelection
    | ColPosition
    | ColPlayer
    | ColSuitability
    | ColFitness
    | Substitutes
    | SubstitutesAvailable
    | NoFixtureScheduled
    | TacticsFormation
    | TacticsHowWePlay
    | TacticsPlaybooks
    | TacticsApply
    | TacticsActive
    | InstrMentality
    | InstrDefensiveLine
    | InstrPressingIntensity
    | InstrWidth
    | InstrTempo
    | InstrDirectness
    | Level1
    | Level2
    | Level3
    | Level4
    | Level5
    | StyleTotalPress
    | StyleHuntBall
    | StyleLowBlock
    | StylePatient
    | StyleWideGame
    | StyleBalanced
    | PlaybookEffects
    | PlaybookCommitments
    | PlaybookBasis
    | AttrGroupPhysical
    | AttrGroupMental
    | AttrGroupTechnical
    | AttrGroupGoalkeeping
    | AttrAcceleration
    | AttrPace
    | AttrAgility
    | AttrBalance
    | AttrJumpingReach
    | AttrStamina
    | AttrStrength
    | AttrAggression
    | AttrComposure
    | AttrConcentration
    | AttrVision
    | AttrPositioning
    | AttrBravery
    | AttrWorkRate
    | AttrLeadership
    | AttrFinishing
    | AttrLongShots
    | AttrDribbling
    | AttrBallControl
    | AttrPassing
    | AttrCrossing
    | AttrTackling
    | AttrMarking
    | AttrHeading
    | AttrFreeKick
    | AttrPenalty
    | AttrReflexes
    | AttrHandling
    | AttrKicking
    | AttrOneOnOne
    | AttrAerialReach
    | SquadTitle
    | SquadSummary
    | SortByLine
    | SortBySkill
    | SortByAge
    | SortByValue
    | SortByName
    | LineGoalkeepers
    | LineDefenders
    | LineMidfielders
    | LineAttackers
    | PlayerAgeMorale
    | PlayerCondition
    | PlayerLevel
    | PlayerPotential
    | PlayerMorale
    | PlayerMatchFitness
    | PlayerContract
    | PlayerAge
    | SelectPlayer
    | SelectPlayerHint
    | StatusInjured
    | StatusSuspended
    | HomeLeagues
    | HomeCups
    | HomeInternational
    | StatCash
    | StatCashSub
    | StatPosition
    | StatPositionShort
    | StatForm
    | StatMorale
    | MoraleHigh
    | MoraleOkay
    | MoraleLow
    | CopyNotification
    | ClearAll
    | Notifications
    | NoNotifications
    | UpcomingFixtures
    | NoUpcomingFixtures
    | RecentResults
    | NoResultsYet
    | TableClub
    | TableRecord
    | TablePoints
    | NoStandings
    | HomeTag
    | AwayTag
    | MatchdayBadge
    | NextMatchBadge
    | VenueHome
    | VenueAway
    | Versus
    | RivalStudiedTitle
    | RivalStudiedBody
    | RivalUnknownTitle
    | RivalUnknownBody
    | CorridorLeft
    | CorridorCenter
    | CorridorRight
    | StyleShortPassing
    | StyleMixedPassing
    | StyleLongBalls
    | StyleDirect
    | StyleCrosses
    | AdjustTactics
    | InboxImportantTitle
    | InboxSeeAll
    | InboxRoutineHint
    | InboxAllClear
    | InboxOpen
    | InboxLater
    | CategoryDevelopment
    | CategoryTransfer
    | CategoryBoard
    | CategoryMatch
    | CategoryInjury
    | CategoryContract
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
          TranslationKey.SearchPlaceholder, "Buscar jugadores, clubes o mensajes", "Search players, clubs or messages"
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
          TranslationKey.TransfersTitle, "Mercado", "Transfers"
          TranslationKey.TransfersSubtitle, "Buscá, negociá y armá tu plantel", "Recruit, negotiate and build your squad"
          TranslationKey.InboxTitle, "Bandeja", "Inbox"
          TranslationKey.TrainingEmphasis, "Énfasis del equipo", "Team emphasis"
          TranslationKey.TrainingThisWeek, "Esta semana", "This week"
          TranslationKey.ColValue, "Valor", "Value"
          TranslationKey.ColWage, "Salario", "Wage"
          TranslationKey.ColStatus, "Estado", "Status"
          TranslationKey.TransfersBudget, "Presupuesto", "Budget"
          TranslationKey.FinanceTitle, "Finanzas", "Finances"
          TranslationKey.FinanceSubtitle, "Caja, autorizaciones y compromisos confirmados", "Cash, authorisations and confirmed commitments"
          TranslationKey.FinanceCash, "Caja", "Cash"
          TranslationKey.FinanceCashSub, "Disponible hoy", "Available today"
          TranslationKey.FinanceTransfers, "Fichajes", "Transfers"
          TranslationKey.FinanceTransfersSub, "Capacidad libre", "Free capacity"
          TranslationKey.FinanceWages, "Salarios", "Wages"
          TranslationKey.FinanceWagesSub, "Compromiso", "Commitment"
          TranslationKey.FinanceForecast, "Previsión", "Forecast"
          TranslationKey.FinanceForecastSub, "30 días", "30 days"
          TranslationKey.FinanceReservations, "Reservas activas", "Active reservations"
          TranslationKey.FinanceObligations, "Obligaciones pendientes", "Pending obligations"
          TranslationKey.FinanceDebt, "Deuda", "Debt"
          TranslationKey.FinanceLedger, "Últimas operaciones", "Recent operations"
          TranslationKey.FinanceLedgerEmpty, "Todavía no hubo movimientos de caja esta temporada.", "No cash movements yet this season."
          TranslationKey.LedgerIncome, "Ingreso", "Income"
          TranslationKey.LedgerExpense, "Egreso", "Expense"
          TranslationKey.LedgerCommitment, "Compromiso", "Commitment"
          TranslationKey.CalendarTitle, "Calendario", "Calendar"
          TranslationKey.CalendarSummary, "Temporada {season} · {count} partidos", "Season {season} · {count} matches"
          TranslationKey.FixturePending, "Pendiente", "Pending"
          TranslationKey.FixtureNext, "Próximo", "Next"
          TranslationKey.NoCareerLoaded, "No hay una carrera cargada", "No career loaded"
          TranslationKey.TacticsFamiliarity, "Familiaridad con el {formation}", "Familiarity with {formation}"
          TranslationKey.TacticsSelection, "Elección del once", "Selection"
          TranslationKey.ColPosition, "Pos", "Pos"
          TranslationKey.ColPlayer, "Jugador", "Player"
          TranslationKey.ColSuitability, "Encaje", "Fit"
          TranslationKey.ColFitness, "Físico", "Fitness"
          TranslationKey.Substitutes, "Suplentes", "Substitutes"
          TranslationKey.SubstitutesAvailable, "{count} disponibles", "{count} available"
          TranslationKey.NoFixtureScheduled, "Sin partido programado", "No fixture scheduled"
          TranslationKey.TacticsFormation, "Formación", "Formation"
          TranslationKey.TacticsHowWePlay, "Cómo jugamos", "How we play"
          TranslationKey.TacticsPlaybooks, "Planes según el partido", "Match-situation playbooks"
          TranslationKey.TacticsApply, "Aplicar", "Apply"
          TranslationKey.TacticsActive, "Activo", "Active"
          TranslationKey.InstrMentality, "Mentalidad", "Mentality"
          TranslationKey.InstrDefensiveLine, "Línea defensiva", "Defensive line"
          TranslationKey.InstrPressingIntensity, "Presión", "Pressing"
          TranslationKey.InstrWidth, "Amplitud", "Width"
          TranslationKey.InstrTempo, "Ritmo", "Tempo"
          TranslationKey.InstrDirectness, "Juego directo", "Directness"
          TranslationKey.Level1, "Muy bajo", "Very low"
          TranslationKey.Level2, "Bajo", "Low"
          TranslationKey.Level3, "Medio", "Medium"
          TranslationKey.Level4, "Alto", "High"
          TranslationKey.Level5, "Muy alto", "Very high"
          TranslationKey.StyleTotalPress, "Presión total", "Total press"
          TranslationKey.StyleHuntBall, "Ir a buscarlos", "Hunt the ball"
          TranslationKey.StyleLowBlock, "Cerrojo y contra", "Low block and counter"
          TranslationKey.StylePatient, "Tenencia paciente", "Patient possession"
          TranslationKey.StyleWideGame, "Juego por las bandas", "Wide game"
          TranslationKey.StyleBalanced, "Equilibrio", "Balanced"
          TranslationKey.PlaybookEffects, "Efectos: {text}", "Effects: {text}"
          TranslationKey.PlaybookCommitments, "Compromisos: {text}", "Commitments: {text}"
          TranslationKey.PlaybookBasis, "Recomendado por: {text}", "Recommended because: {text}"
          TranslationKey.AttrGroupPhysical, "Físico", "Physical"
          TranslationKey.AttrGroupMental, "Mental", "Mental"
          TranslationKey.AttrGroupTechnical, "Técnica", "Technical"
          TranslationKey.AttrGroupGoalkeeping, "Arquero", "Goalkeeping"
          TranslationKey.AttrAcceleration, "Aceleración", "Acceleration"
          TranslationKey.AttrPace, "Velocidad", "Pace"
          TranslationKey.AttrAgility, "Agilidad", "Agility"
          TranslationKey.AttrBalance, "Equilibrio", "Balance"
          TranslationKey.AttrJumpingReach, "Salto", "Jumping reach"
          TranslationKey.AttrStamina, "Resistencia", "Stamina"
          TranslationKey.AttrStrength, "Fuerza", "Strength"
          TranslationKey.AttrAggression, "Agresividad", "Aggression"
          TranslationKey.AttrComposure, "Serenidad", "Composure"
          TranslationKey.AttrConcentration, "Concentración", "Concentration"
          TranslationKey.AttrVision, "Visión", "Vision"
          TranslationKey.AttrPositioning, "Colocación", "Positioning"
          TranslationKey.AttrBravery, "Valentía", "Bravery"
          TranslationKey.AttrWorkRate, "Sacrificio", "Work rate"
          TranslationKey.AttrLeadership, "Liderazgo", "Leadership"
          TranslationKey.AttrFinishing, "Definición", "Finishing"
          TranslationKey.AttrLongShots, "Tiro lejano", "Long shots"
          TranslationKey.AttrDribbling, "Regate", "Dribbling"
          TranslationKey.AttrBallControl, "Control", "Ball control"
          TranslationKey.AttrPassing, "Pase", "Passing"
          TranslationKey.AttrCrossing, "Centros", "Crossing"
          TranslationKey.AttrTackling, "Entrada", "Tackling"
          TranslationKey.AttrMarking, "Marca", "Marking"
          TranslationKey.AttrHeading, "Cabezazo", "Heading"
          TranslationKey.AttrFreeKick, "Tiro libre", "Free kick"
          TranslationKey.AttrPenalty, "Penal", "Penalty"
          TranslationKey.AttrReflexes, "Reflejos", "Reflexes"
          TranslationKey.AttrHandling, "Blocaje", "Handling"
          TranslationKey.AttrKicking, "Saque", "Kicking"
          TranslationKey.AttrOneOnOne, "Mano a mano", "One on one"
          TranslationKey.AttrAerialReach, "Juego aéreo", "Aerial reach"
          TranslationKey.SquadTitle, "Plantel", "Squad"
          TranslationKey.SquadSummary, "{count} jugadores · valor {value} · masa salarial {wages} por semana", "{count} players · value {value} · wage bill {wages} per week"
          TranslationKey.SortByLine, "Por línea", "By line"
          TranslationKey.SortBySkill, "Nivel", "Level"
          TranslationKey.SortByAge, "Edad", "Age"
          TranslationKey.SortByValue, "Valor", "Value"
          TranslationKey.SortByName, "Nombre", "Name"
          TranslationKey.LineGoalkeepers, "Arqueros", "Goalkeepers"
          TranslationKey.LineDefenders, "Defensores", "Defenders"
          TranslationKey.LineMidfielders, "Mediocampistas", "Midfielders"
          TranslationKey.LineAttackers, "Delanteros", "Attackers"
          TranslationKey.PlayerAgeMorale, "{age} años · moral {morale}%", "{age} yrs · morale {morale}%"
          TranslationKey.PlayerCondition, "Estado", "Condition"
          TranslationKey.PlayerLevel, "Nivel", "Level"
          TranslationKey.PlayerPotential, "Potencial", "Potential"
          TranslationKey.PlayerMorale, "Moral", "Morale"
          TranslationKey.PlayerMatchFitness, "Ritmo", "Match fitness"
          TranslationKey.PlayerContract, "Contrato", "Contract"
          TranslationKey.PlayerAge, "Edad", "Age"
          TranslationKey.SelectPlayer, "Elegí un jugador", "Select a player"
          TranslationKey.SelectPlayerHint, "Tocá una fila para ver su ficha", "Pick a row to see their profile"
          TranslationKey.StatusInjured, "Lesionado", "Injured"
          TranslationKey.StatusSuspended, "Suspendido {days}", "Suspended {days}"
          TranslationKey.HomeLeagues, "Ligas", "Leagues"
          TranslationKey.HomeCups, "Copas", "Cups"
          TranslationKey.HomeInternational, "Internacional", "International"
          TranslationKey.StatCash, "Caja", "Cash"
          TranslationKey.StatCashSub, "Disponible", "Available"
          TranslationKey.StatPosition, "Posición", "Position"
          TranslationKey.StatPositionShort, "Pos", "Pos"
          TranslationKey.StatForm, "Racha", "Form"
          TranslationKey.StatMorale, "Moral", "Morale"
          TranslationKey.MoraleHigh, "Alta", "High"
          TranslationKey.MoraleOkay, "Bien", "Okay"
          TranslationKey.MoraleLow, "Baja", "Low"
          TranslationKey.CopyNotification, "Copiar notificación", "Copy notification"
          TranslationKey.ClearAll, "Limpiar todo", "Clear all"
          TranslationKey.Notifications, "Notificaciones", "Notifications"
          TranslationKey.NoNotifications, "Sin notificaciones", "No notifications"
          TranslationKey.UpcomingFixtures, "Próximos partidos", "Upcoming fixtures"
          TranslationKey.NoUpcomingFixtures, "No hay partidos programados", "No upcoming fixtures"
          TranslationKey.RecentResults, "Últimos resultados", "Recent results"
          TranslationKey.NoResultsYet, "Todavía no hay resultados", "No results yet"
          TranslationKey.TableClub, "Club", "Club"
          TranslationKey.TableRecord, "G-E-P", "W-D-L"
          TranslationKey.TablePoints, "Pts", "Pts"
          TranslationKey.NoStandings, "Sin tabla disponible", "No standings available"
          TranslationKey.HomeTag, "L", "H"
          TranslationKey.AwayTag, "V", "A"
          TranslationKey.MatchdayBadge, "Día de partido", "Matchday"
          TranslationKey.NextMatchBadge, "Próximo partido · {date}", "Next match · {date}"
          TranslationKey.VenueHome, "Local", "Home"
          TranslationKey.VenueAway, "De visitante", "Away"
          TranslationKey.Versus, "vs", "vs"
          TranslationKey.RivalStudiedTitle, "El rival te estudió", "Your rival has studied you"
          TranslationKey.RivalStudiedBody, "Vio {count} de tus partidos y espera que ataques sobre todo por {corridor}.", "They have watched {count} of your matches and expect you to attack mostly down {corridor}."
          TranslationKey.RivalUnknownTitle, "El rival todavía no te conoce", "Your rival hasn't studied you yet"
          TranslationKey.RivalUnknownBody, "Es el primer cruce: no tiene informes de tu equipo.", "First meeting: they have no reports on your side."
          TranslationKey.CorridorLeft, "la izquierda", "the left"
          TranslationKey.CorridorCenter, "el centro", "the middle"
          TranslationKey.CorridorRight, "la derecha", "the right"
          TranslationKey.StyleShortPassing, "Toque corto", "Short passing"
          TranslationKey.StyleMixedPassing, "Pase variado", "Mixed passing"
          TranslationKey.StyleLongBalls, "Pelotazo", "Long balls"
          TranslationKey.StyleDirect, "Juego directo", "Direct play"
          TranslationKey.StyleCrosses, "Mucho centro", "Lots of crosses"
          TranslationKey.AdjustTactics, "Ajustar la táctica", "Adjust tactics"
          TranslationKey.InboxImportantTitle, "Bandeja — lo importante", "Inbox — what matters"
          TranslationKey.InboxSeeAll, "Ver los {count} mensajes", "See all {count} messages"
          TranslationKey.InboxRoutineHint, "Los otros {count} son de rutina: el juego los resume y no hace falta abrirlos.", "The other {count} are routine: the game sums them up, no need to open them."
          TranslationKey.InboxAllClear, "Nada urgente: la bandeja está al día.", "Nothing urgent: your inbox is up to date."
          TranslationKey.InboxOpen, "Abrir", "Open"
          TranslationKey.InboxLater, "Después", "Later"
          TranslationKey.CategoryDevelopment, "Desarrollo", "Development"
          TranslationKey.CategoryTransfer, "Mercado", "Transfers"
          TranslationKey.CategoryBoard, "Directiva", "Board"
          TranslationKey.CategoryMatch, "Partido", "Match"
          TranslationKey.CategoryInjury, "Lesión", "Injury"
          TranslationKey.CategoryContract, "Contrato", "Contract"
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
