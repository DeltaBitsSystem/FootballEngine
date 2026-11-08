# World Pulse — un mundo que se mueve solo

Objetivo: que la carrera se sienta un mundo vivo y divertido donde **todos los días pasa algo**, sin
romper los principios del proyecto: todo es determinista, todo tiene una causa en el estado, todo lo
que pasa emite algo visible.

## Diagnóstico (estado actual)

- El bucle diario (`WorldScheduler.executeDayWithBalance`) ya tiene fases: ExtractDueWork →
  ApplyEffectiveRecords → PrepareDay (entrenamiento semanal) → ResolveFixtures → ConfirmResults →
  SettleObligations (finanzas) → ResolveDecisions (agentes: mercado IA, finanzas, juveniles) → CloseDay.
- Lo único que el mundo "cuenta" hoy: resumen semanal de entrenamiento, lesiones, inicio de temporada y
  campeones (`Engine/InboxMessages.fs`, `Domain/WorldNews.fs`). Todo va a la Bandeja del usuario.
- Los clubes IA nunca despiden técnicos, ningún jugador pide irse, no hay rumores, premios, hinchas ni
  presión. Las rutas `News`, `Board`, `Youth`, `Competitions` y `Staff` del menú no tienen página.
- Base aprovechable: `ScoutingMemory` (el rival te estudia), `BoardObjective` + `Career.reviewObjective`,
  `PlayerExperiences` (goles, asistencias, historial contra rivales), `fixture.Events` con goleadores,
  `SeasonHistory`, staff desempleado en `GameState.Staff`. El guardado serializa `GameState` entero, así
  que campos nuevos persisten sin tocar SQLite.

## Principios de diseño

1. **Las historias nacen del estado, no de dados.** Cada noticia tiene un *detector* que lee hechos
   confirmados (resultados, rachas, tabla, contratos, edad) y produce un *suceso* con causa trazable.
   El azar solo decide matices (qué rumor sale primero, el tono), nunca si algo "pasa porque sí".
2. **Determinismo.** Toda aleatoriedad usa una semilla derivada de `ExecutionSeed`, la fecha, el id del
   detector y la entidad (mismo mecanismo que los juveniles en `ClubAgent.generateOne`). Misma partida,
   mismo mundo.
3. **Los sucesos tienen consecuencias.** Un despido cambia el técnico y su táctica; un jugador infeliz
   pierde moral y puede pedir irse; el humor de la hinchada mueve la recaudación y la paciencia de la
   directiva. Una noticia sin efecto solo existe si informa algo real (un récord, un premio).
4. **Core es estructurado, Client traduce.** Core guarda noticias como datos tipados (`NewsKind` con
   ids y números), nunca texto. El cliente las redacta en español/inglés con `Localization`.
5. **Volumen con presupuesto.** Cada día se eligen las N noticias de mayor importancia por alcance
   (mi club / mi liga / mundo), para que siempre haya algo sin inundar. La Bandeja recibe solo lo que el
   usuario puede o debe decidir.
6. **Números en configuración.** Umbrales, pesos y topes viven en un `WorldPulseConfig` validado (no
   literales sueltos), con default y validación, siguiendo la regla de BalanceConfig.

## Modelo (Core)

`Domain/News.fs` (nuevo):

