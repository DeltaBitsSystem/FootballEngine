# Match Balance Plan — Arquitectura Correcta

## Diagnóstico Confirmado

Los 7 bugs del agente son reales. Este plan los corrige **y** agrega la arquitectura
que evita que vuelvan a aparecer. El orden importa: primero correcciones quirúrgicas,
después estructura durable.

No hay backward compatibility. Todo se reescribe limpio.

---

## El Problema Central (que el plan del agente no resuelve)

El agente propuso un AST para serializar fórmulas. Eso no resuelve la fragilidad —
la mueve de lugar. El problema real es que **no hay ninguna representación del
estado del partido que conecte las decisiones individuales con el contexto global**.

Un jugador decide tirar sin saber:
- Que el partido va 0-0 al minuto 80
- Que su equipo lleva 10 minutos sin pasar la mitad de cancha
- Que el rival acaba de perder la pelota en su propio campo
- Que el colectivo está en modo `HighPress`

La solución no es un AST. Es un **`MatchSituation`** — una estructura liviana,
computada una vez por tick o evento, que agrega el estado del partido en
modificadores normalizados que los scorers individuales consumen directamente.

Hay además un segundo problema sistémico: **59 ocurrencias de
`BalanceConfig.defaultConfig` hardcodeado** en el motor. Entrenar pesos es
inútil si 53 módulos los ignoran. Este plan los resuelve todos con una sola
estrategia de propagación consistente.

---

## Principios del Diseño

**P1: `state.Config` es la única fuente de verdad en runtime.**
`defaultConfig` solo existe como valor inicial en `SimState` y como fallback
en `WeightsLoader`. Ningún módulo lo llama directamente.

**P2: El config se propaga por el canal que ya existe, sin romper firmas.**
Cada módulo ya recibe `SimState`, `MatchContext`, o `AgentContext`. El config
viaja por ahí — sin parámetros nuevos en la mayoría de los casos.

**P3: Un solo flujo de datos para decisiones individuales.**
```
SimState → MatchSituation (1x por tick por equipo)
         ↓
AgentContext + MatchSituation → PlayerScorer → ActionScores (raw)
                              → × MatchSituation.Modifiers → ActionScores (final)
                              → PlayerDecision → OnBallIntent
```

**P4: Observabilidad obligatoria para debug.**
`DecisionTrace` explica cada decisión. Cuando el partido termina 0-0 sabés
exactamente qué score tuvo cada acción y por qué fue bloqueada.

**P5: Normalización correcta es un invariante verificado.**
Cada función de scoring garantiza output ∈ [0, 1]. Verificado en property tests.

---

## Auditoría Completa: Los 4 Patrones de `defaultConfig`

La auditoría reveló que los 59 usos caen en 4 patrones con soluciones distintas:

### Patrón A — Módulo ya tiene `SimState` o `MatchContext` (solución trivial)

El módulo ya tiene acceso a `state.Config`. Solo hay que reemplazar
`BalanceConfig.defaultConfig` por `state.Config` o `ctx.Config`.

| Archivo | Ocurrencias | Acceso disponible |
|---------|-------------|-------------------|
| `UtilityActions.fs` | 13 | `state: SimState` en cada función |
| `RefereeAgent.fs` | 3 | `state: SimState` en cada función |
| `ReactiveLoop.fs` | 2 | `state: SimState` en `run` |
| `WinProbability.fs` | 1 | `state: SimState` en `calculate` |
| `Momentum.fs` | 1 | No recibe state — ver Patrón C |
| `DuelAction.fs` | 2 | `ctx: MatchContext` → `ctx.Config` |
| `CrossAction.fs` | 1 | `ctx: MatchContext` → `ctx.Config` |

**Fix:** `let w = BalanceConfig.defaultConfig.X` → `let w = state.Config.X`
o `ctx.Config.X` según corresponda.

### Patrón B — Módulo tiene `AgentContext` (solución via ctx.Weights)

