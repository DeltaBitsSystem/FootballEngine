# El Árbitro como Fuente de Verdad — Plan a Largo Plazo

## Resumen ejecutivo

Diagnóstico confirmado con código en mano: **sí, el árbitro es el problema principal**, y no porque
sancione mal las faltas, sino porque **no existe como autoridad**: sus decisiones no son la fuente
de verdad del motor y, en los congelamientos, **ni siquiera llega a despertar**.

Este documento es un plan multi-fase, orquestado con agentes, para convertir el árbitro en la única
autoridad de las leyes del juego, mejor que Football Manager, en F# idiomático y de alto rendimiento,
con horizonte de años.

---

## 1. Diagnóstico (evidencia en código)

### 1.1 El árbitro duerme en tiempo real

El referee DEBERÍA ser el watchdog que desbloquea balones que nadie recoge. Pero solo se activa
por eventos:

```
MatchStepper.fs:462   refereeFreq = OnEvent [ BallLoose; FoulOccurred; GoalScored ]
EventRouter.fs:33-38  runReferee se setea solo en BallLoose | FoulOccurred | GoalScored
EventRouter.fs:45-49  solo Cognition/Action/Team tienen "periodic"; el referee NO
```

Consecuencia: un balón que queda `Free`/`Airborne` en el medio de la nada no emite NINGÚN evento
→ el árbitro jamás vuelve a despertar → el `StuckBallDelay` (5 s, BalanceConfig.fs:1258) **nunca
entra** → el partido queda congelado para siempre.

