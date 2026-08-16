# Plan de Posicionamiento — Adopción de RoboCup 2D (HELIOS) en FootballEngine

> Estado: **diseño** — este documento es el plan de trabajo. No se ha escrito código de esta fase (excepto el fix del bug de off-pitch, ya aplicado). Antes de implementar, leer el [estudio de RoboCup](#apendice-a--estudio-de-robocup-2d-helios) que lo fundamenta.

## 1. Resumen ejecutivo

El posicionamiento actual cumple su función pero tiene un defecto visible y una deuda de diseño:

- **Bug (ya reproducido):** algunos jugadores se salen de la cancha y quedan atascados afuera. Causa raíz: los targets (de `TeamShape` y de `PlayerIntent`) permiten estar a `±5 m` fuera del campo y, durante balones sueltos / saques, el jugador persigue el balón hasta afuera y se queda ahí porque el intento se re-consolida (`receiveIntent` escribe el target directamente, saltándose el clamp). Test lo confirma: `Away slot 6 X=57.5` con `HalfLength=52.5`.
- **Deuda de diseño:** la forma se genera con una heurística (`basePos + ballPull + LineHeight`), no con un modelo espacial por rol. RoboCup 2D (HELIOS base, usado por ~83% de los equipos) resuelve esto con una **Formación**: un mapa `posición del balón → 11 posiciones ideales` con interpolación, refinado por un **evaluador de campo** y **pares laterales dinámicos**.

Este plan adapta el enfoque RoboCup de forma completa pero **idiomática, determinista y sin redundancia**: reutiliza el contrato `team.Shape.TargetsX/Y` que ya existe (todo el pipeline `shape → intent → steering` queda intacto) y solo sustituye *cómo* se calcula la forma en la capa colectiva.

**Enfoque estructural:** **refactor de `TeamShape` → `TeamPosition`**. El sistema del scheduler que hoy se llama `TeamShape` (en `Systems/Team/Shape.fs`) se **renombra** a `TeamPosition` (`Systems/Team/Position.fs`) porque su responsabilidad deja de ser "dar forma heurística" y pasa a ser "posicionar al equipo": mapa de formación + evaluador + pares. Los tres módulos nuevos (`Formation`, `FieldEvaluator`, `LateralPairs`) son **helpers puros internos del mismo sistema**, NO sistemas nuevos en el scheduler. Se mantiene **un solo sistema** en `SystemsTable.fs` (el mismo lugar, nuevo nombre), y todo el pipeline colectivo→individual queda intacto.

**Decisiones tomadas (cuestionario):**
1. Adoptar la Formación completa tipo HELIOS (no solo el fix).
2. Evaluador de campo (estilo Voronoi de Gliders) para defensa y ataque.
3. Sí, pares laterales dinámicos (LB↔RB, LW↔RW).
4. Test estricto: ningún jugador activo a más de `±1 m` fuera de la línea.

---

## 2. Diagnóstico — por qué el posicionamiento falla hoy

### 2.1 El bug de off-pitch (ya corregido, base del plan)

Cadena causal completa, verificada en código:

1. **El clamp permite salir.** `Steering.fs:87-88` y `Shape.fs:93-94` clampan a `±(HalfLength + 5)` / `±(HalfWidth + 5)` — los jugadores *pueden* quedar 5 m fuera por diseño.
2. **Los targets pueden ser off-pitch.** `Shape.fs` permite targets de reposo a `±5 m` fuera; `Intent.pressSpot` y `receiveIntent` apuntan al balón / punto de llegada, que puede estar fuera (balón a `±5 m`, pase saliendo).
3. **El intento se consolida.** `Intent.fs:14` fija `CommittedUntil = tick + 480<subtick>`. Durante ese tiempo el jugador se mueve al target fuera y se queda.
4. **`receiveIntent` salta el clamp.** `Intent.fs:129-130` escribe `Intent.TargetX/Y` directamente (no pasa por `setIntent`), así que un pase que sale arrastra al receptor afuera.

**Fix aplicado** (requisito del plan):
- `MatchMath.pitchClampF32` / `pitchClamp`: clamp centralizado al campo en `Shared.fs`.
- `Shape.fs`: targets de reposo clampados al campo real (`±HalfLength`/`±HalfWidth`), no `+5`.
- `Intent.fs`: `setIntent` clamp centralmente; `receiveIntent` ahora también clamp.

> Nota: el clamp de *movimiento* en `Steering.fs` se deja en `+5` a propósito — permite una caza momentánea del balón sin jitter en la línea, pero como los targets quedan dentro, el jugador siempre vuelve al campo. Verificado por el test estricto `±1 m`.

### 2.2 La deuda de diseño: forma heurística, no modelada

`TeamShape.shapeTeam` calcula cada target como:

```
targetX = basePosX + LineHeight * shift * dir + ballPull * 0.3 * dir
targetY = basePosY * widthMul
```

Problemas:
- **`ballPull` es global:** mueve la *línea entera* hacia la X del balón con un solo factor (`0.3`). No distingue roles: un lateral y un delantero se desplazan igual, cuando en fútbol real el lateral sube por su banda y el delantero se posiciona en el área.
- **`LineHeight * shift` es lineal:** el "avance/retroceso" es el mismo escalar para los 10 jugadores de campo, multiplicado por un `phaseShift` de 4 valores. No hay noción de *dónde debe pararse cada rol para cada posición del balón*.
- **No hay separación de roles en el eje Y** salvo `widthMul` (escalar global) — los perfiles espaciales de lateral/central/extremo no emergen.

Esto es exactamente lo que la **Formación HELIOS** resuelve: en vez de una fórmula, un **mapa** `balón → 11 posiciones`, con cada rol definiendo su zona del campo para cada zona donde está el balón.

---

## 3. Arquitectura objetivo

### 3.1 Principio de integración: NO tocar el pipeline

El pipeline colectivo → individual actual es:

```
TeamDecide → TeamShape (escribe Shape.TargetsX/Y)
           → PlayerIntent (escribe Intent.TargetX/Y, lee Shape)
           → PlayerSteering (mueve al jugador)
```

La Formación + evaluador + pares viven **todos en la capa de forma colectiva**, y siguen escribiendo a `team.Shape.TargetsX/Y`. `PlayerIntent` y `PlayerSteering` **no cambian** (salvo el clamp del bug). Esto da:

- **Un solo lugar obvio** (principio 2 del AGENTS.md): "dónde se para el equipo" = `TeamPosition` (ex `TeamShape`).
- **Cero redundancia:** el contrato shape→intent→steering ya existe y se reutiliza tal cual.
- **Tests aislados:** la Formación, el evaluador y los pares son módulos puros y testeables por separado.

### 3.2 Módulos — refactor de `TeamShape` → `TeamPosition`

```
Systems/Team/
├── Position.fs           # (renombrado desde Shape.fs) sistema orquestador — UN sistema en el scheduler
│                          #   TeamPosition.step: Formation → LateralPairs → FieldEvaluator → Shape.Targets
├── Formation.fs          # (helper puro) mapa balón→posiciones + interpolación baricéntrica
├── FieldEvaluator.fs     # (helper puro) refinamiento estilo Voronoi
└── LateralPairs.fs       # (helper puro) pares laterales dinámicos
```

- **Rename completo:** `Shape.fs` → `Position.fs`, módulo `TeamShape` → `TeamPosition`, y el `Name = "TeamShape"` en `SystemsTable.fs:41` → `"TeamPosition"`. Se actualizan las referencias (4 llamadas `TeamShape.step` en `PositioningTests.fs`, y `ShapeTests.fs`).
- `Formation.fs`, `FieldEvaluator.fs`, `LateralPairs.fs` son **funciones puras** (sin `World`, solo toman datos que se les pasan). Esto los hace triviales de testear y respeta "agentes leen/escriben, no se llaman entre sí".
- `Position.fs` es el **único sistema del scheduler** (no se agrega ninguno — cero churn de scheduler, solo rename del `Name` y del `Step`).

### 3.3 Detalle por módulo

#### 3.3.1 `Formation.fs` — el mapa balón → posiciones (esencia HELIOS)

**Tipo:**
```fsharp
// Una muestra: con el balón en (bx,by), las 11 posiciones ideales (en metros, marco Home: ataca +X).
type FormationSample =
    { BallX: float<meter>
      BallY: float<meter>
      SlotX: float32[]
      SlotY: float32[] }

// La formación: un conjunto de muestras + la consulta por posición del balón.
type Formation =
    { Samples: FormationSample[] }
```

**Consulta — interpolación baricéntrica sobre los 3 anclas más cercanas** (equivalente práctico de la Delaunay de HELIOS, sin librería de triangulación):

```
dado (bx, by):
 1. hallar las 3 muestras más cercanas al balón (distancia euclídea)
 2. calcular pesos baricéntricos (a,b,c) con a+b+c=1
 3. para cada slot s: slotX_s = a·A.x_s + b·B.x_s + c·C.x_s   (idem Y)
 4. clamp al campo (MatchMath.pitchClamp)
```

**De dónde salen las muestras.** Reutilizar la base ya existente (no redundancia):
- La muestra "neutral" (balón en el centro) = las `BasePositions` del lineup, que ya vienen de `FormationData.getFormation` (ver `Domain/FormationData.fs` — 17 formaciones con coordenadas normalizadas) → `MatchContextBuilder.metersX/metersY`.
- Las muestras "por zona del balón" se generan **determinísticamente a partir de la base** con reglas por rol (no datos duplicados): por ejemplo, con el balón en el tercio ofensivo, el striker sube a `goalX - k`, los laterales suben por sus bandas, el GK queda en `ownGoalX`; con el balón atrás, el equipo compacta hacia atrás. Estas reglas se calibran con pesos en `BalanceConfig` (ver §3.4).

> **Por qué baricéntrica sobre 3 anclas y no una grilla fija + bilineal:** una grilla fuerza una resolución fija; la baricéntrica sobre las 3 anclas más cercanas admite *añadir muestras* por formación (p. ej. 5 anclas: centro, tercio ofensivo, tercio defensivo, banda izquierda, banda derecha) sin cambiar la estructura. Es la misma técnica de HELIOS (`formation_dt.cpp:60-180`) y mantiene el código simple y determinista.

**Determinismo:** no usa RNG; solo geometría. Misma posición de balón → mismos targets.

#### 3.3.2 `FieldEvaluator.fs` — refinamiento estilo Voronoi (Gliders)

RoboCup defensivo (Gliders v1.4) elige posiciones maximizando utilidad: **lejos de oponentes** (vértices de Voronoi) y **cerca de compañeros** (sin amontonarse). Adaptación idiomática:

**Idea:** el target de la Formación es un punto base. Se muestrean `N` candidatos alrededor de ese punto (una pequeña nube de offsets) y se puntúa cada uno:

```
score(c) = w_teammate · spread(c)      // quiero cierta distancia mínima a compañeros (no amontonarme)
         + w_opponent · avoid(c)        // quiero lejos de oponentes (estilo Voronoi)
         + w_goal     · progress(c)     // quiero progreso hacia el arco rival (o cobertura del propio)
```

donde:
- `spread(c) = clamp( (distMinCompañero - dist(c)) / radio , 0, 1 )` — recompensa estar a ≥ `distMinCompañero` del compañero más cercano.
- `avoid(c) = dist(c, oponenteMásCercano) / radio` — recompensa estar lejos del oponente más cercano.
- `progress(c)` = avance en X hacia el arco rival (si ataca) o hacia `ownGoalX` (si defiende).

Se elige el candidato de mayor score. Es un **evaluador de campo** puro (función de utilidad), la misma filosofía que Gliders, sin necesidad de calcular el diagrama de Voronoi explícito — el término `avoid` aproxima el comportamiento Voronoi (elegir el punto que maximiza distancia al oponente más cercano).

**Configuración (en `BalanceConfig`):** pesos `w_teammate`, `w_opponent`, `w_goal`, radio de búsqueda, nº de candidatos. Todos calibrables, nada hardcodeado.

**Costo:** `N` candidatos × `O(11+11)` distancias por jugador, cada N ticks (se reusa la cadencia de `TeamShape`, `EveryN 4<tickDelta>`). En el peor caso ~10 jugadores × 5 candidatos × 22 distancias = ~1.100 distancias cada 4 ticks — despreciable.

#### 3.3.3 `LateralPairs.fs` — pares laterales dinámicos (HELIOS position pairs)

HELIOS define *position pairs*: pares simétricos (LB↔RB, LW↔RW) que pueden **intercambiarse** para balancear cobertura sin romper la formación.

**Regla:** para cada par `(sL, sR)` simétrico en Y, calcular dos opciones:
- **Opción A:** `sL` toma el target izquierdo, `sR` el derecho.
- **Opción B:** invertidos.

Elegir la opción con menor **costo total de desplazamiento** desde la posición actual (o, en defensa, la que mejor cubre al oponente más cercano). Se aplica solo si la ganancia supera un umbral `SwapThreshold` (evita que el equipo "flipee" cada tick — coherencia con la histéresis de `TeamDecide`).

**Determinismo:** regla determinista (menor costo con desempate fijo por slot), sin RNG.

**Nota de coherencia con los tests de rol:** el swap es entre *pares simétricos*, así que las propiedades geométricas (GK atrás, striker adelante, extremos en bandas) se conservan — los tests de `PositioningTests` (rol-geometría) siguen siendo válidos. Solo cambia *quién* (slot) ocupa cada banda, no la geometría general.

### 3.4 `BalanceConfig` — nuevos pesos (nada fuera de config)

Se extiende `TeamConfig` (o un nuevo `PositioningConfig`) con:

| Campo | Rol | Default sugerido |
|---|---|---|
| `FormationAnchors` | nº/posición de anclas de la formación (o regla de generación) | 5 anclas (centro + 4 zonas) |
| `EvalTeammateWeight` | `w_teammate` del evaluador | 0.3 |
| `EvalOpponentWeight` | `w_opponent` (estilo Voronoi) | 0.5 |
| `EvalGoalWeight` | `w_goal` (progreso/cobertura) | 0.4 |
| `EvalSearchRadius` | radio de la nube de candidatos | 4 m |
| `EvalCandidateCount` | nº de candidatos muestreados | 5 |
| `EvalMinTeammateDist` | `distMinCompañero` objetivo | 5 m |
| `LateralSwapThreshold` | umbral de ganancia para el swap | 1.5 m |

> Se siguen AGENTS.md: todo número de comportamiento vive en `BalanceConfig` con nombre descriptivo; se añade a `defaultConfig` y se valida (ver §5).

---

## 4. Flujo final de `TeamPosition.step` (orquestador, ex `TeamShape.step`)

```
TeamPosition.step (por equipo, cada 4 ticks):
  1. Formation.query(ball) → targetsBase[11]            // mapa balón→posiciones
  2. Aplicar directiva (reusar Directive.Params):        // ataque/defensa desplazan la forma
       ataque  → targetsBase += nudge hacia goalX
       defiende→ targetsBase += nudge hacia ownGoalX
     (esto reemplaza el ballPull/LineHeight/phaseShift heurístico)
  3. LateralPairs.solve(frame, targetsBase) → targetsPares  // swap si gana
  4. FieldEvaluator.refine(side, targetsPares, world) → Shape.TargetsX/Y
     (puntuar candidatos: cerca compañeros, lejos rivales, progreso)
  5. clamp final al campo (MatchMath.pitchClamp)
```

Los pasos 3 y 4 pueden reordenarse: aplicar pares antes del evaluador (recomendado: el evaluador refina sobre el target ya asignado al slot).

---

## 5. Orden de implementación y tests

| Fase | Qué | Tests | Criterio |
|---|---|---|---|
| **P0 (hecho)** | Fix off-pitch (clamp centralizado) | `PositioningTests.off-pitch` (±1 m estricto) | verde |
| **P1** | `Formation.fs` (helper puro: tipo + query baricéntrica + generación de muestras desde `BasePositions`/`FormationData`) | `FormationTests`: interpolación correcta, determinismo, geometría por rol para cualquier posición de balón | `dotnet build` 0 err, filtro `Systems.Team.Formation` verde |
| **P2** | `FieldEvaluator.fs` (helper puro: utilidad de candidatos) | `FieldEvaluatorTests`: el candidato evita oponentes, se separa de compañeros, no rompe GK | verde |
| **P3** | `LateralPairs.fs` (helper puro: swap por costo) | `LateralPairsTests`: swap reduce viaje, queda simétrico sin ganancia, determinista | verde |
| **P4** | **Refactor `TeamShape` → `TeamPosition`** + integración de los 3 helpers + `BalanceConfig` + validación | `PositioningTests` (rol-geometría + off-pitch ±1 m), `ShapeTests`→renombrado `PositionTests` (regresión), `ConfigTests` (pesos validados) | suite completa verde + `DeterminismTests` intacto |
| **P5** | Calibración: correr partidos, verificar volúmenes (goles, posesión, offsides) y ajustar pesos en `Training`/`weights.json` | — | volúmenes en rango |

**Criterios de aceptación globales (reuso de REFINEMENT_PLAN):**
1. `dotnet build` → 0 errores, 0 warnings.
2. Tests nuevos verdes con filtro `FootballEngine.Core.Systems.Team.*`.
3. Suite completa verde (determinismo intacto: `Stepper.DeterminismTests`).
4. Fantomas limpio (`dotnet fantomas . --check`).
5. Si cambia volúmenes, recalibrar antes de cerrar.

---

## 6. Qué NO hace este plan (anti-ámbito)

- **No toca `PlayerIntent` / `PlayerSteering`** salvo el clamp del bug. La Formación es *colectiva*; la elección de acción individual (pasar/driblar/tirar) queda intacta.
- **No agrega sistemas al scheduler.** `TeamShape` se refactoriza/renombra a `TeamPosition`; los 3 helpers son internos del mismo sistema, no entidades del scheduler.
- **No introduce librería de triangulación / Voronoi externa.** Todo es geometría propia simple, determinista y sin dependencias nuevas (coherente con `REWRITE_PLAN` que eliminó deps).
- **No duplica `FormationData`/`BasePositions`:** la muestra central REUTILIZA los datos ya existentes; las muestras por zona se generan por reglas calibradas, no se escriben a mano.
- **No es un marcaje hombre-a-hombre explícito** (se eligió evaluador Voronoi en el cuestionario). El término `avoid` del evaluador aproxima el efecto sin la complejidad de asignación de marcadores.

---

## Apéndice A — Estudio de RoboCup 2D (HELIOS)

### A.1 La referencia: HELIOS base / agent2d

`agent2d` (HELIOS base, C++) es el código base estándar de la liga RoboCup Soccer Simulation 2D — usado por ~83% de los equipos (2013). Su sistema de posicionamiento es el **Sistema de Formación** (`rcsc/formation/formation_dt.cpp`).

### A.2 El Sistema de Formación

Definición: un **mapa** `punto focal (típicamente el balón) → posición deseada de cada uno de los 11 jugadores`.

**Dos implementaciones:**
- **FormationDT (Delaunay):** puntos de entrenamiento donde cada uno guarda `(balón, 11 posiciones)`. Se triangula el campo con **triangulación de Delaunay**; con el balón en cualquier punto, se encuentra el triángulo que lo contiene y se **interpola linealmente** (estilo Gouraud shading) entre los 3 vértices.
- **FormationStatic:** posiciones fijas que ignoran el balón (para pruebas/escenarios simples).

**Ventaja clave:** cada rol tiene un **perfil espacial propio por zona del campo** — no es un desplazamiento global; cada jugador "sabe" dónde pararse para cada posición del balón.

**Autoría:** Akiyama & Noda, "Multi-Agent Positioning Mechanism in the Dynamic Environment" (RoboCup 2007), y "Team Formation Construction Using a GUI Tool" (2006) — la herramienta `fedit2` edita estas formaciones visualmente.

### A.3 Roles y pares

- **Roles dinámicos:** `setRoleName`/`setRoleType` — el rol no es fijo por número de camiseta.
- **Position pairs:** pares simétricos (LB↔RB, etc.) que pueden **intercambiarse** (`setPositionPair`) para balancear cobertura — base de nuestros `LateralPairs`.

### A.4 Marco de 3 capas (decisión)

`agent → role → behavior`:
- **Agent:** decide estrategia + rol.
- **Role:** decide comportamiento táctico según la situación (marcar, presionar, apoyar, cubrir).
- **Behavior:** ejecuta la acción (dash, kick, tackle).

Nuestro pipeline ya es análogo: `TeamDecide`(agent/estrategia) → `PlayerIntent`(role) → `PlayerSteering`+`Actions`(behavior). La Formación de HELIOS encaja en la capa *colectiva* (TeamShape), entre la estrategia y el rol individual.

### A.5 Defensa avanzada: evaluador de campo (Gliders)

**Gliders2D** (Prokopenko & Wang) mejoró agent2d con un **evaluador de campo**: en vez de una sola métrica, puntúa múltiples puntos candidatos y elige el mejor según:
- **Voronoi:** el mejor punto está **lejos de oponentes** (los vértices del diagrama de Voronoi de los oponentes maximizan la distancia al oponente más cercano) y **cerca de compañeros**.
- **Offside line / líneas clave:** candidatos en intersecciones útiles.

Impacto medido (vs agent2d): el posicionamiento Voronoi subió el goal difference de `1.616 → 2.387` (Tabla 1 de Gliders2D). Nuestro `FieldEvaluator` adapta este término `avoid` (lejos de oponentes) sin computar el Voronoi explícito.

### A.6 Relevancia y mapeo con nuestro código

| Concepto RoboCup | Fuente | Nuestro equivalente |
|---|---|---|
| Formación (mapa balón→posiciones) | HELIOS FormationDT | `Formation.fs` (helper de `TeamPosition`) |
| Interpolación Delaunay/Gouraud | HELIOS | Interpolación baricéntrica sobre 3 anclas |
| Position pairs dinámicos | HELIOS | `LateralPairs.fs` (helper de `TeamPosition`) |
| Evaluador de campo (Voronoi) | Gliders | `FieldEvaluator.fs` (helper de `TeamPosition`) |
| Marco agent/role/behavior | HELIOS | `TeamDecide`/`PlayerIntent`/`Steering+Actions` (ya existe) |

---

## Apéndice B — Evidencia del bug (referencia para tests)

Reproducido con el test `PositioningTests.off-pitch` (muestreo mid-match):

```
Away slot 6 X=57.5 off-pitch at tick 80000   (HalfLength=52.5 → 5 m fuera)
```

El test original (solo estado final) **no** lo atrapaba porque al terminar el partido el balón vuelve al campo; por eso el test estricto **muestrea durante el partido** (cada `1000<tickDelta> × 40<subtick>` = 40.000 subtick) y verifica que ningún jugador activo exceda `±1 m` fuera de la línea. Con el fix aplicado, los targets quedan dentro y el jugador siempre vuelve.

---

*Documento de diseño — FootballEngine. Sigue las convenciones de `AGENTS.md` (nada fuera de `BalanceConfig`, un solo lugar obvio, determinismo, tests espejo de la estructura del código).*
