# F1 — Rediseño del motor para rendimiento (propuesta para revisar)

Estado: decidido (sección 7). Paso 1 hecho.

## 1. Lo medido (2026-11-12, Ryzen 5 5600X, 6 núcleos / 12 hilos, Release)

| Medida | Valor |
|---|---|
| Un partido aislado | 5,4 s, 261k ticks, ~20 µs por tick, ~1 GB asignado |
| 24 partidos en paralelo | 19,8 s → 0,83 s por partido de throughput |
| 12 partidos en paralelo | cada partido 1,7× más lento que aislado (SMT, memoria, GC) |

| Sistema | Tiempo por partido | Llamadas | Nota |
|---|---|---|---|
| ContactDynamics | 1,6 s | 223k (cada tick) | Sin punto caliente: `findEarliest` 0,54 s, cotas 0,26 s, escritura al frame 0,17 s, inicialización 0,14 s, resto ~0,5 s |
| TeamPosition | 0,87 s | 59k | Los 22 jugadores re-buscan su posición cada ~4 ticks |
| PlayerDecision | 0,78 s | 53k | El Planner: 17,7k búsquedas, 66 evaluaciones cada una |
| Stepper + EventApply | 0,74 s | — | Disparadores, reducción de eventos, observación |
| Resto (14 sistemas) | ~1,4 s | — | Steering, Intent, Actions, etc. |

ContactDynamics: el 99 % de los ticks tiene algún par de cuerpos a distancia de contacto y el 80 % algún cuerpo
cerca de la pelota. Hay 64,8k choques reales por partido. Saltear ticks "vacíos" no ahorra nada.

Ya hecho sin cambiar resultados (hash `b7f74f92b9fb2bb2`): balones rodados reutilizados, pases tabulados en el
Planner, descarte exacto de torso/botín. Esa vía está agotada: lo que queda son decenas de ahorros del 2–5 %.

## 2. Cómo funciona hoy y por qué no escala

1. **Un tick secuencial con estado compartido mutable.** 22 sistemas en orden fijo modifican el `World` en el
   lugar; cada uno ve lo que escribieron los anteriores. El orden es parte del determinismo, así que dos sistemas
   no pueden correr a la vez. Además un tick dura ~20 µs y sincronizar hilos cuesta 2–10 µs: repartir ticks
   entre núcleos no paga.
2. **Cada sistema recalcula su propia geometría.** Quién llega primero a la pelota (InterceptTable, PassIntercept,
   Planner), rivales cercanos (Perception, Position, Planner), distancias entre cuerpos (marcas de contacto,
   evaluador de campo). No existe una "foto del tick" común.
3. **Dos representaciones del cuerpo.** `TeamFrame` (float32, por equipo) y `ContactScratch` (float64, los 22
   juntos) se copian y convierten en cada tick: 0,31 s por partido solo en ir y volver.
4. **La cognición corre más seguido de lo que una persona piensa.** El disparador "geometry" despierta
   TeamPosition e Intent cada ~4 ticks (10 Hz) para los 22 jugadores a la vez. La percepción humana reacciona a
   ~4–6 Hz y no todos re-deciden en el mismo instante.
5. **Records, options y copias en los caminos calientes.** `BallState` inmutable copiado en cada integración,
   `ContactAction option[]`, tuplas en el Planner. ~1 GB por partido limita el escalado paralelo.
6. **El determinismo depende del orden de las sumas en flotante.** Cualquier reestructuración cambia el hash, así
   que cada mejora real exige validar held-out. Un diseño que dure tiene que fijar ese orden por contrato.
7. **El paralelismo entre partidos tiene una barrera por día.** Un día con pocos partidos deja núcleos ociosos.

## 3. Principios del diseño nuevo

- **Fases con dueños, no orden global.** Dentro de una fase, nadie lee lo que otro escribe en esa misma fase. El
  resultado deja de depender del orden de los sistemas de la fase y se puede paralelizar sin cambiarlo.
- **Una sola verdad por tick.** Lo que varios sistemas necesitan se calcula una vez y se lee de ahí.
- **Una representación de los cuerpos.** Un bloque SoA para los 22 jugadores y la pelota, que usan tanto la
  física como la cognición. Sin copias por tick.
- **Cada cosa a su ritmo.** La física a 40 Hz. La cognición escalonada a su propia frecuencia, con presupuesto
  fijo por decisión.
- **Cero asignaciones por tick** en estado estacionario, verificado por un gate.

## 4. Arquitectura propuesta

