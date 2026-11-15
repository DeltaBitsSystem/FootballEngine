# Rediseño de rendimiento del motor (propuesta para revisar)

Fecha: 2026-11-15. Base: `66d0eee`. Máquina de referencia: Ryzen 5 5600X (6 núcleos / 12 hilos), Linux, .NET 10
(SDK 10.0.300), Release, Server GC no concurrente (la configuración de `Training`). Retoma F1
(`MAINTAINABILITY_PERFORMANCE_PLAN.md`, `F1_ENGINE_REDESIGN.md`), pausada el 2026-11-13.

## Resumen

- Hoy, **en caliente**, un partido cuesta **4,24 s** (semilla 42, 261 962 ticks, 16,2 µs/tick) y asigna **1,84 GB**.
  El primer partido de cada proceso tarda 5,7–6,4 s por el JIT, y es el único que mide `dev.fsx profile`.
- **1,5 s por partido en un hilo no es realista** con contacto a 40 Hz y la decisión actual del portador (42 132
  decisiones por partido con búsqueda de cadenas de 4 pasos). Lo realista:
  - **~3,1 s en un hilo** con cambios exactos, que dejan el mismo hash;
  - **~2,5 s** sumando dos cambios numéricamente neutros, validados held-out sin recalibrar;
  - **≤ 100 MB por partido**;
  - **~2,1 → ~1,7 s de pared** con los hilos de cognición que ya existen.

  Bajar más pide recortar trabajo de cognición, y eso ya se midió en contra de la aceptación deportiva (sección 4).
- El rediseño se llama **núcleos exactos y tick sin basura**. El costo está en tres cálculos: correr una pelota
  rodada contra cuerpos, buscar cadenas de acciones y barrer los cuerpos para el contacto. Los tres pasan a ser
  núcleos orientados a datos, compartidos donde hoy se repiten. Además:
  - el tick estacionario no asigna;
  - el plan del portador deja de ser un par de closures y pasa a ser datos;
  - la medición pasa a ser exacta y en caliente.

  Después vienen dos cambios de representación: la rodada 1D tabulada y los cuerpos en float64. La arquitectura
  de sistemas, eventos, triggers y dueños no cambia.
- La evidencia de que el camino exacto rinde es un núcleo de planner construido fuera del repo:
  - dio 0 diferencias en 8 426 búsquedas reales;
  - dio listas de eventos idénticas en partidos completos;
  - cuesta −35–39 % de tiempo por búsqueda y −93 % de asignación;
  - ahorra **−0,38 a −0,61 s por partido**.
- La medición de hoy no sirve para iterar:
  - `Training hash` gasta 35 s por partido en `sprintf "%A"` y no distingue la undécima cifra significativa;
  - el gate `perf` está en rojo, con un presupuesto viejo.

  La etapa 0 lo arregla reutilizando `MatchEvent.id`, que es exacto y cuesta 2,5 ms por partido.

## 1. Línea base medida

### 1.1 Cómo se midió

Los comandos del repo corrieron sobre el build de `Training` en `66d0eee`. El árbol estaba limpio, salvo el
brief sin versionar.

```bash
dotnet Training/bin/Release/net10.0/Training.dll profile                        # = dev.fsx profile
dotnet Training/bin/Release/net10.0/Training.dll profile --gate perf/budget.txt # = dev.fsx perf
dotnet Training/bin/Release/net10.0/Training.dll hash                           # 4 semillas
dotnet Training/bin/Release/net10.0/Training.dll validate 48 --heldout
```

El resto salió de dos herramientas fuera del repo, que la etapa 0 convierte en comandos de `Training`:

- **Sonda:** un proyecto de consola .NET 10 que referencia los DLL de `Training/bin/Release/net10.0` (el mismo
  build), con Server GC no concurrente y en Release. Tiene estos modos:

  | Modo | Qué hace |
  |---|---|
  | `plain N [own]` | N partidos seguidos, sin envoltura. `own` llama a `MatchSession.runCognitionOnOwnThread`. |
  | `profile N` | Envuelve cada `SystemStep` con cronómetro y bytes del hilo, como `profileCmd`, pero sobre partidos en caliente. |
  | `inmatch N` | Reemplaza `PlayerDecision` por una copia que llama al núcleo prototipo y compara las listas de eventos. |
  | `decision` | Captura 8 426 entradas reales del planner (1 de cada 5 decisiones de la semilla 42) y las repite en caliente. |
  | `allocs` | Muestrea `GCAllocationTick` (una muestra cada ~100 KB, por tipo) con un `EventListener`. |
  | `parallel 12` | 12 partidos con `Array.Parallel.map`, como `validate`. |
  | `hashcost` / `idhash` | Costo del digest de `Training hash` contra `MatchEvent.id`. |

  Las variables `DOTNET_TieredPGO`, `DOTNET_TieredCompilation` y `DOTNET_TC_QuickJitForLoops` se probaron sobre
  `plain 3`.
- **Copia instrumentada de Core:** temporizadores por fase en `ContactDynamics.solve` y en
  `TeamPosition.step`. El sobrecosto es ~3 % (4,47–4,54 s contra 4,24).

Regla aplicada: se toma el mínimo de 2–3 corridas en caliente y se compara por µs/tick. El número de ticks varía de
259k a 364k entre semillas, así que un partido suelto no es comparable con otro.

### 1.2 Partido completo

| Medida | Valor |
|---|---|
| Semilla 42 en caliente | 4,24 s · 261 962 ticks · 16,2 µs/tick (`inmatch`, corridas 2–3: 4 239 y 4 235 ms) |
| Semilla 1055 en caliente | 4,26–4,38 s · 259 202 ticks · 16,4–16,9 µs/tick |
| Semilla 2068 en caliente | 5,63–5,78 s · 364 084 ticks · 15,5–15,9 µs/tick |
| Primer partido del proceso | 5,73–5,84 s (`plain`); 6,41–7,00 s con el envoltorio de `Training profile` |
| Asignado | 1,83–1,88 GB (~7,0 KB/tick); 2,38 GB la semilla 2068 |
| GC en un hilo | ~430 gen0, 149–152 ms de pausa por partido |
| Cognición en hilo propio | 3,01 s (1055) y 3,99 s (2068): 11,0–11,6 µs/tick, −30 %. Usa tres hilos auxiliares con espera activa (`Runner`, `Lanes`, `Planner` en `runCognitionOnOwnThread`). |
| 12 partidos en paralelo | 9,6–9,9 s de pared = 0,81 s por partido de throughput. Pausas de GC 1,4–1,7 s (~15 % de la pared). |
| `validate 48 --heldout` | 53,6 s de pared; error total 4,473; 7/8 en banda (offsides 0,7 fuera) |
| `hash` (4 semillas) | 82,3 s de pared. Combinado `8128e9f15c08bf4d`. Semillas: 42 `8ca0585ae0f4ec05` (56 515 eventos, 1-2), 1055 `2db7703acfc325db` (63 973, 2-0), 2068 `cb60deb921ce1f93` (98 705, 2-2), 3081 `6fe54c911d9371a9` (80 362, 1-1) |

### 1.3 Por sistema, en caliente (semilla 42)

Corrida 3 de `profile 3`: 4 425 ms de pared con el envoltorio, 4 074 ms en sistemas, 351 ms fuera de ellos.

| Sistema | ms | % | Llamadas | µs/llamada | µs/tick | MB |
|---|---:|---:|---:|---:|---:|---:|
| PlayerDecision | 1 442 | 32,6 | 54 799 | 26,3 | 5,51 | 1 138 |
| ContactDynamics | 1 170 | 26,4 | 222 761 | 5,25 | 4,47 | 112 |
| TeamPosition | 535 | 12,1 | 57 606 | 9,28 | 2,04 | 73 |
| PlayerSteering | 268 | 6,1 | 112 808 | 2,37 | 1,02 | 9 |
| PlayerIntent | 152 | 3,4 | 57 606 | 2,64 | 0,58 | 150 |
| BallPossession | 147 | 3,3 | 214 473 | 0,68 | 0,56 | 32 |
| TeamAttack | 58 | 1,3 | 67 411 | 0,86 | 0,22 | 83 |
| TeamTransition | 58 | 1,3 | 67 411 | 0,86 | 0,22 | 64 |
| PlayerPerception | 44 | 1,0 | 29 987 | 1,46 | 0,17 | 4 |
| Actions | 41 | 0,9 | 108 087 | 0,38 | 0,16 | 32 |
| KeeperSave | 40 | 0,9 | 214 473 | 0,18 | 0,15 | 11 |
| Otros 14 sistemas | 120 | 2,7 | — | — | 0,46 | 37 |
| Fuera de los sistemas (`Stepper`, `EventApply`, latch, parte del envoltorio) | 351 | 7,9 | — | — | 1,34 | ~90 |

Repartido según la frontera de `CognitionLatch`, el tick cuesta ~9,0 µs de cognición (decisión, posición, intención,
ataque, transición, percepción), ~6,6 µs de cuerpo (contacto, Steering, posesión, pelota, acciones, arquero) y ~1,3 µs
de parte serial.

