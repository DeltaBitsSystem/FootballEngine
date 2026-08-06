# FootballEngine — Plan: Feature Complete Match Engine & Viewer

> **Objetivo:** Superar a Football Manager en simulación de partido.
> **Principio:** Soluciones estructurales, no parches. El compilador nos guía.
> **Filosofía:** Cada sistema tiene su dominio bien definido, tipos que hacen estados imposibles de representar incorrectamente, y tests que verifican comportamiento real de fútbol.

---

## Fase 0: Cimientos — Tipos de Dominio y Reglas (Semana 1-2)

### 0.1 Refactorizar `MatchEventType` y `MatchEvent`
**Problema actual:** `MatchEvent` tiene `SubTick * PlayerId * ClubId * Type` — no captura contexto (posición, calidad, resultado secundario). `MatchEventType` mezcla eventos atómicos con compuestos.

**Solución:**
```fsharp
// Nuevo sistema de eventos estructurado
type EventContext = {
    Position: Spatial
    Quality: float option    // 0-1, qué tan buena fue la acción
    ExpectedGoal: float option
    AssistId: PlayerId option
    SecondaryResult: EventSecondary option
}

type MatchEvent = {
    SubTick: int
    ActorId: PlayerId
    ClubId: ClubId
    Type: MatchEventType
    Context: EventContext
}
```

**Archivos:** `Domain/Match.fs`, `Engine/Match/Core/SimTypes.fs`

### 0.2 Sistema de Tiempo Real — Stoppage Time
**Problema actual:** El partido corre fijo 95 minutos sin tiempo añadido.

**Solución:**
- `StoppageTimeTracker`: acumula tiempo perdido por:
  - Goles (+30s)
  - Tarjetas (+15s)
  - Lesiones (+30s-60s según severidad)
  - Sustituciones (+15s)
  - Set pieces (+5s cada uno)
  - VAR reviews (dinámico)
- Al llegar a 45:00 / 90:00, el árbitro decide cuánto added time
- El partido NO termina hasta que se agote el added time

**Archivos nuevos:** `Engine/Match/Referee/StoppageTimeTracker.fs`
**Archivos modificados:** `Engine/Match/Core/SimulationClock.fs`, `Engine/Match/Simulator.fs`

### 0.3 Foul Contextual — DOGHO, Tactical Foul, Professional Foul
**Problema actual:** Las tarjetas solo dependen de `Aggression`. No detecta situaciones reales.

**Solución:**
```fsharp
type FoulSeverity =
    | Trivial          // sin tarjeta
    | TacticalFoul     // amarilla (rompe ataque prometedor)
    | ProfessionalFoul // amarilla (falta táctica profesional)
    | DOGHO            // roja directa (niega oportunidad clara de gol)
    | SeriousFoulPlay  // roja (entrada violenta)
    | ViolentConduct   // roja (violencia, incluso sin balón)

type FoulContext = {
    Severity: FoulSeverity
    Zone: PitchZone
    DistanceToGoal: float<meter>
    AttackersBetweenFoulAndGoal: int
    BallDirection: AttackDir
    Reckless: bool      // entrada temeraria
    ExcessiveForce: bool // fuerza excesiva
    Intent: FoulIntent  // accidental / tactical / aggressive
}
```

**Archivos:** `Engine/Match/Referee/FoulAnalysis.fs`, `Engine/Match/Referee/RefereeAgent.fs`

### 0.4 Handball Detection
**Solución:**
- Cuando el balón contacta un jugador no-GK fuera de un contexto natural de juego
- Probabilidad basada en: posición del brazo (random factor), distancia al disparo, movimiento deliberado
- Penal si ocurre dentro del área

**Archivos nuevos:** `Engine/Match/Referee/HandballDetector.fs`

---

## Fase 1: VAR — Video Assistant Referee (Semana 3-4)

### 1.1 Tipos de Incidentes Revisables
```fsharp
type VARReviewableIncident =
    | GoalCheck of scoringClub: ClubSide * scorerId: PlayerId * isOwnGoal: bool
    | PenaltyCheck of awardedTo: ClubSide * foulPos: Spatial
    | RedCardCheck of playerId: PlayerId * incident: FoulSeverity
    | OffsideCheck of attackingPlayer: PlayerId * passSubTick: int

type VARDecision =
    | NoIncident       // no se requiere revisión
    | CheckComplete    // se revisó, decisión original se mantiene
    | Overturn         // se cambia la decisión
    | OnFieldReview    // el árbitro va a revisar en el monitor

type VARState = {
    CurrentReview: VARReviewableIncident option
    ReviewHistory: (VARReviewableIncident * VARDecision * int) list
    IsChecking: bool
    CheckStartTime: int
}
```

