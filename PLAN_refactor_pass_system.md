# Plan: Refactorizar el Sistema de Pases desde Cero

## Problema Actual

El sistema de pases tiene 752 líneas en `PassAction.fs` con 3 funciones principales (`resolve`, `resolveLong`, `resolveIntoSpace`) que comparten lógica pero divergen en comportamiento. Problemas concretos:

1. **Self-passes**: `resolveLong` no excluye al pasador del loop de targets (líneas 367-380)
2. **Resolución en 1 tick**: La pelota viaja instantáneamente, sin ventana de interceptación
3. **3 implementaciones de target selection**: `Perception.resolveBestPassTargetThroughMask`, `MatchSpatial.findBestPassTargetFrame`, `CognitiveFrameBuilder.build` — todas duplican lógica
4. **Sin eventos semánticos**: `resolveLong` y `resolveIntoSpace` no emiten `SemanticEvent.PassLaunched`
5. **Quality hardcodeada**: `resolveIntoSpace` usa `quality=0.7` fijo
6. **Dead code**: `SpaceScorer.findBestSpaceTarget` nunca se llama
7. **GK distribution siempre interceptada**: Sin lógica especial para distribution

## Decisiones de Diseño

| Decisión | Elección |
|---|---|
| Target selection | **Una sola función** que todos los caminos llaman |
| Self-pass fix | **Tipo opaco `PassTarget`** — imposible crear self-pass en compile time |
| Pass timing | **Subtick-based (40-80 subticks)** — realista para football manager |
| Semantic events | **Todos los pases** emiten `SemanticEvent.PassLaunched` |
| Space pass quality | **Beta sampling** (mismo que pases cortos) |
| Dead code | **Limpiar todo** |
| GK distribution | **GK pass especial** (lógica separada) |
| Scope | **Launch + trajectory + resolution** (ciclo completo) |

## Arquitectura Nueva

### Estructura de Archivos

```
Engine/Match/Player/Actions/
├── PassDecision.fs          # NUEVO: findBestPassTarget unificado
├── PassLauncher.fs          # NUEVO: launchShort, launchLong, launchSpace, launchGK
├── PassTrajectory.fs        # NUEVO: buildTrajectory, computeFlightTime
├── PassResolution.fs        # NUEVO: resolveArrival (refactorizado de OutcomeResolver)
└── PassAction.fs            # ELIMINAR: reemplazado por los 4 archivos anteriores

Engine/Match/Ball/
├── ContactResolver.fs       # MODIFICAR: detección de llegada por subtick
├── OutcomeResolver.fs       # MODIFICAR: delegar pases a PassResolution
└── BallSystem.fs            # MODIFICAR: integrar nuevo timing
```

### Tipos Nuevos — Self-Pass Imposible en Compile Time

El sistema usa **tipos opacos** para que el compilador rechace cualquier intento de crear un self-pass. No hay guards runtime — la invariant está codificada en los tipos.

```fsharp
// PassDecision.fs

/// Tipo opaco: solo se puede crear via createPassTarget.
/// El constructor es privado — nadie puede fabricar un PassTarget arbitrario.
[<Struct>]
type PassTarget = private PassTarget of idx: int * playerId: PlayerId * position: Spatial * distance: float<meter>

module PassTarget =
    /// Crea un PassTarget. Retorna None si candidateIdx = passerIdx.
    /// Esta es la ÚNICA forma de crear un PassTarget.
    let create (passerIdx: int) (candidateIdx: int) (pid: PlayerId) (pos: Spatial) (dist: float<meter>) : PassTarget option =
        if candidateIdx = passerIdx then None
        else Some(PassTarget(candidateIdx, pid, pos, dist))

    let idx (PassTarget(i, _, _, _)) = i
    let playerId (PassTarget(_, pid, _, _)) = pid
    let position (PassTarget(_, _, p, _)) = p
    let distance (PassTarget(_, _, _, d)) = d

/// Resultado de buscar target — garantiza que no hay self-pass.
type PassCandidate =
    { Target: PassTarget  // ← PassTarget, no int/PlayerId crudo
      LaneClear: bool
      Score: float }

/// Busca el mejor target de pase para un jugador dado.
/// Retorna None si no hay ningún teammate válido (excluye al pasador por construcción).
/// Única implementación — todos los caminos la llaman.
let findBestPassTarget
    (attFrame: TeamFrame)
    (attRoster: PlayerRoster)
    (defFrame: TeamFrame)
    (meIdx: int)                    // ← index del pasador
    (attackDir: AttackDir)
    (passConfig: PassConfig)
    (mask: VisibilityMask option)
    : PassCandidate option          // ← Option: None si solo queda el pasador
```