### 1.4 Dentro de PlayerDecision

- Hay 54 799 llamadas y **42 132 decisiones preparadas**, una cada ~6,2 ticks. El plan anterior contó 54 932
  "decisiones", pero eran llamadas.
- `prepare` cuesta 9,4 µs por decisión (394 ms por partido). Se midió dentro del primer partido del proceso, así
  que incluye JIT y en caliente debería ser algo menor.
- La búsqueda cuesta **25,3 µs** (1,07 s por partido, 25 % del partido). Esa cifra sale de repetir en caliente las
  entradas capturadas; dentro de aquel primer partido daba 32,8 µs.
- Por búsqueda:

  | Medida | Por búsqueda |
  |---|---|
  | Evaluaciones | 69,3, sin agotar el límite de 500 |
  | Raíces | 2,3 |
  | Proyecciones | 13,3 |
  | Valoraciones | 13,7 |
  | Chequeos de factibilidad | 9,1 |
  | Pasos de pelota rodada | 315 |
  | Chequeos jugador-paso | 2 219 |
- Tiempo por parte, con temporizadores (29,9 µs en total):
  - factibilidad 17,7 µs (59 %);
  - proyección 2,4 µs;
  - valoración 1,5 µs;
  - estructura ~8 µs (un `ChainState` con cuatro arreglos por rama, tuplas del haz, `Seq.truncate`, orden,
    cadenas).
- Largo de cadena y ancho del haz, por búsqueda:

  | Variante | µs por búsqueda |
  |---|---:|
  | Cadena de 1 paso | 2,3 |
  | Cadena de 2 pasos | 13,2 |
  | Cadena de 3 pasos | 21,6 |
  | Cadena de 4 pasos (actual) | 25,3 |
  | Cadena de 4, haz 2 | 22,4 |
  | Cadena de 4, haz 1 | 18,4 |
- La búsqueda asigna 22 353 B (~0,94 GB por partido).

### 1.5 Dentro de ContactDynamics y TeamPosition (copia instrumentada, semilla 42, en caliente)

| Fase de ContactDynamics | ms |
|---|---:|
| `findEarliest` (barridos) | 468 |
| ↳ integración de la pelota dentro de `findEarliest` | 111 |
| ↳ superficies de la pelota (torso, botín) | 173 |
| Avance sin impacto | 180 |
| Escritura al `TeamFrame` y marcha | 167 |
| Marcas de pares | 113 |
| `initializeBodies` (copia float32→float64 y constantes por cuerpo) | 96 |
| Resolución de impactos | 57 |
| Compromisos de acción | 56 |
| Equilibrio | 41 |
| Alcance | 39 |
| Marcas de pelota | 34 |

Por tick hay 1,73 cuerpos candidatos a la pelota, 4,85 pares candidatos, 2,24 llamadas a `findEarliest` y
0,235 impactos (52 447 por partido). `initializeBodies` recalcula en cada tick constantes que no cambian en el
partido: perfil, validez de la morfología, radio, altura y aceleración base. Además asigna una lista por
`for side in [ Home; Away ]`.

TeamPosition por fase:

| Fase | ms | Nota |
|---|---:|---|
| Tabla de intercepción | 200 | `computeBothInto`: carrera de la pelota actual contra los 22 |
| `positionTeam` | 253 | |
| ↳ bucle de refinamiento | 194 | 691 322 refinamientos, 12 por llamada, ~280 ns cada uno |
| Tareas defensivas | 79 | |
| Cachés | 9 | |

### 1.6 Asignaciones por sitio (`allocs`, semilla 42, en caliente; 1 861 MB muestreados)

Las closures aparecen con el nombre que les da el compilador (`función@línea`), que apunta al sitio.

| Grupo | MB | Causa |
|---|---:|---|
| **Planner** | **~990** | Hay una `ChainState` por rama (`struct (int*float*float)[]` 368 MB, `ChainState` 286). También tuplas y arreglos del haz (~144), `ChainAction[]` (48), closures `findReceiver@214` (62), `project@410/411` (41) y `next@304` (14), y `Seq.truncate` (24). |
| `FSharpOption<int>` | 105 | `MatchWorld.slotOf` devuelve `int option`, y hay comparaciones como `Some slot = primary` en Intent, Position y DefensiveTransition. |
| CollectiveAttack: `FSharpList<AttackDuty>`, `AttackEpisode`, closures `cover@76`, `duty@59` | 91 | Listas y filtros por tick. |
| Closures del plan (`prepare@299`, `prepare@304-1`) | 52 | `PendingPlan` guarda `PlanSearch` y `PlanFinish` como closures. |
| `Tuple<ClubSide,int,PlayerId>` (+ su `option` y la closure de `Option.map`) | ≥ 49 | `MatchWorld.ballCarrier`, con 12 lectores. |
| Stepper: `runCognition@319`, `Tuple<Rng,World,int>` (`Stepper.fs:172`), `Tuple<MatchFlow, MatchEvent list>` (`Flow.fs:25`) | 46 | Closures y tuplas que se crean en cada tick. |
| `FSharpOption<PlayerId>`, `PlayerId` | 40 | |
| DefensiveTransition: closures `assess@146/162`, `settled@361` | 37 | |
| Position: `FSharpOption<DefensiveTask>`, closure `assignDefensiveTasks@410` | 33 | `Array.tryFind` con lambdas y options de records. |
| `FSharpList<ClubSide>` | 32 | `for side in [ ClubSide.Home; ClubSide.Away ]` en 12 sitios. |
| `struct (float*float*float*float*float)[]` | 31 | El sitio se localiza en la etapa 0. |
| `FSharpRef<ImpactCandidate voption>` | 29 | Un `let mutable` capturado por closure en ContactDynamics se vuelve celda `ref`. |
| `FSharpRef<int>`, `FSharpRef<EventFlags>`, `Double` en caja | 34 | |
| `PhysicalContact`, `Pass`, … | ~20 | Son eventos: asignación inherente. |
| Cola (tipos de < 6,8 MB) | ~235 | |

### 1.7 JIT, paralelismo y herramientas de medición

Así cambia el JIT el costo por tick (`plain 3`, µs/tick):

| Ajuste | Primer partido | En caliente (1055 / 2068) |
|---|---:|---:|
| Por defecto | 21,9 | 16,9 / 15,9 |
| `DOTNET_TieredPGO=0` | 22,5 | 19,8 / 18,5 |
| `DOTNET_TieredCompilation=0` | 20,8 | 20,1 / 19,1 |
| `DOTNET_TC_QuickJitForLoops=0` | 21,8 | 18,9 / 17,8 |
| Umbrales de promoción bajos | 21,8 | 16,6 / 15,6 |

Sin PGO dinámico, el tick en caliente cuesta 16–17 % más. El sobrecosto del primer partido (~1,5 s) no se va con
configuración sin perder ese PGO.

Estado de las herramientas:

- **`Training profile` mide el primer partido**, con JIT incluido: 6,4 s contra 4,24 en caliente. Sus µs/tick
  mezclan compilación con trabajo.
- **El gate `perf` está en rojo hoy.** Ocho sistemas pasan su presupuesto, entre ellos PlayerDecision
  1 143 MB > 486, TeamPosition 73 > 47, TeamAttack 86 > 60 y BallPossession 33 > 13. Además, el presupuesto nombra
  `CognitionLatch`, que ya no está en el calendario.
- **`Training hash` es lento y no es exacto:**
  - aplica `sprintf "%d %A"` a cada evento: 616 µs por evento, **35 s por partido**, 8× el partido;
  - `%A` imprime 10 cifras significativas, así que `0.1 + 0.2` y `0.3` dan el mismo texto;
  - por eso un hash igual no prueba identidad bit a bit.
- **El motor ya tiene un hash exacto:** `MatchEvent.id` es un hash estructural por campo (flotantes por bits) que
  usa para identificar eventos. Cuesta 0,045 µs por evento, **2,5 ms por partido**. FNV-1a sobre los `id` en orden
  da `048a2279e9d768fa` en la semilla 42.
- **Con 12 partidos en paralelo** (la forma de `validate`, `sweep` y la suite), la pausa de GC es ~15 % de la pared.
  El Server GC detiene todos los partidos del proceso.

### 1.8 Prueba de concepto: el núcleo del planner, exacto

El núcleo se construyó fuera del repo, con el algoritmo del apéndice A:

- una arena SoA por hilo;
- una carrera entrelazada que excluye a los rivales lejos de la línea del pase;
- la tabla de pases compartida;
- un top-k estable;
- las cadenas reconstruidas desde enlaces al padre.

Resultados:

- **En 8 426 entradas reales, 0 diferencias** en el `ChainEvaluation[]` completo contra `Planner.search`: acción,
  destino, cadena, valor (flotante igual) y evaluaciones.
- **Costo por búsqueda:** 15,5–16,5 µs contra 25,3 µs (−35–39 %). Asigna 1 482 B contra 22 353 B (−93 %).
- **Chequeos jugador-paso:** bajan de 2 219 a 353 por búsqueda. De los ~91 rivales que se corren por búsqueda
  (9,1 chequeos × 10 de campo), 85 quedan excluidos por la línea del pase.
