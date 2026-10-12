# Movimiento procedural de personajes — arquitectura y plan

## 1. Decisión y alcance

Diseño para FootballEngine en desarrollo, sin usuarios ni compatibilidad de formatos antiguos. Objetivo: movimiento de calidad de producción, con apoyos creíbles, peso, intención y contactos fieles al partido, manteniendo la simulación determinista y la presentación de alto rendimiento.

**Decisión:** sustituir la selección de clips por un planificador de movimiento procedural condicionado por restricciones, con evaluación continua de poses. El motor deportivo sigue siendo la única autoridad. No implementar un segundo simulador de fútbol, un controlador ragdoll activo general ni ruido aleatorio para ocultar defectos.

“AAA” es el objetivo artístico y técnico, no una propiedad garantizada por esta arquitectura. La aceptación exige secuencias nativas cercanas, referencias de movimiento humano, métricas y revisión artística. El plan no promete que un humanoide de calidad insuficiente parezca real sólo por cambiar su animación.

Este documento gobierna la implementación P0–P7 y su evidencia. No autoriza push ni creación de PR/issues. Estado inicial: checkout compartida, únicamente este documento sin seguimiento; preservar trabajo ajeno.

### Incluido

- Locomoción hacia delante, lateral y hacia atrás; aceleración, frenado, pivotes y cambios de dirección.
- Apoyos talón/planta/punta, transferencia de peso, pelvis, torso y brazos coordinados.
- Conducción, preparación, golpe, recepción y recuperación con restricciones de contacto.
- Salto, aterrizaje y reacciones a hechos deportivos explícitos.
- Identidad corporal y fatiga; atención visual sólo con información que el motor expone.
- Reproducción, pausa, velocidad, seek, sustituciones, pérdida de muestras y LOD sin dependencia del FPS.
- Implementación obligatoria de arqueros, caídas, cabezazos, manos y reacciones mediante productores y contratos explícitos; extensibilidad no sustituye cobertura.

### No incluido como atajo

Generar movimiento con una IA online durante el partido, copiar assets de FIFA, desplazar la pelota para alcanzar el pie, modificar la raíz deportiva para corregir una pose, teletransportar apoyos, estirar huesos ni conservar un renderer de clips como fallback.

## 2. Diagnóstico del código actual

Base de diseño: stack documentado en `docs/reviews/DEVELOPMENT_STACK_2026_10_07.md`.

| Ubicación actual | Lo aprovechable | Límite a sustituir |
|---|---|---|
| `Core/Match/Contracts/Frames.fs` | Posición/velocidad 3D, orientación corporal, distancia recorrida, condición, salto | No contiene apoyos, postura corporal ni toda la evidencia de acciones necesaria para una animación completa. |
| `Core/Match/Systems/Ball/FootContact.fs` | Preparación y compromiso deportivo; pie y punto de contacto | Alcance horizontal/altura son proxies; no prueban que el rig pueda alcanzar manteniendo soporte. Contacto de recepción tiene poca anticipación. |
| `Core/Match/Simulator/Replay.fs` | Replay v10 con raíz, velocidad, giro y `FootMotion` | Un único envelope alojado en la pelota puede sobrescribirse; un replay espaciado pierde contactos entre muestras. No basta como pista de movimiento duradera. |
| `Client/AppState/MatchPresentation.fs` | Tiempo de presentación, secuencias, epochs y discontinuidades | La ventana de poses no es por sí sola un historial de apoyos o un checkpoint de animación. |
| `Client/Views/Components/MatchProjection.fs` | Interpolación física y de contacto | La aceleración empieza en cero en la proyección; no usar ese cero como evidencia de ausencia de esfuerzo. Derivadas/interpolación deben validarse. |
| `Client/Match3D/CharacterAsset.fs` | Jerarquía, longitudes, skin, shaders e integridad de assets | Clips pre-muestreados, strings/Map, IK de un pie sin límites articulares ni soporte global. |
| `Client/Match3D/WebGpuStadiumRenderer.fs` | Instancing, paletas, backpressure y thread propietario | Decide clips y corrige huesos dentro del renderer; transición dura por velocidad/acción y tiempo dividido por `40.0`. |

