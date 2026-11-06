# Mapa de rendimiento y memoria — estado medido

Fecha de captura: 2026-10-18 UTC. Base: `a14f253`; única modificación funcional de esta investigación: instrumentación de `FootballEngine.Benchmarks/Program.fs`. No se optimizó ni alteró Core o Client.

## 1. Qué contabiliza el mapa

No confundir **bytes asignados durante una ejecución**, **heap vivo**, **memoria comprometida**, **RSS** y **VRAM**. No son sumables ni intercambiables.

El profiler reconcilia los bytes administrados asignados en el hilo simulador: sistemas + resto = total. El contador de todos los hilos permite observar también la diferencia fuera de ese hilo. El resto no es memoria perdida: es una categoría explícita pendiente de subdivisión entre sesión, eventos, observación, configuración, presentación y exportación.

Los contadores no atribuyen objetos individualmente a sus propietarios ni contabilizan el stack, las páginas del JIT, SQLite, Skia, el driver o VRAM. EventPipe aporta stacks muestreados; no convierte muestras de asignación en una contabilidad exacta por tipo. El mapa de propiedad de todo el producto está enumerado abajo; la cobertura de medición completa del cliente y mundo aún NO está cerrada. El plan exige cerrarla antes de atribuirles mejoras.

## 2. Entorno y escenarios

- Ryzen 5 5600X, 12 procesadores lógicos; .NET 10.0.10; ensamblados Release.
- PCI identifica AMD Navi 14 / familia RX 5500; no se midió su utilización ni VRAM en esta investigación.
- Fixture de `FootballEngine.Benchmarks/BenchContext.fs`, configuración predeterminada, seed del profiler `0xFACE` (64206).
- Fast, Replay cada 20 ticks y Live con política predeterminada. Todos finalizaron en tick **266202**, hash de eventos **17810157418599857732**.
- Los perfiles son procesos fríos instrumentados. No usar sus tiempos como percentiles de producción.
- El escenario llamado Live usa `Simulator.runMatch` y `finish`, no la aplicación, el renderizador ni el consumidor real por slices. No representa latencia de interacción o memoria estable del viewer.

## 3. Ledger administrado

Valores exactos de los contadores en la corrida `perf-ledger-v2-*`; varían entre ejecuciones por JIT, GC y optimizaciones dinámicas. Unidades: bytes, no MiB.

| Contador | Fast | Replay-20 | Live-policy batch |
|---|---:|---:|---:|
| Asignado, hilo simulador | 7,131,082,144 | 3,481,602,456 | 3,637,278,768 |
| Dentro de sistemas | 1,708,880,592 | 1,696,470,296 | 1,697,004,168 |
| Fuera de sistemas | 5,422,201,552 | 1,785,132,160 | 1,940,274,600 |
| Asignado, todos los hilos | 7,131,090,104 | 3,481,609,336 | 3,637,334,272 |
| Heap al finalizar, sin colección forzada | 507,299,712 | 998,796,400 | 1,177,005,728 |
| Heap tras colección completa, outcome vivo | 9,689,152 | 93,036,312 | 9,784,216 |
| Pausa GC durante ejecución, ms | 320.772 | 266.934 | 742.363 |
| Colecciones Gen0 / Gen1 / Gen2 | 431 / 67 / 6 | 185 / 86 / 8 | 217 / 124 / 7 |

El heap posterior a colección es el heap del proceso, no tamaño exclusivo del resultado. La colección se ejecuta fuera del intervalo medido. El profiler conserva vivo el outcome. La medición posterior independiente de exportación no forma parte de los totales anteriores.

Última comprobación con la instrumentación final, Fast: **7,147,000,344** bytes en hilo simulador = **1,723,915,680** en sistemas + **5,423,084,664** fuera. Todos los hilos: **7,147,008,352**; pico working set previo a la sonda de exportación **605,495,296**; heap tras colección **9,673,376**. Las dos identidades del ledger se verificaron automáticamente. No interpretar el pico RSS como memoria exclusivamente administrada.

### Todos los sistemas: perfil Fast v2

Los tiempos son inclusivos de la llamada al sistema, no CPU exclusiva ni porcentaje del partido entero. Las asignaciones son del hilo actual dentro de cada llamada; si se introducen trabajos paralelos deberán usar contadores propios.