- **Dentro del partido** (semillas 42 y 1055, tres corridas cada una):
  - las listas de eventos son **idénticas** (igualdad estructural, flotantes exactos; 56 515 y 63 973 eventos);
  - el partido baja de 4,24–4,37 s a 3,75–3,86 s, −9–14 %;
  - con 12 en paralelo baja de 9,6–9,9 s a 8,6–8,8 s;
  - la asignación baja de 1 948 a 1 036 MB por partido.
- **Lo que queda:** dentro del núcleo nuevo, la mitad del tiempo (~8,7 µs) es rodar la pelota. Son ~269 pasos
  seriales por búsqueda, ~32 ns cada uno: `sqrt`, `exp` de la crisis de arrastre y dos divisiones encadenadas. El
  costo por paso se estima de la factibilidad medida (10,3 µs con 269 pasos y 353 chequeos).

## 2. Causa raíz

Costo del partido ≈ ticks × trabajo por tick + decisiones × trabajo por decisión + reposicionamientos × trabajo por
reposicionamiento:

| Término | Cuenta | Costo |
|---|---|---:|
| Ticks: cuerpo + serial | 261 962 ticks × ~7,9 µs (2,9 s en el partido de 364k ticks) | ~2,1 s |
| Decisiones del portador | 42 132 × (`prepare` ≤ 9,4 + búsqueda 25,3) µs | 1,44 s (PlayerDecision, medido) |
| Reposicionamientos | 57 606 × 9,3 µs | 0,54 s |
| Resto de la cognición | | ~0,3 s |

La meta de 1,5 s equivale a 5,7 µs/tick en un partido de 262k ticks y a 4,1 µs/tick en uno de 364k, entre 2,8× y
3,9× menos que hoy. Hay seis razones por las que el ajuste fino se estancó:

1. **El costo está repartido.** Ningún sistema pasa del 33 %: borrando el planner entero, el partido baja 25 %.
   Llegar a 1,5 s pide bajar los tres sistemas grandes a un tercio a la vez.
2. **Los retoques locales del planner sí estaban agotados, pero su núcleo no.**
   - La forma de los datos cuesta: una `ChainState` de cuatro arreglos por rama, tuplas, closures y un orden
     completo para quedarse con tres.
   - El trabajo redundante también: cada rival recorre todos los pasos aunque esté a 30 m de la línea del pase.
   - Esas dos cosas sumaban 35–39 %, recuperable sin cambiar un bit (sección 1.8).
   - El experimento "estados solo para el haz" bajó 0,28 GB sin ganar tiempo porque sacaba basura pero no trabajo.
     Lo que rinde es sacar trabajo con la forma de los datos.
3. **El piso del planner es una cadena serial.** Cada paso de la pelota rodada depende del anterior a través de
   `sqrt` → `exp` → división. Ningún truco exacto y local la acorta: hay que superponer rodadas independientes (ganancia
   incierta) o calcular la rodada de otra forma (la tabla 1D, numéricamente neutra).
4. **La basura viene de modismos, no de algoritmos.** `option` de tipos de valor, tuplas por referencia, listas
   literales en bucles y closures que capturan mutables: son 24–64 B cada vez, por millones de llamadas.
   - En un hilo cuesta ~0,15 s de pausa.
   - Con 12 partidos en paralelo cuesta ~15 % de la pared: justo donde se usa el motor completo desde el ruteo a
     `QuickMatch` (tests y calibración).
5. **La medición engañaba.**
   - `profile` mezcla el JIT del primer partido.
   - El hash tarda 35 s por partido y no ve la undécima cifra.
   - El presupuesto de `perf` quedó viejo.

   Iterar era lento, y "refactor puro" no estaba probado bit a bit.
6. **Las palancas deportivas ya tienen precio medido** (2026-11-13):
   - 20 Hz baja solo la física, porque la cognición va por eventos, y exige recalibrar todo;
   - re-decidir cada 0,5 s rompe la aceptación de +10: 0,35 contra 0,65;
   - las cadenas de 2 pasos ahorran ~9 %.

## 3. El rediseño: núcleos exactos y tick sin basura

### 3.1 La idea

El costo está en esos tres cálculos y en la basura que dejan los modismos funcionales en el camino caliente:
opciones, listas, tuplas y closures por tick.

- **Núcleos:** los tres cálculos pasan a ser **núcleos**: funciones puras sobre buffers planos, sin asignación en
  estado estacionario, con un contrato de exactitud probado contra la implementación actual.
- **Sin basura:** el tick estacionario no asigna, y un gate lo hace cumplir.
- **Medición:** pasa a ser exacta y en caliente.
- **Representación:** recién con todo eso andando, cambia en dos lugares donde la exactitud bit a bit es lo único
  que frena, sin cambiar el modelo.

Los sistemas, el `SystemsTable`, los eventos, la frontera cognición/cuerpo y el modo de hilo propio quedan como
están. Lo que cambia es lo que hay debajo de cada sistema.

### 3.2 Tres clases de cambio, tres aceptaciones

El hash exacto es una **herramienta de verificación, no una restricción**. El proyecto no tiene compatibilidad hacia
atrás que cuidar, y el determinismo por semilla no depende de que el hash se mantenga. Una etapa exacta se aprueba en
segundos con `hash`. Cualquier cambio puede alterar el hash a propósito si gana tiempo, y entonces paga la aceptación
de la clase N.

| Clase | Qué cambia | Oráculo | Aceptación |
|---|---|---|---|
| **E** exacta | Forma de los datos, orden de recorrido, trabajo descartable por cota | Hash exacto de la etapa 0 sobre las 4 semillas de `hash` y las 48 held-out; igualdad de listas de eventos en 2 partidos; oráculo de entradas capturadas por núcleo | Idéntico, o se rechaza |
| **N** numéricamente neutra | Redondeo o representación; no cambian el modelo ni los parámetros | `validate 48 --heldout` sin recalibrar; `contrast 10 144`; determinismo (misma semilla dos veces; un hilo contra hilo propio) | Cada métrica dentro del intervalo 95 % de la referencia; error total no peor en más de 0,5; +10 dentro de 1 error estándar (~0,14) de la referencia; nuevo hash en el commit |
| **D** deportiva | Modelo, tiempos, cadencias | Held-out con recalibración y +10 ≥ 0,6 | No hay etapas D en este plan |

### 3.3 Núcleo de carrera (`Race`)

Hoy `InterceptTable.RolledBall` rueda la pelota una vez y la corre contra cada cuerpo, con `StepsFor` o
`ReachesWithin`. Lo usan:

- PassIntercept (`margin`, en `prepare`: cada rival se escanea hasta que llega o hasta `MaxSteps` = 50);
- el Planner (factibilidad);
- la tabla de intercepción de TeamPosition.

Cada uno recorre cuerpo por cuerpo y paso por paso, aunque el cuerpo no pueda llegar. El núcleo agrega dos cosas
exactas:

1. **Exclusión por la línea del pase.**
   - La pelota rodada se mueve en línea recta: las dos componentes de la velocidad se escalan por el mismo factor
     en cada paso.
   - Su largo no supera `|v₀|·dt·MaxSteps`.
   - Un cuerpo a distancia `w` de ese segmento no puede cumplir `dx² + dy² ≤ reach[n]²` para ningún `n` del escaneo
     si `w > reach[último paso escaneado] + 1e-6`.
   - El margen de 1e-6 m cubre la deriva por redondeo de las posiciones respecto de la línea ideal (< 1e-12 m en 50
     pasos).
   - Un cuerpo excluido da exactamente lo mismo que el escaneo:
     - en `StepsFor`, `MaxSteps`;
     - en `ReachesWithin`, el valor de `límite ≥ MaxSteps`, que es lo que devuelve hoy cuando nadie llega.
2. **Primera llegada entrelazada.** `FirstArrival` recorre por paso a todos los cuerpos que quedan. Devuelve el
   mínimo de `StepsFor` sobre el grupo, cortado en `límite`, sin escanear hasta el final a los que no llegan. En el
   Planner, los rivales se miran antes que el receptor en cada paso, que es la regla actual: un rival que llega en
   el mismo paso que el receptor gana.

Firma propuesta, en notación de `.fsi`:

```fsharp
namespace FootballEngine.Core.Match

/// Carreras contra una pelota rodada, compartidas por el Planner, PassIntercept y la tabla de intercepción.
/// Sin estado propio fuera de los rollos por hilo; mismas operaciones en el mismo orden que la rodada actual.
module Race =
    [<Struct>]
    type RaceParams = // hoy InterceptTable.InterceptParams, sin cambios de campos
        { Ball: BallConfig
          PlayerSpeedPerSecond: float<meterPerSecond>
          PlayerAcceleration: float<meterPerSecondSquared>
          ControlRadius: float<meter>
          MaxSteps: int }

    /// Tabla de alcance compartida, publicada entera (hoy `reachesFor`).
    val reaches: RaceParams -> float[]

    [<Sealed>]
    type Roll = // hoy RolledBall: posiciones perezosas xs/ys por paso
        member Reset: RaceParams * Spatial -> unit
        member StepsFor: px: float<meter> * py: float<meter> -> int
        member ReachesWithin: px: float<meter> * py: float<meter> * limit: int -> bool
        /// min(limit, min_i StepsFor(xs[i], ys[i])) sobre los `count` primeros cuerpos.
        member FirstArrival: xs: float[] * ys: float[] * count: int * limit: int -> int

    val rent: RaceParams -> Spatial -> Roll // pool por hilo, como RolledBalls
    val giveBack: Roll -> unit
```