```
Tick n
 ├─ Sense   (paralelizable, solo lectura del estado anterior)
 │    TickSnapshot: matriz de distancias 22×22, grilla del campo, tabla de intercepción (una vez),
 │                  rivales cercanos y presión por jugador, línea de offside
 ├─ Think   (por equipo; cada equipo escribe solo en sus propias propuestas)
 │    Team: perceive / transition / decide / position escalonado / attack
 │    Players: perception / intent / steering / decision (solo los que tocan este tick)
 ├─ Act     (dueño único, secuencial)
 │    Restart → Actions → contacto con pie/arquero → ContactDynamics → pelota → posesión
 └─ Apply   reducción de eventos al MatchState (como hoy)
```

### 4.1 `Bodies`: un bloque para los 23 cuerpos

- Arrays por campo (posición, velocidad, aceleración, orientación, altura, radio) con índice de cuerpo 0..21 y
  la pelota aparte, en float64. La cognición lee de ahí; la física escribe ahí.
- `TeamFrame` conserva solo el estado cognitivo y táctico por equipo, no posiciones.
- Desaparecen `initializeBodies` y la escritura de vuelta de ContactDynamics (0,31 s).
- Cambia el redondeo (float32 → float64 en las posiciones): requiere validación held-out.

### 4.2 `TickSnapshot`: la foto común

- Calculada una vez al inicio del tick, en paralelo por equipo si conviene.
- Contenido: distancias entre cuerpos (231 pares), vecinos por jugador, tiempos de intercepción de la pelota para
  los 22 (hoy se calculan en tres sitios), línea de offside, proyección inercial de la pelota.
- Perception, Position, Decision, Planner y las cotas de contacto la leen en vez de recalcular.
- Refactor puro donde la cuenta es idéntica (hash igual); se documenta cada lugar donde no lo es.

### 4.3 Cognición escalonada

- Posicionamiento: cada jugador re-evalúa su objetivo en su propio turno (por ejemplo, 1/8 del equipo por
  tick), salvo que un evento lo despierte (pérdida, pase, cambio de directiva). Hoy son los 22 cada ~4 ticks.
- Planner: búsqueda "anytime" con presupuesto fijo y reutilización del árbol mientras el portador y la
  geometría no cambian de forma relevante.
- Es un cambio deportivo: va con validación held-out y bandas.

### 4.4 Paralelismo

- **Temporadas (throughput):** una cola de partidos con reparto de trabajo (work stealing) sin barrera por día,
  respetando solo las dependencias reales (un partido no depende de otro del mismo día). Cada partido sigue
  siendo determinista por su semilla, así que el orden de ejecución no cambia resultados.
- **Un partido individual (latencia):** la fase Think de los dos equipos en paralelo. Solo se activa si la
  medición muestra que el trabajo por tick supera el costo de sincronizar; en temporadas queda apagado porque
  los núcleos ya están ocupados con otros partidos.
- Nunca se paralelizan los ticks entre sí: el estado n+1 depende del n.

## 5. Estimación honesta

| Paso | Ahorro esperado por partido | Cambia resultados |
|---|---|---|
| `Bodies` único | 0,3–0,5 s | sí (redondeo) |
| `TickSnapshot` | 0,4–0,7 s | no en lo idéntico |
| Posicionamiento escalonado | 0,5–0,6 s | sí |
| Planner anytime | 0,3–0,4 s | sí |
| Cero asignaciones | 0,2–0,4 s (más escalado paralelo) | no |

Un solo núcleo: de 5,4 s a ~3–3,5 s. Llegar a 1,5 s en un partido aislado necesita además paralelizar Think
por equipo o bajar trabajo de la física (por ejemplo, menos pruebas finas de pies con una cota por superficie).
En temporadas la meta ya se cumple hoy por throughput (0,83 s por partido) y mejoraría en proporción.

## 6. Orden de migración

Cada paso deja el motor andando, con tests y hash o validación held-out:

1. Gate de rendimiento y de asignaciones (`Training profile` con presupuesto por sistema).
2. `TickSnapshot` con la intercepción y las distancias (refactor puro).
3. `Bodies` único y fin de `ContactScratch` como copia (held-out).
4. Fases Sense/Think/Act con dueños verificados (refactor; el hash cambia solo si se reordenan lecturas).
5. Posicionamiento escalonado (held-out).
6. Planner anytime (held-out).
7. Cola de partidos sin barrera por día.
8. Think de ambos equipos en paralelo, si la medición lo justifica.

## 7. Decisión (2026-11-12)

Prioridad: **la velocidad de un partido individual**. Las temporadas ya se paralelizan por partido. Sin
compatibilidad hacia atrás: se reemplazan contratos en lugar de adaptarlos. Consecuencias:

- Los pasos 1–6 van tal cual; el 8 (Think de ambos equipos en paralelo) pasa a ser parte del objetivo, no
  opcional, y la fase Think se diseña desde el paso 4 para correr por equipo sin estado compartido escrito.
- El paso 7 (cola sin barrera por día) queda para después: no mejora un partido individual.
- Paso 1 hecho: `dotnet fsi scripts/dev.fsx perf` corre `Training profile --gate perf/budget.txt`, con
  asignación por sistema (independiente de la máquina) y tiempo total del partido en la máquina de referencia.

## 8. Correcciones al medir (2026-11-12)

- **El `TickSnapshot` rinde menos de lo estimado.** La intercepción de la pelota actual ya se calcula una vez
  (TeamPosition) y queda en el `World`. PassIntercept y el Planner calculan pelotas hipotéticas, que no se
  comparten. La estimación de 0,4–0,7 s queda en duda hasta medir qué cálculos se repiten de verdad.
- **"Geometry" no es geometría:** es una cadencia de 4 ticks más eventos.
- **TeamPosition:** refinamiento por jugador 406 ms, intercepción 258 ms, línea defensiva e inercia 54 ms,
  resto chico. Paso 5 aplicado al posicionamiento (`Positioning.RefineGroups = 2`): 895 → 721 ms; held-out 7/8
  en banda y error total 11,91 → 12,45. Hash nuevo `b5ce1b9a365b8c72`.
- **Método:** una corrida varía ±10 % por sistema. Toda decisión se toma con el mínimo de 3 corridas, y cuando
  el resultado deportivo cambia, el tiempo total no es comparable (cambian los ticks y los eventos): se compara
  el sistema tocado.


## 9. Propuesta central: cognición y física en paralelo, desfasadas un tick

Medido: un relevo entre dos hilos que esperan activamente cuesta 0,07–0,08 µs (`Task.Run` + `Wait`: 0,8 µs).
Paralelizar dentro del partido es viable. Pero partir cada sistema en dos rinde poco, porque el tick (~20 µs)
está repartido en muchos sistemas de 1–15 µs, y algunos tienen un único dueño por naturaleza (la física, el
portador).

La división que sí escala es por **qué** se calcula:

| Hilo | Sistemas | Costo hoy por partido |
|---|---|---|
| Cognición | TeamPerceive, TeamTransition, TeamDecide, TeamPosition, TeamEmergent, TeamAttack, PlayerPerception, PlayerIntent, PlayerSteering, PlayerDecision | ~2,6 s |
| Mundo | RestartController, Actions, BallFootContact, KeeperSave, ContactDynamics, Ball*, Referee, Manager, EventApply | ~2,7 s |

En el tick n el hilo del mundo simula la física con las decisiones ya tomadas, mientras el hilo de cognición
decide sobre la foto del tick n−1. Los dos trabajan a la vez y se encuentran una vez por tick (~0,1 µs).
Techo: ~2× (de ~5,3 s a ~2,7 s), sumado a lo que bajen las demás mejoras.

- **Determinista por construcción.** La cognición lee siempre la foto n−1 y escribe solo propuestas (velocidades
  objetivo, intenciones, acciones pendientes) que el mundo aplica en n+1. El resultado no depende de qué hilo
  termina primero, ni de cuántos núcleos haya: con un solo hilo corre las dos mitades seguidas y da lo mismo.
- **Contrato explícito de dueños.** El estado se parte en *físico* (cuerpos, pelota, reloj, marcador; lo escribe
  solo el mundo) y *mental* (intenciones, memoria, forma, planes; lo escribe solo la cognición). Lo que cruza
  la frontera son la foto (mundo → cognición) y las propuestas (cognición → mundo). El compilador lo hace
  cumplir con tipos separados.
- **Cambio deportivo:** las decisiones llegan con 25 ms más de retraso. La reacción humana es de ~200 ms, así que
  es realista, pero cambia resultados: validación held-out y recalibración.
- **Eventos.** Los sistemas de cognición que hoy se despiertan por eventos del tick anterior siguen igual: ya
  leen las banderas del tick previo.

Orden de trabajo:
1. Separar los tipos de estado físico y mental, y la foto que los cruza (cambio grande de contratos; el
   compilador lista cada lectura cruzada).
2. Correr las dos mitades en secuencia con el desfase: valida el diseño sin hilos (held-out).
3. Correrlas en dos hilos con relevo por espera activa; un test exige el mismo hash con 1 y con 2 hilos.

## 10. Frontera medida entre cognición y mundo (2026-11-12)

Lecturas y escrituras de campos de `TeamFrame`/`TeamWorld` por archivo de sistema (búsqueda en el código; el
estado de la pelota y del `MatchState` cruza aparte, por `MatchWorld` y los eventos):