```fsharp
type NewsScope = UserClub | UserLeague | World
type NewsImportance = Minor | Notable | Major | Headline

type NewsKind =
    // Partidos
    | BigWin of winner: ClubId * loser: ClubId * goalsFor: int * goalsAgainst: int
    | Upset of underdog: ClubId * favourite: ClubId * score: int * int
    | Streak of club: ClubId * kind: StreakKind * length: int          // ganados, invicto, sin ganar, perdidos
    | NewLeader of club: ClubId * competition: CompetitionId
    | Clasico of home: ClubId * away: ClubId                            // previa de partido grande
    | HatTrick of player: PlayerId * club: ClubId * goals: int
    | ScoringRun of player: PlayerId * matches: int
    // Presión
    | BoardUnrest of club: ClubId * confidence: int
    | ManagerSacked of club: ClubId * manager: StaffId * matchesInCharge: int
    | ManagerAppointed of club: ClubId * manager: StaffId
    | FansMood of club: ClubId * mood: FansMoodBand
    // Jugadores
    | TransferRequest of player: PlayerId * club: ClubId * reason: UnrestReason
    | ContractRunningOut of player: PlayerId * club: ClubId * monthsLeft: int
    | Milestone of player: PlayerId * kind: MilestoneKind * value: int  // 100 partidos, 50 goles, debut
    | Breakout of player: PlayerId * club: ClubId                       // juvenil que explota
    | Retirement of player: PlayerId * age: int
    // Mercado
    | Rumour of player: PlayerId * from: ClubId * interested: ClubId
    | TransferDone of player: PlayerId * from: ClubId option * toClub: ClubId * fee: decimal
    // Temporada
    | Award of kind: AwardKind * player: PlayerId option * staff: StaffId option * competition: CompetitionId
    | Promotion of club: ClubId * competition: CompetitionId
    | Relegation of club: ClubId * competition: CompetitionId
    | Champion of club: ClubId * competition: CompetitionId

type NewsItem =
    { Id: int; Date: DateTime; Kind: NewsKind; Importance: NewsImportance; Scope: NewsScope
      Clubs: ClubId list; Players: PlayerId list }
```

Estado nuevo:

- `GameState.News: NewsItem list` (acotada, p. ej. 500 más recientes) y `NextNewsId`.
- `Club.BoardConfidence: int` (0–100) y `Club.FansMood: int` (0–100), con historial corto de forma
  (`RecentResults: MatchOutcome list`, últimos 10) para rachas.
- `Club.ManagerSince: DateTime` para "partidos a cargo" y para no despedir a alguien recién llegado.
- `Player.Unrest: UnrestReason option` (`LackOfMinutes | ContractStalled | BiggerClubInterest`).

## Sistemas (cada uno: detector puro + efecto + noticias)

### 1. Pulso de partidos (cada día con partidos)
Lee los fixtures confirmados del día y su lista de eventos.
- Goleada (≥ N goles de diferencia), batacazo (diferencia de reputación ≥ umbral y gana el menor),
  rachas al cruzar 3/5/10, nuevo puntero, hat-tricks, goleador en racha (marca en k partidos seguidos).
- Previa de clásico: 2 días antes de un partido entre los dos clubes de mayor reputación de una liga, o
  entre clubes con historial reciente parejo (`RivalRecord`).
- Importancia: `Headline` si involucra al club del usuario o a los 3 primeros; si no, `Notable`/`Minor`.

### 2. Presión: directiva e hinchada (después de cada partido)
- Puntos esperados por partido = f(diferencia de reputación, local/visitante). Confianza y humor
  suben/bajan por (puntos obtenidos − esperados), con amortiguación y piso/techo.
- Hinchada: además reacciona a ventas de figuras y fichajes importantes. Humor bajo reduce la
  recaudación de local (multiplicador en `FinancePhase`); humor alto la sube.
- Clubes IA: confianza bajo umbral **y** ≥ M partidos a cargo → **despido**. El reemplazo sale del
  staff desempleado (mejor por reputación/habilidad que acepte), cambia `Lineup.Formation` al preferido
  del nuevo técnico, `ManagerSince = hoy`, confianza vuelve a un valor de "luna de miel".
- Usuario: avisos escalonados en la Bandeja (`Notable` al bajar de 40, ultimátum bajo 25). El despido
  del usuario sigue el flujo existente de `Career.dismiss`, ahora también a mitad de temporada.

### 3. Vida de los jugadores (semanal)
- Malestar: titular habitual que no juega k semanas, contrato a < 6 meses sin renovación, o club más
  grande interesado (rumor activo). Malestar baja moral semanalmente; si persiste → `TransferRequest`
  (Bandeja si es tu jugador; el mercado IA lo trata como transferible).
- Hitos: debut, 50/100/200 partidos, 25/50/100 goles (desde `PlayerExperiences`).
- Explosión juvenil: sub-21 cuyo nivel sube ≥ X en la temporada → `Breakout`.
- Retiros a fin de temporada: edad ≥ umbral y nivel en caída → se retira (sale del club) con noticia.