- Archivos:
  - nuevo `Match/Kernels/Race.fs`;
  - `Match/Kernels/BallAero.fs`, movido sin cambios desde `Systems/Ball/Dynamics.fs`, porque Race lo necesita y
    hoy compila en medio de los sistemas.

  Los dos van en el `.fsproj` justo después de `Match/Shared.fs`. `Team/InterceptTable.fs` conserva los agregados
  HELIOS y lee de Race.
- Consumidores:
  - `PassIntercept.margin`: `interceptor = FirstArrival(rivales, MaxSteps)`; la llegada del receptor sigue con
    `StepsFor`.
  - `InterceptTable.computeBothInto`: `StepsFor` con la exclusión.
  - Planner: factibilidad (3.4).

### 3.4 Núcleo de búsqueda de cadenas (Planner)

`Planner.search` conserva su firma en la etapa 1, para que el reemplazo se pueda comparar bit a bit.

- **Firma:** recibe una `ChainState` y `(código, destino, utilidad)[]` y devuelve `ChainEvaluation[]`.
- **Por dentro:** cambia por el algoritmo del apéndice A, ya probado exacto:
  - una arena SoA por hilo en lugar de una `ChainState` por rama;
  - la factibilidad con `Race` (exclusión y orden rival→receptor por paso);
  - el top-k estable por inserción en lugar de ordenar y truncar;
  - la cadena reconstruida desde enlaces al padre, solo cuando mejora el mejor de una raíz.
- **Cachés:** la tabla de pases y la de alcance se publican inmutables y por clave, nunca en un único slot mutable
  compartido. `sweep` corre en paralelo partidos con configuraciones distintas: un slot único sería correcto pero
  se reconstruiría sin parar.
- **Sin `failwith`:** la arena y los buffers crecen al doble, y el tamaño por lado sale de la `ChainState`.
- **Cadenas:** `Chain` solo lo leen los tests (`PlannerTests`). Desde la etapa 4, la producción usa
  `searchInto` (3.5), que no arma cadenas. `search`, con cadenas, queda para tests y diagnóstico sobre la misma
  arena.

### 3.5 El plan como datos

`PendingPlan` (en `Contracts/World.fs`) guarda dos closures: `PlanSearch` y `PlanFinish`. Capturan todo
`prepare`: las opciones, la `ChainState`, el resultado en una celda `ref`, y el estado cognitivo, la condición y la
presión del portador. Eso son ~52 MB de closures más las opciones y la raíz en cada decisión. Pasa a ser un
objeto mutable preasignado, uno por partido, porque hay un solo plan en vuelo (`world.Cognition.Plan` es un único
`voption`):

```fsharp
/// La deliberación del portador. Una por partido y reutilizada. PlayerDecision escribe la entrada; la búsqueda
/// (en el hilo del planner, si lo hay) escribe Result; PlayerDeliberation la lee al vencer, después del Join.
[<Sealed>]
type PlanSlot() =
    member val Active = false with get, set
    member val Due = 0<subtick> with get, set
    member val Side = ClubSide.Home with get, set
    member val Slot = -1 with get, set
    member val Actor = PlayerId 0 with get, set
    // Valores que `prepare` captura hoy: `cognitive` (float de un float32 del frame) y `pressure`
    // (`MatchWorld.opponentsNear`, un int).
    member val Cognitive = 0.0 with get, set
    member val Pressure = 0 with get, set
    member val Options = PlanOptions(16)   // Utility: float[], Code: int[], Target: float[], Count
    member val Root = ChainRoot(11)        // la foto del planner en arreglos propios (hoy ChainState)
    member val Result = PlanResult(16)     // por opción: Value, ActionCode, TargetSlot, Evaluations
```

`finish` mezcla dos fuentes, y el slot tiene que respetar cuál es cuál para que el cambio sea exacto:

- Lee **valores capturados** en `prepare`: `cognitive = float frame.Cognitive.[slot]`, `pressure`, las opciones y
  el resultado. Esos se guardan en el slot.
- En la observación, lee **los arreglos vivos del frame al vencer**: `frame.Condition[slot]` y
  `frame.Cognitive[slot]`. Esos se siguen leyendo del frame en ese momento.

El hash exacto verifica que no se haya cruzado ninguno.

- `Planner.projectInto (root: ChainRoot) ctx world side slot` reemplaza a `Planner.project` sin asignar.
- `Planner.searchInto (root, options, result)` reemplaza a `search` en producción.
- `PlayerDecision.finish (plan: PlanSlot) world tick rng` es la `finish` de hoy, leyendo del slot. Conserva el mismo
  orden de sorteo (`Rng.double` primero), la misma softmax y la misma escritura de `Proposals`.
- El modo de hilo propio queda igual: `planner.Start` recibe una función fija por sesión que busca sobre el slot.
- La observación (`world.Observation.Enabled`) puede seguir asignando: no corre en el tick de un partido rápido.

### 3.6 Tick sin asignación

Estas reglas valen para todo camino que corre en estado estacionario: sistemas, `Stepper`, `EventApply` y núcleos.

| Hoy | Pasa a ser |
|---|---|
| `MatchWorld.slotOf: … -> int option` | `int` con −1 para "no está" (o `int voption`). Se reescriben las comparaciones `Some slot = x`. |
| `MatchWorld.ballCarrier: … -> (ClubSide * int * PlayerId) option` | `struct (ClubSide * int * PlayerId) voption`, sin `Option.map` |
| `for side in [ ClubSide.Home; ClubSide.Away ]` (12 sitios) | `MatchWorld.sides: ClubSide[]` estático |
| `let mutable` capturado por closure (celdas `ref` en ContactDynamics, Position, Stepper) | Campo del scratch del sistema, o bucle explícito |
| Listas por tick (`AttackDuty`, filtros de CollectiveAttack y DefensiveTransition) | Arreglos del scratch del sistema que las produce |
| Tuplas por tick del Stepper (`Flow.fs:25` devuelve `MatchFlow * MatchEvent list`; `Stepper.fs:172`, `Rng * World * int`) | Tuplas `struct` o campos del estado de sesión (etapa 6) |
| `Array.tryFind` / `Array.map` / `Seq.*` con lambdas en bucles calientes | Bucles |
| Closures `runCognition` y `runBody` creadas en cada tick | Creadas una vez por sesión, con campos mutables para lo que cambia por tick |
| `PendingPlan` con closures; `ChainEvaluation[]` por búsqueda | `PlanSlot` (3.5) |

Se permite asignar en tres casos:

- en los eventos y sus registros, que son inherentes: 56k–99k eventos por partido, ~10–20 MB estimados, que la
  etapa 0 mide;
- en el crecimiento de buffers durante el calentamiento;
- en el armado del partido.

El gate exige bytes por tick por sistema (independiente de la máquina) y ≤ 100 MB por partido en cada una de las 4
semillas de `hash`, incluida la 2068, de 364k ticks. Eso equivale a ≤ ~0,27 KB/tick.

### 3.7 Cuerpos en ContactDynamics (exacto)

- **`BodyConstants`**, dentro del `ContactScratch` y con ContactDynamics como dueño. Guarda, por índice de cuerpo:
  el `PlayerId`, el radio, la altura, la validez de la morfología y la aceleración máxima base. Se recalcula cuando
  el `PlayerId` del slot difiere del guardado, que es lo que pasa en un cambio o una expulsión; el costo es una
  comparación por cuerpo y tick. Es exacto porque son los mismos valores con las mismas operaciones, una vez.
- **Pasadas fusionadas:**
  - una sola pasada sobre los 22 para inicializar, calcular el alcance y marcar pelota y pares;
  - una sola pasada para escribir al frame y calcular la marcha.

  Cada cuerpo hace la misma aritmética en el mismo orden.
- **Sin asignación:** la celda `ref` de `ImpactCandidate voption`, los arreglos de tuplas y las listas literales
  pasan al scratch.
- **Prototipo primero, en una copia, como se hizo con el planner.** `F1_ENGINE_REDESIGN.md` §15 avisa que no hay
  punto caliente (~300 ns por jugador y tick en ~10 pasadas), así que la estimación es ancha y la etapa tiene un
  umbral de abandono.

### 3.8 Stepper (exacto)

- **Disparadores:** `fire` se evalúa para ~25 sistemas por tick. Pasa a máscaras precalculadas (banderas de
  eventos, cadencia y compuertas de flujo): un sistema que no puede disparar se salta con un AND y una comparación.