| Sistema | Llamadas | ms | Bytes asignados |
|---|---:|---:|---:|
| TeamPosition | 73246 | 1317.533 | 504619496 |
| PlayerDecision | 44308 | 793.504 | 781856832 |
| PlayerSteering | 114000 | 445.579 | 11382912 |
| PlayerIntent | 73246 | 181.779 | 130417720 |
| RestartController | 228001 | 179.615 | 146865488 |
| TeamAttack | 82801 | 78.813 | 57019712 |
| PlayerPerception | 22130 | 60.549 | 1466104 |
| BallPhysics | 228001 | 58.854 | 7957392 |
| TeamTransition | 82801 | 46.223 | 23963752 |
| BallContact | 228001 | 43.245 | 7832152 |
| PlayerBodyPhysics | 228001 | 37.607 | 8358776 |
| Actions | 97945 | 33.842 | 10407560 |
| BallPossession | 176905 | 30.039 | 6622848 |
| TeamPerceive | 58131 | 21.582 | 8021016 |
| BallFootContact | 266202 | 20.127 | 1916464 |
| BallOutcome | 176905 | 14.439 | 60176 |
| TeamEmergent | 22130 | 11.536 | 0 |
| ManagerSystem | 97 | 4.597 | 74824 |
| RefereeFouls | 111 | 1.969 | 33384 |
| RefereeCards | 21 | 1.490 | 3984 |
| TeamDecide | 72 | 0.864 | 0 |

Total tiempo dentro de sistemas: **3383.786 ms** de **11228.063 ms** de simulador. El resto incluye sesión y finalización, instrumentación, GC y trabajo fuera de la tabla; no equivale solamente a captura de movimiento.

## 4. Hallazgo prioritario: Fast exporta texto siempre

`MatchSession.finish` llama `MatchDump.export` automáticamente en Fast. `MatchDump.export` construye summaries para todos los eventos, usa `sprintf "%A"` y `MatchEvent.describe`, y después serializa un modelo con tipos privados.

La sonda final, independiente y posterior al partido, midió **5379.635 ms**, **4,377,310,856 bytes asignados**, **2 caracteres**, y comprobó que el resultado era literalmente **`{}`**. Esto es un problema de corrección además de rendimiento; investigar la visibilidad/serialización del DTO y cubrir el contenido con tests. No deducir la causa exacta del JSON vacío únicamente de los dos caracteres.

La traza CPU Fast registró stacks inclusivos de aproximadamente 7.28 s en `MatchDump.export`, 4.50 s en `advanceTo` y 3.09 s en `Stepper.stepInto`. Son muestras de una corrida distinta, incluyen callees y NO se suman. La repetición de exportación está caliente y tampoco puede restarse como si fuera el coste exacto del export original.

**Consecuencia:** no priorizar compute GPU sobre este coste. Primero sacar el export del camino obligatorio y corregir su salida explícita, sin perder los consumidores de Training/calibración.

## 5. Mapa de propiedad y vida útil

