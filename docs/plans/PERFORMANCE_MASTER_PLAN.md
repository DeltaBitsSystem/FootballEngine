# FootballEngine — plan de rendimiento, memoria y GPU

## Objetivo y contrato

Hacer viable una carrera profunda, rápida y explicable para el lanzamiento de 2027. Competir con Football Manager en tiempo de espera, respuesta táctica y confianza en la simulación; no afirmar superioridad sin un benchmark comparable del otro producto.

Base de decisiones: [mapa y mediciones](../evidence/PERFORMANCE_MEMORY_MAP.md). El mapa conserva el baseline y agrega evidencia de la primera implementación al final.

### Primera entrega CPU implementada

- G1: eliminado `MatchOutcome.DumpJson` y el export automático de Fast. Export explícito streaming/Result con eventos tipados, versión 2, contenido probado y nombres de EventKind cacheados; no hay consumidores de Training que dependieran del campo eliminado.
- G2, camino batch: `finish` no materializa outputs visuales que nadie consume. Replay conserva todas sus boundaries en el archivo sin duplicarlas en el resultado del advance; `advanceTo` explícito mantiene sus outputs. Contexto inmutable reutilizado por advance, no recreado cada tick. El rediseño de ownership/backpressure Live de G2 sigue pendiente.
- G3, primeras optimizaciones: selección lineal de roles con los mismos desempates; planner sin Options temporales por jugador proyectado y sin arrays/ordenación para hallar el segundo defensor. No cambian profundidad, cadencias, presupuesto de búsqueda ni scoring.
- GPU, medición nativa/VRAM, mundo y rediseños restantes siguen pendientes; esta entrega no afirma haber completado G0–G5.

Prioridad: eliminar trabajo que ningún consumidor necesita, reducir asignaciones y copias, mejorar localidad/vectorización CPU, y trasladar a GPU trabajo suficientemente paralelo que reduzca latencia **de extremo a extremo**. No maximizar utilización GPU por sí misma ni sustituir CPU ocupado por esperas GPU.

No backward compatibility: reemplazar los contratos obsoletos de manera coherente, retirar implementaciones y actualizar todos los consumidores. Si cambia un formato, versionarlo y rechazar explícitamente archivos incompatibles. Nunca borrar partidas ni recuperar datos de otra carrera silenciosamente. No rutas legacy, flags old/new, adapters o doble motor. Las capacidades de hardware/políticas de presentación no son flags de migración.

## G0 — cerrar el mapa antes de optimizar cada área

### Instrumentación ya disponible

`FootballEngine.Benchmarks/Program.fs`: tiempo y bytes por cada sistema, total del hilo, total de todos los hilos, residual fuera de sistemas, heap antes/después/tras colección, GC y pausas, pico RSS previo a export-probe, tick/hash y sonda independiente de exportación con detección de `{}`. Todo fuera de producción, sin alterar RNG ni scheduler.

### Instrumentación obligatoria restante

1. Subdividir el residual de sesión: creación/validación/config, drenaje/aplicación, clock/officials, observación/hash, captura de roots/evidence, snapshots, publicación/copia, compresión y export final. Scopes exclusivos, no sumar padre e hijo dos veces. Propagar contadores por worker si se paraleliza.
2. Emisión de `allocation-ledger.json`: revision/runtime/build/CPU/GPU/driver/resolución/config hash/input hash/semilla/modo, contadores, unidades, límites del intervalo y overhead de la propia instrumentación. Contabilizar explícitamente el residual; nunca distribuirlo proporcionalmente para aparentar precisión.
3. EventPipe: CPU + GC + allocation samples con stacks/tipos y eventos perdidos. Heap snapshots en inicio, frontier intermedio, fin con resultado vivo y tras liberar sesión. Reportar dominators/retained size, no llamar a muestras «todos los bytes».
4. Inventario runtime de arrays y buffers: owner, tipo, longitud, capacidad, payload, cabeceras medidas según runtime, vida útil, peak y devolución. Diferenciar copia de referencia, copia de payload y shared storage.
5. Ledger nativo de recursos: wrap de creación/destrucción y upload de **cada** buffer/textura/staging/target propio en WebGPU, SQLite/Skia por herramientas nativas. IDs estables, owner/generation, live logical bytes y peak. Texturas: dimensiones, formato, mipmaps, capas, sample count, block compression y row pitch. Bytes lógicos no equivalen a VRAM física del driver; reportar esta última por separado cuando el backend la exponga.
6. Timestamps GPU por pass, CPU submit vs GPU complete, bytes de uploads/readbacks, stalls/fences y tamaño de colas. No `WaitForGpu` en producción para medir. Resolver timestamps asíncronamente; registrar ausencia de la capacidad en vez de inventar números.
7. Mundo/persistencia/UI: perfiles por fase de día, negociaciones, entrenamiento, regeneración, creación/cierre de temporada; saves/load con mundos pequeños y grandes. Medir `MatchRuntime` real con consumo lento, no solo `finish` en modo Live.
8. Cadena de automatización: validar sumas del ledger, no negativos, hashes iguales con profiler activado/desactivado, no fugas al soltar owners, snapshots/export completos. CI conserva reportes pequeños; trazas grandes como artefactos con retención.