- **Estado por tick:** se crean una vez por sesión las closures y tuplas que hoy se crean en cada tick (sección 1.6).

### 3.9 Representación (clase N)

**N1 — Rodada 1D tabulada, dentro de `Race`.**

- **Modelo:** la pelota rodada va en línea recta, así que la posición en el paso `n` es `origen + u·s(n; v₀)`, con
  `s` la rodada 1D escalonada de hoy (frenar, después avanzar). Hay una tabla `S[v₀, n]` sobre una grilla uniforme
  de `v₀` y `n ≤ MaxSteps`, con interpolación lineal en `v₀`.
- **Resolución:** es `InterceptConfig.RollTableResolution`, 0,01 m/s por defecto. Va en `BalanceConfig`, con su
  porqué y su validación.
- **Rango:** la grilla llega hasta una velocidad máxima validada (por ejemplo, 45 m/s). Por encima, la pelota se
  rueda paso a paso como hoy.
- **Quiebre:** `nextGroundSpeed` recorta la velocidad en cero (`max 0.0 (speed - loss)`). Si la pelota se detiene
  dentro del escaneo, `s(n; v₀)` tiene un quiebre en `v₀` y la interpolación lineal ahí es ~100 veces peor. Por
  eso cada nodo guarda el paso en que se detiene: si alguno de los dos nodos de la celda se detiene dentro de
  `MaxSteps`, la pelota se rueda paso a paso como hoy. Con la config por defecto eso pasa solo con v₀ < 1,25 m/s
  (125 de 4 500 celdas): pelotas que ya casi no ruedan.
- **Tamaño:** ~4 500 × 51 valores float64 ≈ 1,8 MB por configuración, más un entero por nodo. Es inmutable, se
  comparte entre partidos y se guarda en un diccionario concurrente acotado, con clave en los campos de
  `BallConfig` usados, `MaxSteps` y la resolución.
- **Error, medido** con una sonda fuera del repo: `BallAero.dragDeceleration` del build, config por defecto, la
  rodada escalonada como referencia, 11 puntos por celda y n = 0..50.

  | Resolución | Peor error en celdas suaves | Peor error en celdas con quiebre |
  |---|---:|---:|
  | 0,02 m/s | 2,5e-6 m | 1,2e-4 m |
  | **0,01 m/s** | **6,3e-7 m** | 6,0e-5 m (van paso a paso) |
  | 0,005 m/s | 1,6e-7 m | 3,0e-5 m |

  El error baja con h², como corresponde a la interpolación lineal. Hoy la predicción ya difiere de la pelota real
  (efecto, botes) en órdenes mayores. No es un cambio de modelo: una carrera solo cambia de veredicto si alguien
  está a menos de ~1e-6 m del borde de su alcance. El hash cambia; las distribuciones no deberían.
- **Ganancia:** el paso de rodada pasa de ~32 ns seriales (`sqrt`, `exp`, divisiones) a ~2 ns (dos lecturas y una
  interpolación, independientes entre pasos).

**N2 — Cuerpos en float64 como única verdad** (es la propuesta 4.1 de `F1_ENGINE_REDESIGN.md`).

- **Representación:**
  - posición, velocidad y altura de los 22 cuerpos viven en un bloque SoA float64 que escribe solo ContactDynamics;
  - `MatchWorld.posX`/`posY` leen de ahí;
  - `TeamFrame` deja de guardar posiciones;
  - `CognitionView` copia float64.
- **Ganancia:** desaparecen la copia float32→float64 de `initializeBodies` y el redondeo a float32 en la escritura.
- **Por qué es N:** cambia el redondeo, no el modelo.
- **Orden:** el compilador lista cada lector. Se hace al final porque es el cambio de contrato más grande.

### 3.10 Lo que no cambia

- El orden del `SystemsTable` y sus triggers.
- Los eventos y la semántica de `EventApply`.
- `CognitionLatch`, `CognitionView` y el modo de hilo propio.
- `DeliberationTime` (4 ticks).
- Los parámetros del Planner (cadena 4, haz 3, límite 500).
- Los 40 Hz.
- Ningún sistema consume RNG de otra forma.

## 4. Alternativas descartadas

**A. Física a 20 Hz o multi-rate.**

- **Medido:** el 2026-11-13, a 20 Hz el partido bajó de 7,2 a 5,5 s, −24 %. Bajó la física; la cognición no,
  porque va por eventos. Exige partir a la mitad todas las duraciones y cadencias y recalibrar todo.
- **Proyección:** sobre este rediseño, a 20 Hz el partido bajaría de ~2,5 a ~1,9 s en un hilo, y casi nada con
  hilos, porque ahí la cognición ya es el camino crítico.
- **Multi-rate:** la física a 20 Hz solo para los cuerpos lejanos ahorra ~0,4 s (§15 del F1), a cambio de
  posiciones que avanzan cada 50 ms en el visor y cotas con un tick de desfase.
- **Veredicto:** se descarta como camino principal. Tiene el mayor riesgo deportivo para una ganancia que los hilos
  borran. Solo se reabre si, después de este plan, el throughput de tests y calibración sigue siendo el problema.

**B. Recortar trabajo de cognición** (re-decidir menos, cadenas más cortas, haz más angosto, planner "anytime").

- **Medido:**
  - con `RedecisionInterval` de 20 subticks, el +10 baja de 0,65 a 0,35 y falla la aceptación de F3.2;
  - las cadenas de 2 pasos ahorran ~9 % del partido;
  - el haz 2 ahorra 11 % de la búsqueda.
- **Por qué no:** cambia velocidad por calidad de decisión, que es la promesa central del juego. El +10 está en el
  borde (0,55 de promedio, error estándar ~0,14).
- **Veredicto:** se descarta.

**C. Paralelismo primero** (más hilos dentro del partido, ventana cognitiva de K ticks).

- **Medido:**
  - el hilo propio da −30 % de pared hoy, pero con tres hilos auxiliares en espera activa;
  - en los usos que importan desde el ruteo a `QuickMatch` (12 partidos en paralelo en `validate`, `sweep` y la
    suite), los hilos no suman y la espera roba núcleos;
  - la ventana K=4 rinde −19 % ideal (§14 del F1), pero exige rehacer la frontera de `EventApply` y atrasa
    decisiones K ticks;
  - el atraso de un tick en el control motor ya subió el error held-out de 12,45 a 16,57 (§11).
- **Veredicto:** se descarta como camino principal. El hilo propio queda para el partido del usuario.

**D. Núcleos en código nativo o GPU.**

- Cada llamada tendría un costo de interop, y son millones por partido.
- El hash exacto dependería de otro compilador: contracción a FMA, otra `exp`.
- Suma un segundo toolchain.

No se midió: se descarta por razonamiento.

## 5. Contratos, dueños, orden e invalidación

### 5.1 Tipos nuevos o cambiados

| Tipo | Dónde | Etapa | Reemplaza |
|---|---|---|---|
| `Race.RaceParams`, `Race.Roll`, `Race.FirstArrival` | `Match/Kernels/Race.fs` | 2 | `InterceptTable.InterceptParams`, `RolledBall`, `RolledBalls` |
| `BallAero` (sin cambios) | `Match/Kernels/BallAero.fs` | 2 | El módulo dentro de `Systems/Ball/Dynamics.fs` |
| Arena SoA del planner (privada) | `Systems/Players/Planner.fs` | 1 | Una `ChainState` por rama, y las tuplas y `Set` del haz |
| `PlanSlot`, `PlanOptions`, `ChainRoot`, `PlanResult` | `Contracts/World.fs` | 4 | `PendingPlan` con closures; `ChainEvaluation[]` en producción |
| `MatchWorld.slotOf`, `ballCarrier`, `sides` | `Match/Shared.fs` | 4 | `int option`, `option` de tupla, listas literales |
| `BodyConstants` (dentro de `ContactScratch`) | `Contracts` (scratch de ContactDynamics) | 5 | El recálculo por tick en `initializeBodies` |
| `InterceptConfig.RollTableResolution` | `Contracts/BalanceConfig.fs` | 3 | — |
| Bloque de cuerpos float64 | `Contracts/Frames.fs` | 7 | Posiciones float32 en `TeamFrame` y la copia en `ContactScratch` |

### 5.2 Dueños y orden

- **Orden:** el tick no cambia de orden. Los núcleos corren dentro del sistema que los llama y no tienen estado del
  partido.
- **Escritores únicos:**
  - `PlanSlot`: PlayerDecision escribe la entrada, la búsqueda escribe `Result` y PlayerDeliberation lo lee y lo
    apaga. Es la misma secuencia que hoy con `world.Cognition.Plan`, incluido el `Join` del hilo del planner.
  - `BodyConstants` y el bloque float64: solo ContactDynamics.
  - El scratch de cada sistema: solo ese sistema.