| Área / propietario actual | Datos y vida útil | Coste observado / mecanismo | Medición restante |
|---|---|---|---|
| `MatchFactory`, `World`, `Frames` | SoA por equipo, perfiles, scratch, diccionarios; sesión | Inicialización y arrays persistentes; buena base para locality | Payload por campo + cabeceras/alineación mediante inventario runtime; no tamaños supuestos |
| `SystemsTable`, `Stepper`, `EventApply` | Orden, triggers, buffers y cambios de estado; tick/sesión | Ledger de todos los sistemas; resto de stepper sin subdividir | Drenaje, routing, clock, officials y observación separados |
| `Team/Position.fs` | Roles, candidatos y geometría; reevaluación | 505 MB/corrida, mayor tiempo entre sistemas | Tipos/stacks, asignación de roles y recomputaciones duplicadas |
| `Players/Planner.fs`, `Decision.fs` | Fronteras, estados, arrays de posiciones/movimiento; decisión | 782 MB en PlayerDecision; `advance` busca movimiento por slot y genera arrays/ordenaciones | Evaluaciones/candidato, bytes por búsqueda; misma profundidad y presupuesto |
| `Team/CollectiveAttack.fs`, `DefensiveTransition.fs` | Episodios, roles, aprendizaje, candidatos | 57 / 24 MB en sus sistemas; no son el mayor coste medido | Separar aprendizaje y selección sin cambiar eventos |
| `Restart/`, `Intent`, `Steering` | Plan inmutable, ejecución física, targets | Ledger individual; no eliminar contratos para ahorrar memoria | Desglosar validación y proyección |
| `MatchSession` | Eventos, outputs, commands; sesión/advance | `captureMotion` genera dos arrays de roots y los concatena cada tick; `producedMotion` crece hasta el frontier | Contadores exactos capture/publicación/finish y máximos de capacidad |
| `MotionEvidenceJournal` | Anillo acotado; sesión | Bounded; no confundir con lista ilimitada de boundaries | Overflow, capacidad, bytes de structs medidos en runtime |
| `Replay`, `MotionTrack` | Snapshots/roots/evidence, bloques, compresión; archivo | Replay retiene ~93 MB de heap del proceso tras GC en fixture | Copias `ToArray`, buffers stream, bytes comprimidos por sección y seek |
| `Observation`, hash, dump | Evidencia/identidad/texto; tick/finalización | Export desproporcionado; observación acotada no equivale a coste cero | Stacks de allocation y ledger por etapa; pérdidas de EventPipe |
| `WorldScheduler`, `WorldRunner` | Mapas de entidades, fixtures, fases; día/temporada | Recorre fixtures, agentes y batches; NO perfilado en esta captura | Días sin partido, jornadas densas, 10 temporadas; retained heap y commits |
| `SnapshotRepository`, SQLite | Payload, chunks, hash, compresión, slots; guardado | Buffers administrados + cachés SQLite nativas | Bytes serializados/comprimidos, pico por save/load, latencia y RSS |
| `MatchRuntime`, `MatchPresentation`, archivos de motion | Canales, slices, snapshots y publicaciones | Canales bounded salvo comandos; worker separado | Profiler por hilo, ocupación/edad de colas y cargas reales con consumidor |
| `MotionRuntime`, `MotionPalette`, `PoseEvaluation` | Planes, pose scratch, 22 paletas; render | Evaluación/IK/matrices CPU; scratch ya reutilizado | ms/actor, allocations/frame y coste de subir matrices |
| `WebGpuStadiumRenderer`, shaders | Mesh único instanciado, texturas, depth/MSAA/HDR, uniform/storage buffers | GPU rasteriza y aplica skinning; CPU envía transformaciones/paletas | Ledger Create/Destroy de buffers/texturas + timestamps GPU y uploads |
| HUD/Avalonia/Skia | Canvas/bitmap retenidos, regiones dirty y controles | No reinstaurar bitmap completo por frame | Managed/native por frame, resize, ocultar/reabrir, teardown |
| Datos/mods/assets | JSON/GLB/texturas, índices; carga/cache | Carga fría, no tick; GPU y CPU pueden conservar duplicados | Inventario decoded/raw/upload, dueño y condición de liberación |

El renderer ya tiene worker dedicado, HUD retenido y 22 instancias de una sola malla. La evidencia anterior en `MATCHVIEWER_RENDER_PERFORMANCE.md` no es una medición nueva ni un motivo para repetir optimizaciones entregadas. El host actual exige Linux/X11/XWayland; portabilidad y dispositivos modestos forman parte del producto.

## 6. Reproducción y artefactos

```bash
dotnet build FootballEngine.Benchmarks/FootballEngine.Benchmarks.fsproj -c Release
dotnet run --project FootballEngine.Benchmarks/FootballEngine.Benchmarks.fsproj -c Release --no-build -- --repetitions 3 --warmups 1 --profile --output /tmp/opencode/perf-current-release
dotnet FootballEngine.Benchmarks/bin/Release/net10.0/FootballEngine.Benchmarks.dll --profile-worker --scenario fast --output /tmp/opencode/perf-ledger-final-fast
```

Repetir el worker con `--scenario replay-20-ticks` y `--scenario live-policy`. El perfil original completo produjo JSON, Markdown, `cpu.nettrace` y Speedscope en `/tmp/opencode/perf-current-release`. El worker independiente emite `system-profile.json` versión 2. Los artefactos temporales no forman parte del repositorio; este documento conserva los resultados relevantes.

Benchmark no instrumentado por sistema: 3 semillas 4096–4098, 1 warmup y verificación de cada semilla dos veces. Medianas Fast **6335.866 ms**, Replay **5429.959 ms**, Live-policy **4885.825 ms**; asignación mediana **5278.643 / 2955.671 / 3247.685 MiB**. Verificación determinista aprobada en los tres escenarios. Tres muestras no justifican afirmar un p95 poblacional estable ni compararse comercialmente con Football Manager.

La captura inicial coincidió con builds de instrumentación: tratar sus latencias como orientativas. Los workers independientes posteriores se ejecutaron secuencialmente tras su finalización; la sonda final confirmó los mismos tick y hash. Separar en futuras campañas compilación, JIT, tracing, medición caliente y pruebas de corrección.

## 7. Validación de esta entrega