`AgentContext` no tiene `BalanceConfig` directamente pero sí tiene
`Decision: DecisionConfig`. El fix es agregar `Weights: BalanceConfig` a
`AgentContext` y propagarlo desde `PlayerContext.build`.

| Archivo | Ocurrencias | Solución |
|---------|-------------|----------|
| `CollectiveModifiers.fs` | 6 | `ctx.Weights.Collective.Modifiers` |
| `PlayerScorer.fs` | 5 | `ctx.Weights.Individual` (ya en plan original) |
| `PlayerPersonality.fs` | 1 | `ctx.Weights.Personality` |

**Fix en `AgentContext`:**
```fsharp
type AgentContext =
    { // ... campos existentes ...
      Weights: BalanceConfig }  // AGREGAR
```

`AgentContext.build` recibe `state: SimState` → pasar `state.Config` al construir.

### Patrón C — Módulo sin estado de partido (funciones puras que necesitan config explícito)

Módulos que son funciones puras sin acceso a `SimState`. Necesitan recibir
el sub-config relevante como parámetro explícito.

| Archivo | Ocurrencias | Solución |
|---------|-------------|----------|
| `EmergentLoops.fs` | 4 | Pasar `EmergentWeights` como parámetro |
| `ChemistryTracker.fs` | 2 | Pasar `ChemistryWeights` como parámetro |
| `TeamDirector.fs` | 1 | Pasar `TeamDirectorWeights` como parámetro |
| `Momentum.fs` | 1 | Pasar `MomentumWeights` como parámetro |
| `Interception.fs` | 4 | Pasar `InterceptionWeights` como parámetro |
| `EnvironmentSystems.fs` | 4 | Pasar `EnvironmentWeights` como parámetro |

Estos módulos son llamados desde `MatchStepper` u orquestadores que ya tienen
`state.Config`. El caller extrae el sub-config relevante y lo pasa.

**Ejemplo — `EmergentLoops.fs` antes:**
```fsharp
let updateCompactness (shortPassSuccess: float) (current: EmergentState) : EmergentState =
    let w = BalanceConfig.defaultConfig.Collective.Emergent
    ...
```

**Después:**
```fsharp
let updateCompactness (w: EmergentWeights) (shortPassSuccess: float) (current: EmergentState) : EmergentState =
    ...
// Caller (MatchStepper): EmergentLoops.updateCompactness state.Config.Collective.Emergent rate current
```

### Patrón D — Casos especiales

| Archivo | Ocurrencias | Situación | Solución |
|---------|-------------|-----------|----------|
| `Core/SimState.fs` | 1 | `member val Config = BalanceConfig.defaultConfig` | Correcto — es el valor inicial. No cambiar. |
| `ML/WeightsLoader.fs` | 2 | `Ok BalanceConfig.defaultConfig` como fallback | Correcto — es el fallback de deserialización. No cambiar. |
| `PassAction.fs` | 3 | `Chemistry.FamiliarityPassBonus` | `ctx.Config.Collective.Chemistry` |
| `xGCalculator.fs` | 2 | `calculateWithDefaults` | Eliminar `calculateWithDefaults`, pasar config explícito siempre |

`xGCalculator.calculateWithDefaults` es el único que necesita ser eliminado
como función. Se llama desde `PlayerScorer` que ya tendrá `ctx.Weights`.

---

## Fase 1 — Bug Fixes Quirúrgicos

*Objetivo: partido funciona, goles ocurren. Sin cambios de arquitectura.*

### 1.1 — `AgentContext.Weights: BalanceConfig`

**Archivo:** `Player/PlayerContext.fs` y `Player/Decision/` (todos los módulos
que usan Patrón B)

Este es el primero porque los fixes 1.2–1.5 en `PlayerScorer` dependen de él.

```fsharp
// AgentContext — agregar campo
type AgentContext =
    { ...
      Weights: BalanceConfig }

// AgentContext.build — al final
{ ...
  Weights = state.Config }
```

### 1.2 — Eliminar `+ 1.0` de `shootScore` maxPossible

**Archivo:** `Player/Decision/PlayerScorer.fs`