### 1.2 Flujo VAR
1. **Detect:** Incidente ocurre → `VARDetector` evalúa si es revisable
2. **Check:** VAR revisa en background (simulado, 10-30 subticks de "delay")
3. **Decision:** Si hay error claro → `OnFieldReview` o `Overturn`
4. **Apply:** `VARApplicator` modifica el estado (anula gol, concede/revoca penal, etc.)

### 1.3 Probabilidad de detección VAR
- Basada en: claridad del error, ángulo de cámara (simulado), importancia del partido
- Goles: siempre se revisan (offside, foul en la jugada, handball)
- Penales: siempre se revisan
- Rojas directas: siempre se revisan

**Archivos nuevos:**
- `Engine/Match/Referee/VAR/VARDetector.fs`
- `Engine/Match/Referee/VAR/VARReview.fs`
- `Engine/Match/Referee/VAR/VARApplicator.fs`
- `Engine/Match/Referee/VAR/VARTypes.fs`

**Archivos modificados:**
- `Engine/Match/Referee/RefereeAgent.fs` (integra VAR en el loop)
- `Engine/Match/Simulator.fs` (maneja transiciones de VAR)
- `Domain/Match.fs` (agrega eventos de VAR)

---

## Fase 2: Advantage Play — Ley de Ventaja (Semana 5)

### 2.1 Sistema de Advantage
**Problema actual:** Toda falta para el juego inmediatamente.

**Solución:**
```fsharp
type AdvantageState =
    | NoAdvantage
    | AdvantagePlaying of foulSubTick: int * fouledTeam: ClubSide * reason: string
    | AdvantageCalledBack of foulSubTick: int // se cobró la falta después

type AdvantageDecision =
    | PlayOn of reason: string    // ventaja aplicada
    | StopPlay of reason: string  // se cobra la falta
```

**Lógica:**
- Cuando ocurre falta, `AdvantageEngine` evalúa:
  - ¿El equipo fouled tiene posesión inmediata?
  - ¿Está en posición de ataque prometedor?
  - ¿Hay espacio para explotar?
  - ¿Qué zona del campo? (en área defensiva → menos probable advantage)
- Si `PlayOn`: el juego continúa, se registra la falta pero no se cobra
- Si dentro de 3-5 segundos el equipo fouled pierde la ventaja → se cobra la falta original
- El árbitro grita "Play on!" (evento de commentary)

**Archivos nuevos:** `Engine/Match/Referee/AdvantageEngine.fs`
**Archivos modificados:** `Engine/Match/Referee/RefereeAgent.fs`, `Engine/Match/Actions/DuelAction.fs`

---

## Fase 3: Set Pieces — Rutas y Rutinas (Semana 6-8)

### 3.1 Set Piece Taker Selection
**Problema actual:** Se elige por posición (ML/MR para corners), no por atributos.

**Solución:**
- Score de taker basado en: `FreeKick`, `Crossing`, `Passing` según tipo de set piece
- El entrenador elige los mejores ejecutores

### 3.2 Defensive Wall
**Problema actual:** No existe pared en tiros libres.

**Solución:**
- Para free kicks dentro de distancia de tiro (~30m del arco):
  - 1-4 jugadores forman pared
  - Posición: entre balón y poste más cercano
  - Saltan con probabilidad (basada en Bravery/Jumping)
  - El GK posiciona al resto del equipo (offside trap, marcaje)

### 3.3 Corner Routines
**Problema actual:** Cross al jugador más fuerte/cabezador. Sin variación.

**Solución:**
```fsharp
type CornerRoutine =
    | NearPost          // al primer palo
    | FarPost           // al segundo palo
    | EdgeOfBox         // afuera del área para remate de segunda
    | ShortCorner       // pase corto a compañero cercano
    | FlickOn           // jugador del primer palo cabecea al segundo
    | NearPostFlickOn   // variante del near post

type CornerDefense =
    | ZonalMarking of zones: int    // 2-4 zonas
    | ManMarking                     // cada defensor marca un atacante
    | Mixed of zonalCount: int      // combinación
```

**Rutinas de ataque coordinadas:**
- Bloqueo (screening) de defensores
- Movimientos de distracción
- Jugadores que se quedan afuera del área para rechaces