Consecuencia: hoy se corrige el pie sobre una pose ya elegida. El diseño nuevo invierte el orden: **primero soporte y tarea; después movimiento del cuerpo que los satisface**.

## 3. Autoridad y fronteras

```text
Core: raíz / movimiento / preparación / contactos / participación / hechos deportivos
  ↓ captura inmutable y ordenada; reloj y unidades explícitos
Client Animation: plan de soporte + tareas + continuidad + pose corporal
  ↓ PosePalette + métricas visuales, sin escribir en Core
Match3D: skinning / cámara / iluminación / presentación
```

1. El planificador visual no modifica `World`, RNG deportivo, balón, reglas, decisiones ni tiempos de compromiso.
2. La raíz de un personaje representa exactamente la raíz de Core. Pelvis y postura admiten correcciones **locales acotadas**, no root motion oculto.
3. La animación no atribuye fuerza, falta, posesión, lesión o contacto nuevo. Una inclinación estética no es evidencia deportiva.
4. Si el movimiento deportivo es imposible para el rig, registrar residual y fallar el gate de calidad. Corregir el proxy/contrato o el rig en su propietario, con regresión deportiva explícita; nunca “rescatarlo” moviendo el balón.
5. Un ajuste que afecta fútbol pertenece a `BalanceConfig`; uno exclusivamente visual pertenece a `AnimationConfig` validada y versionada. No contaminar el balance deportivo con preferencias de cámara o curvas de brazos.
6. Core no conoce huesos, GLB, cámara ni LOD. Los metadatos físicos de morfología/proxy son independientes del rig.

## 4. Contratos nuevos, sin legacy

### Evidencia de movimiento

Diseñar un contrato inmutable, independiente de `RenderPlayer` y del mesh. Identidad: jugador + generación de participación, no índice visual. El slot es sólo una dirección GPU.

- `MotionRootSample`: tick de frontera, posición/velocidad, facing, velocidad angular, condición continua, modo grounded/airborne y secuencia de salto. Derivadas con procedencia definida; no inferir aceleración cruzando discontinuidades.
- `MotionTask`: identidad de ocurrencia secuencial, actor, familia, effector, fase, inicio/compromiso/fin, objetivo espacial y su revisión. Separar tarea prevista de contacto confirmado/cancelado.
- `MotionContact`: identidad, instante y effector real, punto/superficie/normal cuando existen, balón y cinemática entrante/saliente cuando el productor las conoce. Campos no medidos no se rellenan con ficción.
- `MotionDiscontinuity`: participación, teleport/reinicio espacial, regresión temporal, hueco de datos o nueva sesión; no todo cambio de `MatchFlow` debe borrar un apoyo todavía válido.
- `MotionProfile`: morfología, lateralidad y estilo estático. Fatiga viene de Core, no de un contador de animación.

La pista de tareas/contactos es **ordenada y multi-actor**: no usar `BallState.FootMotion` como almacén del cuerpo entero. El flujo vive publica preparación y cambios, no sólo el evento final. Puede coexistir temporalmente durante implementación interna, pero la entrega del corte elimina el contrato sustituido y sus consumidores, sin adaptador público.

No mezclar el ledger visual de ocurrencias con `MatchEvent.id` estructural ni con el inspector de observación: tienen objetivos y políticas de retención distintos. Un contacto visual referencia su acción/hecho deportivo cuando esa identidad existe.

### Tiempo y coordenadas

- Un único `MotionTime` basado en ticks de frontera + fracción; la duración de tick viaja en el encabezado. Prohibido introducir otros `/40.0` en animación.
- Core: X/Y del campo, Z vertical; personaje/renderer: Y vertical, +Z forward. Conversión única probada de vectores, quaternions y matrices, usando la convención existente de `System.Numerics`.
- Altura del balón distingue underside y centro; no confundir centro con punto de superficie o pivote de bota.
- Separar marco local de pie, socket de contacto y superficie de bota. La punta del hueso `toe` no define todos los contactos posibles.