- Build `FootballEngine.slnx`: correcto, sin warnings en esta ejecución incremental.
- Build `FootballEngine.Client.Tests`: correcto, 5 warnings en archivos no modificados (identificador reservado, construcción IDisposable e indexación ambigua).
- Build Benchmarks Release: correcto, sin warnings.
- Fantomas del archivo modificado y `git diff --check`: correctos.
- Tres escenarios con verificación determinista del benchmark; tres workers con ledger reconciliado; worker final Fast confirma export vacío y conserva tick/hash.
- No se volvieron a ejecutar las suites completas Core/Client en esta entrega de instrumentación. Los builds no equivalen a tests ejecutados.

## 8. Primera implementación CPU — después del baseline

Los apartados anteriores describen las mediciones iniciales, no el código optimizado. Esta entrega elimina el export obligatorio del resultado, mantiene export explícito correcto y evita generar/duplicar presentación en `finish` cuando no tiene consumidor. La captura de replay sigue completa; los lectores de `advanceTo` siguen recibiendo los mismos outputs.

También se reemplazaron los sorts usados para seleccionar dos defensores y una cobertura por scans estables; en el planner se eliminaron Options por jugador proyectado y el filtrado/mapeo/ordenación usados para encontrar el segundo defensor. Ninguna optimización reduce inteligencia ni altera ticks, RNG, scheduler o presupuesto de búsqueda.

### Misma campaña Release, mismas semillas

BenchContext/defaultConfig; semillas 4096–4098, un warmup, tres muestras y verificación de cada semilla dos veces. Se compararon también cada semilla/modo contra el JSON histórico del baseline: **los nueve casos conservaron ticks, cantidad de eventos y hash**. Todos los escenarios verificaron determinismo de estado/eventos/posiciones/estadísticas en sus ejecuciones repetidas.

| Escenario | Mediana antes, ms | Mediana después, ms | Asignado antes, MiB | Asignado después, MiB |
|---|---:|---:|---:|---:|
| Fast | 6335.866 | 1925.023 | 5278.643 | 1016.788 |
| Replay-20 | 5429.959 | 3388.031 | 2955.671 | 2447.918 |
| Live-policy batch | 4885.825 | 3306.985 | 3247.685 | 2174.492 |

Fast: **3.29x throughput**, **69.6% menos tiempo**, **80.7% menos asignaciones** en esta campaña local. La latencia histórica tuvo builds concurrentes como ya se documentó; el factor no es una garantía universal. Las nuevas mediciones se realizaron sin builds/tests en paralelo. Live-policy sigue sin representar el viewer interactivo.

Worker frío instrumentado, seed FACE: de 11,618.925 a **3890.918 ms**; de 7,147,000,344 a **1,124,068,728 bytes** administrados en el hilo simulador. Residual fuera de sistemas: **75,958,576 bytes**; pico RSS antes del export-probe **101,580,800 bytes**. Tick **266202** y hash **17810157418599857732**, idénticos al baseline.

La sonda de exportación explícita produce ahora **6,362,174 caracteres**, no `{}`; tarda **419.318 ms** y asigna **71,301,560 bytes** en la corrida fría posterior al partido. Antes: 5379.635 ms y 4,377,310,856 bytes para `{}`. El formato nuevo es JSON versión 2 con datos de eventos tipados, contexto, estadísticas y marcador; el escritor streaming evita construir una lista de summaries. El consumidor que requiere string asume ese buffer explícitamente. Los streams siguen siendo propiedad del llamador y los errores de escritura se devuelven como Result.

### Validación adicional

- Core completo final: **627/627**, 119.170 s en Debug. Incluye regresión batch/publicación/replay y referencia ordenada de roles en 32 disposiciones simétricas para ambos equipos. Una corrida anterior de 625 tests tardó 94.226 s: existe variabilidad considerable en la suite paralela; usar la campaña por partido para atribuir mejoras, no prometer volver a 40 s con estos datos.
- Client Release: **162/162**, después de los cambios de contratos.
- Build solución Release, Client.Tests Release y Benchmarks Release: correctos; warnings existentes separados en logs (78 en la solución recompilada, 5 en Client.Tests, 0 en Benchmarks).
- Traza CPU/GC posterior generada correctamente; no se introdujo dependencia de GPU ni cambios del renderizador.
- Fantomas correcto en Benchmarks, MatchDump, MatchSimulationTypes y los dos archivos de tests modificados; `git diff --check` correcto. No se normalizaron los bloques preexistentes no formateados de MatchSession, Planner y Position para evitar mezclar cambios ajenos al rendimiento.

Artefactos: `/tmp/opencode/cpu-optimized-benchmark/{benchmark.json,benchmark.md,cpu.nettrace,cpu.speedscope.json,system-profile.json}`, `/tmp/opencode/cpu-optimized-final-fast/system-profile.json`, logs de tests `/tmp/opencode/cpu-optimized-*-tests.log`. No se realizaron commits.
