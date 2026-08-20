# Plan de Implementación — Action Chains (planificación multi-paso del portador)

> Estado: **diseño** — propuesta lista para implementar test-first. Basado en la
> investigación HELIOS (`action_chain_graph.cpp`: best-first, `MAX_CHAIN_LENGTH=4`,
> `MAX_EVALUATE_LIMIT=500`, `sample_field_evaluator.cpp`) y en
> [ROBOCUP_DEEP_RESEARCH.md](ROBOCUP_DEEP_RESEARCH.md) paso 1.
>
> **La idea en una frase:** el portador no decide la mejor *primera* jugada, sino
> la mejor *secuencia* de 2-4 jugadas (pase → pase → tiro), y ejecuta solo la
> primera acción de la cadena ganadora. Mismo softmax, mismos markers, misma
> ejecución — solo cambia el *score* de cada opción: pasa a ser el valor del
> mejor estado alcanzable, no el valor de 1 paso.
>
> **Reglas del juego (años, sin redundancia):** cero sistemas nuevos en la tabla,
> cero cambios en `Actions.fs`, cero re-implementaciones de mecánicas existentes
> (pases, intercepción, xG). Un módulo puro + un estado derivado + un consumidor.

---

## 1. Arquitectura objetivo

```
PlayerDecision.step (sistema existente, cadencia existente)
  └─ choose (existente) — por cada candidato de 1er nivel:
       ├─ score_1paso (hoy)
       └─ score_cadena (NUEVO): Planner.search — valor del mejor estado a 2-4 acciones
  └─ escribe marker Phase 200 (existente, sin cambios)
  └─ escribe TeamWorld.ActivePlan (NUEVO, estado derivado)
        → PlayerIntent.step lo lee para intents de recepción futura (extensión mínima)
```

Capas: el `Planner` es **puro** (no toca `World`, no usa `Rng`). El determinismo
queda garantizado por construcción; los tests construyen `ChainState` a mano.

### 1.1 Nuevos archivos

| Archivo | Contenido |
|---|---|
| `Systems/Players/Planner.fs` | Módulo puro `Planner`: `ChainState`, generadores, búsqueda best-first acotada, `value` (evaluador) |
| `Contracts/Frames.fs` o `Contracts/World.fs` | `ChainPlan` + `TeamWorld.ActivePlan : ChainPlan option` — **único toque a contracts** (aditivo, no rompe firmas) |

### 1.2 Config (disciplina `BalanceConfig`)

| Dónde | Campo | Default sugerido | Qué hace |
|---|---|---|---|
| Nueva sección `PlannerConfig` (11ª, en `BalanceConfig`) | `MaxChainLength` | 4 | Profundidad máx. de la cadena (incl. 1ª acción) |
| | `MaxEvaluateLimit` | 500 | Presupuesto de estados evaluados (HELIOS) |
| | `Level1DribbleDirections` | 16 | Direcciones muestreadas para dribble de 1er nivel |
| | `LaterDribbleDirections` | 8 | Idem para niveles siguientes (HELIOS: 8×3) |
| | `PlanTTL` | 480 subticks | Vida del `ActivePlan` (misma ventana que `CommittedUntil`) |
| `Weights.Planner` (nuevo, ML-tunable, en `WeightSections.generated`) | `ProgressionWeight` | ~1.0 | Valor del avance del balón (`ball.x` hacia portería rival) |
| | `ShootValueWeight` | ~1.0e6 relativo | Bono si desde el estado final hay tiro (`xGValue`) |
| | `DangerBase[]` | `[20,10,5,3,2,1,0]` | Penalización por distancia al rival (HELIOS, indexado por `min(dist,6)`) |
| | `DangerZoneWeights` | `1.0 / 0.5 / 0.25` | Escala de peligro por zona (`x<−20 / x<20 / resto`) |
| | `PossessionSecurityWeight` | ~0.3 | Valor de que el balón termine en receptor seguro |

Regla 4 de AGENTS.md: todo número de comportamiento vive acá, nada hardcodeado.

---

## 2. El módulo puro `Planner`