### Replay y paquetes

Reservar un **nuevo corte de replay (v11 si sigue siendo el siguiente disponible)** con pista de movimiento y anclas/checkpoints visuales. Rechazar v10 y anteriores en el nuevo lector. No preservar `Spin` antiguo ni readers duales.

El paquete de rig pasa a un nuevo esquema: bind pose, tabla semántica de articulaciones, longitudes, rangos swing/twist, polos, sockets de bota/palma/cabeza, geometría de soporte, morfología permitida y coeficientes de movimiento. Validar fuera del hot path. Nombres de huesos se resuelven una vez a índices; IDs de familias/estilos no son strings por frame.

## 5. Pipeline procedural

### A. Preparación de entradas

Consumir las muestras deportivas en orden, **una sola vez por muestra**, independientemente de cuántas veces se presente un frame. Conservar una ventana pequeña para curvas y predicción; no ordenar, buscar todo el roster ni reconstruir arrays en cada render.

La velocidad local se calcula respecto al facing deportivo; permitir backpedal y side-step. El avatar no puede girar su raíz visual para fingir que corre hacia delante. Un predictor de trayectoria visual sirve para aterrizar el próximo pie, no para tomar decisiones de fútbol.

### B. Planificación de apoyos

Cada pie mantiene una fase: `Support`, `ToeOff`, `Swing`, `Landing`; el controlador corporal distingue `Standing`, `Locomotion`, `Pivot`, `Airborne`, `Task`, `Recovery`.

- Histeresis y ventanas mínimas de compromiso evitan que una pequeña variación de velocidad cambie todo el gait.
- Cadencia/longitud de paso se adaptan a velocidad, aceleración, giro, morfología y tarea. No multiplicar tiempo global por velocidad ni reiniciar fase al cambiar modo.
- Durante soporte, el ancla queda fija en mundo; cambia la zona de apoyo talón/planta/punta mediante roll de pie, no deslizando el ancla.
- Proyectar aterrizajes dentro de alcance, separación de pies y límites de cruce. En giros, resolver pisada exterior/interior y pivote antes de acumular torsión imposible.
- Si la raíz se aleja del apoyo, anticipar liberación o paso de recuperación; no compensar indefinidamente con pelvis o rodilla.
- Distinguir vuelo de carrera, salto de Core y doble soporte al caminar. No forzar dos pies al suelo durante un salto.

Optimización: elegir entre un conjunto **fijo y pequeño** de candidatos de pisada; score de alcance, soporte, trayectoria y preparación. Sin search combinatorio, sistema ML online ni solver genérico.

### C. Trayectorias de effector

Planificar splines por tramos con posición y velocidad de frontera; continuidad C1 obligatoria, C2 donde sea practicable. Perfil de elevación y toe clearance depende del paso. La bota orienta la superficie adecuada, no sólo un punto.

Un objetivo replanificado conserva la pose/velocidad actual. No reemplazar toda la curva y producir un salto. Diferenciar objetivos previstos de contactos confirmados: la captura posterior puede ajustar el tramo final, pero nunca reescribir el pasado presentado.

### D. Peso y cuerpo

Estimar soporte y transferencia de peso con un modelo cinemático reducido. Ajustar pelvis dentro de un volumen permitido para satisfacer piernas, balance y preparación; no afirmar que es un cálculo dinámico exacto del centro de masa.

Inclinación responde a aceleración/frenado/giro filtrados en tiempo de simulación. Pelvis y tórax contrarrotan; brazos compensan fase, carga y tarea. Cabeza/atención tienen rangos y tasas máximas y sólo apuntan a targets expuestos; no consultar posiciones ocultas para “mirar inteligentemente”.