Esto reproduce los datos: todas las corridas terminan en 45-51 min con 0 goles (baseline del
lab: 45.3', 7 tiros, 0 goles; diag/matches.json: 46 de 50 en 0-0).

### 1.2 El árbitro NO decide las faltas

Las faltas y tarjetas se deciden en la capa del JUGADOR, no del árbitro:

```
DuelAction.fs:150,432   FoulAnalysis.decideCard severity yellows    ← decide TARJETAS
DuelAction.fs:149-157   emite IssueYellow/IssueRed como RefereeAction list
ActionSystem.fs:21-22   aplica directo RefereeApplicator.apply      ← nunca pasa por RefereeAgent
```

Mientras tanto `RefereeAgent.decideCard` (RefereeAgent.fs:124-145) es **código muerto**: nunca se
llama. El "árbitro" de verdad es `FoulAnalysis`, que corre dentro de `DuelAction`.

### 1.3 Al árbitro le pasan `None, None`

```
RefereeAgent.fs:158   let refActions = decide None None ctx state
```

`att`/`def` siempre son `None` → `injuryIntent` (RefereeAgent.fs:83-94) es **código muerto**: nunca
hay lesiones, nunca entra el juicio del árbitro sobre el incidente.

### 1.4 DropBall no reinicia nada

```
RefereeAgent.fs:96-103         StuckBallIntent → DropBall state.AttackingSide
RefereeApplicator.fs:186-198   solo pone Control=Free y v=0 en el MISMO punto
```

No reposiciona el balón, no lo entrega a nadie, no lo contesta. Un balón que llega suelto queda
suelto. El `fix-B` prototipado en `scripts/validate_fixes.fsx` (`resolveContest`: duelo 1v1 del
más cercano de cada equipo, radio 8 m) valida que esa es la resolución correcta para `Contesting`.

### 1.5 Resumen causal

| Problema visible | Causa raíz |
|---|---|
| Congelamiento a 45-51' (balón `Free`/`Airborne` suelto) | Referee no corre periódicamente → `StuckBallDelay` nunca dispara |
| Congelamiento eterno en `Contesting` (GKs al medio) | Nada resuelve balones contestados: `DropBall` no existe para ese estado |
| 0-0 sistemático (0.7-1.9 tiros/partido) | Fuera de scope árbitro: `ShootMinThreshold`, doble penalización de distancia (PLAN.md F2) |
| Tarjetas/lesiones irrealistas | Decididas fuera del árbitro (`DuelAction`), sin contexto |
| Lesiones nunca ocurren | `decide None None` → `injuryIntent` es código muerto |

---

## 2. La tesis: el árbitro como fuente de verdad

En FM, el árbitro es un generador de ruido blanco. Aquí haremos lo opuesto:

> **El árbitro es la única autoridad sobre todas las leyes del juego. Ningún agente decide
> directo sobre: gol/no-gol, fuera de juego, faltas, tarjetas, balones fuera, saques, balones
> estancados, tiempo de descuento, VAR. Los agentes SOLO reportan incidentes (vía
> `SemanticEvent`) y el árbitro emite la determinación definitiva (`RefereeAction`), que es la
> única que muta el estado del partido.**

Diseño en una frase: **los jugadores proponen, el árbitro dispone, el `MatchFlow` ejecuta.**

### Los 5 principios

1. **Autoridad única.** Ningún agente escribe tarjetas, interrumpe el flujo o reposiciona el balón.
2. **Vigilancia activa.** El árbitro corre SIEMPRE (cada N subticks + wake por evento). Es el
   watchdog de último recurso: todo balón estancado tiene resolución garantizada en ≤ N s.
3. **Decisión → Evento → Decisión.** Cada `RefereeAction` produce `MatchEvent`/semantics que
   realimentan al motor. Nada de magia en la caja.
4. **Contextualizado.** El árbitro lee zona, fase de juego, historia del partido, tempo,
   fatiga, estado de ánimo, lesión — no solo `(severity, yellows)`.
5. **Configurable por BalanceConfig**, para calibrar sin tocar código.

### Arquitectura: sub-agentes del árbitro

El `RefereeAgent` deja de ser un monolito y se convierte en orquestador de sub-agentes:

```
RefereeAgent  (orquestador, scheduler autoritativo)
├── LawEnforcer         → balones fuera: banda/córner/saque de meta (ya existe, refinar)
├── GoalAuthority       → confirmar/anular gol + fuera de juego → VAR
├── FoulAuthority       → ABSORBE FoulAnalysis (hoy en DuelAction) y decide severidad+tarjetas con contexto
├── RestartOfficer      → DropBall-duel, posicionamiento de reanudaciones
├── StalledBallWatchdog → Free/Airborne/Contesting estancados → duelo o entrega
├── TimeAdministrator   → tiempo de descuento, pausas por tarjeta/gol/cambio
├── VAROfficer          → existe (VAR/, VARReview, VARDetector) pero jamás se engarza al flujo
└── GoalkeeperGuard     → hold del GK ≥ X s → libre indirecto, pérdida de tiempo
```

Cada uno: `decide : RefereeContext -> RefereeDecision list` y **nada toca estado directo**;
devuelven determinaciones que el orquestador aplica vía un único `RefereeApplicator`.

---

## 3. Anatomía de un sub-agente (decisión → aplicación)

```
[<Struct>] type RefereeContext =
    { Config: BalanceConfig.Referee
      State: SimState          // vista filtrada del mundo
      Attacker: Player option
      Defender: Player option
      Ball: BallPhysicsState
      Zone: PitchZone
      Phase: MatchPhase
      Score: int * int
      Discipline: DisciplineHistory }

type RefereeDecision =
    | PlayOn
    | Foul of FoulArgs          // severidad + offside; NO tarjeta aún
    | Card of CardArgs          // siempre junto a un Foul
    | Restart of SetPieceKind * ClubSide
    | StopFlow of Reason
    | ResumeFlow
    | ConfirmGoal | AnnulGoal
    | DropBall of pos * side pair
    | TimeWasting of ClubSide
    | Waive of advantage

// orquestador:
let tick (ctx: RefereeContext) : RefereeDecision list =
    [ LawEnforcer.decide ctx
      FoulAuthority.decide ctx
      StalledBallWatchdog.decide ctx   // corre SIEMPRE
      TimeAdministrator.decide ctx
      ...
    ] |> List.concat
```

Regla durariana: **ningún otro módulo puede producir un `RefereeAction`.** Hoy
`PlayerAgent.fs:8` y `ActionResolver.fs:137,182` devuelven `ActionResult * RefereeAction list`.
Pasan a devolver solo `SemanticEvent.FoulReported(attacker, defender, location, slide)` y el
árbitro decide con contexto completo.

---

## 4. Fases de ejecución (multi-agente, horizonte de años)

Cada fase tiene: objetivo, archivos que toca, criterio de salida medible. No se salta de fase.

### F0 — "Que exista la fuente de verdad" (2-3 sprints) — PRIORIDAD

**Resuelve:** los congelamientos infinitos.

- **F0.1 — Wake schedule.** `MatchStepper.fs:462` pasa a
  `WhenFlow([IsLive], OnEventOrEveryN([BallLoose; FoulOccurred; GoalScored], 10 s))` y
  `EventRouter.fs:49` agrega periodicidad a `RunReferee`.
  Criterio: el partido que hoy muere a 45-51' llega ≥ 90'.
- **F0.2 — DropBall-duel.** `DropBall` posiciona el balón en el punto del incidente, invoca un
  duelo 1v1 justo (reusar `resolveContest` validado) y **garantiza control en ≤ 3 s**.
  Criterio: 0 stalls `Free`/`Contesting` en el lab.
- **F0.3 — Absorber tarjetas.** Mover `FoulAnalysis.decideCard` y su aplicación
  (`ActionSystem.fs:21-22`) dentro del flujo árbitro. Las tarjetas saldrán solo del `RefereeAgent`.

**Salida:** 10 partidos del lab a ≥ 90' reales, 0 stalls, deterministas con semilla.

### F1 — "El árbitro decide el foul" (2-3 sprints)

- **F1.1** `FoulAuthority` ordena severidad + tarjeta con contexto: zona, fase, condición previa
  del jugador, ventaja, historial de tarjetas. Deja de ser `severity → tarjeta` fija.
- **F1.2** `DuelAction.fs:150,432` deja de llamar a `FoulAnalysis.decideCard`; emite
  `FoulReported`. Solo la `FoulAuthority` traduce report → falta → tarjeta.
- **F1.3** Toda tarjeta sale como `MatchEvent` completo (quién, minuto, zona, motivo) — también
  sirve para comparativa vs FM.

**Salida:** faltas/partido y amarillas dentro de promedios reales (20-30 faltas, 2-4 amarillas).
Grep: `RefereeAction list` solo existe en `RefereeAgent`.

### F2 — "El árbitro cuenta el tiempo" (2 sprints)

- **F2.1** `TimeAdministrator`: descuento como acumulador de segundos perdidos por motivo
  (las 5 categorías ya existen en `StoppageReason`).
- **F2.2** Regla del GK: hold ≤ 6 s → libre indirecto (ya existe `MaxHoldSubTicks`), tarjeta por
  pérdida de tiempo repetida, balón detenido con jugador caído → duelo.
- **F2.3** Pausas por cambio (injertado en el flujo de sustituciones).

**Salida:** extra time medio 2.5'-5'; tests deterministas del minuto 90+4.

### F3 — "Advantage, psicología y contexto" (mejor que FM) (3-4 sprints)

- **F3.1** `AdvantageEngine` (existe `AdvantageEngine.fs`, ignorado): falta táctica → si el
  ataque sigue, ventaja; si muere en ≤ 2 s, devuelve la falta. FM no hace esto bien.
- **F3.2** Juicio con contexto: resultado, localía (existe `HomeBonus`), importancia del partido,
  historial disciplinario, intencionalidad. Pesos en BalanceConfig.
- **F3.3** Manejo de juego: en zona de ataque, interpretar micromanías, "ventaja profesional".
- **F3.4** Persona del árbitro: `RefereeProfile` (permisivo/estricto, suele usar VAR) por partido.

**Salida:** historias-module (cada comportamiento aislado y testeable); "este árbitro dejaría
seguir la última jugada" como escenario verificable.

### F4 — "Performance e idioma F#" (continuo)

- Sin `failwith`; `Result` en lo fallible; sin asignaciones innecesarias.
- `[<Struct>]` en `RefereeContext`/Decisiones; buffers reutilizados en `RefereeApplicator`;
  `Array`/`ResizeArray` en vez de `List` en los hot paths.
- Benchmark: el árbitro ≤ 5% del costo total del tick.

---

## 5. Orquestación con agentes (el mecanismo de trabajo)

| Paso | Agente | Deliverable |
|---|---|---|
| 1 | explore | Mapa de quién escribe/lee el referee (grep `RefereeAction`, `FoulAnalysis`) |
| 2 | general | F0.1-F0.2 (wake + dropball-duel) con ciclo `dotnet fsi scripts/validate_fixes.fsx` |
| 3 | general | F0.3 (absorber `FoulAnalysis`) |
| 4 | explore | Datos reales de referencia: faltas/partido, amarillas, extra-time (para targets) |
| 5 | general | F1-F3 por ramas, cada una cerrada con `dotnet test --filter "Layer1"` |

Regla inmutable: **cada fase termina con `dotnet build` + `dotnet fantomas . --check` verdes y el
lab mostrando un partido ≥ 90' con números reales de fútbol.** Nunca se avanza de fase con el
criterio de salida sin cumplir.

---

## 6. Declaración de cierre (definition of ready)

- [ ] Cada ley del juego tiene UNA única autoridad.
- [ ] El balón no puede quedar estático: watchdog garantiza reanudar en ≤ 10 s.
- [ ] Benchmark de 50 partidos en objetivos reales: ~2.5 goles/partido, ~25 tiros, 5+ corners,
      ~20-30 faltas, ~2-4 amarillas, extra-time 2.5'-5'.
- [ ] El árbitro se SIENTE en el partido: decisiones visibles como `MatchEvent`.
- [ ] F# idiomático: `fantomas --check` verde, 0 runtime `failwith`.
- [ ] Determinismo con semillas preservado; invariantes de Layer1 intactas.

---

## 7. Mapa de archivos a transformar

| Hoy | Futuro |
|---|---|
| `RefereeAgent.fs` (243 ln) monolito | orquestador + sub-agentes + scheduler correcto |
| `RefereeApplicator.fs` único escritor | se mantiene como único escritor |
| `FoulAnalysis.fs` llamado desde DuelAction | solo responde cuando el árbitro lo consulta |
| `DuelAction.fs:150,432` decide tarjetas | emite `FoulReported`; no devuelve `RefereeAction` |
| `ActionSystem.fs:21-22` aplica tarjetas | se elimina; el árbitro hace su propio pass |
| `RefereeWeights` (7 campos) | 20-40 pesos contextuales en BalanceConfig + validación |
| `StoppageTimeTracker` | integrado con `TimeAdministrator` |
| `VAR/` existe pero desconectado | engarzado vía `VAROfficer` (solicitud determinista) |

---

## 8. Próximos pasos

1. **F0.1**: `MatchStepper.fs:462` → `OnEventOrEveryN(..., 10 s)` + periodicidad en
   `EventRouter.fs:49`.
2. **F0.2**: reescribir `DropBall` como duelo en el punto del incidente (reusar `resolveContest`
   de `scripts/validate_fixes.fsx`, ya validado).
3. **F0.3**: mover decisión de tarjetas de `DuelAction` a `FoulAuthority`.
4. Refactorizar `RefereeAgent.agent`: reemplazar `decide None None` por `RefereeContext` real.

Verificación de cada paso: `dotnet fsi scripts/validate_fixes.fsx` (hoy: baseline 45' → objetivo
≥ 90' y goles).

---

## 9. Plan heredado (ya aplicado, se conserva como referencia)

El anterior `PLAN.md` fue absorbido y eliminado; sus 3 fases están **completas** en el código:

| Fase | Problema | Estado |
|---|---|---|
| 1 | Slot index ↔ Roster index confundidos (self-passes) | ✅ `SlotAccess.fs` creado, ~20 sitios corregidos |
| 2 | `AngleSpreadBase = 0.8 rad` (dispersión enorme, 0 goles) | ✅ `0.15` en `weights.json` y `weights_trained.json` |
| 3 | 5 campos `OnTarget*` muertos en `ShotConfig` | ✅ eliminados de BalanceConfig, WeightsLoader, validación, JSON |

### Criterios de realismo heredados (objetivos del balance, no del árbitro)

```bash
dotnet build
dotnet test FootballEngine.Tests/FootballEngine.Tests.fsproj --filter "Layer1"
dotnet test FootballEngine.Tests/FootballEngine.Tests.fsproj --filter "Layer2"
```

- Jugador medio con finishing 50 acierta al arco ~50% desde 15 m.
- E2E de 5 partidos: pases completados > 70%, goles en ≥ 3/5, ~10-15 tiros/partido, ~4-6 a puerta.
- Los hallazgos actuales del lab (doble penalización de distancia en `PlayerScorer.shootScore`,
  `ShootMinThreshold = 0.35` inalcanzable, xG duplicado en `OutcomeResolver`) son la F2 de este
  mismo plan, no un plan aparte: mismo lab, misma semilla, misma fuente de verdad.