- **Lectores:**
  - `BodyConstants` lo lee solo ContactDynamics.
  - El bloque float64 lo lee todo el mundo, a través de `MatchWorld.posX`/`posY`.
  - La cognición en su hilo lee la copia de `CognitionView`: la lista de dueños de `CognitionView` se actualiza en la
    etapa 7, y sus tests de reflexión lo exigen.

### 5.3 Invalidación

| Caché | Clave | Se invalida | Dueño |
|---|---|---|---|
| Tabla de alcance | `RaceParams` | Otra clave: se publica una tabla nueva entera | Race |
| Tabla de pases del planner | Resolución + parámetros de pase de la `ChainState` | Otra clave (diccionario inmutable por clave) | Planner |
| Tabla de rodada 1D | Campos de `BallConfig` usados + `MaxSteps` + resolución | Otra clave (diccionario concurrente acotado) | Race |
| `BodyConstants` | (lado, slot) + `PlayerId` | El `PlayerId` del slot cambió | ContactDynamics |
| Arena, rollos, scratch | Por hilo o por mundo | Nunca: se reutilizan y crecen ×2 al calentar | El núcleo o el sistema |
| `PlanSlot` | Uno por partido | `Active = false` al deliberar | PlayerDecision / PlayerDeliberation |

### 5.4 Determinismo y hash

- **Etapas E:** mismas operaciones en el mismo orden, el mismo consumo de RNG y el mismo orden de eventos. Las
  arenas por hilo no entran en ningún valor. Trampas que el oráculo debe atrapar y la revisión mirar:
  - **FMA:** el JIT no contrae `a*b + c`; nunca usar `Math.FusedMultiplyAdd`.
  - **`min`/`max`:** medido en .NET 10 con FSharp.Core 11, `min`/`max` sobre float dan lo mismo que
    `Math.Min`/`Math.Max`: −0,0 < +0,0 y NaN se propaga. Una comparación escrita a mano
    (`if a < b then a else b`) no da lo mismo: pierde el NaN cuando viene primero y, entre dos ceros, elige según
    el orden de los argumentos. No se reemplazan por comparaciones.
  - **SIMD:** en `Vector256`, `+ − × ÷ √` y las comparaciones son IEEE por carril, igual que en escalar.
    `Vector256.Exp` existe, pero no da los mismos bits que `Math.Exp`: medido, difiere en 40 644 de 100 000
    entradas, hasta 2 ulp. La `exp` de la resistencia queda escalar en las etapas E.
  - **Conversiones:** las conversiones float32↔float64 quedan en los mismos lugares.
- **Hash nuevo:** FNV-1a sobre `MatchEvent.id` de cada evento aplicado, en orden. Es exacto por campo y por bits,
  siempre que el test de completitud de la etapa 0 pase.
- **Referencias por plataforma:** `Math.Exp`, `Atan2`, `Sin` y `Pow` vienen de la libm de la plataforma, así que el
  hash puede diferir entre sistemas operativos. Toda referencia se guarda con la máquina y el SO.
- **Etapas N:** el hash cambia a propósito una vez por etapa, y la nueva referencia va en el commit de la etapa con
  la evidencia held-out. Antes de cerrar la etapa deben pasar el determinismo (misma semilla dos veces) y el test
  `CognitionThread` (un hilo contra hilo propio).

### 5.5 Reglas de arquitectura

Ninguno de los 9 principios de `AGENTS.md` cambia. Se agrega una sola cosa: la carpeta **`Match/Kernels/`**.

- **Qué guarda:** cálculos puros que comparten sistemas de carpetas distintas.
- **Restricciones:** sin estado propio fuera del scratch por hilo, sin eventos y sin RNG; leen sus números de la
  config que reciben.
- **Llamadas:** los sistemas llaman núcleos; los núcleos no llaman sistemas.
- **Precedente:** hoy ese código vive en `Team/InterceptTable.fs` y lo usan Players.

`AGENTS.md` se actualiza en la etapa 2 con:

- la carpeta;
- la regla "buffers SoA mutables solo en núcleos y scratch de sistemas, con su porqué";
- la regla "el tick estacionario no asigna".

## 6. Ganancia esperada y plan de medición

### 6.1 Por etapa (semilla 42, un hilo, en caliente; base 4,24 s)

| Etapa | Clase | Ahorro | Acumulado | Asignado | Cómo se estimó |
|---|---|---:|---:|---|---|
| 0 medición | — | 0 | 4,24 s | 1,84 GB | — |
| 1 búsqueda de cadenas | E | 0,38–0,61 s | ~3,8 s | ~1,0 GB | Medido: el núcleo prototipo dentro del partido |
| 2 núcleo de carrera | E | 0,10–0,20 s | ~3,6 s | ~1,0 GB | `prepare` (≤ 394 ms) + tabla de intercepción (200 ms), × 20–35 %. Como referencia de eficacia, en el planner (etapa 1) la exclusión ya saca 85 de ~91 rivales; en `margin`, hoy un rival que no llega escanea los 50 pasos |
| 3 rodada 1D (N1) | N | 0,30–0,50 s | ~3,2 s | ~1,0 GB | 269 pasos/búsqueda × (32 → ~2 ns) × 42 132 = 0,34 s, más las rodadas de `margin` y la tabla |
| 4 tick sin asignación | E | 0,10–0,25 s | ~3,0 s | ≤ 0,1 GB | Pausa de GC medida 0,15 s. Sacar basura sin sacar trabajo no rindió tiempo en el historial, así que se cuenta la pausa y poca localidad |
| 5 ContactDynamics | E | 0,15–0,35 s | ~2,8 s | ≤ 0,1 GB | Fases medidas × 15–30 %; `initializeBodies` (96 ms) baja casi entera |
| 6 Stepper | E | 0,08–0,15 s | ~2,7 s | ≤ 0,1 GB | 351 ms fuera de los sistemas × 25–40 % |
| 7 cuerpos float64 (N2) | N | 0,15–0,25 s | ~2,5 s | ≤ 0,1 GB | `initializeBodies` + la parte de redondeo de la escritura (167 ms) |

Solo con las etapas exactas (0–2, 4–6): **~3,1 s** (2,7–3,4). Con las N: **~2,5 s** (1,9–3,0). Los rangos suman
el extremo de cada etapa: son el mejor y el peor caso, no un intervalo de confianza.

**Pared con hilo propio.** Es una estimación: se restó el ahorro de cada etapa en su mitad.

| Momento | Cognición | Cuerpo | Serial | Sobrecosto de ráfagas | Pared |
|---|---:|---:|---:|---:|---:|
| Hoy | 9,0 | 6,6 | 1,3 | 1,0 | 11,3 µs/tick (medido) |
| Solo etapas exactas | ~6,3 | ~5,4 | ~0,9 | ~0,9 | ~8,1 µs/tick, ~2,1 s |
| Con las N | ~4,8 | ~4,6 | ~0,9 | ~0,8 | ~6,5 µs/tick, ~1,7 s |

El reparto cambia en cada etapa, así que se vuelve a medir en cada una.

**Throughput.** `validate 48`, hoy 53,6 s, escala con el costo por partido en un hilo y con la caída de la pausa de
GC (~15 % → ~2 %): queda en ~34 s con las etapas exactas y ~28 s con las N. La suite completa baja en proporción a
sus tests de partido completo; se mide antes y después con `scripts/test.fsx`.

### 6.2 Qué meta es realista

| Meta | Veredicto |
|---|---|
| ≤ 100 MB por partido | **Sí**, con etapas exactas (etapa 4). El piso son los eventos. |
| ≤ 1,5 s en un hilo | **No.** Lo realista es ~3,1 s con etapas exactas o ~2,5 s con las N. |
| ≤ 1,5 s de pared | **Al borde**, solo con las N y el hilo propio (~1,7 s estimado), y no es una promesa. |
| Bajar más | Pide un cambio deportivo en la cognición (alternativas B y C), que hoy está medido en contra. |

Propuesta de metas nuevas para F1:

- `validate 48` ≤ 35 s;
- ≤ 3,2 s por partido en caliente en un hilo;
- ≤ 2,2 s de pared con hilo propio;
- ≤ 100 MB por partido.

Si las etapas N aprueban, las metas se ajustan a lo medido.

### 6.3 Cómo se mide

- **Máquina:** siempre la de referencia, con el mismo build, Release y Server GC (como `Training`). Los presupuestos
  de tiempo valen solo ahí; los de bytes valen en cualquiera.
- **Antes y después de cada etapa:**
  - `Training profile --warm 3`: mínimo por sistema de 3 corridas en caliente, más el primer partido aparte;
  - `Training throughput 12`;
  - `Training hash`, exacto, con 4 semillas.
- **Al cerrar cada etapa:** `Training hash 48 --heldout`.
- **En las etapas N, además:**
  - `Training validate 48 --heldout`;
  - `Training contrast 10 144`;
  - `Training hash --own-thread` igual a `Training hash`.
- **Cada informe registra:** commit, máquina, SO, comando, semillas, ticks, µs/tick, B/tick, pausa de GC, pared de
  `validate` y métricas.
- **Comparación:** se comparan µs/tick y totales de `validate 48`, nunca la pared de un partido después de un cambio
  N.