### 3.4 Free Kick Routines
```fsharp
type FreeKickRoutine =
    | DirectShot        // tiro al arco
    | LayOff            // pase corto a compañero que cruza/tira
    | OverTheWall       // sobre la pared al segundo palo
    | NearPostRun       // jugador corre al primer palo
    | TrickPlay         // jugada ensayada (pasar atrás, etc.)
```

### 3.5 Throw-In Routines
```fsharp
type ThrowInRoutine =
    | ShortThrow        // pase corto al más cercano
    | LongThrow         // lanzamiento largo al área (tipo Rory Delap)
    | QuickThrow        // rápido si hay espacio
```

### 3.6 Second Phase Play
**Problema actual:** Los rechaces de corners se convierten en loose balls sin estructura.

**Solución:**
- Después de un corner rechazado:
  - Equipo atacante mantiene forma de presión
  - Jugadores afuera del área posicionados para rechaces
  - Equipo defensivo intenta clear o counter

### 3.7 Penalty Setup
**Problema actual:** No hay posicionamiento del GK ni del kicker antes del tiro.

**Solución:**
- GK elige lado basado en: análisis del kicker (histórico), atributos, random
- Kicker elige lado basado en: atributos, composure, histórico del GK
- Resto de jugadores fuera del área (regla real)

**Archivos nuevos:**
- `Engine/Match/Actions/SetPieceRoutines.fs`
- `Engine/Match/Actions/WallBehavior.fs`
- `Engine/Match/Actions/CornerRoutines.fs`
- `Engine/Match/Actions/SecondPhase.fs`

**Archivos modificados:**
- `Engine/Match/Actions/SetPlayAction.fs`
- `Engine/Match/Actions/SetPieceAgent.fs`

---

## Fase 4: Action Types — Variedad de Acciones (Semana 9-10)

### 4.1 Pass Types
```fsharp
type PassType =
    | GroundPass        // pase raso
    | LoftedPass       // pase elevado
    | ThroughBall       // pase al espacio entre líneas
    | Chip              // pase por encima de un defensor
    | DrivenPass        // pase fuerte y directo
    | CutBack           // pase desde la línea de fondo hacia atrás
    | LayOff            // pase de primera hacia atrás
    | OneTwo            // pared (pase y corrida)
```

### 4.2 Shot Types
```fsharp
type ShotType =
    | DrivenShot        // tiro fuerte y directo
    | PlacedShot        // tiro colocado
    | Chip              // globo al GK
    | Volley            // de volea
    | HalfVolley        // media volea
    | Header            // de cabeza
    | FirstTimeShot     // de primera
    | Curler            // tiro con efecto
```

### 4.3 Tackle Types
```fsharp
type TackleType =
    | StandingTackle    // tackle de pie
    | SlideTackle       // entrada de pie deslizante
    | Block             // bloqueo de tiro/pase
    | Jockey            // contención sin entrar
```

### 4.4 Dribble Types
```fsharp
type DribbleType =
    | SpeedDribble      // correr con espacio
    | SkillDribble      // regate en espacio reducido
    | ShieldDribble     // proteger el balón
    | KnockAndRun       // mandar la pelota y correr
```

**Archivos modificados:** `Engine/Match/Actions/PassAction.fs`, `Engine/Match/Actions/ShotAction.fs`, `Engine/Match/Actions/DuelAction.fs`
**Archivos nuevos:** Según necesidad de separar lógica

---

## Fase 5: Player Personality & Decision Making (Semana 11-12)

### 5.1 Player Personality
**Problema actual:** Las decisiones solo dependen de atributos numéricos.

**Solución:**
```fsharp
type PlayerPersonality = {
    Flair: float              // 0-1, probabilidad de intentar algo creativo
    Consistency: float        // 0-1, qué tan predecible es
    ImportantMatches: float   // 0-1, rendimiento en partidos importantes
    Controversy: float        // 0-1, probabilidad de comportamiento controversial
    Leadership: float         // 0-1, influencia en compañeros
    Teamwork: float           // 0-1, qué tan bien juega en equipo
    Ambition: float           // 0-1, motivación personal
    Pressure: float           // 0-1, cómo maneja la presión
    Sportsmanship: float      // 0-1, fair play vs juego sucio
    Temperament: float        // 0-1, control emocional
}
```

Derivado de atributos mentales pero con variación individual.

### 5.2 Team Chemistry & Familiarity
**Problema actual:** Chemistry existe pero solo afecta precisión de pases.