```fsharp
// ANTES
let maxPossible =
    d.ShootFinishingWeight + d.ShootLongShotsWeight + d.ShootComposureWeight
    + d.ShootDistNormWeight + d.ShootPosDirectnessWeight + d.ShootPosDepthWeight
    + d.ShootSTBonus + d.ShootDecisionsWeight + d.ShootBraveryMod
    + d.ShootConcentrationMod + d.ShootXGWeight + d.ShootBraveryPressureMod
    + 1.0  // ← ELIMINAR

// DESPUÉS: sin el + 1.0
```

### 1.3 — Corregir `passScore` maxPossible con negativos

**Archivo:** `Player/Decision/PlayerScorer.fs`

`tempoBias` y `directBias` son negativos en el numerador pero están en el
denominador, lo que infla el score. El `maxPossible` solo debe incluir
contribuciones al máximo teórico positivo.

```fsharp
// DESPUÉS: maxPossible sin tempoBias ni directBias
let maxPossible =
    d.PassPassingWeight + d.PassVisionWeight + d.PassTargetBonus
    + phaseBonusMax + creativityBonusMax + busMod + confidenceMod
    + riskMod + focusMod + influenceSpaceBonusMax
    + d.PassDecisionsWeight + d.PassConcentrationMod
    + d.PassTrajectoryBonus + d.PassAnticipationBonus
```

### 1.4 — Reemplazar `defaultConfig.Individual` por `ctx.Weights.Individual`

**Archivo:** `Player/Decision/PlayerScorer.fs` — 4 ocurrencias
**Archivo:** `Player/Decision/PlayerPersonality.fs` — 1 ocurrencia
**Archivo:** `Player/Decision/CollectiveModifiers.fs` — 6 ocurrencias

```fsharp
// ANTES (en las 4 funciones de PlayerScorer + PersonalityFS)
let iw = BalanceConfig.defaultConfig.Individual

// DESPUÉS
let iw = ctx.Weights.Individual

// CollectiveModifiers — applyModifiers ya tiene ctx: AgentContext
let w = ctx.Weights.Collective.Modifiers
```

Eliminar `PlayerPersonality.deriveWithDefaults` — reemplazar cada llamada por
`PlayerPersonality.derive player ctx.Weights.Personality`.

Eliminar `xGCalculator.calculateWithDefaults` — reemplazar por
`xGCalculator.calculate model ctx.Weights.XG`.

### 1.5 — Bajar Shot Quality Gate

**Archivo:** `BalanceConfig.fs` — el valor que lee `ShotAction`

```fsharp
// En defaultConfig
ShotQualityGate = 0.15f  // era 0.35
```

Tiros desde 20-25m dejan de ser automáticamente off-target.

### 1.6 — Propagación Patrón A (state.Config en módulos con SimState)

**Archivos:** `UtilityActions.fs` (13), `RefereeAgent.fs` (3),
`ReactiveLoop.fs` (2), `WinProbability.fs` (1), `DuelAction.fs` (2),
`CrossAction.fs` (1), `PassAction.fs` (3)

Fix mecánico: `BalanceConfig.defaultConfig.X` → `state.Config.X` o `ctx.Config.X`.

```fsharp
// ANTES — UtilityActions.fs, evaluatePress
let uw = BalanceConfig.defaultConfig.Utility

// DESPUÉS
let uw = state.Config.Utility
```

### 1.7 — Propagación Patrón C (parámetro explícito en funciones puras)

**Archivos:** `EmergentLoops.fs` (4), `ChemistryTracker.fs` (2),
`TeamDirector.fs` (1), `Momentum.fs` (1), `Interception.fs` (4),
`EnvironmentSystems.fs` (4)

Agregar el sub-config como primer parámetro. Los callers en `MatchStepper`
y orquestadores extraen `state.Config.X` y lo pasan.