## 7. Plan por etapas

El orden es por ganancia sobre riesgo. Cada etapa deja el motor andando y se verifica sola.

Dueños de archivos sin solapamiento:

- las etapas 4, 5 y 6 pueden ir en paralelo: la 4 excluye `ContactDynamics.fs` y `Stepper/`, que son de la 5 y la 6;
- las etapas 1, 2 y 3 son secuenciales, porque tocan los mismos archivos.

### Etapa 0 — Medición confiable (sin cambio de resultados)

**Cambios:**

- `Training/Program.fs`:
  - `hashCmd` pasa a FNV-1a sobre `MatchEvent.id`. `hash --legacy` conserva `%A` por un commit, para registrar las
    dos referencias, y después se borra.
  - `profile --warm N` descarta el primer partido y lo informa aparte. Por sistema reporta el mínimo de µs/llamada,
    µs/tick y B/tick, más el total y la pausa de GC.
  - Comandos nuevos `allocs` (muestreo por tipo con `EventListener` sobre `GCAllocationTick`) y `throughput N`
    (N partidos en paralelo: pared por partido y fracción de pausa).
- `perf/budget.txt`: se reescribe en µs/tick total (máquina de referencia) y B/tick por sistema, con los valores
  medidos en esta etapa.
- `scripts/dev.fsx`: `check` corre el gate en caliente.
- Test nuevo en `FootballEngine.Core.Tests/Contracts/` (registrado en su `.fsproj` y en `Program.fs`): **cada campo
  de cada caso de `EventData` cambia `MatchEvent.id`.**
  - **Entradas:** eventos reales de un partido, uno por caso visto, más builders para los casos que no aparecen.
  - **Mutaciones:** se muta cada campo por reflexión (+1 ULP en flotantes, +1 en enteros, otro lado, toggle de
    options).
  - **Sensibilidad:** quitar un campo de `hashEventData` hace fallar el test.

**Aceptación:**

- se guardan las referencias nuevas y viejas de las 4 semillas y de las 48 held-out en `66d0eee`;
- el hash es estable en dos corridas y con `--own-thread`;
- `hash` de 4 semillas tarda < 10 s de pared;
- `profile --warm 3` varía < 3 % entre corridas;
- el gate pasa en verde con el presupuesto nuevo.

**Ganancia:** DX. El hash baja de 82 s a segundos, y el perfil queda sin ruido de JIT.

### Etapa 1 — Núcleo de búsqueda de cadenas (E)

**Cambios:**

- `Systems/Players/Planner.fs`: `search` pasa al algoritmo del apéndice A, con la misma firma. Las cachés quedan
  inmutables por clave.
- `FootballEngine.Core.Tests/Systems/Players/`:
  - `PlannerReference` es una copia literal del `Planner.search` actual, que hace de modelo de referencia
    independiente;
  - un test compara las dos implementaciones sobre ≥ 2 000 entradas reales. Se toman de un partido corrido con
    `Stepper`: `Planner.project` del portador más las opciones que arma `PlayerDecision`, para lo cual se expone esa
    construcción como `internal` con `InternalsVisibleTo` hacia Core.Tests. Alcanzan los primeros ~15 000 ticks,
    porque hay una decisión cada ~6 ticks.
  - La igualdad es estructural, con flotantes exactos.
  - Sensibilidad: un margen de exclusión de −0,1 m hace fallar el test.

**Aceptación:**

- hash idéntico en 4 + 48 semillas;
- `PlannerTests` en verde;
- PlayerDecision ≤ 1,1 s y ≤ 0,3 GB por partido en `profile --warm 3`.

**Ganancia:** 0,38–0,61 s (medido); −0,9 GB.

**Abandono:** una diferencia es un bug, porque la equivalencia ya está demostrada. La etapa se abandona solo si en
el partido gana < 0,3 s.

### Etapa 2 — Núcleo de carrera compartido (E)

**Cambios:**

- `Match/Kernels/Race.fs` y `Match/Kernels/BallAero.fs`, nuevos; `FootballEngine.Core.fsproj` los compila justo
  después de `Match/Shared.fs`.
- `Team/InterceptTable.fs`, `Players/PassIntercept.fs`, `Players/Planner.fs`, `Ball/Dynamics.fs` (sin `BallAero`).
- `AGENTS.md`: la carpeta `Kernels/` y sus reglas.
- Tests de propiedades en `FootballEngine.Core.Tests/Kernels/RaceTests.fs`:
  - `FirstArrival = min StepsFor`;
  - `ReachesWithin = (StepsFor ≤ límite)`;
  - la exclusión nunca descarta a un cuerpo que llega, incluso con casos adversos pegados al borde del alcance;
  - una exclusión sin margen hace fallar el test.

**Aceptación:**

- hash idéntico en 4 + 48 semillas;
- `prepare` y la tabla de intercepción bajan en `profile --warm 3`, comparando subfases con temporizadores
  temporales en una rama de medición.

**Ganancia:** 0,10–0,20 s (estimada).

**Abandono:** si gana < 0,05 s, queda solo el movimiento a `Kernels/`, por claridad, sin `FirstArrival`.

### Etapa 3 — Rodada 1D tabulada (N1)

**Cambios:**

- `Kernels/Race.fs`: la tabla, el paso de detención por nodo y su caché por clave.
- `Contracts/BalanceConfig.fs` y `Config/ConfigValidation.fs`: `RollTableResolution`.
- Test de cota de error: la tabla contra la rodada 2D escalonada, en puntos medios de la grilla y en una muestra
  aleatoria con semilla fija. Exige ≤ 1e-6 m en las celdas suaves y la rodada escalonada exacta en las celdas con
  quiebre. Sensibilidad: sin la regla del quiebre, el test falla (6e-5 m medidos).

**Aceptación:** la de la clase N (sección 3.2), contra las métricas held-out de `66d0eee`, que son las mismas que
después de las etapas 1 y 2.

**Ganancia:** 0,30–0,50 s (estimada).

**Abandono:**

- si una métrica sale de su intervalo o el +10 cae más de 1 error estándar, se revierte;
- necesitar recalibración significaría que el juego es sensible a 1e-6 m, y eso es un defecto a informar, no un
  ajuste a hacer;
- también se revierte si gana < 0,15 s.

### Etapa 4 — Tick sin asignación y plan como datos (E)

**Cambios:**

- `Contracts/World.fs`: `PlanSlot` reemplaza a `PendingPlan`.
- `Players/Decision.fs`, `Players/Planner.fs` (`projectInto`, `searchInto`) y `Simulator/MatchSession.fs` (la
  función de búsqueda fija para el hilo del planner).
- `Match/Shared.fs`: `slotOf`, `ballCarrier`, `sides`.
- Los sitios de la sección 1.6, sistema por sistema según `Training allocs`: `Team/CollectiveAttack.fs`,
  `Team/Position.fs`, `Team/DefensiveTransition.fs`, `Players/Intent.fs`, `Players/Actions.fs`,
  `Players/Steering.fs`, `Players/BodyPhysics.fs`, `Manager/Manager.fs`, `Stepper/EventApply.fs`, etc.

**Aceptación:**

- hash idéntico;
- ≤ 100 MB por partido en cada una de las 4 semillas de `hash`;
- B/tick por sistema dentro del presupuesto nuevo;
- `throughput 12` con pausa de GC < 3 % de la pared.

**Ganancia:** 0,10–0,25 s en un hilo (estimada); −10–13 % en throughput.

**Abandono:**

- un sitio cuyo arreglo empeora claramente el código por < 1 MB por partido se deja como está;
- si los eventos solos superan 100 MB, se documenta el piso con la cuenta y se fija la meta ahí.

### Etapa 5 — ContactDynamics (E)

**Cambios:**

- `Systems/ContactDynamics.fs`, partido por responsabilidad como pedía F3.3: cuerpos, barrido y respuesta.
- `ContactScratch` (con `BodyConstants`).

**Paso previo:** prototipo en una copia, con temporizadores por fase y hash exacto.

**Aceptación:** hash idéntico; ContactDynamics −15 % o más en `profile --warm 3`.

**Ganancia:** 0,15–0,35 s (estimada).

**Abandono:** si el prototipo no llega a −15 % con hash idéntico, quedan solo las constantes y la limpieza de
asignación.

### Etapa 6 — Stepper (E)

**Cambios:** `Stepper/Stepper.fs` y `Stepper/Scheduler.fs` (máscaras y estado por sesión).

**Aceptación:** hash idéntico; el tiempo fuera de los sistemas baja ≥ 20 %.

**Ganancia:** 0,08–0,15 s (estimada).

**Abandono:** si baja < 10 %, se revierte todo menos la limpieza de asignación.

### Etapa 7 — Cuerpos float64 como única verdad (N2)

**Cambios:**

- `Contracts/Frames.fs`, `Systems/ContactDynamics.fs`, `Match/Shared.fs` (`posX`/`posY`).
- `Stepper/CognitionView.fs` y sus listas de dueños.
- La proyección de replay en `Simulator/`.
- Todo lector que liste el compilador, incluido el visor en Client y `FootballEngine.Client.Tests`.