**Solución:**
- **Familiarity:** pares de jugadores que juegan juntos frecuentemente
  - Mejora pases entre ellos
  - Mejora movimientos coordinados (one-twos, overlapping runs)
  - Se construye con el tiempo (acumulativo)
- **Team Cohesion:** nivel grupal
  - Afecta pressing coordinado
  - Afecta offside trap
  - Afecta transiciones

### 5.3 Opponent Modeling
**Solución:**
- Los jugadores adaptan decisiones basándose en:
  - ¿El defensor es lento? → intentar velocidad
  - ¿El defensor es agresivo? → usar su momentum
  - ¿El GK es débil en un lado? → tirar al otro
  - ¿El equipo rival presiona alto? → pases largos

**Archivos nuevos:**
- `Engine/Match/Player/PlayerPersonality.fs`
- `Engine/Match/Player/ChemistryTracker.fs`
- `Engine/Match/Player/OpponentModel.fs`

---

## Fase 6: Match Viewer — Feature Complete (Semana 13-15)

### 6.1 Event Timeline
- Barra horizontal con todos los eventos del partido
- Iconos por tipo de evento (gol, tarjeta, sustitución, etc.)
- Click en evento → salta a ese momento del replay
- Zoom en la timeline

### 6.2 Match Statistics Panel
- Panel lateral con estadísticas en vivo:
  - Posesión (%)
  - Tiros (total, on target, off target, blocked)
  - xG acumulado
  - Pases (completados, intentados, %)
  - Corners
  - Faltas
  - Tarjetas
  - Distancia recorrida por jugador
  - Sprint count

### 6.3 Heat Maps
- Mapa de calor por jugador (dónde estuvo en el campo)
- Mapa de calor por equipo
- Toggle entre jugadores

### 6.4 Passing Network
- Visualización de red de pases entre jugadores
- Grosor de línea = cantidad de pases
- Tamaño de nodo = toques del jugador

### 6.5 Zoom & Pan
- Zoom in/out con scroll del mouse
- Pan con drag
- Botón "follow ball" para cámara automática

### 6.6 Commentary Overlay
- Texto de commentary que aparece sobre el pitch
- Basado en eventos: "¡Gran atajada!", "Peligro!", "Golazo!"
- Contextual: nombre del jugador, tipo de acción

### 6.7 Player Stats Overlay
- Click en jugador → muestra stats del partido:
  - Distancia recorrida
  - Pases completados
  - Dribbles exitosos
  - Duelos ganados
  - xG, xA

### 6.8 2.5D Perspective (opcional)
- Vista con perspectiva en lugar de top-down puro
- Pitch con inclinación

**Archivos modificados:** `Views/Components/MatchViewer.fs`, `Views/Components/MatchProjection.fs`
**Archivos nuevos:**
- `Views/Components/EventTimeline.fs`
- `Views/Components/StatsPanel.fs`
- `Views/Components/HeatMap.fs`
- `Views/Components/PassingNetwork.fs`
- `Views/Components/CommentaryOverlay.fs`
- `Views/Components/PlayerOverlay.fs`

---

## Fase 7: Sistemas Avanzados (Semana 16-18)

### 7.1 Expected Goals (xG) Model
```fsharp
type xGModel = {
    DistanceToGoal: float<meter>
    AngleToGoal: float<degree>
    ShotType: ShotType
    BodyPart: BodyPart
    AssistType: AssistType
    PressureLevel: float
    IsOneOnOne: bool
    IsSetPiece: bool
}
```

### 7.2 Dynamic Morale & Momentum
- Momentum del partido afecta:
  - Confianza de jugadores
  - Agresividad
  - Toma de riesgos
- Goles cambian momentum drásticamente
- Atajadas importantes también

### 7.3 Weather & Pitch Conditions
- Lluvia: balón más rápido en piso, más lento en aire
- Pitch seco: balón más rápido
- Pitch mojado: resbalones más probables
- Afecta decisión de pases largos vs cortos

### 7.4 Crowd Influence
- Home advantage dinámico basado en:
  - Tamaño del estadio
  - Resultado actual (crowd más animado si van ganando)
  - Importancia del partido
  - Afecta: decisiones del árbitro (sutil), moral del home team, presión en away team

### 7.5 Substitution Intelligence
- El manager sugiere sustituciones basándose en:
  - Fatiga
  - Resultado
  - Táctica necesaria
  - Jugadores disponibles en el banco

---

## Arquitectura de Implementación

### Orden recomendado

