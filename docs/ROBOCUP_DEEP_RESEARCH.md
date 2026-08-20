# Investigación RoboCup profunda — qué aprendemos, cómo superar a FM, realismo+diversión

> Estado: **investigación** — síntesis del análisis de código real de RoboCup Soccer
> Simulation 2D, hecho sobre el código fuente clonado en `/tmp/opencode/robocup/`
> (`helios-base`, `Cyrus2DBase`, `librcsc`, `rcssserver`). Complementa
> [ROBOCUP_RESEARCH.md](ROBOCUP_RESEARCH.md) (capa 1, ya implementada como F1–F6)
> y [ROBOCUP_ADOPTION_PLAN.md](ROBOCUP_ADOPTION_PLAN.md). Objetivo: superar a
> Football Manager con realismo (80–90%) y diversión a la vez.

## Fuentes

| Repo | Rol | Notas |
|---|---|---|
| `helios-base/helios-base` | HELIOS / agent2d | Base del ~83% de la liga; campeón 2018–2024 (7 títulos consecutivos) |
| `Cyrus2D/Cyrus2DBase` | Gliders2D v2.6 + Cyrus | Campeón 2016 (Gliders) y 2021 (Cyrus); fork de HELIOS + físicas de librcsc |
| `helios-base/librcsc` | Librería base reutilizable | Física, geometría, world model, intercept simulation |
| `rcsoccersim/rcssserver` | Servidor oficial | Física y reglas reales (stamina/effort, offside, kickable area) |
| Papers | HELIOS2024 (Springer, 2025), Cyrus2D (arXiv 2401.03406), Cyrus2D Base (arXiv 2211.08585) | Contexto de resultados y método |

---

## Parte 1 — Lo que ya adoptamos (F1–F6) y el siguiente salto

La capa 1 ya está implementada: **pasos de interceptación como moneda de
coordinación** (`InterceptTable.fs`), pressing por rol, situación por pasos, pase
contra el interceptor más rápido, línea de offside colectiva, Voronoi `d_opp − d_our`.
El análisis a fondo del código revela la siguiente capa, ordenada por ROI.

### 1. Planificación multi-paso (action chains) — el mayor salto de realismo restante

HELIOS no decide 1 acción: arma **cadenas de hasta 4 acciones con búsqueda
best-first acotada** (`planner/action_chain_graph.cpp:59-66`:
`DEFAULT_MAX_CHAIN_LENGTH = 4`, `DEFAULT_MAX_EVALUATE_LIMIT = 500`). El portador
piensa "pase a X → X me devuelve → disparo", y solo ejecuta la primera acción de
la mejor cadena. Evaluador de estados (`src/player/sample_field_evaluator.cpp`):

```
base = ball.x + max(0, 40 − dist(ball, portería rival))
+1.0e6 si can_shoot_from, +5.0e5 si soy yo el portador
peligro = max sobre la cadena de {20,10,5,3,2,1,0}[min(distRival,6)]
          × {1.0 si x<−20, 0.5 si x<20, 0.25 resto}          (action_chain_graph.cpp:414-507)
```

`PlayerDecision.choose` (Decision.fs:153) es un softmax de **1 paso**. La
diferencia entre "el portador elige la mejor primera jugada" y "el portador ve
2-3 jugadas adelante" es la diferencia entre un motor de probabilidades y
fútbol. Barato en F#: best-first acotado a 500 estados cabe en el presupuesto
de un tick. **Este es el cambio #1 recomendado.**

### 2. La velocidad de pase como decisión (SmartKick) — micro-realismo barato

HELIOS elige `speed = min(maxBallSpeed, calc_first_term_geom_series_last(1.8, dist, decay))`
(`planner/bhv_pass_kick_find_receiver.cpp:405-418`): la **velocidad justa para
que el balón llegue** con la física de decaimiento geométrico. Pase al hueco =
fuerte, pase al pie = suave. Server real: `ball_decay = 0.94`, pase máx. teórico
`2.7/(1−0.94) = 45 m`. Decidir la velocidad con la serie geométrica cerrada hace
que los pases se vean "humanos" sin tocar nada más.

### 3. Marcaje por bloqueo simulado, no por proximidad (`src/player/bhv_basic_block.cpp`)