### 2.1 `ChainState` — la proyección

El planner nunca ve `World`; recibe una snapshot inmutable construida una vez por
tick de decisión por `PlayerDecision`:

```fsharp
type ChainState =
    { BallX : float<meter>; BallY : float<meter>
      BallVx : float<meterPerSecond>; BallVy : float<meterPerSecond>
      CarrierSlot : int
      Side : ClubSide
      OurPos : (int * float<meter> * float<meter>)[]   // slot, x, y
      OppPos : (int * float<meter> * float<meter>)[]   // rivales activos (sin GK)
      OffsideLine : float<meter>
      Dir : float }                                    // goalDirection

/// Resultado de un paso de simulación de cadena
type ChainStep =
    { State : ChainState        // estado proyectado tras la acción
      Actions : int list        // códigos de acción ejecutados (1=pase,2=tiro,3=drible,4=clear)
      Value : float }
```

Regla anti-redundancia: la proyección se construye con los helpers existentes
(`MatchWorld.posX/posY`, `activeSlots`, `offsideLine`, `opponentsNear`) y se
descarta al terminar — **no** se guarda en `World`.

### 2.2 Búsqueda best-first acotada

```
search : PlannerConfig -> PlannerWeights -> ChainState -> ChainStep list
```

1. Nodo raíz = estado actual, profundidad 0.
2. Cola de prioridad por `value` (mayor primero), acotada a `MaxEvaluateLimit`
   expansiones totales.
3. **Nivel 1** (path vacío): los mismos candidatos que hoy (`passOptionsFor` +
   shot + cross + dribble + clear) — el plan NO cambia el menú de opciones.
4. **Niveles siguientes**: pases re-filtrados (`PassIntercept.feasible`) y
   dribble simple con `LaterDribbleDirections` (HELIOS `ActGen_SimpleDribble`).
5. Expansión de un pase: proyecta el balón a su punto de recepción
   (física cerrada, mismo modelo que `PassIntercept`/`InterceptTable`), mueve el
   receptor al punto de control, avanza el tick simulado. **Nunca toca `World`.**
6. La mejor cadena de cada candidato de 1er nivel = su `score_cadena`.

### 2.3 Evaluador `value` (port de HELIOS con piezas existentes)

```fsharp
value (w : PlannerWeights) (s : ChainState) : float =
    let xg = xGValue-equivalente s                       // reusar la fórmula de Decision.fs
    w.ProgressionWeight * (ball.x / HalfLength)          // avance
  + w.ShootValueWeight  * (si can_shoot desde s)         // bono de tiro
  + w.PossessionSecurityWeight * (receptor más cercano, rival más lejano)
  - w.DangerBase[min(opponentsNear s, 6)] * w.DangerZoneWeights[zona]
```

- `can_shoot` reusa `xGValue` (Decision.fs:14) — la creencia del portador y el
  valor del tiro ya están unificados por diseño.
- El peligro se toma como el **máximo a lo largo de la cadena** (HELIOS
  `action_chain_graph.cpp:414-447`): una jugada que termina en zona de presión
  vale menos aunque el camino sea libre.

### 2.4 El `ActivePlan` y la coordinación del receptor

```fsharp
type ChainPlan =
    { FutureReceivers : (int * float<meter> * float<meter>)[]  // slot, punto de recepción, tick esperado
      ExpiresAt : int<subtick> }
```

- `PlayerDecision.step` lo escribe junto al marker Phase 200 (mismo tick).
- `PlayerIntent.step` (mismo sistema de siempre): además de `receiveIntent`
  (que ya corre al punto de llegada del pase en vuelo), un branch nuevo — si el
  slot aparece en `ActivePlan.FutureReceivers`, recibe intent `ExecuteRun` hacia
  su punto de recepción proyectado. **Extensión de ~10 líneas, no sistema nuevo.**
- Se limpia en `PlayerDecision.step` cuando no hay portador o cambió el portador,
  o vence `ExpiresAt`. Determinista por construcción.