```
Fase 0 (Cimientos)
  ├── 0.1 MatchEvent refactor
  ├── 0.2 Stoppage Time
  ├── 0.3 Foul Contextual
  └── 0.4 Handball

Fase 1 (VAR)
  ├── 1.1 VAR Types
  ├── 1.2 VAR Detector
  ├── 1.3 VAR Review flow
  └── 1.4 VAR Applicator

Fase 2 (Advantage)
  └── Advantage Engine

Fase 3 (Set Pieces)
  ├── 3.1 Taker Selection
  ├── 3.2 Wall Behavior
  ├── 3.3 Corner Routines
  ├── 3.4 Free Kick Routines
  ├── 3.5 Throw-In Routines
  ├── 3.6 Second Phase
  └── 3.7 Penalty Setup

Fase 4 (Action Types)
  ├── 4.1 Pass Types
  ├── 4.2 Shot Types
  ├── 4.3 Tackle Types
  └── 4.4 Dribble Types

Fase 5 (Personality)
  ├── 5.1 Player Personality
  ├── 5.2 Chemistry & Familiarity
  └── 5.3 Opponent Modeling

Fase 6 (Viewer)
  ├── 6.1 Event Timeline
  ├── 6.2 Stats Panel
  ├── 6.3 Heat Maps
  ├── 6.4 Passing Network
  ├── 6.5 Zoom & Pan
  ├── 6.6 Commentary
  └── 6.7 Player Overlay

Fase 7 (Avanzados)
  ├── 7.1 xG Model
  ├── 7.2 Dynamic Morale
  ├── 7.3 Weather
  ├── 7.4 Crowd
  └── 7.5 Sub Intelligence
```

### Dependencias entre fases

```
Fase 0 → Fase 1 (VAR necesita foul context y eventos ricos)
Fase 0 → Fase 2 (Advantage necesita foul context)
Fase 0 → Fase 3 (Set pieces necesitan eventos ricos)
Fase 1,2,3 → Fase 4 (Action types se integran con todo)
Fase 4 → Fase 5 (Personality afecta qué actions elige)
Fase 0-5 → Fase 6 (Viewer consume todos los eventos y datos)
Fase 6 → Fase 7 (Avanzados se integran con viewer)
```

---

## Tests Requeridos

Cada fase debe incluir tests:

### Tests de Reglas
- Offside: 10+ scenarios (own half, level, second-last defender, etc.)
- VAR: goal review, penalty review, red card review
- Advantage: play on, call back, no advantage scenarios
- Foul: DOGHO, tactical, professional, serious foul play
- Handball: deliberate, natural position, penalty area

### Tests de Set Pieces
- Corner: near post, far post, short, flick-on
- Free kick: direct, layoff, over wall, trick play
- Wall: correct number, correct position, jump behavior
- Penalty: GK dive, kicker placement, rebound

### Tests de Determinismo
- Cada simulación debe ser determinística con la misma seed
- Tests de regresión para cada cambio

---

## Por qué esto supera a Football Manager

| Aspecto | FM | FootballEngine (plan) |
|---------|-----|----------------------|
| Set pieces | Scripts predefinidos | Rutinas dinámicas con IA |
| VAR | No existe | Sistema completo con 4 tipos de review |
| Advantage | No existe | Ley de ventaja contextual |
| Foul context | Básico | DOGHO, tactical, professional, severity |
| Handball | No existe | Detección contextual |
| Pass types | Implícito | 8 tipos explícitos |
| Shot types | Implícito | 8 tipos explícitos |
| Player personality | Hidden attrs | Sistema explícito de 10 traits |
| Chemistry | Binario | Familiarity acumulativa + team cohesion |
| Opponent modeling | No existe | Adaptación en tiempo real |
| xG | Post-match | Modelo en tiempo real |
| Weather | No existe | Condiciones dinámicas |
| Crowd | Home bonus fijo | Influencia dinámica |
| Viewer | 2D básico | Timeline, heat maps, passing network, zoom |

---

## Notas de Implementación

1. **Cada fase es mergeable independientemente** — no bloquear una fase con otra
2. **El compilador guía** — cambiar firmas está bien, el type system nos dice qué romper
3. **Tests primero** para lógica compleja (VAR, offside, fouls)
4. **Performance**: mantener SoA donde importa (40Hz), pero no optimizar prematuramente en sistemas cognitivos (1-2Hz)
5. **Determinismo**: todo debe ser determinístico con la misma seed — crítico para replays y debugging