### 4. Mercado con ruido (diario)
- Cuando un agente IA elige un objetivo (`ManagerIntent.InitiateTransfer`), con cierta probabilidad
  determinista se publica antes un `Rumour` (y puede causar malestar en el jugador).
- Toda transferencia IA completada sobre un umbral de fee → `TransferDone`.

### 5. Ceremonia de temporada (al cerrar la temporada)
- Campeones, ascensos y descensos como titulares (reemplaza `WorldNews.deriveSeasonMessages`).
- Premios por liga: goleador, mejor jugador (por goles+asistencias+nivel), mejor joven, técnico del año
  (mayor diferencia entre posición esperada y real).

### 6. Presupuesto de noticias
- `NewsDesk.publish`: ordena por importancia y alcance, guarda hasta K por día y por alcance, descarta
  duplicados (misma clase + misma entidad dentro de una ventana de días).
- Solo pasan a la Bandeja los tipos accionables del club del usuario (pedido de transferencia, ultimátum
  de la directiva, rumor sobre tu jugador, contrato por vencer).

## Integración en el bucle diario

Una fase nueva `WorldPulse` dentro de `ResolveDecisions`, **después** de los agentes (así ve las
transferencias del día) y antes de `CloseDay`:

```
runAgents → WorldPulse.run (detectores diarios; semanales si Day % 7 = 0) → NewsDesk.publish
```

El cierre de temporada (`SeasonPhase`/`WorldRunner.advanceToNextSeason`) llama a la ceremonia.

## UI (Client)

- **Noticias** (ruta `News` ya existente): feed con filtros *Mi club / Mi liga / Mundo*, titulares con
  escudos y fotos-shirt, agrupado por día.
- **Inicio**: bloque "Lo que pasa en el mundo" con los 3 titulares del día, debajo de la Bandeja.
- **Directiva** (ruta `Board`): medidor de confianza, humor de la hinchada, objetivo y su evolución.
- Redacción: un `NewsText.render : NewsItem -> string * string` (título, cuerpo) por idioma, con 2–3
  variantes de frase por tipo elegidas por `Id` (determinista) para que no suene repetido.

## Fases de implementación

Cada fase compila, pasa la suite completa y agrega sus tests. Se entrega por commits revisables.

| Fase | Contenido | Tests de aceptación |
|---|---|---|
| 1. Base | `News.fs`, campos nuevos en `GameState`/`Club`/`Player`, `WorldPulseConfig` + validación, semilla determinista, `NewsDesk.publish`, fase `WorldPulse` vacía enganchada | misma semilla → mismas noticias; presupuesto diario respetado; deduplicación; guardado/carga conserva `News` |
| 2. Partidos | detectores de goleada, batacazo, rachas, puntero, hat-trick, racha goleadora, previa de clásico | escenarios construidos: 3 victorias seguidas → `Streak`; gana el chico por reputación → `Upset`; sin partidos → ninguna noticia de partido |
| 3. Presión | confianza y humor por partido, efecto en recaudación, despidos IA con reemplazo, avisos al usuario | racha de derrotas baja confianza y despide tras M partidos; no despide antes de M; ganar sube; el reemplazo viene del staff desempleado y cambia la formación; recaudación sigue al humor |
| 4. Jugadores y mercado | malestar, pedidos de transferencia, contratos por vencer, hitos, explosiones juveniles, rumores, transferencias IA | suplente k semanas → malestar → pedido; hito exacto al cruzar 100 partidos; rumor antes de la compra IA; retiro solo con edad y caída |
| 5. Temporada | campeones, ascensos/descensos, premios | premios coherentes con las estadísticas de la temporada; un solo premio por tipo y liga |
| 6. UI | página Noticias, bloque en Inicio, página Directiva, textos ES/EN | capturas reales con `--shots`; todas las `NewsKind` tienen texto en ambos idiomas (test) |

Medición extra: un corpus de una temporada completa (`--match-corpus` o test de temporada) que reporte
noticias por día por alcance, despidos por temporada y pedidos de transferencia, para calibrar que
"siempre pase algo" sin saturar (objetivo inicial: 3–8 noticias de liga por fecha, 1–3 despidos por
temporada en la liga, al menos un suceso del club del usuario por semana).