```fsharp
// EmergentLoops — DESPUÉS
let updateCompactness (w: EmergentWeights) (shortPassSuccess: float) (current: EmergentState) =
    ...

// ChemistryTracker — DESPUÉS
let updateFamiliarity (w: ChemistryWeights) (current: float) (successfulPass: bool) (minutesPlayed: float) =
    ...

// Interception — DESPUÉS
let estimateTimeToBall (iw: InterceptionWeights) (physCfg: PhysicsConfig) (player: Player) ... =
    ...

// EnvironmentSystems — DESPUÉS
let ballSpeedModifier (ew: EnvironmentWeights) (weather: WeatherCondition) =
    ...
```

### 1.8 — Tests de regresión de Fase 1

```fsharp
// Shoot score de un ST élite en el área ∈ [0.5, 1.0]
test "shootScore elite ST in box above 0.5" { ... }

// Pass score normalizado ∈ [0.0, 1.0] para cualquier input
test "passScore always in [0, 1]" { ... }

// Tiro desde 22m pasa quality gate
test "shot from 22m passes quality gate" { ... }

// Ningún módulo usa defaultConfig (grep test en CI)
test "no module calls BalanceConfig.defaultConfig outside SimState and WeightsLoader" { ... }
```

---

## Fase 2 — `MatchSituation`: el puente colectivo→individual

*Objetivo: el estado del partido afecta las decisiones individuales con balón.*

### 2.1 — Tipo `MatchSituation`

**Archivo nuevo:** `Match/Core/MatchSituation.fs`

```fsharp
namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Types

/// Estado situacional del partido, computado una vez por tick por equipo.
/// Todos los modificadores normalizados a rangos razonables — no son [0,1]
/// sino multiplicadores directamente aplicables sobre ActionScores.
[<Struct>]
type MatchSituation =
    { MinuteNorm: float        // 0.0 = min 0, 1.0 = min 90+
      GoalDiff: int            // perspectiva del equipo atacante
      IsDrawLate: bool         // empate después del minuto 70
      IsLosingLate: bool       // perdiendo después del minuto 60
      MomentumNorm: float      // -1..1 → 0..1 (0.5 = neutro)
      PossessionPressure: float // tiempo sin posesión norm 0..1
      TeamInTransition: bool   // acaba de recuperar/perder la pelota
      TeamPressingHigh: bool   // pressing activo

      // Multiplicadores pre-calculados, directamente aplicables
      ShootMod: float          // [0.7, 1.5]
      PassMod: float           // [0.7, 1.5]
      DribbleMod: float        // [0.7, 1.3]
      LongBallMod: float }     // [0.7, 1.4]
```

### 2.2 — `MatchSituationBuilder`

**Archivo nuevo:** `Match/Core/MatchSituationBuilder.fs`

```fsharp
module MatchSituationBuilder =

    let compute (side: ClubSide) (state: SimState) (clock: SimulationClock) : MatchSituation =
        let ms = state.Config.MatchSituation
        let minute = minuteNorm state clock
        let minuteReal = minute * 90.0
        let gd = goalDiffFor side state
        let isDrawLate = gd = 0 && minuteReal > 70.0
        let isLosingLate = gd < 0 && minuteReal > 60.0
        let momNorm = (state.Momentum + 10.0) / 20.0
        let posPress = possessionPressure state clock side
        let bb = (getTeamState state side).Blackboard
        let inTransition = bb.OurPhase = Transition && bb.JustLostBall

        let shootMod =
            let urgency = if isLosingLate then ms.ShootLosingLateMod
                          elif isDrawLate then ms.ShootDrawLateMod
                          else 1.0
            let momentum = 1.0 + (momNorm - 0.5) * ms.ShootMomentumScale
            clampFloat (urgency * momentum) 0.7 1.5

        let passMod =
            clampFloat (if gd > 0 then ms.PassWinningMod
                        elif inTransition then ms.PassTransitionPenalty
                        else 1.0) 0.7 1.5

        let dribbleMod =
            clampFloat (if inTransition then ms.DribbleTransitionMod else 1.0) 0.7 1.3

        let longBallMod =
            clampFloat (if isLosingLate then ms.LongBallLosingLateMod
                        elif posPress > ms.LongBallPressureThreshold then ms.LongBallPressureMod
                        else 1.0) 0.7 1.4

        { MinuteNorm = minute; GoalDiff = gd; IsDrawLate = isDrawLate
          IsLosingLate = isLosingLate; MomentumNorm = momNorm
          PossessionPressure = posPress; TeamInTransition = inTransition
          TeamPressingHigh = bb.OurPhase = Pressing
          ShootMod = shootMod; PassMod = passMod
          DribbleMod = dribbleMod; LongBallMod = longBallMod }
```