Filtros/springs tienen integración estable con dt conocido o solución analítica. Prohibido usar `lerp(alpha constante por frame)` como suavizado: cambia con el FPS.

### E. Tareas futbolísticas

- Un golpe reserva pie de acción, ventana de soporte y espacio para pelvis/torso/brazos desde preparación. Contacto confirmado manda sobre estilo.
- Resolver aproximación, superficie de bota, orientación y velocidad de effector en el instante de compromiso. Un punto cercano en un frame aislado no prueba un golpe coherente.
- Recepción usa cinemática relativa y amortiguación visual; conducción alterna sólo cuando la evidencia deportiva lo permite. No inventar touches para hacer bonito el gait.
- Tareas se componen con locomoción mediante máscaras semánticas y prioridad de restricciones, no interpolando dos esqueletos enteros con un peso arbitrario.
- Choques, cabezazos, tackles, manos y arquero requieren evidencia y familia propias. Sin ésta, no convertir proximidad o `HasBall` en un contacto confirmado.

### F. Resolver restricciones

Orden explícito: raíz autoritativa → contacto comprometido → soporte válido → alcance/límites articulares → balance local → silueta/estilo → movimiento secundario.

Resolver piernas/brazos con IK analítica y límites swing/twist; una corrección corporal de iteraciones fijas para tareas acopladas. Las prioridades no autorizan violar anatomía: si las restricciones duras no tienen solución, devolver residual tipado, pose acotada y diagnóstico. Nunca iterar hasta convergencia sin límite.

Precalcular cadenas y rangos descendientes. Construir una vez transformaciones globales y paleta final; no reescribir toda la paleta después de corregir cada effector.

### G. Naturalidad controlada

Estilo es variación de amplitud, timing, anchura de paso, pelvis y brazos dentro de restricciones. Semilla visual independiente por identidad de jugador + episodio; no RNG deportivo ni `Random` por frame.

Evitar sincronización de 22 jugadores sin agregar jitter a apoyos/contactos. Asimetría coherente y fatiga real son preferibles a ruido. Ruido de alta frecuencia en articulaciones no representa vida humana.

## 6. Continuidad, replay y seek

El planificador **sí tiene estado** (apoyos, fase, velocidad de filtros); fingir que cada pose puede generarse desde un snapshot aislado produce robotización o sliding. Su evaluación de un plan cerrado es pura: `plan + MotionTime → pose`.

- Estado y planes pertenecen al runtime de presentación, no al dispositivo GPU. Reattach/recreación de renderer no reinicia gait.
- Checkpoints visuales acotados contienen fase, anclas, planes activos y filtros; usan versión del planificador/config/rig y referencia de pista.
- Seek restaura el ancla precedente y reconstruye con muestras deportivas, no con renders previos. Paso de planificación igual en live y replay; ninguna exigencia de reproducibilidad binaria entre arquitecturas sin medirla.
- Archivo replay permite regenerar planes con versión/config pinneadas y checkpoints; no almacenar una matriz por hueso por frame para el partido entero.
- En pausa se conserva exactamente la pose. Cambiar 1x/2x/4x cambia el avance de `MotionTime`, no las curvas del movimiento ni su estilo.
- Una muestra repetida no avanza estado. Un salto de timeline no dispara miles de pasos en el render thread: restauración/reconstrucción se hace en trabajo acotado fuera de presentación, mostrando hold explícito hasta recuperar contexto.
- Un hueco de poses o tareas invalida continuidad conocida; telemetría explícita, nueva ancla controlada. No inventar un contacto que se perdió.

## 7. Integración y ownership en F#

Nueva carpeta `FootballEngine.Client/Animation/`, compilada antes de Match3D:

| Módulo | Responsabilidad única |
|---|---|
| `Contracts.fs` | Entradas, tiempo, IDs, tareas, planes y resultados/residuales. |
| `Config.fs` | Parámetros visuales versionados y validación. |
| `RigDefinition.fs` | Semántica de rig/proxies/sockets, límites y tablas precalculadas. |
| `MotionInput.fs` | Adaptación desde replay/presentation, coordenadas y discontinuidades. |
| `Trajectory.fs` | Splines, derivadas, predicción acotada y cambio continuo de objetivos. |
| `SupportPlanner.fs` | Fases, anclas, selección de pisada, pivotes y recuperación. |
| `TaskPlanner.fs` | Preparación/contacto/cancelación y reserva de effectors. |
| `BodyPose.fs` | Pelvis/torso/brazos/atención condicionados al plan. |
| `ConstraintSolve.fs` | IK limitada, acoplamiento acotado y residuales. |
| `PoseEvaluation.fs` | Evaluación continua y una construcción final de paleta. |
| `MotionRuntime.fs` | Consumo secuencial, buffers, checkpoints y reconstrucción. |

No crear un framework universal de nodos/plugins ni un archivo monolítico `AnimationEngine.fs`. Funciones matemáticas puras; buffers con owner explícito; `Result` en carga/validación. Fallos del hot path son valores diagnósticos, no `failwith`.

`CharacterAsset` se limita a mesh/texturas y carga de definición compilada; se elimina el actual `CharacterClip`, `Clips` y `CharacterPose` de runtime. `WebGpuStadiumRenderer` recibe raíces y paletas, no elige gait, task, pie ni clip. Se elimina `PlayerVisualState` como selector de movimiento; un enum de UI independiente sólo permanece si tiene consumidor real.

Datos autorados siguen siendo útiles como referencias, siluetas y curvas parametrizadas **compiladas offline**. No son un segundo camino de reproducción de clips. No reconstruir datasets masivos ni hacer muestreo GLTF al iniciar cada renderer.

## 8. Rendimiento y presupuestos iniciales

Son **objetivos de aceptación por medir**, no resultados actuales. Medir en Ryzen 5 5600X/RX 5500 XT como baseline local, además de una máquina inferior antes de declarar portabilidad.

- Planificador: O(jugadores × candidatos fijos), presupuesto inicial p95 ≤ **0,30 ms** por muestra para 22 jugadores.
- Evaluación/constraints/paletas: p95 ≤ **0,50 ms** para 22 jugadores a calidad cercana, fuera de GPU skinning. Reportar p99 y max, no sólo promedio.
- Hot path estable: **0 B de asignación managed** para planificación/evaluación con buffers preasignados; carga, exportación y cambio de capacidad se miden aparte.
- Buffers de estado/planes/checkpoints de la ventana live: objetivo ≤ **2 MiB** para 22 jugadores, excluyendo mesh/texturas y archivo replay. Retención no crece con duración del partido.
- Primera evaluación tras seek: presupuesto explícito de latencia separado; ninguna pausa larga en render thread ni recomputación desde kickoff.
- No cap artificial de FPS. Mantener instancing y skinning GPU actual; no 22 meshes duplicados ni un draw por hueso.

Usar arrays planos de structs/SoA según profiling, arenas fijas por instancia, índices de rig y ventanas indexadas. Sin `Map<string,...>`, LINQ, reflexión, búsqueda de cadenas, listas de candidatos o closures capturantes por pose. Primero CPU SIMD/analítica y GPU skinning; compute GPU para pose sólo si la medición demuestra beneficio neto incluyendo sincronización y debug.

LOD es de **evaluación visual**, no de planificación: el plan base no cambia al mover cámara. Reducir secundarios y trabajo fino para personajes lejanos con hysteresis/crossfade; no degradar contacto crítico o soporte. No “saltar ticks” del plan para ahorrar CPU. El MVP mantiene los 22 jugadores a igual calidad hasta tener métricas estables.

## 9. Calidad medible y pruebas

Umbrales iniciales que deben calibrarse con rig, cuantización y referencias:

| Gate | Criterio inicial |
|---|---|
| Apoyo | Drift horizontal ≤ 1 cm durante soporte válido; registrar p95/max, no ocultar deslizamiento con cámara. |
| Contacto cercano | Residual de superficie ≤ 2 cm; compromiso evaluado en su frontera exacta, sin tolerancia de un tick que esconda un desfase sistemático. |
| Anatomía | Longitudes conservadas con tolerancia numérica; límites articulares respetados; cero NaN/Inf, huesos invertidos o rodillas que cambian de polo sin transición. |
| Continuidad | Pose/velocidad C1 al replanificar; saltos por discontinuidad permitidos sólo con causa registrada. |
| Tiempo | Misma pose a igual MotionTime con presentación 30/60/144/240 Hz; 1x/4x, pausa y seek dentro de tolerancia definida. |
| Autoridad | RNG, events hash y resultado deportivo iguales con animación off/on, diferentes LOD/cámaras y render stall. |
| Naturalidad | Revisión temporal de apoyo, equilibrio, esfuerzo, silueta y variación; aprobar también con cámara fija y sin blur. |

Corpus obligatorio: idle→arranque, caminar→sprint→freno, giro de 45/90/180°, giro a alta velocidad, lateral/backpedal, pasos cortos cerca de pelota, golpes con ambos pies en movimiento, recepción entrante lateral, cancelación, contactos seguidos, salto/aterrizaje, agotamiento, sustitución, reinicio y seek durante contacto. Arquero/manos/cabeza requieren implementar productores deportivos reales, nunca inferir hechos por proximidad.

Pruebas unitarias matemáticas + properties de restricciones + integración de replay/epochs + pruebas de asignación + benchmark headless de 22 actores. Tests en `Client.Tests/Animation/`; contratos deportivos nuevos en `Core.Tests`. Las métricas visuales no sustituyen screenshots/vídeos nativos: capturar secuencias preparación→contacto→recuperación y slow motion. Comparación AAA requiere material equivalente y criterios explícitos.

## 10. Plan de implementación por gates

| Gate | Trabajo / entrega revisable | Salida necesaria |
|---|---|---|
| P0 — Evidence first | Corpus actual, slow motion, residuales del rig, baseline CPU/memoria y necesidades de contratos. | Diagnóstico repetible; identificar casos deportivos físicamente incompatibles, no ajustar todo “a ojo”. |
| P1 — Contratos y rig | Pista multi-actor, identidad/tiempo/procedencia, definición de rig, sockets y límites; contenedor replay independiente. Corte productor→pista→runtime→palette→renderer+checkpoint. | Fixtures ejecutables temporales y roundtrip/rechazo; transporte separado del último-input; corte vertical funcionando. |
| P2 — Núcleo de soporte | Runtime secuencial, fases, splines, anclas, IK con límites, standing/walk/run/sprint/lateral/backpedal/pivot. | Foot-lock y continuidad en 22 actores; independencia 30/60/144/240 Hz y seek básico ya probados. |
| P3 — Peso y estilo | Pelvis/torso/brazos, carga, fatigue/profile y replanificación. | Secuencias de arranque/freno/giro creíbles; independencia del FPS y presupuesto. |
| P4 — Tareas y contactos | Planificación completa desde preparación, superficie/velocidad de bota, cancelación/recepción y soporte de acción. | Ambos pies, tareas consecutivas y móvil; residuales/timing y raíz deportiva aprobados. |
| P5 — Timeline y renderer | Replay nuevo/checkpoints/seek, runtime separado de GPU; sustituir selección de clips y contrato antiguo. | Live/replay equivalentes; reattach/pausa/seek; ningún legacy path restante. |
| P6 — Cobertura corporal | Salto/aterrizaje, manos/arquero/cabeza/reacciones con evidencia explícita, mejoras de rig/mesh donde necesarias. | Corpus completo de las familias soportadas; ausencia de gestos que inventan fútbol. |
| P7 — Producción | LOD fino, profiling CPU/GPU, controles de debug, documentación y revisión artística comparativa. | Gates funcionales/visuales/performance completos; deuda y límites declarados. |