Cyrus no asigna marcadores por distancia: **simula el dribble del rival** —
horizonte de 40 ciclos, el balón avanza a `0.7` m/ciclo en la dirección que
minimiza `−target.x + max(0, 40−dist(target, (−50,0)))` (barrido de 36
direcciones, `L205-225`); el primero que alcanza bloquea
(`cyclesToReachDistance ≤ cycle`). Histéresis: si el objetivo cambió < 2 m
(5 m si está a > 15 m) en los últimos 5 ciclos, se mantiene (`L70-79`). Regla de
oro defensiva: los defensas **no persiguen detrás de su línea** si
`ball > defenseLine + 10` (`L114-156`). Es el "marcaje al espacio que el rival
va a ocupar" que el marcaje posicional no tiene.

### 4. Trampa de offside con step-up individual (`src/player/bhv_basic_move.cpp:187-224`)

Ya existe la línea (F5); falta el step-up: subir **15 m** solo si se cumplen 5
condiciones de sincronización:

1. `me.x < −37.0` (estoy en nuestro tercio)
2. `opp_min < mate_min` (el rival llega antes que nosotros)
3. `homePos.x > −37.5` o `ball.inertiaPoint(opp_min).x > −36.0`
4. `second + 3.5 > me.x` — el **2º compañero más retrasado está detrás de mí**
5. `ball.x > me.x + 4.5` — el balón está por delante

La trampa deja de ser un clamp y pasa a ser una *decisión individual
sincronizada por la línea*.

### 5. Stamina como esfuerzo con umbrales (modelo effort/recovery del server real)

Verificado en `rcssserver/src/player.cpp:2623-2664`: `effort` en [0.6, 1.0] que
**decae bajo `effortDecThr` = 30% de stamina y se recupera sobre `effortIncThr` =
60%**; `recovery` en [0.5, 1.0] con decaimiento bajo 2400. Velocidad real =
`dash_power × dash_power_rate × effort / player_decay` — al 60% de effort corres
a 0.6 m/cycle. Parámetros base (server_param.cpp): `dash_power_rate = 0.006`,
`player_decay = 0.4`, `player_speed_max = 1.05`, `stamina_max = 8000`,
`stamina_inc_max = 45`, `kickable_area = 1.085 m` (player_size 0.3 + margin 0.7 +
ball 0.085). Las `Condition` lineales podrían adoptar los umbrales: la fatiga
pasa de "más lento" a "cambia el comportamiento en rangos".

### 6. Dribble como intención multi-ciclo con revalidación

`planner/bhv_normal_dribble.cpp:200-227`: registra una intención
`n_turn/n_dash/n_kick` y **cada ciclo revalida contra la mejor cadena**: si la
mejor acción ya no es ese dribble, se cancela. Muestreo de 16 direcciones,
`n_turn < 3`, anti-oponente con bonus de pasos (`short_dribble_generator.cpp`).
El `Intent` ya tiene commitment + exit triggers (concepto heredado); el salto es
que el dribble sea una *secuencia planificada* y no una acción de 1 tick.

### 7. Formación aprendida de game logs (HELIOS formation learning)

HELIOS no tiene formaciones "dibujadas": las **aprende**. `formations-dt/normal-formation.conf`
es una triangulación de **Delaunay con 115 vértices** (balón en (54.5,−36) →
portería (0,0); balón en el centro → defensa en x≈−15.5), interpolada por el
triángulo que contiene el balón. Tres formaciones por situación
(normal/defense/offense) + ~10 específicas de set play. Akiyama publicó el
algoritmo: clustering de snapshots de partidos → Delaunay → asignación de roles.
`Formation.fs` ya tiene la consulta por samples con interpolación; el paso es el
**pipeline de aprendizaje**: correr partidos, agrupar posiciones por zona del
balón, y *generar* la formación del equipo. Es literalmente la tesis del
proyecto: **"el manager moldea al equipo"**.

### 8. Cómo se consulta la formación (balón predicho)