### 2.3 — `MatchSituationConfig` en `BalanceConfig`

```fsharp
type MatchSituationConfig =
    { ShootLosingLateMod: float        // default 1.35
      ShootDrawLateMod: float          // default 1.15
      ShootMomentumScale: float        // default 0.30
      PassWinningMod: float            // default 1.20
      PassTransitionPenalty: float     // default 0.80
      DribbleTransitionMod: float      // default 1.20
      LongBallLosingLateMod: float     // default 1.30
      LongBallPressureThreshold: float // default 0.60
      LongBallPressureMod: float }     // default 1.25
```

### 2.4 — Integrar en `AgentContext` y `MatchStepper`

`AgentContext` recibe `MatchSituation: MatchSituation` (ya tiene `Weights`
de Fase 1). `MatchStepper` computa una instancia por equipo por tick y la
pasa al builder de contexto.

### 2.5 — Aplicar en `PlayerScorer.computeAll`

```fsharp
let computeAll (ctx: AgentContext) (matchMemory: MatchMemory) (exp: ExperienceModifiers) : ActionScores =
    let sw = ctx.Weights.Individual.Shoot
    let sit = ctx.MatchSituation
    let clampScore (s: float<decisionScore>) (m: float) =
        LanguagePrimitives.FloatWithMeasure<decisionScore> (clampFloat (float s * m) 0.0 1.0)

    { Shoot    = shootScore ctx matchMemory exp sw |> fun s -> clampScore s sit.ShootMod
      Pass     = passScore ctx matchMemory exp     |> fun s -> clampScore s sit.PassMod
      Dribble  = dribbleScore ctx matchMemory exp  |> fun s -> clampScore s sit.DribbleMod
      Cross    = crossScore ctx matchMemory exp
      LongBall = longBallScore ctx matchMemory exp |> fun s -> clampScore s sit.LongBallMod }
```

---

## Fase 3 — Observabilidad: `DecisionTrace`

*Objetivo: cuando el partido termina 0-0, sabés exactamente por qué.*

### 3.1 — Tipo `DecisionTrace`

**Archivo nuevo:** `Match/Player/Decision/DecisionTrace.fs`

```fsharp
[<Struct>]
type ActionScoreTrace =
    { RawScore: float
      SituationalMod: float
      FinalScore: float
      Blocked: bool
      BlockReason: string }

type DecisionTrace =
    { SubTick: int
      PlayerId: PlayerId
      Shoot: ActionScoreTrace
      Pass: ActionScoreTrace
      Dribble: ActionScoreTrace
      Cross: ActionScoreTrace
      LongBall: ActionScoreTrace
      ChosenAction: string
      SoftmaxTemperature: float
      MatchSituationSnapshot: MatchSituation }
```

### 3.2 — Feature flag en `BalanceConfig`

```fsharp
type DiagnosticsConfig =
    { TraceDecisions: bool
      TracePlayerId: int option  // None = todos
      MaxTracesPerMatch: int }   // default 500
```

### 3.3 — Emisión en `PlayerDecision`

`PlayerDecision.decide` devuelve `OnBallIntent option * DecisionTrace option`.
La trace es `None` cuando el flag está apagado — cero overhead en producción.

---

## Fase 4 — Validación de `BalanceConfig`

*Objetivo: el config se valida al cargar, no en runtime.*