P1 y P5 requieren cortes de contratos; implementar en branch de desarrollo y aterrizar un stack integrado, no mantener dos backends productivos por conveniencia. Los prototipos offline no se convierten en fallback. Cada gate incluye tests y evidencia antes de avanzar; no acumular meses de código sin inspección temporal.

Prioridad recomendada: **P0→P2 antes de ampliar gestos**. La calidad de apoyos, frenado y transferencia de peso determina cómo se siente todo el partido; una biblioteca grande de celebraciones no arregla locomoción robótica.

## 11. Riesgos que bloquean una declaración AAA

1. Alcance deportivo incompatible con piernas reales: resolver con proxy/morfología compartidos y cambios deportivos deliberados; no stretch.
2. Recepciones sin preparación suficiente: necesitarán evidencia de aproximación o reconocer que sólo puede animarse una reacción, no inventar intención pasada.
3. Root locomotion demasiado abrupta: el motor debe ofrecer una trayectoria físicamente razonable; una pose no puede eliminar su discontinuidad.
4. Rig de 22 joints/mesh actual: sirve al prototipo, no fija el techo de calidad. Ampliar articulaciones y skin weights únicamente con necesidad artística medida y presupuesto GPU explícito.
5. Humanoide puramente matemático demasiado limpio: requerirá referencias, authoring de siluetas y evaluación artística. Procedural no significa “sin arte”.
6. Replay espaciado o pérdida de tasks: contrato nuevo y checkpoints antes de asegurar fidelidad.
7. Solver demasiado complejo: candidatos e iteraciones limitados; profiling antes de paralelismo/GPU/frameworks.

El código duradero se obtiene de contratos pequeños, ownership claro, formatos estrictos, tests temporales y herramientas de diagnóstico. No de diseñar hoy todas las posibilidades del juego ni prometer compatibilidad perpetua.

## 12. Contratos cerrados de la review (antes de P2)

Estado de ejecución y mediciones: `docs/evidence/PROCEDURAL_CHARACTER_MOTION.md`. P0/P1 están en curso; esta especificación no implica que sus gates estén aprobados. El corte del 2026-10-08 ya persiste checkpoints, fija identidades reales y compara paletas completas; siguen pendientes integración del replay guardado, contrato de rig definitivo y gates dinámicos/visuales. P2 no se cierra con sólo esas pruebas temporales.

### Disponibilidad y cadencia

Cada registro lleva secuencia, actor/generación, tick de ocurrencia y frontera de disponibilidad. El productor no predice confirmaciones: preparación es objetivo previsto; confirmación/cancelación es una revisión nueva. Runtime ingiere una frontera por tick en orden, aun sin snapshot visual. Live y replay recorren exactamente esas fronteras; leer el archivo completo no permite adelantar su disponibilidad. Presentación tiene un tick de retraso explícito, no anticipación de hechos. Revisiones tardías afectan sólo planes futuros; nunca mutan el pasado. Recepción sin preparación declara `InsufficientPreparation`.

`FootContact` aplica impulso durante sistema t y su estado se publica en frontera t+1: compromiso visual = t+1, ocurrencia deportiva = t, disponibilidad = t+1. Capturar cinemática entrante inmediatamente antes del impulso y saliente inmediatamente después; punto previsto en preparación y confirmado antes del impulso son campos distintos. No deducirlos del snapshot posterior ni corregirlos mediante tolerancia temporal.

Fixtures obligatorios: fronteras individuales vs batches, presentación incremental vs archivo completo, dos contactos entre snapshots, preparación cancelada, recepción tardía, confirmación revisada, igualdad exacta pre/post-step y disponibilidad; todos ejecutables antes de P2.

### Ownership, transporte y recuperación

