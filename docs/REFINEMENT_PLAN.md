# Refinement Plan — FootballEngine Core

> Estado: **rewrite cerrado** (Core compila 0 errores/0 warnings, solución completa buildea, Client wiring OK). A partir de aquí: **refinar a fondo** — recuperar el valor del motor viejo reimaginado para la arquitectura nueva (SoA, sistemas desacoplados, determinismo, O(1) por tick).

## Principio de naming de tests

**Los tests se nombran como el código que prueban.** La convención existente en `FootballEngine.Core.Tests` ya es un espejo de la estructura del proyecto:

| Carpeta de código | Módulo de tests | Filtro Expecto |
|---|---|---|
| `Contracts/` | `Contracts.RngTests`, `Contracts.ConfigTests`, `Contracts.EventFlagTests`, ... | `FootballEngine.Core.Contracts.Rng` |
| `Stepper/` | `Stepper.StepperTests`, `Stepper.FlowTests`, `Stepper.SchedulerTests`, ... | `FootballEngine.Core.Stepper.Flow` |
| `Systems/Ball/` | `Systems.Ball.PhysicsTests`, `Systems.Ball.ContactTests`, ... | `FootballEngine.Core.Systems.Ball.Physics` |

Cada fase del plan lista **sus tests con el mismo nombre que el módulo que toca** (`Systems.Referee.Fouls`, `Systems.Referee.VAR`, `Systems.Players.Perception`, `Systems.Players.Actions.SetPiece`, `Systems.Team.Decide`, ...). El prefijo del filtro siempre es `FootballEngine.Core.` + carpeta + módulo. **No existen "Layer1/Layer2".**

---

## Fase R1 — Árbitro como pipeline de restarts

**Sistema:** `Systems/Referee/Fouls.fs`, `Cards.fs` · `Stepper/EventApply.fs`

**Problema:** la falta solo suma stats; no hay tiro libre, el penal es inalcanzable, la severidad siempre es `Minor`+`NoCard`.

**Técnica:** `Foul` → si `Fouls.evaluatePlayOn` (zona + posesión del agraviado en transición) → **ventaja** con ventana `AdvantageWindow` y callback si no progresa; si no → `RestartPlan` embebido en el evento. Severidad con contexto (zona, `lastMan`, atacantes en línea, `RecklessProbability` del duelo). `Penalty` nace de la misma rama (`FoulInBox` → `SetPieceKind.Penalty`). Un solo sistema emite `EventData.Foul` + `EventData.SetPiece`; `EventApply` ya aplica restarts.

**Performance:** 1 evaluación por falta; cero overhead en ticks sin falta.

**Tests:** `Systems.Referee.FoulsTests` (play-on, severidad por contexto, penal en área, lastMan), `Systems.Referee.CardsTests` (escalado por severidad), `Stepper.EventApplyTests` (restart correcto tras falta).

---

## Fase R2 — Gol físico + VAR con confirmación

**Sistema:** `Systems/Ball/Contact.fs`, `Outcome.fs` · `Systems/Referee/VAR.fs` · `Stepper/Flow.fs`

**Problema:** gol probabilístico, autogol inexistente, VAR inalcanzable.

**Técnica:** gol **geométrico** — `isInGoal` (X sobre la línea, |Y| < postes, Z < travesaño) en el sub-tick donde el balón cruza la línea. La probabilidad queda en el save del GK, no en el gol. `scorer` vía `LastTouchBy` → **autogol** nativo. `VARReview` se enciende: el `Goal` emite `VARRequested = ofside sospechoso` (snapshot ya existe en `Shot.fs`) → `Flow.VARReview` → `GoalStands/GoalOverturned`. El VAR solo se dispara dentro del margen `VarOffsideMargin`, no en cada gol.

**Performance:** una comparación por cruce de línea; VAR solo cuando el margen lo amerita.

**Tests:** `Systems.Ball.GoalDetectionTests` (línea, postes, travesaño, autogol), `Systems.Referee.VARTests` (margen, overturn), `Stepper.FlowTests` (secuencia VAR completa).

---

## Fase R3 — Campo de percepción cognitivo

**Sistema:** `Systems/Players/Perception.fs`, `Decision.fs`, `Intent.fs`

**Problema:** percepción = booleanos sin lectores; pase sin canal libre; sin cono de visión.

