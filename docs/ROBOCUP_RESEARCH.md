# Investigación RoboCup 2D — HELIOS + Gliders/Cyrus

> Estado: **investigación** — este documento sintetiza el estudio de código real de
> RoboCup Soccer Simulation 2D, hecho sobre el código fuente clonado en
> `/tmp/opencode/robocup/` (`helios-base`, `librcsc`, `Cyrus2DBase`). Objetivo:
> adoptar lo que hace a un simulador de fútbol realista (80–90%) y divertido,
> idiomático en F# y de alto rendimiento, para superar a Football Manager.

## Fuentes

| Repo | Rol | Notas |
|---|---|---|
| `helios-base/helios-base` | HELIOS base / agent2d | ~83% de la liga usaba este base |
| `helios-base/librcsc` | Librería base reutilizable | física, geometría, world model |
| `Cyrus2D/Cyrus2DBase` | Gliders2D v2.6 + Cyrus | campeón RoboCup 2016 (Gliders) y 2021 (Cyrus) |

---

## Las 6 ideas más transferibles (ordenadas por ROI para realismo 80–90%)

### 1. Posicionar al balón PREDICHO, no al balón actual

HELIOS no consulta la formación con la posición actual del balón: usa
`ball.inertiaPoint(ball_step)` donde `ball_step = min(selfStep, teammateStep,
opponentStep)` — el balón en el momento en que alguien lo intercepta
(`strategy.cpp:572-585`). Física cerrada O(1):

```
inertia_n_step_point(p, v, n, decay) = p + v·(1 − decayⁿ)/(1 − decay)
```

El "quién va al balón" se resuelve con pasos de interceptación
(`selfStep`/`teammateStep`/`opponentStep`); ese mismo número decide la situación
(ataque/defensa), quién presiona, a quién pasar y dónde pararse. No hay líder:
cada agente lee la misma tabla y decide por reglas locales.

> **FootballEngine hoy:** `Formation.query` recibe la posición actual del balón
> (`Position.fs:137-139`). Cambiar a balón predicho en el paso de intercepción +
> un `InterceptTable` ligero es el cambio de mayor realismo por dólar.

### 2. La intercepción como "moneda" de coordinación

La `InterceptTable` calcula cuántos ciclos tarda cada jugador en llegar (turnos +
dashes con stamina simulada). De ahí salen:
- **Pressing:** Gliders cambió el umbral HELIOS `self_min < opp_min + 3` por
  `pressing` por rol: `13` default, `7` medios, `23` laterales
  (`bhv_basic_move.cpp:158-175`).
- **Situación:** `opp_min ≤ our_min − 2 → Defense`, etc. (`strategy.cpp:493-543`).
- **Tackle:** solo si `prob ≥ 0.8` (campo rival) / `0.5` (campo propio).

> El salto es modelar **tiempo de llegada con física** (aceleración, velocidad
> máx, giro `asin(control/dist)`) en vez de distancia euclídea, y exponerlo como
> `selfStep/teammateStep/opponentStep` globales.

### 3. Pases con modelo de tiempo del interceptor + riesgo

Gliders añade a HELIOS los **pases arriesgados** explotando la geometría del
offside (`strict_check_pass_generator.cpp`): un pase es factible si
`o_step + risk ≤ step`, con `risk ∈ {1,2}` ciclos según si cruza la línea de
offside en diagonal (el rival "atrapado" no puede arrancar). Modelan al
interceptor con **turnos + dashes + área de control**, no distancia.

### 4. Evaluador de campo Voronoi (Gliders) — jugar al espacio

Gliders computa el **Voronoi exacto de los oponentes** (vía Delaunay,
circuncentros) y puntúa:

```
score(p) = d_min_opp(p) − d_min_our(p)
```

El mejor punto es el "hueco": lejos del rival más cercano, cerca del nuestro.
Colocan a los 3 delanteros en las **intersecciones de los segmentos de Voronoi
con la línea de offside** (`strategy.cpp:720-939`).

> **FootballEngine hoy:** `FieldEvaluator.refine` puntúa `spread/avoid/progress`
> sobre una nube circular. La mejora Gliders: score base `d_opp − d_our` y
> candidatos = vértices Voronoi + cruces con la línea de offside.

### 5. Trampa de offside colectiva

Gliders coordina: (a) clamp global de home-positions a `offsideLineX − 1`
(`strategy.cpp:942-963`); (b) delanteros sobre los cruces Voronoi de la línea;
(c) **step-up individual** solo si el 2º defensor cubre tu espalda
(`bhv_basic_move.cpp:187-224`); (d) línea defensiva (2º defensor más atrasado,
`updateOurDefenseLine`) difundida por el portero.

### 6. Stamina como restricción táctica

Gliders modula `dash_power` por rol/zona: recuperación bajo `50%` stamina
(potencia `my_inc − 25`), sprint máx. en zonas clave, `unmarking` solo con
`stamina > 6000`. La fatiga **cambia el comportamiento**, no solo la velocidad.

---

## Qué NO copiar (anti-ámbito)

| Módulo RoboCup | Por qué NO | Alternativa FootballEngine |
|---|---|---|
| Localización (partículas/sectores) | Sim omnisciente y determinista, sin ruido | Nada |
| Protocolo say/hear + round-robin | Un solo proceso, estado compartido | `SimState` ya es el canal |
| Delaunay/Voronoi exactos | Más caro de mantener; IDW de 3 anclas basta | Mantener `Formation.query` |
| `ServerParam`/física de 10ms | Engine más grueso (subtick 1/40s) | Reutilizar `BalanceConfig` |
| Re-planificación best-first (500 evals/ciclo) | Controlar presupuesto en F# | Best-first acotado |

---

## Plan de adopción sugerido (mapeado a archivos)

1. **`InterceptTable`** (nuevo helper puro en `Systems/`): `stepsToReach` con
   modelo cerrado (serie geométrica). Caché por tick.
2. **`Position.fs`**: consultar formación con `ball.inertiaPoint(minStep)`.
3. **`Pass.fs`**: factibilidad por ciclos del interceptor + `risk` de offside
   (config en `BalanceConfig`).
4. **`FieldEvaluator.fs`**: score base `d_opp − d_our`; candidatos = círculo +
   cruces con línea de offside; offside line como estado del `WorldModel`.
5. **`Steering`/`PlayerAgent`**: `stepsToReach` como umbral para press/tackle/
   ir-al-balón.
6. **Línea defensiva** (2º defensor) y **offside line** como magnitudes
   colectivas en `SimState`; trampa de offside en `Position.fs` + step-up.

---

*Documento de investigación — FootballEngine. Sigue las convenciones de
`AGENTS.md` (nada fuera de `BalanceConfig`, un solo lugar obvio, determinismo,
tests espejo de la estructura del código).*