```fsharp
// PassLauncher.fs

type LaunchResult =
    { Ball: BallPhysicsState
      Trajectory: BallTrajectory
      Events: DomainEvent[] }

/// Lanza un pase corto a un target específico.
/// target: PassTarget — garantiza que no es el pasador.
let launchShort
    (subTick: int)
    (clock: SimulationClock)
    (passerIdx: int)
    (target: PassTarget)            // ← PassTarget, no Player crudo
    (ctx: MatchContext)
    (state: SimState)
    : LaunchResult

/// Lanza un long ball al mejor target adelantado.
/// Internamente usa findBestPassTarget — si solo queda el pasador, retorna None.
let launchLong
    (subTick: int)
    (clock: SimulationClock)
    (passerIdx: int)
    (ctx: MatchContext)
    (state: SimState)
    : LaunchResult option           // ← Option: None si no hay target válido

/// Lanza un pase al espacio (through ball).
let launchSpace
    (subTick: int)
    (clock: SimulationClock)
    (passerIdx: int)
    (targetCell: int)
    (ctx: MatchContext)
    (state: SimState)
    : LaunchResult

/// Distribución del GK (lógica especial).
let launchGKDistribution
    (subTick: int)
    (clock: SimulationClock)
    (gkIdx: int)
    (ctx: MatchContext)
    (state: SimState)
    : LaunchResult
```

```fsharp
// PassTrajectory.fs

/// Calcula tiempo de vuelo basado en distancia y tipo de pase.
let computeFlightTime
    (distance: float<meter>)
    (passSpeed: float<meter / second>)
    (subTicksPerSecond: int)
    : int<subtick>

/// Construye la trayectoria completa.
let buildTrajectory
    (subTick: int)
    (clock: SimulationClock)
    (kickerId: PlayerId)
    (origin: Spatial)
    (target: Spatial)
    (speed: float<meter / second>)
    (vz: float<meter / second>)
    (intent: InFlightIntent)
    : BallTrajectory
```

```fsharp
// PassResolution.fs

/// Resuelve la llegada de un pase (competition entre jugadores).
let resolveArrival
    (ball: BallPhysicsState)
    (traj: BallTrajectory)
    (homeFrame: TeamFrame)
    (awayFrame: TeamFrame)
    (homeRoster: PlayerRoster)
    (awayRoster: PlayerRoster)
    (cfg: PhysicsConfig)
    (iw: InterceptionWeights)
    (subTick: int)
    : ContactResolver.Contact
```

### Garantía Compile-Time

```
PassAction.resolve(subTick, ctx, state, clock, target)
  └─ target: Player  ← CUALQUIER Player, incluyéndote a vos

PassLauncher.launchShort(subTick, clock, passerIdx, target, ctx, state)
  └─ target: PassTarget  ← Solo se crea via PassTarget.create(passerIdx, ...)
                              └─ Si candidateIdx = passerIdx → None
                              └─ Si candidateIdx ≠ passerIdx → Some PassTarget
```

**El compilador rechaza:**
```fsharp
let target = PassTarget.create passerIdx passerIdx pid pos dist
// Error: No match inhibitor pattern? No — retorna None.
// Pero None propaga: si findBestPassTarget retorna None, no se llama launchShort.

// Intentar fabricar un PassTarget sin pasar por create:
let bad = PassTarget(5, pid, pos, 10.0)
// Error: The constructor of PassTarget is not accessible from this code location
```

**La invariant se preserva en toda la cadena:**
1. `findBestPassTarget` solo retorna `PassCandidate` con `PassTarget` válidos
2. `launchShort` recibe `PassTarget` (no puede ser self)
3. `launchLong` internamente llama `findBestPassTarget` (misma garantía)
4. `launchSpace` no usa `PassTarget` (target es una celda espacial, no un jugador)
5. `launchGKDistribution` tiene su propio lógica (GK nunca se pasa a sí mismo)

### Flujo Nuevo

```
PlayerDecision.decide
  → ctx.BestPassTargetIdx ahora es PassTarget option (no int16 voption)
  → OnBallIntent.Pass(targetPid) | LongBall | PassIntoSpace(cell)
  → ActionResolver.resolveIntent
      → Pass: PassLauncher.launchShort(passIdx, target.PassTarget, ...)
      → LongBall: PassLauncher.launchLong(passIdx, ...)
      → PassIntoSpace: PassLauncher.launchSpace(passIdx, cell, ...)
      → GK: PassLauncher.launchGKDistribution(gkIdx, ...)

Launch functions:
  → PassTarget.create(passIdx, candidateIdx, ...) en findBestPassTarget
  → computeFlightTime(distance, speed) → 40-80 subticks
  → buildTrajectory → BallTrajectory { EstimatedArrivalSubTick }
  → betaSample quality (si no es GK)
  → Emit SemanticEvent.PassLaunched (TODOS los tipos)
  → Ball.Control = Airborne

BallSystem.run (cada tick):
  → BallPhysics.update
  → ContactResolver.find
      → checkBallArrived(ball, subTick) — compara subTick con EstimatedArrivalSubTick
      → evaluateArrival (si llegó)
  → OutcomeResolver.resolve
      → PossessionGained | BallLoose | Interceptor
```