**Aceptación:**

- la de la clase N;
- `CognitionThread` en verde;
- el visor sin cambios visibles: se verifica con el flujo de capturas `Shots/`.

**Ganancia:** 0,15–0,25 s (estimada; §4.1 del F1 estimó 0,3–0,5 s sobre otra base).

**Abandono:** si gana < 0,15 s o falla la aceptación N, porque es el cambio de contrato más grande.

## 8. Riesgos y cuándo abandonar

| Riesgo | Mitigación / criterio |
|---|---|
| **Estimaciones optimistas.** Solo la etapa 1 está medida; el F1 ya encontró que los retoques locales dan 2–5 %. | Cada etapa estimada se mide en una copia o rama antes de integrarse. **Criterio global:** si después de las etapas 1 y 2 el ahorro acumulado es < 0,5 s, se revisan las estimaciones de 4–6 antes de seguir, porque descansan en el mismo tipo de cuenta que la etapa 2. |
| **Exactitud sutil:** FMA, orden de operaciones, `min`/`max`, −0,0, SIMD, conversiones. | Un oráculo por núcleo con entradas reales, hash exacto en 52 semillas y pruebas de sensibilidad que demuestran que el oráculo detecta un núcleo mal hecho. |
| **El oráculo cubre de menos.** | Test de completitud de `MatchEvent.id` (etapa 0). Referencias por máquina y SO. |
| **Hilos.** | Arenas y rollos por hilo; un solo plan en vuelo; `CognitionThread` y `hash --own-thread` en cada etapa. |
| **Las etapas N cambian el partido** (el número de ticks varía 259k–364k). | Se comparan µs/tick y `validate 48`, no partidos sueltos. Mismas 48 semillas y mismos intervalos. |
| **Legibilidad.** El código SoA es menos idiomático. | Queda en pocos archivos (Planner, Race, ContactDynamics), con comentarios de porqué ("mismas operaciones en el mismo orden que X, para…"). Lo demás sigue en F# idiomático: `voption` y tuplas `struct` también lo son. |
| **Presupuestos dependientes de la máquina.** | Tiempo solo en la máquina de referencia; bytes en todas. |
| **Alcance que se infla.** | Ninguna etapa toca lógica deportiva. Un defecto deportivo encontrado en el camino va por separado, con su evidencia. |
| **Primer partido en frío** (~1,5 s extra; es el "resultado instantáneo" del usuario). | Fuera de este rediseño. Medido: ninguna variable del JIT lo baja sin perder el PGO. La opción a evaluar aparte es calentar el motor en segundo plano al cargar la carrera. |

## Apéndice A — Algoritmo del núcleo del planner (probado exacto)

Es el algoritmo del prototipo que dio 0 diferencias en 8 426 búsquedas y listas de eventos idénticas. Todo lo que no
se dice aquí es igual a `Planner.search`: mismas fórmulas, mismo orden de raíces, mismos presupuestos.

**Arena por hilo** (`ThreadLocal`). La capacidad inicial es 256 estados y crece al doble. Por estado `st`:

| Campo | Contenido |
|---|---|
| `OurX`, `OurY`, `OppX`, `OppY` | `[st·11 + i]`, en el orden de `OurPos`/`OppPos` |
| `BallX`, `BallY`, `Carrier`, `Line`, `Horizon` | Pelota, portador, línea de offside y horizonte |
| `Visited` | `SlotMask` |
| `Parent` | El estado de origen |
| `ActKind`, `ActSlot`, `ActX`, `ActY` | La acción que produjo el estado |
| `Score` | |

**Por búsqueda:**

- La velocidad de cada posición se resuelve una vez, con la misma búsqueda por slot que hace `moved`.
- `ourIndexOf` y los arreglos de slots se arman al empezar.
- La tabla de pases se toma de la caché por clave.

**`project`** es `advanceWith`, con estos detalles:

- el jugador fijado toma su posición;
- quien tiene velocidad avanza `x + vx·flight` y se recorta con `min halfL (max -halfL x)`, igual en `y`;
- quien no se mueve copia su posición;
- la línea es el rival de campo más profundo, `max (x·dir)`, y vuelve multiplicada por `dir` solo si hay movimiento
  rival; si no, se hereda;
- el horizonte es `max 0 (horizonte − flight)`.

**`valueOf`** es `Planner.value`: busca el rival más cercano con `sqrt`, da apoyo si el primer compañero distinto del
portador está a menos de `SupportDistance`, y termina en `DecisionValue.held`.

**`pass st slot`** hace las comprobaciones en este orden:

1. índice;
2. offside, con `x·dir > max 0 (max (bx·dir) (línea·dir))`;
3. portador o arquero;
4. distancia mínima y máxima;
5. la tabla interpolada: `índice = min (int (d/res)) (n−2)` y fracción lineal en velocidad y segundos;
6. el horizonte;
7. la factibilidad.

Después proyecta y pone pelota, portador y acción.

**Factibilidad exacta:**

```text
si d ≤ 0 o d > maxPass: falso
u = (receptor − pelota)/d;  L = v₀·dt·MaxSteps + 1e-6;  R = reach[MaxSteps−1] + 1e-6
candidatos = rivales de campo cuya distancia al segmento [pelota, pelota + u·L] sea ≤ R
  (along = clamp(0, L, p·u); c = p − along·u; |c|² ≤ R²)
v = u·v₀; x = pelota
para k = 0 .. MaxSteps−1:
    si k > 0: sp = √(vx²+vy²); si sp > 1e-9: pérdida = (rr·g + BallAero.dragDeceleration sp)·dt;
              escala = max 0 (sp − pérdida)/sp; v = v·escala
              x = x + v·dt
    r² = reach[k]²
    si algún candidato cumple (cx−x)² + (cy−y)² ≤ r²: falso
    si el receptor lo cumple: verdadero
falso
```

**Haz:**

- Raíces en el orden de las opciones.
- Por nivel 2..`MaxChainLength`, por raíz y por estado del haz, en este orden:
  1. el tiro, si está a menos de `ShootingRange`;
  2. pases a compañeros no visitados y no arqueros, en orden de posición;
  3. la conducción.
- `spend()` y `frontierLimit` reparten el presupuesto entre las raíces que quedan, como hoy.
- El top-k es estable: se inserta después de todos los guardados con puntaje ≥, lo que equivale al orden estable más
  truncado de hoy.
- La cadena se reconstruye desde `Parent` solo cuando el puntaje de una raíz mejora.

## Resultado de la implementación (2026-11-15)

Medido en la máquina de referencia, semilla 42, `Training profile --warm 2` (con el cronómetro por sistema puesto).

| | Antes (`66d0eee`) | Después |
|---|---:|---:|
| Partido en caliente, un hilo | 5,0 s (19,1 µs/tick) | **2,79 s (10,7 µs/tick)** |
| Pared con hilo propio | — | **2,18 s** |
| Asignado por partido | ~1,9 GB | **93 MB** |
| Pausa de GC por partido | 174 ms | 30 ms |
| `validate 48 --heldout` | 53,6 s | **37,0 s** |
| `hash` (4 semillas) | 82 s, inexacto | 9 s, exacto por bits |

- **Resultados deportivos sin cambios:** el hash exacto de las 4 semillas (`02649d27c08b6269`) y el de las 48
  held-out (`89afd669e671a36b`) son los mismos que en `66d0eee`, y la salida de `validate 48 --heldout` es idéntica.
- **Etapas hechas:**
  - 0: hash exacto, `profile --warm N`, total asignado y `perf/budget.txt` nuevo;
  - 1: núcleo del planner, con un test de equivalencia contra una copia literal del original;
  - 2: exclusión y `FirstArrival` en `InterceptTable` y `PassIntercept`, con cachés por clave;
  - 3: rodada 1D tabulada con la regla del quiebre, que **no cambió ningún hash** porque las carreras devuelven pasos
    enteros;
  - 4: plan como datos (`PlanSlot`), `voption` en `MatchWorld` y episodios con dueño mutable;
  - 5: ContactDynamics, con −66 % de asignación pero solo ~5 % de tiempo;
  - 6: Stepper, con disparadores en máscaras: limpieza, la ganancia quedó dentro del ruido.
- **Descartada, la etapa 7 (cuerpos en float64):** se implementó y se midió. ContactDynamics quedó en 4,63 µs por
  llamada contra 4,46 sin el cambio, así que no ganó nada y cambiaba resultados. Por el criterio de abandono no se
  integró.
- **Dónde queda el costo:** ContactDynamics ~1,0 s, PlayerDecision ~0,45 s, TeamPosition ~0,36 s, PlayerSteering
  ~0,24 s, y Stepper más resto ~0,33 s. Para bajar más hay que trabajar sobre la aritmética real del contacto
  (integración de la pelota, barridos, trigonometría de la marcha), no sobre la asignación.
- **Los núcleos no se movieron a `Match/Kernels/`:** quedaron en `Team/InterceptTable.fs`, donde ya estaban, así que
  `AGENTS.md` no cambia.
