# Plan de Adopción RoboCup — InterceptTable y coordinación por pasos

> Estado: **implementado (F1–F6)** — todas las fases completadas test-first, una
> feature por agente. Basado en [ROBOCUP_RESEARCH.md](ROBOCUP_RESEARCH.md).
> Suite completa: 209/209 verde, determinismo intacto. Objetivo: realismo
> 80–90% + diversión, superando a Football Manager.

## 1. Resumen ejecutivo

El motor ya tiene posicionamiento táctico (Formación + evaluador + pares, ver
`POSITIONING_PLAN.md`). Lo que falta para el salto de realismo es la **moneda de
coordinación de RoboCup**: el *paso de interceptación* (`stepsToReach`). HELIOS
usa ese único número para decidir quién va al balón, presionar, pasar y
posicionar. Este plan lo introduce como **helper puro**, sin tocar el pipeline.

**Enfoque estructural:** helpers puros en `Systems/`, un nuevo estado en
`SimState` (caché por tick), y consumidores que leen ese número. Cero sistemas
nuevos en el scheduler (se reusa la cadencia de los existentes).

## 2. Fases (test-first, una feature por agente)

### F1 — `InterceptTable` (helper puro de física cerrada) ✅

**Qué:** módulo puro que calcula `stepsToReach : World -> ClubSide -> slot -> int`
y los agregados `selfStep / teammateStep / opponentStep`.

**Modelo** (adaptación de `intercept_simulator_self_v13` + `inertia_n_step_point`):

```
given ball (px,py,vx,vy) y jugador (x,y, speed):
  t_steps = el menor n tal que dist( ball.inertiaPoint(n), playerAt(n) ) <= controlRadius
  playerAt(n) = x + v·(1 − decay^n)/(1 − decay)   // dash dir ya alineado
  controlRadius = kickableArea ≈ 2.5 m (config)
```

Versión idioma F#: serie geométrica cerrada, sin simular ciclo a ciclo (salvo
loop de pasos hasta convergencia, acotado a 50). Determinista, sin RNG.

**Arquitectura:**
- `Systems/Team/InterceptTable.fs` — helper puro (función por equipo, no sistema).
- Estado nuevo en `World`/`TeamWorld` (o `Blackboard`): caché `SelfStep` /
  `TeammateStep` / `OpponentStep`, refrescada con cadencia existente.

**Tests (`InterceptTableTests.fs`):**
1. balón estático → jugador cerca tiene step bajo, lejano alto (monotonía).
2. balón en movimiento → predice punto futuro (mayor velocidad = menor step
   para un jugador delante).
3. determinismo: mismo world → mismos steps.
4. GK excluido / slot sidelined no contribuye.
5. `opponentStep` mira al rival más rápido, no al más cercano.

**Consumidor 1 (integración):** `Position.fs` consulta la formación con
`ball.inertiaPoint(minStep)` en vez de la posición actual del balón.

### F2 — Pressing y "ir al balón" por pasos ✅

**Qué:** `Steering.fs` / `PlayerIntent` usa `selfStep <= mateStep && selfStep <
opponentStep + pressing` para decidir quién persigue el balón suelto, en vez de
distancia euclídea. Umbral `pressing` por rol en `BalanceConfig` (default 13,
medios 7, laterales 23 — Gliders).

**Tests:** con balón suelto, el slot con menor step persigue y los demás no;
el pressing alto hace perseguir aunque un rival llegue antes.

### F3 — Situación ofensiva/defensiva por pasos ✅

**Qué:** `Decide.fs` / directiva usa `opp_min <= our_min − 2 → Defend`,
`our_min <= opp_min − 2 → Attack` (HELIOS `updateSituation`). Reemplaza la
heurística por zona.

**Tests:** balón en nuestro tercio con rival lejos → Attack; balón suelto
disputado → Neutral; etc.

### F4 — Pase con modelo del interceptor + riesgo ✅

**Qué:** `Pass.fs` evalúa factibilidad por `o_step + risk <= step`. `risk`
configurable (default 0; 1–2 al cruzar línea de offside en diagonal). Modelo de
interceptor: `estimate_min_reach_cycle` (componente perpendicular a la
trayectoria / velocidad máx).

**Tests:** pase con oponente que llega antes → rechazado; con `risk=2` y
geometría de offside → aceptado; determinista.

### F5 — Offside line y línea defensiva como estado ✅

**Qué:** exponer `offsideLine` (ya existe en `MatchWorld`) y **línea defensiva**
(2º defensor más atrasado, `updateOurDefenseLine`) como magnitudes colectivas;
clamp de home-positions a `offsideLineX − 1` en `Position.fs`.

**Tests:** la línea defensiva = 2º defensor; los targets de reposo nunca superan
`offsideLineX − 1` en defensa.

### F6 — Evaluador Voronoi `d_opp − d_our` ✅

**Qué:** `FieldEvaluator.refine` puntúa candidatos con
`score = d_min_opp − d_min_our` (además de los términos actuales), y los
candidatos incluyen cruces con la línea de offside. Pesos en `BalanceConfig`.

**Tests:** con dos rivales simétricos, el candidato elegido está en el hueco
(lejos de ambos); determinista; GK no se mueve.

---

## 3. Orden de implementación

| Fase | Depende de | Tests |
|---|---|---|
| F1 | — | `InterceptTableTests` + `PositioningTests` (regresión) |
| F2 | F1 | `InterceptPressingTests` |
| F3 | F1 | `DecideTests` (extensión) |
| F4 | F1 | `PassInterceptTests` |
| F5 | — | `OffsideLineTests` |
| F6 | F5 | `FieldEvaluatorTests` (extensión) |

## 4. Criterios de aceptación globales

1. `dotnet build` → 0 errores.
2. Tests nuevos verdes con filtro `FootballEngine.Core.Systems.*`.
3. Suite completa verde (determinismo intacto: `Stepper.DeterminismTests`).
4. Fantomas limpio (`dotnet fantomas . --check`) sobre los archivos tocados.
5. Volúmenes en rango (goles, posesión, offsides) — recalibrar antes de cerrar.

---

*Documento de diseño — FootballEngine.*