### Cambios en CognitiveFrame y PlayerContext

```fsharp
// ANTES (CognitiveFrame):
type CognitiveFrame = {
    BestPassTargetIdx: int16[]    // ← raw int, puede ser == meIdx
    BestPassTargetPos: Spatial voption[]
    ...
}

// AHORA:
type CognitiveFrame = {
    BestPassTarget: PassTarget[]          // ← array de PassTarget opacos
    BestPassTargetPos: Spatial voption[]
    ...
}

// ANTES (PlayerContext):
type AgentContext = {
    BestPassTargetIdx: int voption      // ← None si es self
    ...
}

// AHORA:
type AgentContext = {
    BestPassTarget: PassTarget option    // ← PassTarget opaco, nunca self
    ...
}

// La lógica de cognición:
let bestPassTarget = findBestPassTarget attFrame attRoster defFrame meIdx ...
agentCtx.BestPassTarget <- bestPassTarget |> Option.map (fun c -> c.Target)
// Si bestPassTarget es None, no hay candidato → passBlocked = true
```

### Cambios en PlayerDecision

```fsharp
// ANTES:
let passBlocked =
    not mask.CanPass
    || match ctx.BestPassTargetIdx with
       | ValueNone -> true
       | ValueSome _ -> scores.Pass < passThreshold

// AHORA:
let passBlocked =
    not mask.CanPass
    || ctx.BestPassTarget = None    // ← None significa no hay target válido (incluyendo self)
    || scores.Pass < passThreshold
```

### Cambios Clave en ContactResolver

```fsharp
// ANTES: shouldTriggerArrival usaba dist + convergence
// AHORA: llegada se detecta por tiempo + proximidad

let checkBallArrived (ball: BallPhysicsState) (currentSubTick: int) : bool =
    match ball.Trajectory with
    | Some traj ->
        // La pelota llegó según el tiempo estimado
        currentSubTick >= int traj.EstimatedArrivalSubTick
    | None -> false

// Competition se activa cuando la pelota está airborne Y llegó
// Si no llegó, NoContact (ball sigue volando)
```

### Cambios en BalanceConfig

```fsharp
// PassConfig existente — agregar:
type PassConfig = {
    // ... existente ...
    
    // NUEVOS:
    FlightTimeMin: int<subtick>      // 40 subticks (1 segundo)
    FlightTimeMax: int<subtick>      // 80 subticks (2 segundos)
    FlightTimePerMeter: float        // subticks por metro
    ArrivalWindow: int<subtick>      // ventana de competición (10 subticks)
    
    // Long ball:
    LongBallFlightTimeMin: int<subtick>  // 60 subticks
    LongBallFlightTimeMax: int<subtick>  // 120 subticks
    
    // GK:
    GKDistributionSpeed: float<meter / second>
    GKDistributionAccuracy: float
    GKShortPassMaxDist: float<meter>  // max distancia para pase corto del GK
}

// GKConfig existente — agregar:
type GKConfig = {
    // ... existente ...
    
    // NUEVOS:
    DistributionDecisionDelay: int<subtick>  // tiempo de decisión del GK
    ShortDistributionBias: float             // probabilidad de pase corto vs long
}
```

### Eliminación de Código

| Archivo/Línea | Acción | Razón |
|---|---|---|
| `PassAction.fs` (752 líneas) | **Eliminar** | Reemplazado por 4 archivos modulares |
| `Perception.resolveBestPassTargetThroughMask` | **Eliminar** | Consolidado en `PassDecision.findBestPassTarget` |
| `MatchSpatial.findBestPassTargetFrame` | **Eliminar** | Consolidado en `PassDecision.findBestPassTarget` |
| `SpaceScorer.findBestSpaceTarget` | **Eliminar** | Nunca se llama |
| `SpaceScorer.findThroughBallTarget` | **Mover** a `PassDecision` | Lógica de space pass integrada |
| `OutcomeResolver` (sección Aimed) | **Mover** a `PassResolution` | Resolución de pases delegada |
| `PlayerContext.fs:121-122` guard `<> meIdx` | **Eliminar** | `PassTarget.create` ya lo previene |
| `CognitiveFrameBuilder.fs:156` guard `j <> i` | **Eliminar** | `PassTarget.create` ya lo previene |
| `MatchSpatial.fs:213` guard `i <> meIdx` | **Eliminar** | `PassTarget.create` ya lo previene |
| `Perception.fs:201` guard `i <> meIdx` | **Eliminar** | `PassTarget.create` ya lo previene |
| `PassAction.resolveIntoSpace:577` guard `i <> pIdx` | **Eliminar** | `PassTarget.create` ya lo previene |
| `PassAction.resolveIntoSpace:624` guard `i <> pIdx` | **Eliminar** | `PassTarget.create` ya lo previene |