- **Cadencia (hecho documentado, no bug):** `PlayerIntent` corre antes que
  `PlayerDecision` en la tabla (SystemsTable.fs:43-57), así que el plan escrito
  en el tick T se consume en T+4. Un ciclo de latencia, igual que hoy con el
  pase en vuelo. Es parte del determinismo, no un defecto.

### 2.5 Eventos

**Ningún `EventKind` nuevo.** La cadena es razonamiento interno; sus efectos son
visibles a través de los `PassLaunched/PassCompleted/Shot` existentes — el
viewer verá secuencias de 2-3 pases que hoy no ocurren (regla 7 cumplida por sus
acciones constituyentes). Si algún día el UI necesita un overlay de "jugada
planeada", se agrega entonces; no hoy.

---

## 3. Qué NO se toca (anti-redundancia, la cláusula de "años")

| Pieza | Por qué queda intacta |
|---|---|
| `SystemsTable.fs` | Cero sistemas nuevos — `PlayerDecision` ya corre a la cadencia correcta (WhenLive, EveryN 4) |
| `Actions.fs` | Los markers Phase 200/201 y su ejecución no cambian: la cadena solo mejora el *score* que produce el marker |
| `PassIntercept.fs`, `InterceptTable.fs` | El planner consume `passOptionsFor` (que ya filtra con `feasible`) — **una** implementación de pase/intercepción para siempre |
| `Decision.xGValue` | El evaluador reusa la fórmula — creencia y valor nunca divergen |
| `Intent.fs` commitment | La maquinaria `CommittedUntil`/exit triggers ya da la histéresis; el plan solo añade un branch de recepción futura |
| `SetPieceRoutines` (Phase 201) | Las jugadas de balón parado siguen su camino; la integración futura se hace *con* el planner, no duplicándolo (ver §5) |

---

## 4. Fases (test-first, una feature por fase)

### A1 — Núcleo puro del planner (`Planner.fs`)

**Tests (`Tests/Systems/Planner.fs`):**
- `PlannerTests.fs` — una combinatoria de 2 pases seguidos de tiro produce una
  cadena cuyo score supera a cada pase individual; presupuesto respetado
  (contador de expansiones ≤ `MaxEvaluateLimit`); pruning: receptor marcado o
  pase infactible (`PassIntercept`) nunca se expande; determinismo: mismo estado
  → misma cadena.
- `PlannerEvaluatorTests.fs` — `value` monótono en avance de balón, monótono en
  peligro (rival cerca baja el valor), bono de tiro presente solo con tiro
  factible, peso de seguridad de posesión.

**Aceptación:** verde; `dotnet fantomas . --check`; sin tocar contracts todavía.

### A2 — Config y contracts (aditivos)

**Tests:** `ConfigTests` — `PlannerConfig` valida (`MaxChainLength` ≥ 2,
`MaxEvaluateLimit` > 0); `WeightMerge` carga `Weights.Planner` de
`weights_trained.json` (merge genérico, sin código nuevo).

**Cambios:** sección `PlannerConfig` en `BalanceConfig` + `Weights.Planner` en
`WeightSections.generated.fs` (regenerar) + `ChainPlan`/`ActivePlan` en
Contracts. Único PR que toca la zona congelada; aditivo, sin firmas rotas.

### A3 — Integración en `choose` + `ActivePlan` + receptor

**Tests:**
- `ChainIntegrationTests.fs` — el softmax de `choose` recibe scores de cadena y
  elige la acción que abre la mejor secuencia (estado preparado a mano); el
  marker Phase 200 escribe el mismo código de acción que hoy (1-5, sin códigos
  nuevos); `ActivePlan` escrito y consumido: el receptor del 2º momento recibe
  intent `ExecuteRun` (assert sobre `frame.Intent`).
- `ChainLifetimeTests.fs` — el plan muere con pérdida de posesión, cambio de
  portador y `ExpiresAt`.
- E2E (`ChainSequenceTests.fs`) — en 5 partidos seedeados, ocurre ≥ 1 secuencia
  de 3+ pases consecutivos del mismo equipo; ventanas estadísticas existentes
  (pases, goles, posesión) dentro de tolerancia ± — recalibrar si se mueven.