- **Cognición → mundo (propuestas):** `TargetVelX/Y`, `TargetFacing`, `PendingActions`, `Memory`, `Cognitive`,
  `GoalkeeperSlot`.
- **Mundo → cognición (foto):** posiciones, velocidades y orientación de los cuerpos, más la pelota, el reloj y el
  flujo del partido.
- **Escritos por los dos lados (conflictos a resolver):**
  - `Intent.TargetX/Y/ExitTrigger`: RestartController manda a los jugadores a sus puestos en una reanudación.
    Es un plan, no física: RestartController pasa a la cognición y sus autorizaciones siguen saliendo como
    eventos.
  - `Directive.SinceTick`: lo escriben TeamDecide y ManagerSystem. El entrenador es cognición.
  - `Condition` (fatiga): la escribe Steering, pero es estado del cuerpo. Pasa al mundo, junto al movimiento.
  - `Phase`, `Width`: nombres repetidos en records distintos, no un conflicto real.

La frontera es angosta: unos 8 campos de propuesta, la foto física y 3 conflictos con dueño obvio. El paso 1 de
la sección 9 (tipos separados) es tratable.

## 11. Experimento: desfasar las órdenes motoras un tick (2026-11-12)

Se emuló en secuencia el diseño de la sección 9 en su forma más simple: la física aplicaba la velocidad y la
orientación objetivo que Steering propuso el tick anterior. Held-out 48 contra la referencia (`b5ce1b9a365b8c72`):

| Métrica | Referencia | Desfasado |
|---|---|---|
| Error total | 12,45 | 16,57 |
| Córners | 31,6 | 35,2 |
| Goles | 3,6 | 4,2 |
| Pases completados | 84,3 % | 83,3 % |

El cambio se revirtió. Un tick de retraso en el control motor hace llegar tarde a los receptores: el control
motor es un reflejo y tiene que leer el estado del mismo tick.

Corrección del diseño: la frontera entre hilos va **por encima de Steering**. El hilo del mundo corre Steering,
Actions, la física y la pelota sobre el tick actual (~3,1 s hoy); el hilo de cognición corre percepción, forma,
intención y decisión con la foto del tick anterior (~2,1 s). El desfase pasa a las intenciones y decisiones, que
cambian a 2–10 Hz. Techo: ~1,7× en vez de ~2×. Antes de construirlo hay que emular ese desfase (Steering y
Actions leyendo intenciones y acciones del tick anterior) y validarlo igual que este.

## 12. Frontera por encima de Steering: hecha y validada (2026-11-12)

`CognitionLatch` (primero en el `SystemsTable`) copia las intenciones y la forma de cada equipo a lo que lee
Steering, y Actions ejecuta una acción pendiente desde el tick siguiente al que se decidió. Held-out 48: error
total 12,45 → 11,94, 7/8 en banda. Hash `0727ce16b65c8d5d`. Un test verifica el relevo y falla si Steering lee las
intenciones en vivo.

Lo que falta para correr la cognición en otro hilo, en orden:
1. **Foto física de solo lectura** para la cognición: posiciones, velocidades, orientación, estado de contacto y
   fatiga de los cuerpos, más la pelota y el `MatchState`, copiados al inicio del tick. La cognición deja de leer
   el `TeamFrame` vivo.
2. **Salidas propias por lado:** un `EventBuffer` y un registro de observación por hilo, unidos en orden fijo
   (primero cognición, después mundo); `PendingActions` con doble buffer como las intenciones.
3. **Dueños únicos** de `Condition` (al mundo) y `Directive` (a la cognición), según la sección 10.
4. **Dos hilos con relevo por espera activa;** un test exige el mismo hash con 1 y con 2 hilos.

## 13. Reanudaciones partidas entre cognición y mundo (2026-11-12)

`RestartPlanning` (cognición) arma el plan y manda a los jugadores a sus puestos; el plan viaja en
`RestartPlanned` y `EventApply` lo aplica al estado, igual que un fallo de planificación
(`RestartPlanChanged PlanningFailed`). `RestartController` (mundo) solo autoriza, ejecuta el saque y vuelve al
juego: es el único que escribe el flujo. El árbitro señala la dirección desde la concesión, no desde el plan.
El plan llega un tick más tarde. Held-out 48: error total 11,62, 7/8 en banda (los córners siguen fuera).
Hash `84dd920156d15efd`. Perfil: un partido en 5,49 s.

El test de cliente "4x playback keeps HUD updates…" falla con la suite completa también en la base
(1x 64 ticks, 4x 160): depende del reloj de pared bajo carga y queda pendiente aparte.