**Salida G0:** cada recurso propio tiene owner/vida útil y contador; cada total tiene categorías exclusivas o residual visible; nativo/VRAM y managed separados. No iniciar una optimización de GPU no medida. El mapa actual no cumple todavía toda esta salida para Client/World.

## G1 — eliminar export diagnóstico obligatorio y corregir contenido (P0)

Archivos: `Match/Simulator/{Simulator,MatchSession,MatchDump}.fs`, contratos de resultado, consumidores de Training/Tools/World y tests.

- Fast debe significar resultado deportivo, no «resultado + descripción de todos los eventos + JSON obligatorio». Sacar el string de dump de la finalización común; export explícito fuera del motor de ticks, propiedad del consumidor que lo pide.
- DTO público/interno serializable verificado o writer explícito `Utf8JsonWriter`; no depender de reflexión de tipos privados ni `sprintf "%A"` para schema de máquina. Rediseñar payload de eventos tipados y su versión; texto humano solo a demanda.
- Escritura streaming a destino acotado cuando no se necesite string en memoria. Emitir eventos y estadísticas completos, incluyendo identidad y causa; no «optimizar» omitiéndolos.
- Actualizar TODOS los consumidores de `DumpJson`; eliminar campo/contrato antiguo, no conservar wrapper de compatibilidad. No adelantar ejecución mediante `lazy` si solo aplaza el mismo coste y retiene el mundo entero.
- Tests: JSON no vacío, metadata/score/stats/events leíbles, eventos complejos y VAR, roundtrip de esquema, determinismo, error de I/O como Result. Confirmar que la calibración sigue recibiendo datos reales.

**Aceptación:** el perfil Fast sin export no contiene stacks de exportación; solicitar export sigue funcionando correctamente. Medir ahorro, no restar la sonda caliente del tiempo frío como si fueran idénticos. Primer presupuesto propuesto: reducir >=70% la asignación Fast de la misma campaña, revisándolo con la primera implementación completa; no es una mejora ya obtenida.

## G2 — presentar por demanda sin retener un partido entero (P0)

Archivos: `MatchSession.fs`, `MotionEvidence.fs`, `Replay.fs`, `MotionTrack.fs`, `MatchRuntime.fs`, archivos de presentación y tests.

- Mantener física/contactos/evidence autoritativos y orden deportivo en CPU. Separar avanzar el mundo de materializar boundaries para un consumidor.
- Fast sin consumidor no construye arrays de roots ni outputs visuales por tick. Consumir/descartar el journal correctamente para no provocar overflow ni afectar eventos.
- Live usa publicación acotada y ownership explícito de bloques; un lector no puede observar un buffer reutilizado. Registrar generaciones, frontier y pérdida/recovery explícita. No reutilizar arrays antes del último consumidor.
- Replay escribe bloques incrementalmente y conserva el índice necesario; eliminar copias y acumulaciones duplicadas. Evidencia de contactos sin pérdida; sampling visual no puede esconder sporting events o modificar resultados.
- Revisar `captureTeam`: evitar dos `Array.init` + `Array.append` por tick; reutilizar staging privado o escritura directa al bloque. Separar capacidad de longitud válida.
- `finish` no recibe un enorme `advance` cuyo output visual desecha. No resolver solo partiéndolo en slices pequeños si sigue materializando datos inútiles en Fast.
- Backpressure solo en frontera de publicación; nunca dentro de un tick parcial. Política explícita para consumidores lentos y ventana recuperable.

**Aceptación:** mismos eventos, RNG y estado final con cualquier política visual; tests de replay/seek/substituciones/checkpoint y recovery. Peak de memoria visual acotado por ventana en Live/Fast, no proporcional a todos los ticks jugados. Verificar 2 partidos simultáneos y lector detenido.