**Aceptación:** suite completa verde (209 + nuevos), determinismo intacto
(Stepper.DeterminismTests).

### A4 — Rendimiento y endurecimiento

**Tests:** `PlannerBudgetTests` — tick de decisión < 1 ms con 22 jugadores;
alocaciones por match dentro de baseline ± 20%; invariantes (`Invariants.check`)
sobre matches completos con cadenas activas.

**Aceptación:** perf targets del REWRITE_PLAN §3 intactos; docs/PERF.md si se
toca.

---

## 5. Puntos de extensión diseñados (el "que dure años")

El planner es un **motor de búsqueda de estados**, no un generador de pases: la
única varianza por contexto es el **conjunto de generadores**. Por eso estos
futuros usos NO son rewrites, son config de generadores + bias:

1. **Playbook (TRAINING_PLAYBOOK_PLAN.md)** — una jugada ensayada es un
   *template de cadena*: el evaluador recibe un bias positivo hacia los estados
   que cumplen sus momentos. Cadenas y jugadas comparten la misma superficie de
   scoring; el entrenamiento no agrega código al motor, agrega pesos.
2. **Cadenas defensivas** — el mismo `search` aplicado al lado sin balón: "quién
   recibirá el balón del rival y dónde" (marcaje anticipado, el paso 3 de la
   investigación RoboCup). El módulo planea *estados*, no solo pases.
3. **Balón parado** — el 1er nivel de una falta/córner puede usar el mismo
   evaluador con generadores de set piece; `SetPieceRoutines` sigue siendo el
   ejecutor (Phase 201), el planner solo propone.
4. **Distribución del GK** — el portero elige objetivo por valor de cadena con
   los mismos generadores de pase.
5. **Aprendizaje** — la formación aprendida de logs (paso 7 de la investigación)
   puede entrenar `DangerBase`/`ProgressionWeight` por equipo: el planner es el
   consumidor natural de pesos aprendidos, no un hardcode.

Regla de cierre: **toda nueva inteligencia de "mirar hacia adelante" entra por
el planner o por un bias en su evaluador** — nunca como un segundo motor de
búsqueda. Esa es la cláusula anti-redundancia.

---

## 6. Riesgos y mitigaciones

| Riesgo | Mitigación |
|---|---|
| Presupuesto (500 estados/tick) degrada el sim | `MaxEvaluateLimit` configurable + test de presupuesto en A4; estados son matemática barata (distancias), sin alocaciones en el núcleo |
| El juego se vuelve "demasiado perfecto" (RoboCup metagame) | El softmax con temperatura existente conserva varianza; `DangerBase` penaliza jugadas en zonas de presión; recalibración estadística en A3 |
| Latencia de cadencia receptor (T+4) se siente rara | Es la misma latencia que ya tiene la recepción de pases en vuelo; documentado como contrato, no como bug |
| Divergencia entre el simulador del planner y la física real | El planner reusa los modelos cerrados de `InterceptTable`/`PassIntercept` (la misma física que valida la factibilidad); test de coherencia: la proyección del planner coincide con la física del match dentro de tolerancia |
| Contract drift (toque a zona congelada) | Único PR aditivo (A2); nada de firmas rotas; regla del REWRITE_PLAN §5.6 |

---

## 7. Criterios de aceptación globales

1. `dotnet build` → 0 errores; fantomas limpio.
2. Tests nuevos verdes con `--filter "FootballEngine.Core.Systems.Planner"` y
   `--filter "FootballEngine.Core.Systems.PlayerDecision.Chain"`.
3. Suite completa verde; determinismo intacto.
4. Cero sistemas nuevos en `SystemsTable.fs`; cero cambios en `Actions.fs`;
   grep de duplicación: `passOptionsFor`/`xGValue` referenciados, no copiados.
5. Ventanas estadísticas dentro de tolerancia (recalibrar en A3 antes de cerrar).

---

*Documento de diseño — FootballEngine. Sigue las convenciones de `AGENTS.md`
(nada fuera de `BalanceConfig`, un solo lugar obvio, determinismo, tests espejo
de la estructura del código).*