Core produce registros por frontera; sesión los captura independientemente del muestreo de poses. Un único runtime Client ingiere y conserva planes. RenderWorker último-input transporta sólo frames/paletas, nunca ledger. Core replay define secciones identificadas y bytes de checkpoints opacos sin referencia a Client.Animation. Ventana live fija: 256 fronteras; overflow invalida continuidad y exige restaurar checkpoint más cursor de pista, nunca omitir silenciosamente tasks. Archivo conserva pista completa indexada por bloques; no está sujeto al límite live. Seek procesa como máximo 256 fronteras por lote fuera del render thread y restaura checkpoint precedente cada 128 ticks. Cancelación/cierre invalida epoch; reconstrucción obsoleta no publica. Hidden, stalls y reattach no destruyen runtime ni reinician gait.

### Checkpoint e identidad

Encabezado estricto: versión de planner, hashes de config/rig/profile, identidad de sesión/pista, tick y cursor. Payload completo: generaciones, fases, anclas, reservas/tareas, segmentos y derivadas de curvas, filtros y velocidades, semillas visuales y contadores. Índice cada 128 ticks limita reconstrucción. Rechazar incompatibles explícitamente, sin lector legacy. Medir memoria live, tamaño de archivo y latencia/allocations seek por separado.

### Restricciones y morfología

| Restricción | Clase | Incompatibilidad |
|---|---|---|
| Raíz deportiva y balón | Hard, inmutables | Nunca corregirlos |
| Longitudes, finitud y límites articulares | Hard | `JointLimit` y pose acotada sin stretch |
| Contacto confirmado y soporte reservado | Hard objetivo, no licencia anatómica | `UnreachableContact` / `SupportConflict` |
| Preparación disponible | Hard causal | `InsufficientPreparation` |
| Balance local, silueta y secundarios | Soft | Residual medido |

Reservas por intervalo semiabierto y effector; tareas simultáneas se ordenan por compromiso, secuencia y actor. Confirmación prevalece sobre preparación; cancelación libera sólo futuro. Cuatro candidatos y dos iteraciones corporales fijas, desempate por índice; no convergencia abierta. Morfología física pertenece a Core/player profile y es independiente del rig; rig/sockets/proxy se validan offline en Client. Cambiar alcance deportivo exige decisión explícita y regresiones deportivas propias.

### Matriz cerrada de familias

| Familia obligatoria | Productor / evidencia | Owner visual | Gate |
|---|---|---|---|
| Locomoción, pivotes | Frames: raíz, velocidad, facing y derivadas causales | SupportPlanner | P2 |
| Foot strike | BallFootContact: preparación, cancelación, impulso pre/post, superficie | TaskPlanner | P4 |
| Reception/dribble | BallFootContact: touches explícitos, cinemática relativa; recepción sin anticipación marcada | TaskPlanner | P4 |
| Salto/landing | Movimiento deportivo: episodio airborne y frontera de aterrizaje | SupportPlanner/BodyPose | P6 |
| Arquero/manos | Sistemas GK: intento, captura, desvío, liberación y effector explícitos | TaskPlanner | P6 |
| Throw-in | Restart/impulso: preparación y release manos | TaskPlanner | P6 |
| Cabeza | Sistema aéreo: preparación, contacto y cinemática pre/post | TaskPlanner | P6 |
| Reacciones/caídas | Eventos deportivos explícitos: choque, falta, lesión, resultado | BodyPose/TaskPlanner | P6 |

La evidencia ausente se implementa en el productor indicado; ningún consumidor inventa contacto desde distancia, posesión o intención.

### Métricas y operación

Medir superficie/socket transformado real: drift, penetración, normal/orientación y velocidad relativa en compromiso exacto. Continuidad por familia con derivadas respecto a segundos de simulación, no del frame. Benchmarks Release con warmup documentan frecuencia, joints, cuatro candidatos y dos iteraciones; reportan ingestión, solver, end-to-end y GC por separado. Kernel 0 B no prueba presentación allocation-free. P5 elimina contratos/clips/readers reemplazados y completa operación, no posterga FPS/seek a ese gate. Capturas nativas del parent X11 incluyen GPU child; secuencias temporales inspeccionadas, no sólo bitmap Avalonia. Sin comparaciones equivalentes no se declara AAA.