```fsharp
type ValidationError =
    | OutOfRange of field: string * value: float * lo: float * hi: float
    | InvalidRelation of string

let validate (cfg: BalanceConfig) : ValidationError list =
    [ if cfg.Decision.ShootMinThreshold < 0.0 || cfg.Decision.ShootMinThreshold > 1.0 then
          yield OutOfRange("ShootMinThreshold", cfg.Decision.ShootMinThreshold, 0.0, 1.0)
      if cfg.MatchSituation.ShootLosingLateMod < 1.0 then
          yield OutOfRange("ShootLosingLateMod", cfg.MatchSituation.ShootLosingLateMod, 1.0, 2.0)
      if cfg.Decision.PassMinThreshold > cfg.Decision.ShootMinThreshold then
          yield InvalidRelation("PassMinThreshold must be <= ShootMinThreshold")
      // ... ]

let loadAndValidate path : Result<BalanceConfig, ValidationError list> =
    let cfg = deserialize path
    match validate cfg with
    | [] -> Ok cfg
    | errs -> Error errs
```

---

## Orden de Implementación

```
Fase 1: Bug fixes + propagación completa      (~6-8 horas)
  1.1  AgentContext.Weights: BalanceConfig
  1.2  Eliminar +1.0 de shootScore
  1.3  Corregir passScore maxPossible
  1.4  Reemplazar defaultConfig.Individual × 12 (PlayerScorer + Collective + Personality)
  1.5  Bajar ShotQualityGate a 0.15
  1.6  Propagación Patrón A — state.Config en módulos con SimState (22 ocurrencias)
  1.7  Propagación Patrón C — parámetro explícito en funciones puras (15 ocurrencias)
  1.8  Tests de regresión + grep test en CI

Fase 2: MatchSituation                        (~4-5 horas)
  2.1  MatchSituation.fs (tipo)
  2.2  MatchSituationBuilder.fs
  2.3  MatchSituationConfig en BalanceConfig
  2.4  Integrar en AgentContext + MatchStepper
  2.5  Aplicar en PlayerScorer.computeAll
  2.6  Tests

Fase 3: DecisionTrace                         (~2-3 horas)
  3.1  DecisionTrace.fs
  3.2  Feature flag en DiagnosticsConfig
  3.3  Emisión en PlayerDecision

Fase 4: Validación de BalanceConfig           (~2 horas)
  4.1  validate + loadAndValidate
  4.2  Property tests de invariantes
```

**Total estimado:** 14-18 horas de trabajo efectivo.

---

## Métricas de Éxito

| Métrica | Target | Cómo verificar |
|---------|--------|----------------|
| Goles por partido | 2.0–3.5 | Simular 100 partidos |
| Tiros por partido | 10–18 | Contar `ShotAttempt` events |
| Tiros desde 20m+ | > 10% del total | Filter por distancia |
| Pases por partido | 300–500 | Contar `PassAttempt` events |
| Frecuencia 0-0 | < 8% | Contar resultados |
| Shoot score ST élite en área | ∈ [0.55, 0.85] | Test unitario |
| `BalanceConfig.defaultConfig` fuera de `SimState.fs` y `WeightsLoader.fs` | 0 | Grep en CI |
| Todos los ActionScores ∈ [0, 1] | siempre | Property test |
| DecisionTrace explica cada decisión | sí | Test de trace |

---

## Lo que este plan NO hace (y por qué)

**No introduce un AST de fórmulas.** Los bugs son errores puntuales de
normalización y propagación, no una falla del modelo de fórmulas imperativo.
El AST agrega indirección sin resolver ninguno de los 7 problemas concretos.

**No cambia el softmax ni `PlayerDecision`.** El mecanismo de selección es
correcto. Los inputs eran incorrectos.

**No toca `CollectiveModifiers` de movimiento sin balón.** Ya funciona.
`MatchSituation` cubre las decisiones con balón — son dominios distintos.

**No introduce calibración automática.** Eso se hace perturbando `BalanceConfig`
desde un optimizador externo una vez que el motor produce resultados válidos.
Calibrar sobre resultados rotos no tiene sentido.