## G3 — CPU: geometría y decisión sin cambiar inteligencia (P1)

Priorizar según bytes/tiempo medidos, no según tamaño del archivo.

1. `TeamPosition`: medir subetapas; reutilizar active-slot index y scratch por sesión/equipo; evitar `filter/map/sort` temporales en bucles de roles. Sustituir ordenar toda la lista para hallar un mínimo por selección estable de una pasada, con desempate por identidad idéntico.
2. `PlayerDecision` / `Planner`: arrays indexados por slot para movimiento, scratch para fronteras y candidatos, beam bounded de capacidad conocida. Evitar `Array.tryFind` anidado en `advance`, proyecciones/ordenaciones repetidas y copias de estado completo por rama. Conservar horizonte, profundidad, scoring, legalidad, límites y ranking; no bajar `MaxEvaluateLimit` para fabricar speedup.
3. `InterceptTable`: compartir cálculos realmente equivalentes de ambos equipos dentro del mismo frontier y precomputar trayectoria si el perfil lo justifica. Sin cachés que sobrevivan cambios de balón, ocupación o reglas. Medir SIMD sobre SoA antes de GPU.
4. `Intent` y `RestartController`: eliminar conversiones temporales, mantener plan estable y ejecución física; ninguna simplificación de legalidad, timeout artificial o teleport.
5. Evitar trabajo duplicado por triggers reevaluados tras eventos. No cambiar cadencia ni eliminar activaciones como optimización transparente: eso cambia fútbol y requiere una decisión explícita de diseño y corpus de comportamiento.

**F# idiomático de alto rendimiento:** records/DU y funciones puras en fronteras; arrays SoA y bucles mutables encapsulados en kernels calientes; `Span`/`ReadOnlySpan` con lifetime local; `ValueOption` y struct tuples medidos, sin convertir indiscriminadamente todo a struct; no boxing en loops. Pooling solo con propiedad, reset y devolución probadas; no estado global mutable compartido. No eliminar las unidades de medida ni identidades de dominio.

**Aceptación:** hashes/estado/eventos idénticos en corpus multisemilla para cambios semánticamente neutros; rendimiento comparado con input/config/semilla idénticos. Nada de actualizar expected hashes para tapar diferencias.

## G4 — GPU: quitar al CPU trabajo visual medido (P1)

### Diseño objetivo

Mantener en CPU soporte, intención, confirmación de contactos y todos los datos que determinan resultados deportivos. En GPU: evaluación visual de poses, jerarquías/matrices y skinning cuando el benchmark demuestre beneficio. El renderer ya aplica skinning y usa instancing; el trabajo NUEVO es evitar generar/subir todas las paletas desde CPU si sale rentable.

- Backend WebGPU compute de presentación, con tipos de buffer F# compactos y WGSL; layout explícito, padding y stride verificados por tests. No enviar records .NET por reinterpretación suponiendo su ABI.
- CPU envía planes visuales compactos, raíces y cambios, no geometría duplicada. Rig y datos estáticos residentes en GPU.
- Trabajo por actor con dispatch agrupado; jerarquía de huesos por niveles o workgroup con barreras válidas. No tratar huesos dependientes como independientes.
- Buffer de salida de compute consumido directamente por vertex shader. Ring de staging y N frames in-flight acotados; releases por fences/generation. Cero lectura síncrona de paletas al CPU por frame.
- IK visual puede pasar a GPU, pero soporte/contactos autoritativos no se recalculan allí para influir sobre el balón. Residuos visuales en diagnóstico asíncrono; invalidación al cambiar rig, actor o generation.
- HUD: preservar dirty regions y canvas retenido; medir si composición adicional en GPU ayuda antes de reemplazar texto/layout de Avalonia/Skia.
- GPU puede competir con HDR/MSAA/shading y empeorar frame time: medir tiempo total compute+raster+present, no solo «CPU liberado».

### Decisión sobre compute deportivo

No mover agentes, árbitro, RNG, event apply ni física autoritativa de un partido de 22 jugadores a GPU en la primera implementación. Dispatch/readback por tick puede costar más que el trabajo actual y perjudicar determinismo multiplataforma.

Prototipo posterior permitido: evaluación masiva de geometría/candidatos de muchos partidos independientes, batch residente y resultados compactos. Medir packing/upload/queue/compute/readback/merge y consumo de memoria con lotes 1/8/32/128. Comparar contra CPU SIMD y paralelismo por partido. Cuantización/tie-breaks y reducciones estables son requisitos; ninguna reducción FP cuyo orden dependa del driver determina el marcador. Si no se puede garantizar identidad deportiva, no entra en el motor autoritativo.