**Técnica:** un pase SoA por equipo/tick construye el CFrame real: `nearestOpp`, `oppsNear[7m]`, `pressCount`, `laneClear` (detección de canal: dos jugadores bloqueando el segmento pasador→receptor, O(k), k≈5) y `facingCone` (el receptor que no te ve no es objetivo). `Decision` usa `laneClear` como gate de pase y `openness` real.

**Performance:** buffers pre-allocados, sin allocations por tick.

**Tests:** `Systems.Players.PerceptionTests` (laneClear, cono, pressCount), `Systems.Players.DecisionTests` (gate de pase, receiver eligibilidad).

---

## Fase R4 — Time-to-ball real en interceptación

**Sistema:** `Systems/Ball/Contact.fs`, `Possession.fs`

**Problema:** el lento parado gana la carrera.

**Técnica:** modelo cinemático vectorizado: `t = (d − captureRadius) / (maxSpeed × conditionFactor × intentFactor)` por candidato en un pase sobre `PosX/Y`, `Pace`, `Condition` (ya en SoA). Gana el primero en llegar, no el más cercano. `intentFactor` (press/recover) premia a quien ya corre.

**Performance:** ~11 divisiones por llegada.

**Tests:** `Systems.Ball.ContactTests` (carrera ganada por tiempo, no distancia), `Systems.Ball.PossessionTests` (captura por intención).

---

## Fase R5 — Fatiga cognitiva

**Sistema:** `Systems/Players/Perception.fs`, `Decision.fs`, `Actions/Pass.fs`, `Actions/Duel.fs`

**Problema:** la condición solo afecta la velocidad.

**Técnica:** `cognition = sigmoid(cond)` por jugador, precalculado cada N ticks en un `float[]` y multiplicado por: temperatura efectiva de decisión, radio de percepción, `ReceptionQuality` y utilidad de pressing. Mecanismo batch: 0.5 céntimos de tick por equipo.

**Performance:** array precalculado, sin cálculo por evento.

**Tests:** `Systems.Players.DecisionTests` (calidad de decisión cae con condición), `Systems.Players.Actions.PassTests` (recepción degradada).

---

## Fase R6 — Memoria que decide

**Sistema:** `Systems/Players/Decision.fs` · `Stepper/EventApply.fs`

**Problema:** `PlayerMemory` se escribe y nadie la lee.

**Técnica:** `MemoryVector` de 6 floats por jugador con **decay exponencial O(1)** en batch cada 12000 subticks. Se alimenta donde hoy ya se escribe (`EventApply`/`Duel`) y se lee en `Decision` como término lineal: `passFailureMod`, `streakMod`, `duelHistoryMod` → "está fallando → conservador".

**Performance:** 6 floats/jugador, decay en lote.

**Tests:** `Systems.Players.DecisionTests` (racha de fallos → utilidad conservadora, decay temporal).

---

## Fase R7 — Roles colectivos dinámicos

**Sistema:** `Systems/Team/Decide.fs`, `Shape.fs`

**Problema:** pressing global sin coordinación; roles posicionales estáticos.

**Técnica:** al cambiar la directiva, un **asignador lineal** reparte `roleBias: float[7]` (Press/Support/Hold/Anchor/MakeRun/Cover/Recover): slot más cercano al balón → press, el más retrasado → anchor, 2 intermedios → cover, con fallback determinista. El vector multiplica las utilidades de intent existentes. Solo se re-asigna cuando cambia la directiva (~cada minuto).

**Performance:** nada por tick; re-asignación solo en cambio de directiva.

**Tests:** `Systems.Team.ShapeTests` (asignación de roles, fallback determinista), `Systems.Team.DecideTests` (bias aplicado a intents).

---

## Fase R8 — Set pieces reales

**Sistema:** `Systems/Players/Actions/SetPiece.fs`, `Systems/Team/Shape.fs`

**Problema:** "el más cercano lanza un pase"; `FreeKickShotChance` muerta como constante.

**Técnica:** (1) `SetPiecePlacement` — en `RestartDelay`, perturba `BasePositions` de atacantes/defensas hacia zonas del área (cerca/lejos/borde) según tipo; (2) `WallPlacer` — en libres ≤32 m, 3-4 defensores en línea perpendicular a 9.15 m; (3) rutina del ejecutor: ≤32 m → tiro directo con `FreeKickShotChance` real, >32 m → pase al mejor rematador (header). Se elimina el `roll = 0.5` fijo.

**Performance:** solo en `RestartDelay`.

