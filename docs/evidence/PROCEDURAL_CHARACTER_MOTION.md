# Movimiento procedural — evidencia de implementación

## Estado (2026-10-08)

Trabajo en la checkout original. Estado inicial registrado: únicamente `docs/plans/PROCEDURAL_CHARACTER_MOTION.md` sin seguimiento. No push, PR/issues ni worktree.

**P0/P1 en curso; P2–P7 no aprobados.** No declaración AAA ni de presupuestos finales cumplidos.

## Implementado y comprobado

- `Core/Match/Contracts/MotionEvidence.fs`: journal multi-actor acotado, identidad, ocurrencia/disponibilidad separadas y overflow explícito. Morfología física propiedad de `PlayerProfile`, independiente de nombres de huesos.
- `BallFootContact`: preparación, confirmación y cancelación; impulso en sistema t, publicación/commit en frontera t+1; punto previsto conservado y cinemática entrante/saliente. Recepción explícita sin anticipación inventada.
- `MatchSession`: captura raíces activas y evidencia **cada tick**, independientemente de poses; incluye objetivo de atención (pelota) explícito. No se transporta el ledger por último-input de RenderWorker.
- `MotionTrack`: bloques comprimidos indexados de 128 fronteras. Replay pasa a v11 con sección de movimiento independiente de Client; lector de poses no acepta v10.
- Runtime visual secuencial en `Client/Animation/`, retenido por `MatchPresentationStream`; evaluación de planes no avanza el estado. Restauración tipada de fases/anclas/filtros/semillas/cursor y rechazo de epochs obsoletos.
- Corte vertical conectado: productor → pista → runtime de presentación → evaluación CPU/paleta → renderer GPU. `WebGpuStadiumRenderer` ya no selecciona clips ni ejecuta IK; exige paletas del consumidor.
- Rig numérico, cadenas precalculadas, sockets de soporte reales, IK analítica rígida y residuales tipados. Prueba real del asset conserva longitudes y sockets de apoyo a menos de 1 cm en reposo.
- Atención cabeza/torso acotada hacia la pelota expuesta. Cambio deportivo deliberado: `Movement.FacingIntentDistance` evita que microcorrecciones de formación hagan girar continuamente a un jugador asentado. Regresión específica aprobada; no se cambió alcance de contacto para satisfacer arte.
- Checkpoints binarios completos e índice cada 128 ticks, persistidos como sección opaca del replay. Identidad integrada con SHA-256 de configuración, bytes del rig y perfiles; identidad de pista derivada de `MatchInput`. No identidad provisional en el stream productivo.
- Ingestión validada antes de mutar: rechaza raíces duplicadas/no finitas, evidencia no disponible y saltos de secuencia. Una generación retirada consume cursor sin reservar miembros de la nueva participación. Restauración valida actor/generación/tareas/cursor y estado numérico.
- Lectores estrictos: índices contiguos sin overlaps, truncación/trailing data, discriminante de lateralidad y frontera de payload coherente con su índice. `InvalidDataException` se convierte en `Result`, no escapa al consumidor. `MotionReplay.load` rechaza replay antiguo, secciones ausentes/duplicadas, identidad ajena y timebase incompatible; conserva ownership de sus bytes.
- Ventana vencida: el render pide recuperación y mantiene hold; el owner de simulación/archivo reconstruye fuera del render thread, como máximo 127 avances desde checkpoint. Publicación bajo gate/epoch; cierre y cancelación impiden reanimar un stream. No rebobina el planner live. Ventana histórica independiente de 32 fronteras, sin duplicar las 256 fronteras live.

## Pruebas ejecutadas

| Validación | Resultado observado |
|---|---|
| Builds Core, Core.Tests, Client, Client.Tests y probe nativo | Correctos tras sus ediciones; warnings preexistentes |
| `FootballEngine.Core.MotionEvidence` | 7/7: disponibilidad, archivo completo/incremental, batching live/replay, bloques, revisiones, overflow e índices dañados |
| `FootballEngine.Core.FootMotion` | 16/16, incluyendo ticks exactos pre/post-impulso y recepción |
| `…Systems.Players.SteeringTests` | 9/9, incluyendo orientación de reposo hacia pelota bajo microcorrecciones |
| Suite Core completa, corte final | **555/555**, 111,39 s |
| `FootballEngine.Client.ProceduralMotion` | **13/13**: socket real, longitudes, finitud, gaze, curvas/derivadas, checkpoints binarios, identidad, reconstrucción, epochs y kernel 0 B |
| `FootballEngine.Client.MotionTimeline` | **7/7**: 22 actores/484 matrices completas a 30/60/144/240 Hz, 1x/4x, pausa y seek persistido; ingestión atómica/generaciones, archivos dañados, cierre/recuperación, configuración, asignaciones y memoria |
| Suite Client completa, primer intento | Detectó 4 fallos: se estaban enviando 93 miembros del roster al runtime. Corregido: sólo participantes activos, continuidad por identidad y conteo por frontera |
| Suite Client completa, corte final | **159/159**, 12,51 s; expectativa de v10 sustituida por v11 |
| Formato y whitespace | Fantomas `--check` sobre 17 archivos del corte y `git diff --check`: correctos |

El test de asignación detectó inicialmente 1.152 B/pose en Debug. Se eliminaron closures y tuplas de referencia intermedias; el kernel real de 22 joints mide **0 B/pose** tras warmup en 1.000 evaluaciones. Ingestión de 22 actores también mide **0 B** tras warmup, incluyendo locomoción, pivote y preparación/confirmación. Se reutiliza el resultado de éxito para no asignar un `Result` por frontera. Esto **no** demuestra que producción de muestras, checkpoint/export ni presentación completa sean allocation-free.