**Total: ~100 líneas de guards eliminadas** — la invariant vive en el tipo, no en runtime checks.

### Impacto en Otros Archivos

| Archivo | Cambio |
|---|---|
| `ActionResolver.fs:131-171` | `resolveIntent` llama a `PassLauncher.*` en vez de `PassAction.*` |
| `ActionResolver.fs:143-148` | `OnBallIntent.Pass targetPid` → `PassAction.resolve(...)` se reemplaza por `PassLauncher.launchShort(...)` |
| `PlayerDecision.fs:93-105` | `SpaceScorer` se importa desde nueva ubicación |
| `PlayerDecision.fs:119-123` | `ctx.BestPassTargetIdx` → `ctx.BestPassTarget` (PassTarget option) |
| `PlayerContext.fs:84-167` | `BestPassTarget` se computa via `PassDecision.findBestPassTarget` |
| `CognitiveFrameBuilder.fs:149-196` | Usa `PassDecision.findBestPassTarget` en vez de lógica inline |
| `BallSystem.fs:13-120` | `OutcomeResolver.resolve` recibe `PassResolution` como parámetro |
| `SimStateOps.fs` | Sin cambios significativos |
| `BalanceConfig.fs` | Nuevos campos en `PassConfig` y `GKConfig` |
| `ConfigValidation.fs` | Validación de nuevos campos |
| `Tests` | Actualizar builders y assertions |

## Orden de Implementación

### Fase 1: Tipos y Config (sin behavior change)
1. Agregar nuevos campos a `BalanceConfig.PassConfig` y `GKConfig`
2. Actualizar `ConfigValidation.fs`
3. Crear tipos `PassCandidate`, `LaunchResult` en archivos nuevos

### Fase 2: PassDecision (target selection unificado)
1. Crear `PassDecision.fs` con `findBestPassTarget`
2. Consolidar lógica de CognitiveFrameBuilder + Perception + MatchSpatial
3. Eliminar las 3 implementaciones duplicadas
4. Actualizar `PlayerContext.fs` y `CognitiveFrameBuilder.fs`

### Fase 3: PassLauncher (launch functions)
1. Crear `PassLauncher.fs` con `launchShort`, `launchLong`, `launchSpace`, `launchGKDistribution`
2. Implementar beta sampling para space passes
3. Implementar GK distribution especial
4. Asegurar que TODOS emiten `SemanticEvent.PassLaunched`

### Fase 4: PassTrajectory (flight time realista)
1. Crear `PassTrajectory.fs` con `computeFlightTime` y `buildTrajectory`
2. Calcular flight time basado en distancia (40-80 subticks)
3. Actualizar `BallTrajectory.EstimatedArrivalSubTick`

### Fase 5: PassResolution (resolución por subtick)
1. Crear `PassResolution.fs` con `resolveArrival`
2. Modificar `ContactResolver.find` para usar `checkBallArrived` por tiempo
3. Modificar `OutcomeResolver.resolve` para delegar a `PassResolution`

### Fase 6: Integración
1. Actualizar `ActionResolver.resolveIntent` para usar nuevos módulos
2. Actualizar `BallSystem.fs` para integrar nuevo timing
3. Eliminar `PassAction.fs` completo

### Fase 7: Tests y Calibración
1. Actualizar `DiagnosticTests.fs` para nuevos ranges
2. Calibrar `BalanceConfig` para obtener 2-4 goles por partido
3. Verificar que self-passes ya no ocurren
4. Verificar que pases tienen flight time realista

## Riesgos

| Riesgo | Mitigación |
|---|---|
| ContactResolver existente se rompe | Mantener `shouldTriggerArrival` como fallback temporal |
| GK distribution empeora | Implementar con config separada, defaults conservadores |
| Tests fallan masivamente | Calibrar después de integrar, no durante |
| Performance con más subticks | `BallSystem` ya corre cada tick, el overhead es mínimo |

## Métricas de Éxito

| Métrica | Target |
|---|---|
| Goles por partido | 2-4 (promedio) |
| Self-passes | 0 |
| Tiempo de vuelo de pase | 40-80 subticks (1-2 segundos) |
| Eventos semánticos por pase | 1 (todos los tipos) |
| Líneas de código eliminadas | ~300+ (PassAction.fs completo) |
| Archivos nuevos | 4 (modulares, <200 líneas cada uno) |