**Gate GPU propuesto:** reducir >=20% el tiempo CPU del bloque visual candidato y no empeorar p95 end-to-end ni calidad. Umbral de aceptación, no promesa. Probar RX 5500, integrada/modesta y dispositivo sin timestamp queries. Core/headless debe funcionar sin GPU. Una implementación CPU canónica de simulación no es backward compatibility.

## G5 — mundo, concurrencia y persistencia (P1)

- Presupuesto de paralelismo único: partidas independientes, commit de resultados en orden estable. Medir throughput y peak RSS juntos; no `ProcessorCount-1` fijo si cada partido retiene centenares de MB ni paralelismo anidado por sistema.
- Límites por memoria medida, cancelación en fronteras y comandos versionados. No compartir scratch mutable, RNG o caches entre partidos.
- Índices de fixtures por fecha y de jugadores por club/posición para evitar escaneos de todo el mundo en cada agente. Invalidación por cambios reales; immutable snapshots en la frontera, kernels locales para trabajo intensivo.
- Guardado streaming/chunked, compresión/hash sin duplicar todo el mundo; serialización fuera de ticks y commits atómicos. Preservar schema rechazo, backups válidos y protección contra saves de otra carrera.
- Carrera de 10 temporadas REALES con jugadores, transferencias, lesiones y partidos; no sustituirla por fixtures marcados como jugados. Registrar evolución de memoria, economía y calidad de plantilla.

## Campaña de validación y presupuestos

1. Matriz de ejecución: Release y Debug por separado; profiler apagado/encendido; Fast, Replay, Live real; semillas fijas 101 y FACE + corpus 1–5, 101–105, 201–202, 777 y semillas de regresión. Fixtures sintético y generado de mods.
2. Warmup y procesos fríos separados. >=7 muestras para mediana; >=30 para percentiles orientativos, informar dispersión y no sobrerrepresentar p95 con 3 muestras. Misma máquina/config/driver, sin builds/tests en paralelo.
3. Medir ns/tick, ticks/s, ms/partido, bytes/tick, bytes/decisión, GC pause, RSS/heap/VRAM peaks, replay/guardar bytes y colas. Geometría CPU vs GPU con el mismo volumen y sin ocultar sincronización.
4. UI: interacción p95 <100 ms; presentación objetivo 60 Hz (16.67 ms p95 de frame completo en hardware/resolución de referencia). Son objetivos a validar, no garantía de todas las máquinas. Simulación Fast: primer objetivo >=2x throughput frente al baseline de la campaña completa y reducción fuerte de asignaciones sin perder inteligencia.
5. Headless determinista, replay/seek/checkpoint, cambios de instrucciones y sustituciones, eventos causales, invariantes de balón parado y corpus estadístico. Nunca cambiar resultados deportivos al activar GPU visual, observación o captura.
6. Build solución, Client.Tests y Benchmarks; ejecutar Core/Client completos, suites de herramientas relevantes, Fantomas y `git diff --check`. Para GPU/UI, usar screenshots deterministas reales y revisarlas; probar resize, ocultar/reabrir, cambio de rig y teardown prolongado.
7. Control de regresiones: correctness estricta en CI; tiempo solo en runner estable, presupuesto por escenario y tolerancia basada en ruido observado. Comparar contra reportes del mismo fixture, no históricos con cobertura distinta.

## Orden de entrega y revisión

1. G0 instrumentación de sesión/export y contenido de dumps; validar y congelar baseline.
2. G1 export explícito y correcto, de punta a punta con Training/Tools.
3. G2 boundaries/publicación/archivo acotados, con cliente completo.
4. G3 optimizaciones CPU dirigidas por perfiles, una familia de kernels por diff coherente.
5. G0 cliente/GPU completo, seguido de G4 compute visual si supera el gate.
6. G5 rendimiento de carrera y guardado; paralelismo dimensionado por memoria.
7. Campaña completa y comparación publicada: rendimiento + corrección + calidad, no solo FPS.

Cada entrega deja contratos finales, consumidores actualizados, código obsoleto eliminado y evidencia reproducible para revisión manual. No implementar todos los cambios en un diff masivo ni presentar un checkpoint como certificado de corrección. No hay autorización en este documento para descartar trabajo ajeno o borrar datos.