El fixture temporal compara todas las componentes de todas las matrices, no sólo planes. Las raíces superior/inferior están disponibles explícitamente antes de interpolarlas. La igualdad de pose no aprueba residuales dinámicos de apoyo ni golpe: las reservas todavía no componen todo el cuerpo.

## Benchmark headless reproducible del corte

```bash
dotnet build Tools/MatchViewer3D/StadiumRendererProbe.fsproj -c Release
DOTNET_TieredCompilation=0 dotnet run --no-build -c Release \
  --project Tools/MatchViewer3D/StadiumRendererProbe.fsproj -- \
  --motion-bench docs/evidence/procedural-motion/timeline-bench.json
```

Informe completo: `procedural-motion/timeline-bench.json`. Ryzen 5 5600X, .NET 10.0.10, 22 actores × 22 joints, 257 fronteras + 1.000 poses de warmup, entradas sintéticas preasignadas. Tiered compilation desactivada para no comparar ventanas antes/después de optimización JIT. No GPU ni upload/HUD en esta medida.

| Trabajo | n | p95 ms | p99 ms | max ms | Managed B, total |
|---|---:|---:|---:|---:|---:|
| Ingestión/planificación de 22 actores | 2.048 | 0,0065 | 0,0067 | 0,4189 | 0 |
| Evaluación/constraints/paletas de 22 actores | 4.096 | 0,0457 | 0,0482 | 0,1158 | 0 |
| Ingestión + evaluación CPU | 2.048 | 0,0535 | 0,0544 | 0,3009 | 0 |
| Checkpoint + decode + reconstrucción de 127 fronteras + primera pose | 64 | 5,5841 | 21,4865 | 21,4865 | 36.645.768 |

Sin GC en las tres ventanas hot. Seek incluye lectura, validación y creación de runtime; el primer caso frío no se descarta, y hubo dos Gen0. No son latencias de seek UI completas.

Asignación de construcción: live **1.707.872 B**, histórico **247.416 B** (1.955.288 B combinados). La prueba añade scratch y las 484 paletas y comprueba **≤ 2 MiB** para estos buffers managed, conservando las 256 fronteras live. Excluye archivo completo, muestras deportivas, snapshots/HUD/renderer y memoria temporal de decode; no se declara límite de memoria del proceso completo.

Archivo del fixture de 2.305 fronteras: pista **325.700 B**, checkpoints **278.153 B**. No extrapolación a un partido completo ni aprobación de familias pendientes. El benchmark actual no representa todavía cuatro candidatos más dos iteraciones corporales completas para contactos acoplados.

## Captura nativa y baseline

`procedural-motion/baseline-native.png` fue capturada desde el parent X11 e inspeccionada. Incluye el GPU child, a diferencia del bitmap Avalonia.

Baseline anterior, Release, probe de 22 jugadores, 1160×698, 4× MSAA, 165 frames medidos después del warmup:

- CPU + espera GPU (no timestamps GPU): mediana 6,432 ms; p95 7,042 ms.
- Submission CPU: mediana 4,930 ms; p95 5,541 ms.
- Managed 25.695 B/frame; upload HUD 102.299 B/frame.

El probe antiguo tenía órbitas sintéticas y reajustaba el contacto en cada frame: **no sirve como evidencia de realismo ni como secuencia de contacto causal**. A petición del usuario se sustituyó por arranque → tramo recto → frenado → reposo → pivote limitado. Se inspeccionó otra captura parent X11 (`/tmp/opencode/procedural-first-pivot.png`) del corte procedural, todavía amplia y anterior al gaze final.

La segunda medida tiene distinto escenario, por lo que **no se afirma mejora de rendimiento mediante esa comparación**. Faltan secuencias cercanas, slow motion, escenarios deportivos reales y comparación equivalente.

Nueva captura inspeccionada: `procedural-motion/timeline-native.png`, parent X11 con GPU child, 1160×698 y 4× MSAA. Probe Release de 180 frames, 165 medidos: CPU+espera GPU mediana **6,411 ms**, p95 **6,939 ms**; submission mediana **4,927 ms**, p95 **5,453 ms**; **30.896 B/frame** managed y **93.117 B/frame** de upload HUD. Es una cámara amplia y un reloj de diagnóstico, no evidencia temporal cercana ni una comparación equivalente con el baseline.

## Pendientes que impiden cerrar P1/P2

- Conectar lectura del replay guardado a la ruta UI de recorded replay/seek y recuperación después de finalizar el worker. `MotionReplaySource` está probado, pero la ruta recorded actual todavía vuelve a ejecutar la simulación. El journal deportivo desbordado sigue fallando explícitamente; recuperar la ventana visual no recupera evidencia deportiva perdida.
- Usar/probar raíces canónicas en la integración completa de presentación, incluida interpolación entre snapshots espaciados, sustituciones, discontinuidades y contacto. El fixture de paletas no sustituye este gate de integración.
- Conformidad offline de sockets/proxy completa, nuevo esquema de rig y eliminación final de clips/contratos/readers sustituidos.
- Gates dinámicos de apoyo/pivote, familias futbolísticas restantes y métricas temporales por superficie/familia. Las reservas de tareas existen, pero todavía no gobiernan todas las poses de contacto.
- Presupuestos de las familias completas, presentación real con archivo de partido completo/seek y evidencia visual cercana. CPU/allocations/buffers del núcleo actual tienen medidas reproducibles; no equivalen a aprobar el pipeline final, el proceso completo o portabilidad a otra máquina.

La implementación no está terminada; los pendientes se mantienen dentro del alcance P0–P7 original.