`strategy.cpp:550-647`: el punto de referencia es el **balón inercial al paso de
interceptación mínimo** — `ball_step = min(1000, teammateStep, opponentStep,
selfStep)` (568-575) → `ball_pos = ball.inertiaPoint(ball_step)` (577) →
`f->getPositions(ball_pos, ...)` (585). Clamp de fuera de juego:
`max_x = offsideLineX − 1.0` (587-618), y si un compañero intercepta en < 50
pasos, `max_x` sube hasta `trap_pos.x`. Confirmar que `Position.fs` consulta con
balón predicho y no con el actual.

### 9. Set plays como rutinas planificadas

`src/player/setplay/`: `Bhv_SetPlay` despacha por tipo de saque
(`bhv_set_play.cpp:73-174`); kickoff con pase alternante
(`bhv_set_play_kick_off.cpp`); retraso deliberado (regateo de tiempo) si se oyó
`recovery` en los últimos 10 ciclos y vas ganando, o últimos 500 ciclos ganando
por ≤ 1 (`is_delaying_tactics_situation`, 547-598). El lanzador reusa la misma
cadena de acciones que en juego (`Bhv_PlannedAction`). El siguiente nivel es
variar rutinas según la oposición.

### 10. Coordinación sin comunicación (lo que ya replicamos)

- Sincronización implícita: todos comparten la intercept table y `Strategy::updatePosition`
  (función determinista del balón futuro) — el "quién va al balón" emerge sin líderes.
- `Bhv_BasicMove` (`bhv_basic_move.cpp:74-91`): corro al balón si
  `!kickableTeammate && (self_min ≤ 3 || (self_min ≤ mate_min && self_min < opp_min + 3))`.
- Comunicación explícita (refuerzo): emisor rotatorio por `time.cycle() % candidate_unum.size()`
  (`sample_communication.cpp:246-322`), `sayIntercept` ("yo llego antes",
  1361-1416), `sayOffsideLine` (1434-1481), `sayDefenseLine` (el portero, 1487-1528).
  En FootballEngine el canal es `SimState` — más fuerte que say/hear; el
  `PassMessage` de HELIOS para coordinar recepciones ya se hace con estado.

---

## Parte 2 — Por qué y cómo ser mejor que Football Manager

**Lo que FM es:** un motor de *attribute checks* — cada acción es una comparación
de probabilidades ocultas (Finishing+Composure+Technique vs Reflexes+Positioning+
Handling con modificadores de contexto). No es una simulación física de 22
agentes; es un generador estocástico de eventos con una capa de visualización.
Por eso existe el "FM'ed" (2 tiros, 1 gol, 37% de posesión y te dan vuelta el
partido): las probabilidades son opacas, el jugador no puede *ver* la causa.
El propio SI publicó el modelo de conversión de chances: "un 1v1 = 1 de 3, las
chances importan más que el jugador" — realista como *promedio*, invisible como
*causa*.

**Las 4 ventajas estructurales de FootballEngine (que FM no puede copiar fácilmente):**

1. **Causa encadenada visible.** En FM un gol es una tirada de dados con
   modificadores. En FootballEngine es un hecho físico verificable: pase con lane
   bloqueado → recepción con presión → tiro con ángulo malo → gol. El usuario
   puede **ver por qué**, y la táctica importa porque cambia *dónde ocurren las
   cosas en el campo*, no solo números.
2. **Determinismo + reproducibilidad.** FM no puede re-jugar el mismo partido.
   FootballEngine sí: mismo seed, mismo rival, táctica cambiada → comparar
   exactamente qué cambió. El análisis táctico se vuelve una herramienta real.
3. **Emergencia en vez de modificadores.** "Gegenpress" en FM es un multiplicador
   de probabilidades. Acá es un parámetro de un sistema real: pressing con física
   de interceptación, línea defensiva con offside line, fatiga que cambia el
   pressing efectivo. El *porqué* del éxito o fracaso de una táctica es observable.
4. **El sistema aprende.** Formación aprendida de logs + memoria por jugador (R6)
   + calibración ML (`weights.json`) = un mundo que evoluciona con las decisiones
   del usuario. FM es estático por partido; FootballEngine tiene dos bucles de
   aprendizaje (match-level y season-level) diseñados.

**Lo que sí copiar de FM:** la profundidad de gestión alrededor del partido (ya
está en el World Engine: scouting, finanzas, juveniles) y la narrativa de
temporada. FM es el mejor juego de *gestión*; la diferenciación es que el
*fútbol* sea real. Riesgo a cuidar: que el match engine sea tan bueno que el
World Engine se sienta plano.