**Tests:** `Systems.Players.Actions.SetPieceTests` (rutina ≤32m/ >32m, barrera), `Systems.Team.ShapeTests` (placement en córner).

---

## Fase R9 — Colectivo: WinProbability + patrones + momentum

**Sistema:** `Systems/Team/Decide.fs`, `Emergent.fs` · `Contracts/BalanceConfig.fs`

**Problema:** sin probabilidad de victoria, sin adaptación, momentum sin respuesta.

**Técnica:** (1) `WinProbability` = modelo lineal de 4 inputs (`goalDiff`, `xGdiff`, `momentum`, `minute`) calibrable en `BalanceConfig` → alimenta la directiva (riesgo en los últimos 15'). (2) `AdaptivePatterns`: `EventWindow` cuenta por kind → 3 contadores de canal (centro/izquierda/derecha) que sesgan `CrossBaseUtility` vs `PassBaseUtility`. (3) `MomentumShifted` activa la respuesta del manager. Todo lee datos existentes; solo 3 pesos nuevos.

**Performance:** 3 contadores + 1 modelo lineal por minuto.

**Tests:** `Systems.Team.DecideTests` (directiva por winProb y minuto), `Systems.Team.EmergentTests` (sesgo por canal), `Contracts.ConfigTests` (3 pesos nuevos validados).

---

## Fase R10 — Manager real + penales/extra-time

**Sistema:** `Systems/Manager/Manager.fs` · `Systems/Players/Actions/Shot.fs`, `GK.fs` · `Stepper/Flow.fs`

**Problema:** sub por condición ≤0.35, sin penales, `IsKnockout` muerto.

**Técnica:** (1) `Manager` lee `ManagerConfig` (hoy muerto): ventanas por marcador (`Losing/Drawing/Winning`), reacción a `RedCardIssued` (compactar) y a `Injury` (sub inmediata). (2) `PenaltyShootout`: mini-simulación determinista que **reusa** `Shot` + `GK.save` — misma física, misma RNG encadenada, sin código duplicado. `IsKnockout` se cablea desde el fixture.

**Performance:** solo al final del partido o en evento de tarjeta/lesión.

**Tests:** `Systems.Manager.ManagerTests` (sub por marcador, reacción a roja/lesión), `Systems.Players.Actions.ShootoutTests` (determinismo, reuso de save), `Stepper.FlowTests` (empate knockout → penales).

---

## Fase R11 — Calibración viva + dump

**Sistema:** `Contracts/BalanceConfig.fs` · `Stepper/Stepper.fs` · `Contracts/TeamStats.fs`

**Problema:** `weights.json` no se carga en producción; sin dump de partidos; stats incompletas para calibrar.

**Técnica:** (1) `WeightMerge.load` cableado al arranque: si existe `weights.json`, merge sobre `defaultConfig` **una vez** (cero coste runtime). (2) `MatchDump`: exporta `EventBuffer` + `TeamStats` a JSON **fuera del hot path** (solo `RunMode.Full`). (3) `TeamStats` gana dribbles, crosses, xG agregado — acumulados donde hoy ya se escriben.

**Performance:** carga única al arranque; dump solo en modo Full.

**Tests:** `Contracts.ConfigTests` (weights.json cargado en producción), `Stepper.StepperTests` (MatchDump determinista), `Contracts.StatsTests` (nuevas métricas acumuladas).

---

## Orden de ejecución

| Tramo | Fases | Por qué |
|---|---|---|
| 1 — Arbitraje | R1 → R2 | Lo más visible: faltas que detienen, goles reales, VAR |
| 2 — Cerebro del jugador | R4 → R5 → R6 | Cinemática, fatiga, memoria = corazón de la simulación |
| 3 — Percepción y colectivo | R3 → R7 → R8 | Campo visual, roles, balón parado |
| 4 — Cierre | R9 → R10 → R11 | Manager, penales, datos |

**Criterios de aceptación por fase:**
1. `dotnet build` → 0 errores, 0 warnings.
2. Tests nuevos verdes con filtro `FootballEngine.Core.<Sistema>`.
3. Suite completa verde (determinismo intacto: `Stepper.DeterminismTests`).
4. Fantomas limpio (`dotnet fantomas . --check`).
5. Si la fase cambia volúmenes (fouls, goals, set pieces), recalibrar en `Training` y actualizar `weights.json` antes de cerrar.