---

## Parte 3 — Realismo y diversión a la vez

**La lección más importante de RoboCup: el realismo óptimo es aburrido.** Los
campeones juegan un metagame quirúrgico: 2-3 toques, pressing permanente, cero
riesgo. Un motor que copie a RoboCup ciegamente produce fútbol irreal (todo el
mundo igual, sin personalidad). RoboCup optimiza para *ganar*; un juego optimiza
para *sentir*.

1. **Errores con causa, no ruido.** En RoboCup el pase fallido emerge de la
   física (el interceptor llega antes, el ángulo es malo). En FM el error es una
   tirada opaca. La diversión vive en el medio: el error debe ser *comprensible*
   (causa visible en el campo) pero *no predecible al 100%* (tiene varianza).
   Regla: **nunca un fallo sin una razón visible en el campo**.
2. **Variedad táctica viable por diseño.** RoboCup no la tiene (el metagame
   devora todo). Para ser divertido, el 5-4-1 debe poder ganar 1-0, el tiki-taka
   debe poder aburrir al rival, el contragolpe debe tener su momento. Eso es
   *equilibrio deliberado* (`BalanceConfig` + tests estadísticos), no realismo
   puro.
3. **Drama que emerge del modelo.** `Decide.fs` ya tiene el germen: desventaja →
   winProb baja → directiva Attack → línea más alta → espacios → contraataque
   rival. El drama debe ser una *consecuencia del sistema*, nunca un script.
   RoboCup no lo tiene porque no le importa; FM lo simula con números ocultos;
   FootballEngine lo tiene estructuralmente.
4. **La incertidumbre justa.** Modelo de conversión realista (1v1 ≈ 1/3, chance
   promedio ≈ 1/10) como base; los momentos de sorpresa (golazo de 0.3 xG, poste
   en el 90') deben ser *raros pero visibles y con causa física* (deflector,
   portero en racha, ángulo improbable).
5. **Determinismo y drama no son opuestos.** El drama viene de la varianza
   legítima de la física (Rng seedeado: postes, deflectores, porteros), no de
   números ocultos. La ventaja: reproducir el drama y explicarlo.

**Anti-ámbito confirmado (no copiar):** localización/sensores (sim omnisciente),
protocolo say/hear (SimState es mejor canal), 500 estados evaluados por tick
(presupuesto acotado), física de 100 ms (subtick 1/40s es más fino), y el
metagame de ganar-a-toda-costa.

---

## Resumen — 7 pasos recomendados (por ROI)

| # | Cambio | Fuente RoboCup | ROI |
|---|---|---|---|
| 1 | **Action chains**: best-first ≤ 4 acciones, 500 estados, evaluador simple | `action_chain_graph.cpp` | Realismo máximo restante |
| 2 | **Velocidad de pase con serie geométrica** (al pie vs al hueco) | `bhv_pass_kick_find_receiver.cpp:405` | Barato, se siente de inmediato |
| 3 | **Marcaje por bloqueo simulado** (simular dribble rival 40 ciclos) | `bhv_basic_block.cpp` | Defensa inteligente |
| 4 | **Step-up de offside** (5 condiciones de sincronización) | `bhv_basic_move.cpp:187-224` | Completa la trampa (F5) |
| 5 | **Effort/recovery con umbrales** (30%/60%, effort 0.6-1.0) | `rcssserver/src/player.cpp:2623` | Fatiga real |
| 6 | **Dribble multi-ciclo con revalidación** | `bhv_normal_dribble.cpp:200-227` | Secuencias largas |
| 7 | **Formación aprendida de logs** (Delaunay/clustering) | HELIOS formation learning | Tesis del proyecto completa |

Todos entran con el patrón probado: señal nueva al blackboard o término nuevo en
una utilidad (o un sistema de 3 líneas en la tabla) — sin tocar contratos,
test-first, determinismo intacto.

---

*Documento de investigación — FootballEngine. Sigue las convenciones de
`AGENTS.md` (nada fuera de `BalanceConfig`, un solo lugar obvio, determinismo,
tests espejo de la estructura del código).*