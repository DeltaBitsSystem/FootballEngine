# Match Viewer 3D — plan maestro

**Auditoría: 28-09-2026. Estado: vertical 3D integrado, no producto visual terminado.** Este documento reemplaza el plan anterior y es la autoridad única para arquitectura, assets, presentación 3D y gates de entrega del visor. Describe el worktree actual, incluidos archivos aún no versionados; no presupone que todo esté en HEAD. Las decisiones siguientes son propuestas de implementación, salvo las observaciones expresamente verificadas.

## 1. Visión y éxito

Construir un visor mantenible durante décadas: fútbol humano, espacialmente creíble y cinematográfico que permita **leer una decisión colectiva, intervenir y reconocer sus consecuencias reales**. Aspiramos a superar Football Manager en calidad visual, presentación e inteligencia/legibilidad; es una ambición de producto, no una superioridad demostrada ni una afirmación comercial.

- **Verdad antes que espectáculo:** Core es autoridad de física, identidad, tiempo, reglas y eventos. Cámara, animación, audio y HUD explican el partido; nunca lo resuelven ni inventan contactos, goles o causalidad.
- **Calidad:** humanos articulados, contactos coherentes, balón legible, césped/materiales físicamente plausibles, iluminación y estadio a escala. Broadcast, cámara táctica y diagnóstico comparten la misma escena.
- **Presentación:** campo protagonista, marcador y comentario sincronizados, repeticiones identificadas, pausas útiles, comandos con aceptación/aplicación visibles y accesibilidad completa.
- **Longevidad:** contratos abiertos/versionados, fuentes editables archivadas, renderer y host sustituibles, builds offline desde entradas congeladas. Ningún proveedor generativo o servicio remoto es dependencia del juego.
- **Sin backward compatibility:** reemplazar contratos, replay, empaquetado y arquitectura cuando convenga. Rechazar versiones incompatibles explícitamente; no mantener lectores antiguos, adaptadores perpetuos ni un segundo viewer 2D.

**Éxito verificable:** gates G0–G6 aprobados con artefactos; capturas del partido real a 1536×1024 y 1920×1080; 60 FPS conforme al presupuesto de §7; ninguna divergencia autoritativa con/sin visor; tareas de lectura y control superadas. [`objetive.png`](objetive.png) es referencia de encuadre, densidad y jerarquía, no prueba de calidad ni permiso para copiar marcas. Una comparación externa futura debe registrar versión, configuración, escenas y protocolo comparable; hasta entonces solo se publican resultados propios.

## 2. Auditoría factual y límites

Rutas relativas a la raíz. Se inspeccionaron código, proyectos, planes, JSON del GLB y la escena fuente abierta en Blender; se volvió a compilar el probe, pero no se cerró una sesión real de partido ni se ejecutó benchmark. Las capturas y cifras del plan anterior son antecedentes, no gates revalidados. No depender de evidencias alojadas únicamente en `/tmp/opencode`.

| Área / evidencia | Estado observado | Consecuencia |
| --- | --- | --- |
| `FootballEngine.Client/Match3D/WebGpuStadiumRenderer.fs` | WebGPU real: buffers indexados, WGSL, `Depth24Plus`, uniforms de 24 matrices, presentación sin readback por frame. Luz difusa simple y variación sinusoidal aplicada a todos los materiales. Sin muestreo de texturas, PBR, sombras, skinning, LOD, culling espacial ni AA multisample (count=1). | Buena prueba de transporte GPU; no renderer de producción visual. UVs importadas no significan texturas renderizadas. |
| Host en el mismo archivo | Ventana hija Xlib, recortes X Shape para dos paneles opacos; requiere Linux y handle `XID`. Bounds usan `RenderScaling`. | XWayland no equivale a Wayland nativo. No hay composición alfa general, paridad multiplataforma ni prueba HiDPI por el mero escalado. |
| Lifecycle GPU | `IDisposable`, resize y reconfiguración de surface Lost/Outdated; errores a `Trace`. Ahora limpia recursos si falla la inicialización parcial, libera lotes/buffers incompletos y recursos temporales de frame en `finally`. | El callback request es comprobado inmediatamente después de invocarlo, pero su asincronía real no está demostrada; device-loss, OOM, suspensión, ownership compartido y 50 ciclos siguen sin probar. Surface-loss no es recuperación de device-loss. |
| `StadiumAsset.fs` | Aplana geometría, agrupa por **nombre** de material, exige cámara y exactamente 22 raíces `Match 1/2 player N` más `Match ball`; corrige colores de césped por nombre. | Contrato ligado a una escena; riesgo de colisiones de materiales, duplicación y rigidez. Sustituir nombres convencionales por metadatos e IDs validados. |
| `Views/MatchPitch/MatchPitchControl.fs` | Timer UI nominal de 60 Hz y muestreo del stream; ahora pasa a Hermite el `dt` medido en segundos, no un mínimo artificial de 1 s. | El timer no demuestra 60 FPS. Epochs/seek/rebind, underflow explícito y errores recuperables siguen pendientes. |
| `Views/Components/MatchProjection.fs`, `MatchVisualTypes.fs` | Replay v7 aporta `PlayerId` por slot y ahora proyecta orientación corporal/velocidad angular autoritativas; proyección valida cardinalidad/nombres y asigna `VisualSlot` a titulares activos. Suplentes activos reutilizan determinísticamente huecos; sin dorsal inventado. | Cortes todavía usan distancia/flow/ocupación, no `ContinuityEpoch`/eventos de contacto tipados; oficiales se proyectan, no se animan. |
| Transformaciones GPU | Usa `VisualSlot` validado para las 22 raíces; ausentes se trasladan fuera de cámara, nunca a posiciones de formación fabricadas. Mundo `(X,0,-Y)`, balón `(X,0.11+BallZ,-Y)`; yaw consume orientación persistida de Core incluso al detenerse. Ignora `RenderFrame.Officials`. | El renderer aún rota una malla rígida: no hay esqueleto ni contactos visuales; tampoco modelos/renders de árbitros. Definir altura/radio de balón y contratos de contacto. |
| Cámara | Renderer usa cámara fija del GLB, actualizada al cambiar tamaño. `MatchCamera.fs` sigue definiendo `Camera2D` y perfiles sin implementación 3D equivalente. | No existe aún director broadcast ni seguimiento 3D integrado. |
| `AppState/MatchPresentation.fs`, `MatchRuntime.fs` | Worker live y canal semántico separados de ventana visual acotada; reloj visual con Stopwatch, pausa, 1×/2×/4× y hold en frontera. Poses publicadas por runtime. | Base útil, pero una ventana de poses no es archivo de replay ni timeline de eventos fiable; alinear HUD, animación y audio al mismo tiempo presentado. |
| `Core/Match/Simulator/Replay.fs` | Formato breaking v7: roster IDs estables en header y snapshots de posición/velocidad/altura/spin, orientación y cinemática angular, activos, intención, árbitros y flow. Sin `ContinuityEpoch` ni contrato completo de ejecución/contacto. | G2 parcial: añadir continuidad/eventos y contrato de contacto; no reparar en shaders lo que falte en Core→presentación. Los formatos previos se rechazan. |
| `Views/Components/MatchViewer.fs` | Match Day live: marcador, manager, comandos, comentario/eventos y controles. Paneles superpuestos condicionados a ancho ≥1300, coordinados con dimensiones fijas del host. | Verificar acceso equivalente en tamaños menores, teclado, overlays, locales y desfase entre estado semántico y escena retrasada. |
| Assets reales | `Assets/MatchViewer/stadium.glb`: 40 184 616 bytes; 958 nodos, 454 mallas, 35 materiales, 30 entradas de textura/3 imágenes, 2 cámaras, **0 skins/0 animaciones**. Generator: glTF Blender I/O v5.2.40; extensión `KHR_materials_emissive_strength`. | Los 1 072 040 triángulos de primitivas indexadas únicas no son el coste instanciado por frame. Medir draw calls/triángulos efectivos. No inferir versión de Blender del generator del exportador. |
| Procedencia | `manifest.json` v2 ahora registra `stadium.glb`, sus dos fuentes, hashes SHA-256, exportador glTF I/O 5.2.40 y ausencia de skin/animación. `LICENSES.md` declara el derecho del estadio como no verificado y bloquea distribución. | El manifiesto fija integridad, no autoría, derechos ni export reproducible. No considerar los modelos actuales el estándar humano final. |
| Proyecto/paquetes | `.fsproj` referencia Core, compila renderer y antiguos módulos Skia; copia `stadium.glb`. `Directory.Packages.props`: Avalonia 12.1.3, FuncUI 2.0.0, SharpGLTF.Core 1.0.7, Silk.NET.WebGPU y Native.WGPU 2.23.0. | Versiones centralizadas; falta gate publish/RID, manifiesto/licencias y paquete completo. Localizar DLL por RID no demuestra soporte de ese SO. |
| Probes/tests | `StadiumRendererProbe.fsproj` referencia Client, usa poses sintéticas y timer 30 Hz. `AssetProbe.fsx` cuenta mallas/skins/clips: avisa pero no falla al faltar animación. Tests live existentes cubren buffer, frontera, pausa, cierre, orden por ID y continuidad de balón; Core prueba independencia de política visual. | No certifican personajes, composición, lifecycle GPU ni rendimiento. Convertir probes útiles en gates automatizados con salida no cero. |
| Captura | `Shots/Shots.fs` usa `RenderTargetBitmap.Render(window)`; opciones de seed/tamaño y vídeo existen. | No asumir que captura una ventana nativa hija. Demostrar inclusión de campo WebGPU y HUD mediante captura real de ventana; reloj de pared/settle no garantiza mismo tick. |

**Antecedentes que deben reproducirse:** el plan anterior documentaba incompatibilidad ProGPU preview 12.1.1 con Avalonia 12.1.3 y abortos de ciertas funciones nativas (`wgpuInstanceProcessEvents`, `wgpuShaderModuleGetCompilationInfo`). Los scripts exploratorios permanecen en `Tools/MatchViewer3D/`; no son dependencias elegidas ni evidencia actual de compatibilidad. Fijar y probar el conjunto bindings/header/binario nativo antes de actualizarlo.

### Relación con planes existentes

- [`MATCH_VIEWER_MASTER_PLAN.md`](MATCH_VIEWER_MASTER_PLAN.md): conservar intención live, read models, decisiones y comandos aceptados/rechazados/aplicados. Sus layouts, pseudocódigo, roadmap y selección de variantes con seed no son implementación probada ni gobiernan el nuevo visor. Este plan sustituye sus decisiones gráficas/presentacionales en conflicto: no RNG de presentación en runtime.
- [`MASTER_PLAN.md`](MASTER_PLAN.md): autoridad de diseño de reinicios; exigir causa, lado, ejecutor, identidad y lifecycle de restart en el stream. La UI no traduce estados internos en reglas nuevas ni autoriza contactos. Sus contratos propuestos deben contrastarse con Core al implementarlos.
- No crear otro roadmap 3D. Registrar decisiones y resultados de gates en este documento; adjuntar evidencias como artefactos, no como planes paralelos.

## 3. Arquitectura objetivo y contratos breaking

```text
Core / sesión autoritativa + comandos con revisión
  ├─ snapshots visuales inmutables + eventos ordenados/causales
  └─ read models semánticos con tick efectivo
       ↓
Timeline de presentación común (live / replay grabado / captura fija)
       ↓
Proyección pura → director/cámara + animación + HUD/audio
       ↓
SceneFrame independiente de UI/API gráfica
       ↓
Renderer → host/compositor GPU → Avalonia (HUD y accesibilidad)

Blender + fuentes profesionales → validación/export offline → pack inmutable
                                                        ↓ AssetLibrary
```

**Límites de responsabilidad:** Core no depende de Avalonia, Blender o GPU. Proyección/cámara/animación evaluables sin ventana. `MatchPitchControl` solo enlaza sesión, input, viewport y lifecycle; no interpreta replay ni posee lógica futbolística. Separar del archivo monolítico host nativo, recursos GPU, importer, shaders, escena y diagnósticos. Renderer no depende de tipos bajo `Views/Components`.

| Contrato nuevo | Contenido obligatorio / invariante |
| --- | --- |
| `PresentationEnvelope` | Versión, SessionId, secuencia, tick, frecuencia/unidades de reloj, época de continuidad, revisión de plantilla y hash de configuración. Memoria inmutable o lease con ownership explícito; no compartir arrays mutables de World. |
| `EntityPose` | EntityId estable, PlayerId cuando aplica, equipo/rol, ocupación vigente, activo/visible, dorsal real, transform/velocidad/orientación y referencias de apariencia. Cardinalidad variable: expulsiones, cambios, oficiales. No usar índice, nombre ni dorsal como identidad. |
| Balón/contactos | Posición 3D, velocidad 3D, orientación/spin con unidades; definir si Z representa centro o separación del suelo. ContactId, actor, parte corporal, tick y causa autoritativos. Ninguna corrección visual mueve la pelota autoritativa. |
| Evento visual | EventId, tick efectivo, orden estable, actor/objetivo, causa/parentId, restart/acción y payload tipado. Separar hechos confirmados, revisión VAR e inferencias analíticas. No perder eventos al sobrescribir poses; consumo idempotente y política explícita de seek/replay. |
| `SceneFrame` | Tiempo presentado, entidades visibles, pose esquelética, cámara, iluminación, overlays anclados e IDs de assets. Función de entradas versionadas y tiempo; sin Stopwatch/RNG dentro del evaluador. |
| Pack visual | Manifest schema/version, contenido direccionado por hash, unidades/ejes, materiales, skeleton/clip schema, variantes/LOD, licencias y versión del compilador/exportador. Rechazo temprano de features no soportadas, no ignorarlas silenciosamente. |

### Tiempo, inteligencia y animación

1. Mantener ventana visual acotada y eventos fiables separados. Las poses pueden descartarse; no los hechos ni acknowledgements. Inyectar reloj; el reloj de pared solo decide el tick presentado en live. Capture/replay fija tick y fracción explícitos. HUD/comentario/audio usan ese tick, mientras la cola de comandos informa además su estado operativo actual.
2. Interpolar con `dt` real en segundos, tangentes físicas y límites probados. Cortar por epochs/contactos/reinicio/cambio de lado/ocupación; no únicamente por distancia. Hold en underrun; no extrapolar goles, contactos o teleports. Muestreo adaptado a eventos críticos si 10 Hz no conserva el contacto.
3. Animación es representación: locomoción por distancia, orientación estable al detenerse, clips con marcadores de contacto, blending/IK de pies y porteros. Root motion visual no gobierna World. Si faltan datos para un contacto correcto, ampliar Core→presentación; no falsificarlo. Reconstruir estado de animación y cámara al hacer seek mediante evaluación pura o checkpoints versionados.
4. Director determinista con prioridades explícitas: acción/posesión, ocasión, reinicio, pausa y repetición. Evitar cortes que oculten la decisión; no anticipar eventos futuros en live. Cámara táctica y minimapa muestran posiciones reales y orientación de ataque; cámara fija sirve de referencia geométrica.
5. Inteligencia legible: presión, coberturas, líneas y opciones de pase a partir de datos observados/autoritatitivos disponibles. Mostrar fuente temporal y distinguir estimación de hecho. Cada explicación debe enlazar evidencia, evento y actor; no inventar métricas o afirmar causalidad solo por correlación. Mismos hechos alimentan comentario, overlays y resumen.
6. Comandos llevan sesión, CommandId y revisión esperada; UI nunca cambia la alineación visual hasta aplicación autoritativa. Repeticiones y cámara lenta no alteran el reloj del partido ni habilitan comandos contra un pasado.

### Decisión gráfica y de composición

**WebGPU sigue siendo el candidato inicial, no una obligación para décadas.** Conservar inversión solo tras G1. Contratos de escena/pack son independientes de Silk.NET y del host; no construir varios backends antes de necesitarlo.

- Probar composición GPU real de escena y Avalonia con sincronización/ownership documentados: clipping, transparencia, popups, foco, IME, input y captura. Cero viajes GPU→CPU→GPU por frame; una transferencia GPU→GPU presupuestada es aceptable si se mide.
- Xlib/X Shape es un experimento integrado, no arquitectura final universal. Evaluar APIs de interop soportadas por las versiones reales; memoria compartida/semaforización debe demostrarse, no deducirse de un nombre como `CompositionDrawingSurface`.
- Si el candidato no supera G1, decidir sustitución de backend/host o stack gráfico mediante la misma escena de aceptación y coste de mantenimiento. No prolongar parches privados de UI, agujeros de paneles o bitmap CPU como solución final. No hay migración irreversible a una dependencia sin gate.
- Lifecycle explícito: Creating → Ready → Suspended/Recreating → Ready o Failed → Disposed. Un dueño por recurso/hilo, teardown también tras creación parcial; renderer desacoplado del UI thread si G1 prueba que es necesario, con mailbox acotado y sin carreras resize/dispose. Fallo visible y diagnosticable, sin corromper la sesión.

### 3.1. Ejecución motriz contextual — diseño y estado de implementación

**Objetivo:** que el cuerpo exprese una acción futbolística viable y sus restricciones, no una etiqueta de velocidad. Un giro a derecha libre y uno bajo presión frontal deben diferir en trayectoria, orientación, protección, apoyos y exposición del balón. La calidad frente a Football Manager es una aspiración; solo una evaluación externa comparable permite afirmar superioridad.

**Auditoría de código, 2026-10-01:** la arquitectura real ya reside en `FootballEngine.Core/Match`, aunque los ejemplos de rutas de `AGENTS.md` describen la organización anterior. Se conservan sus principios, no se crea un segundo engine en Client.

| Punto actual | Limitación comprobada | Consecuencia para el diseño |
| --- | --- | --- |
| `Contracts/Frames.fs`, `TeamFrame` | Posición, velocidad, condición, intención y percepción; ahora también orientación, velocidad y aceleración angular persistentes en SoA. Sin soporte ni ejecución motriz. | El estado corporal es autoritativo; no deducir orientación de velocidad en Client. Falta un contrato explícito `BodyFrame`. |
| `Systems/Players/Steering.fs` | Sigue siendo el único integrador de posición; ahora integra giro con límites configurados de velocidad/aceleración angular y frena el giro para evitar overshoot. No planifica protección contextual. | Evolucionar el dueño actual del movimiento; no añadir un segundo integrador. El movimiento lateral y orientación independiente aún faltan. |
| `Systems/Players/Actions.fs` | Ejecuta pases/remates/centros/despejes y restarts aplicando impulsos, sin una ejecución compartida de preparación/contacto/recuperación. | La fase de contacto debe gobernar el impulso real, no un callback del clip. |
| `Simulator/Replay.fs` | Replay v7 captura, codifica y restaura cinemática corporal; aún no persiste ejecución/contactos. | Extender captura/codec/stream con el ledger; no reconstruir hechos desde proximidad. |
| `Views/Components/MatchProjection.fs` | Conserva la clasificación de locomoción por velocidad, pero transporta orientación/angular velocity del snapshot y las interpola por ángulo más corto. | Reemplazar inferencia de acción por contratos motrices de Core; aún no hay aceleración corporal en la proyección. |
| `Match3D/StadiumAsset.fs`, `WebGpuStadiumRenderer.fs` | Vértices con índice de raíz; yaw usa orientación corporal de Core. Sin joints/weights ni skinning. | Separar personaje riggeado del estadio y añadir evaluación esquelética; rotar una malla rígida no cierra el gate. |

#### Responsabilidades y dependencias

```text
Percepción e intención futbolística
    → propuesta de acción
    → planificación motriz y restricciones corporales (Core)
    → ejecución/contactos y movimiento autoritativos (Core)
    → snapshot motriz + ledger de eventos
    → muestreo de presentación
    → selección de movimiento + blending + IK
    → pose esquelética → skinning GPU
```

- **Core es dueño** de posición, orientación corporal, aceleración, límites de giro, estabilidad, preparación/compromiso/recuperación, protección y exposición de pelota, oportunidades de contacto, interrupciones y consecuencias. No importa rigs, nombres de huesos, clips, Skia ni WebGPU.
- **Presentación es dueña** de huesos, retargeting, selección de clips compatibles, mezcla, mirada, inclinación y corrección visual por IK. Puede refinar una pose, pero no cambiar una trayectoria, recuperar una pelota perdida ni inventar una patada.
- **Assets son datos offline:** rig, curvas, rangos válidos de velocidad/giro, ventanas de apoyo/contacto, alcance e interrupción. Un movimiento se acepta por capacidad, no por un nombre como `turn_right_pressure_03`.
- Los límites físicos necesarios en Core se expresan en perfiles/configuración de dominio independientes del pack visual. No derivar reglas del partido de qué clips estén instalados. Cambiar pack, LOD o backend no altera la simulación.
- Un dueño por escritura: planificación escribe la propuesta motriz; ejecución consume esa propuesta; el integrador corporal escribe posición/velocidad/orientación; el sistema de balón aplica contactos. No introducir agentes que se llamen entre sí.

#### Contrato mínimo de dominio

Definir tipos explícitos en `Core/Match/Contracts/` y almacenamiento SoA acorde con `TeamFrame`. Los nombres siguientes expresan responsabilidades propuestas, no APIs existentes:

| Contrato | Contenido y semántica |
| --- | --- |
| `BodyFrame` | Orientación de pelvis/cuerpo independiente de desplazamiento, velocidad angular, aceleración, estabilidad, soporte y disponibilidad de extremidades. Unidades/ejes explícitos. |
| `MotorRequest` | Objetivo de locomoción/acción, orientación deseada, foco de atención, jugador/posición relevante y restricciones. No IDs de clip. |
| `MotorExecution` | ID estable de ejecución por jugador, acción tipada, inicio, fases, compromiso, contactos previstos, recuperación, causa e interrupción. Los momentos se expresan en subticks del engine. |
| `MotorContext` | Contexto local orientado al cuerpo: vector/velocidad de amenazas, tiempo estimado de aproximación, espacio accesible, relación con balón, pie disponible, fatiga y calidad perceptiva. Evitar un único booleano `underPressure`. |
| `MotorContact` | Actor y contraparte, punto/volumen, extremidad, instante/ventana y resultado real. Contacto previsto no equivale a contacto ocurrido. |
| Snapshot de presentación | Copia inmutable de estado corporal, ejecución/fase, objetivos de contacto, identidad y epoch; suficiente para muestrear un instante sin depender de frames vistos anteriormente. |

La percepción del jugador no tiene acceso omnisciente: una amenaza no percibida puede generar una mala elección. Las restricciones físicas y colisiones sí se resuelven contra el mundo real. Guardar la causa elegida evita que el renderer reinterprete una decisión con información posterior.

#### Caso rector: giro a derecha con pelota

1. Intención propone avanzar/girar; percepción identifica amenazas relevantes por ángulo, distancia, velocidad relativa y horizonte de aproximación.
2. El planificador evalúa soluciones viables: giro abierto, recorte con frenado, pivote protegido o continuación/descarga si el giro no es viable. No siempre elegir protección por haber un rival cerca.
3. Con amenaza frontal percibida, una solución protegida puede orientar el cuerpo entre rival y pelota, reducir velocidad, elegir pie alejado de la amenaza y una secuencia de apoyos. La dirección depende de la geometría, no de un clip fijo.
4. Ejecución conserva continuidad: no cambia cada tick de pie o solución. Tiene compromiso, condiciones de replanteo e interrupción por hechos relevantes; los márgenes de histéresis viven en configuración.
5. Movimiento integra aceleración y giro corporal posibles. La conducción avanza mediante contactos autoritativos y tramos físicos de pelota, no una corrección visual que la pega al pie.
6. El defensor sigue teniendo oportunidad real de disputar la pelota. Protección, balance y fatiga afectan la ejecución; no garantizan éxito.
7. Presentación representa la misma ejecución con pose, blending e IK. Si ningún movimiento soporta el contacto solicitado, diagnostica falta de cobertura del pack; no modifica el partido para disimularla.

El horizonte motriz es acotado y replanificable: suficiente para frenar, apoyar, girar y recuperar, no para fijar el resultado de una jugada. Distinguir intención táctica de larga duración, planificación corporal de corto horizonte y evaluación de pose por frame.

#### Tiempo, contacto y orden del stepper

- Iniciar/replanificar acciones ante propuestas o hechos relevantes; avanzar ejecuciones físicas activas con el reloj de simulación. La integración periódica no justifica despertar agentes cognitivos cada frame.
- Antes de implementar, especificar qué estado lee cada sistema y cuándo se hace efectivo un contacto. La tabla actual ejecuta física/contacto antes de steering y acciones; no insertar un callback después y asumir sincronía.
- La preparación no aplica impulso. Al llegar la ventana prevista, comprobar alcance, pie disponible y estado del balón; aplicar una vez el contacto real o emitir cancelación/fallo. Pasar, rematar, centros, despejes, porteros y restarts deben usar la misma semántica de ejecución.
- Un cambio de posesión invalida la ejecución pendiente según sus reglas. No permitir que una patada prevista impulse un balón que ya no está a alcance.
- Eventos de inicio/cambio/interrupción/contacto transportan ID y causa; posiciones y progreso continuos viven en snapshots. No generar un evento por hueso o frame.
- Consumo exactamente una vez por ID de contacto; ni avanzar en bloques ni reproducir/seek vuelven a ejecutar impulsos. El ledger preserva contactos incluso cuando se descartan poses.
- Interpolación no cruza epochs, sustituciones ni discontinuidades; la identidad motriz es del jugador, no del slot visual reutilizable.
- Replay conserva checkpoints de ejecución y eventos necesarios para seek. Fase y variantes se reconstruyen desde tiempo de simulación y datos persistidos, no por acumulación de `deltaTime` gráfico.

#### Animación sostenible

Empezar con un rig canónico, biblioteca pequeña con metadatos y selección/blending deterministas condicionados por trayectoria, orientación, fase y contactos. Añadir IK para mantener apoyos y alcanzar contactos dentro de tolerancias. Separar evaluación de pose de subida de buffers/skinning; el renderer consume poses, no decide fútbol.

La locomoción admite desplazamiento lateral y retroceso manteniendo orientación hacia balón/rival. Root motion de autoría es referencia para compatibilidad y warping acotado, nunca un segundo dueño de posición. Adaptar duración antes del contacto sin desplazar el instante autoritativo; rechazar ajustes fuera de rangos válidos. No estirar piernas ilimitadamente para ocultar incompatibilidades.

Motion matching es una evolución posible detrás del mismo contrato cuando exista un corpus suficiente y métricas que demuestren mejora. No introducirlo como dependencia inicial ni prometer generación procedural de movimientos humanos de calidad sin datos de movimiento adecuados. Procedural significa componer/adaptar movimientos a hechos, no inventar esqueletos al azar durante el partido.

Separar configuración **futbolística** en `BalanceConfig` (capacidad de giro, estabilidad, preparación, recuperación, exposición, fatiga) de configuración **visual** versionada (blending, IK, warping, LOD). La primera se valida y afecta hashes autoritativos; la segunda no modifica resultados. No añadir thresholds futbolísticos al shader o a `MatchProjection`.

#### Secuencia de implementación y gates

| Gate | Entrega completa | Aceptación |
| --- | --- | --- |
| M0 — contrato y corpus | Decisión de ejes/unidades, escritores, fases, eventos, cronología y escenas pareadas; auditoría de atributos/animaciones disponibles y derechos. | Cada hecho tiene dueño y consumidor; ninguna regla requiere renderizar. No sustituye G1/G3. |
| M1 — estado corporal | `BodyFrame`, inicialización, orientación persistente, límites de giro/aceleración y locomoción lateral; captura/codec/stream/proyección con tests. El 3D consume orientación de Core aunque use placeholder temporal. | Parado no vuelve a un yaw de equipo; girar no teletransporta orientación; seek, sustitución y replay preservan identidad/estado. Placeholder no acredita animación humana. |
| M2 — giro contextual | Plan y ejecución de giro libre/protegido, contactos de conducción, interrupciones y defensa real; diagnóstico causal. | Escenarios pareados difieren por causas, no IDs de clip; amenaza frontal/lateral/trasera, aproximación, pie, fatiga y rival no percibido. |
| M3 — vertical riggeado | Un humano licenciado con rig/locomoción/giro/protección, importer validado, evaluación de pose, skinning, blending e IK conectados a M2. | Vídeos reales 0.25×/1×, error de contacto/apoyo medido; cero aceptación basada en malla rígida rotada. |
| M4 — acciones compartidas | Pases, remates, recepción, entradas/duelos, porteros y restarts por preparación/contacto/recuperación autoritativos. | La pelota no sale antes del contacto; cancelación por pérdida de alcance/posesión; mismo hecho en live y replay. |
| M5 — calidad y escala | Corpus ampliado, caídas/recuperación, sincronía multi-actor, métricas/performance, posible evaluación de motion matching. | Matriz de acciones/contextos y presupuestos G3/G6 aprobados; comparación externa con protocolo, no afirmación comercial. |

Cada gate construye el proyecto afectado y ejecuta sus tests antes del siguiente. Cambios breaking en contratos/replay son deliberados: regenerar fixtures y actualizar todos los productores/consumidores, sin lectores legacy ni dos pipelines en paralelo. No borrar trabajo compartido o documentos previos como parte de esa limpieza.

**Pruebas obligatorias:** mismos inputs/seed/comandos producen estado/eventos idénticos con y sin viewer y con diferentes cadencias/bloques de avance; las mismas entradas de presentación producen la misma pose a un instante tras pausa, seek y 30/60/144 Hz; los contactos no se duplican; iniciar una acción imposible no crea un impulso; replanificación no oscila; swaps de roster no heredan ejecuciones. Medir distancia pie-balón en contacto, deriva del apoyo durante soporte, error de orientación, warping usado, penetraciones y coste CPU/GPU. Fijar tolerancias con metros y corpus aprobado antes de cerrar gates, no inventar resultados alcanzados.

**Estado a 2026-10-01:** M0 quedó documentado como contrato propuesto, no como gate de aceptación. M1 está **parcialmente implementado**: `TeamFrame` posee orientación/angular velocity/angular acceleration; `PlayerSteering` integra orientación con límites configurados; MatchFactory y sustituciones inicializan orientación sin heredar movimiento; Replay v7 captura/codifica estos valores; proyección/interpolación los preservan y el renderer los usa en lugar de inferir yaw desde velocidad o volver a yaw fijo al parar. Tests cubren límites de giro, codec/proyección y rechazo de versión previa. Esto no implementa `MotorRequest`, `MotorExecution`, contactos, orientación independiente de locomoción, locomoción lateral ni planificación contextual. M2 y M4 siguen sin implementar; el test de giro aislado no acredita el caso pareado libre/frontal. M3 está bloqueado por corpus/derechos: `stadium.glb` tiene 0 skins/0 animaciones y `players.blend`/PNG proceden de un generador de sprites de malla estática, no de un humano riggeado. No hay asset humano con licencia redistribuible disponible aquí. M5 no está medido. Próximo gate de Core: percepción de amenazas observadas, ejecución y ledger de contactos con pruebas de hit/miss/interrupción antes de conectar clips; esta orientación parcial no es animación 3D terminada.

## 4. Blender y adquisición profesional: herramientas realmente descubiertas

### Corrección de autoría de brazos — 2026-10-02

**Revisión posterior: la primera corrección fue rechazada visualmente y no acredita calidad.** Se sustituyó el IK de muñeca en locomoción por oscilación explícita de hombro/flexión de codo en el marco del torso. Muñeca alineada con antebrazo, codo con topología continua y pesos mezclados; mangas menos abultadas. Giro/protección/patada tienen curvas de brazos distintas en el corpus, pero esto no demuestra su selección contextual en el partido. `Tools/MatchViewer3D/ReviewPlayerMotion.py` reproduce ocho fases de cinco clips desde tres vistas; `evidence/player-arms-run-cycle.png` contiene el ciclo inspeccionado. Once tests pasan, incluyendo ángulos/longitudes en 24 muestras por ciclo. Los tests no certifican naturalidad. Sigue siendo un humano procedural simplificado, no motion capture ni gate M3 aprobado.

La reevaluación de curvas existentes durante IK anulaba las rotaciones de brazos antes de grabarlas. `player_authoring.py` ahora desacopla la acción durante la resolución y la vuelve a enlazar para insertar claves. La locomoción contrapesa la trayectoria de cada pierna con su brazo ipsilateral, conserva flexión de codo y evita invertir el codo por encima del hombro. Pack GLB/manifiesto/fuente regenerados con Blender 5.2.2; diez tests de `CharacterAnimation` aprobados, incluyendo muñecas junto al torso, balanceo de ambos brazos y altura de codos. Poses inspeccionadas en Blender y renders en `evidence/authoring-idle.png` y `evidence/authoring-run.png`. Esto corrige reposo/locomoción; no acredita biomecánica humana final, contactos físicos, protección contextual, saltos ni aceptación del partido real.

El primer intento confirmó que Blender no estaba ejecutándose; luego se abrió `pitch-floodlit.blend`, se inspeccionó la escena y se actualizó el add-on según su instrucción oficial. Estado actual: Blender **5.2.1 LTS**, add-on **1.7 / protocolo 11** al día, telemetry consent `false`, escena `Scene` con 966 objetos y 65 materiales. La captura del viewport muestra el estadio existente; no se modificó ni exportó. `premium_generators` está vacío; Poly Haven, Sketchfab, Poly Pizza, Hyper3D y Hunyuan están desactivados, y Tripo informa que requiere Premium. Estos resultados describen solo esta instalación/conexión; no implican que no existan otros flujos profesionales fuera de MCP. No se generó ni importó ningún asset.

| Uso offline | Herramientas descubiertas | Condición/fallback |
| --- | --- | --- |
| Inspección | `get_addon_status`, `get_scene_info`, `get_object_info`, `get_viewport_screenshot`, `bpy_api_lookup`, `describe_node_type` | Restablecer conexión y consultar estado/escena antes de automatizar. No asumir una escena vacía ni una versión. |
| Materiales, HDRI y modelos genéricos | `get_polyhaven_status`, `get_polyhaven_categories`, `search_polyhaven_assets`, `get_polyhaven_asset_preview`, `download_polyhaven_asset`, `set_texture` | Verificar estado y preview; conservar fuente CC0 y autor. `set_texture` reemplaza slots: trabajar sobre copia de autoría. |
| Modelos específicos profesionales | `get_sketchfab_status`, `search_sketchfab_models`, `get_sketchfab_model_preview`, `download_sketchfab_model` | Downloadable y acceso/licencia por modelo; búsqueda no garantiza rig, calidad o derecho de redistribución. |
| Recursos estilizados auxiliares | `get_polypizza_status`, `search_polypizza_models`, `download_polypizza_model` | CC0/CC-BY por asset; atribución CC-BY obligatoria. No reemplazar humanos protagonistas por low-poly si no cumplen dirección artística. |
| Generación de objeto individual | `get_hyper3d_status`, `generate_hyper3d_model_via_text` / `generate_hyper3d_model_via_images`, `poll_rodin_job_status`, `import_generated_asset` | Solo con integración confirmada; seguir modo MAIN_SITE/FAL_AI/Premium y sus identificadores reales. |
| Otras integraciones generativas | `get_hunyuan3d_status`, `generate_hunyuan3d_model`, `poll_hunyuan_job_status`, `import_generated_asset_hunyuan`; `get_tripo_status`, `generate_tripo_model`, `poll_tripo_job_status`, `import_generated_asset_tripo` | Tripo requiere Premium según catálogo. Seguir flujo devuelto por estado/generación; no inventar endpoints ni garantía de seed. |
| Preparación/exportación | `execute_blender_code`, `export_scene` (GLB/FBX) | Consultar RNA/enums y nodos por tipo; exportar objetos explícitos. Para rig/shape keys revisar `apply_modifiers=false`; archivar settings completos. El helper no demuestra por sí solo export reproducible. |

**Orden de adquisición:** modelo real profesional con derechos adecuados y rig/animaciones evaluables; generación offline ya configurada cuando no haya un modelo idóneo; artista/rigging/retopología para cerrar calidad. No priorizar figuras humanas compuestas por primitivas ni generar un estadio entero. Generar un objeto cada vez; duplicar/modularizar piezas aprobadas en Blender. Primitivas solo para calibración de metros, tests o placeholders identificados, nunca como aceptación visual.

**Proceso futuro:** consultar estados → buscar/previsualizar y comprobar licencia → adquirir en escena de trabajo → inspeccionar `world_bounding_box`, escala, origen, orientación, materiales y clipping → retopología/UV/PBR/rig/clips/LOD → screenshot y nueva inspección de escena → export validado. Un modelo generado con materiales no implica topología animable. Una imagen de referencia necesita archivo/URL real; no inventarlo ni sustituir sin acuerdo una entrada de imagen por texto.

**Si no hay acceso, licencia o modelo:** registrar bloqueo por familia de assets, usar un pack congelado ya aprobado o encargar/autorizar adquisición profesional. Mantener placeholder explícito solo en desarrollo. No bloquear builds con llamadas de red ni declarar completado el gate humano. Consultar `get_*_status` antes de utilizar cada integración; catálogo disponible no significa servicio configurado.

## 5. Determinismo y gobernanza de assets/formatos

### Tres garantías diferentes

- **Core:** renderer y política de presentación no alteran resultado, eventos ni hash del estado. La seed del motor pertenece a su contrato existente; no es licencia para introducir generación visual en runtime.
- **Presentación:** **ninguna generación aleatoria en runtime, ni siquiera un PRNG con seed**. Variantes de público, cuerpos, ropa, comentario, audio o efectos se hornean/seleccionan en autoría y quedan almacenadas; runtime aplica datos y reglas estables a IDs/ticks. Movimientos analíticos deterministas no son generación estocástica. No reloj de pared, orden de threads o número de frames como entrada estética.
- **GPU:** exigir mismo estado de escena para mismas entradas y toolchain fijada. Comparaciones de imagen con tolerancias por backend/GPU; no prometer píxeles bit a bit iguales entre drivers o plataformas.

Las firmas generativas descubiertas no exponen una garantía de seed/reproducibilidad. **Generación estocástica solo como autoría offline**: incluso si un proveedor añade seed, no aceptar reproducibilidad sin demostrarla. Congelar salida original, `.blend` aprobado, glTF/GLB, prompts/referencias/inputs con sus derechos, ID de job/modelo/proveedor/fecha y parámetros. Nunca regenerar desde el servicio durante build, test, instalación o partido.

### Pack y export reproducibles

1. Separar fuentes de autoría, intermedios y pack runtime. Archivar `.blend`, texturas originales, rigs, clips, scripts y licencias en almacenamiento duradero versionado (Git/LFS o artefactos content-addressed con retención); nada depende de archivos temporales o URLs que expiran.
2. Manifest por asset: ID estable, revisión y SHA-256 de fuente/salida/dependencias, autor/proveedor/URL, licencia exacta/SPDX o texto, atribución, derechos de modificación/redistribución, inputs generativos, herramienta/add-on/exportador y configuración. No confundir licencia de código con la del modelo, imagen o marca.
3. glTF 2.0/GLB como intercambio; esquema propio para semántica futbolística, rig y clips, no nombres de nodos. Definir metros, sistema de coordenadas, forward, escalas aplicadas, tangentes, color lineal/sRGB, canales PBR, normal maps, alpha, skeleton/rest pose, nombres/tiempos de contactos y LOD. Permitir únicamente extensiones implementadas/validadas.
4. Toolchain fijada: Blender/exportador/Python/add-ons, compilador de pack y compresores. Export headless con script/settings versionados, rutas relativas, orden estable y sin timestamp/seed implícitos. MCP facilita autoría e inspección; CI reproduce desde fuentes congeladas sin MCP ni servicios externos.
5. **Gate obligatorio:** dos exports/builds limpios desde las mismas fuentes producen mismos checksums de glTF/GLB y pack distribuido. Si el exportador introduce metadatos volátiles, eliminar solo esos campos mediante canonicalizador versionado y comparar artefacto canónico + buffers/texturas; documentar también hashes raw. Si persisten diferencias geométricas/animación, el gate falla. No llamar reproducible a dos regeneraciones visualmente parecidas.
6. Versionar separadamente esquema de stream/replay, pack, skeleton/clips y presentación/shaders. Major incompatible → error claro y regeneración offline; no lector legacy. Cada evidencia fija todas las versiones y hashes. Conservar fuentes y documentación para poder reconstruir tras cambiar backend o proveedor.
7. Build valida referencias, límites de tamaño, ausencia de rutas externas, hashes y licencias; publish incluye pack/manifest/atribuciones/runtime nativo apropiado. Carga runtime local, validada y acotada; assets faltantes fallan con diagnóstico, no con identidades o geometría inventadas.

## 6. Roadmap secuencial con gates

Cada gate tiene dueño por rol, commit/pack de entrada, comandos, resultado y evidencias persistentes. **Todos están abiertos**: hay piezas implementadas, no aceptación integral. Las correcciones P0 de verdad visual preceden al acabado artístico. Trabajo de adquisición puede avanzar en paralelo después de fijar el contrato, sin cambiar el orden de aceptación.

| Gate / dueño | Entrega y aceptación verificable | Dependencias / riesgos |
| --- | --- | --- |
| **G0 — Baseline y especificación** · responsable técnico + QA | **Parcial:** máquina/toolchain registradas en §7; escena Blender y hash del GLB inspeccionados; builds Core/Client/probe y pruebas deterministas ejecutadas. `docs/evidence/MATCHVIEWER_G1_XWAYLAND.md` conserva captura de ventana GPU+HUD y su reproducción. Falta sesión autoritativa, aceptación formal de derechos, corpus, coordenadas/altura y decisión de plataformas. | Worktree compartido; derechos del GLB no verificados. La captura es un probe sintético, no un partido. |
| **G1 — Host, ABI y lifecycle** · gráficos/plataforma | **Parcial:** screenshot persistente muestra estadio, 22 placeholders animados y HUD en XWayland; Tab+Enter activa el control del HUD. Probe informa 23 lotes estáticos/13 dinámicos y el modo de ciclo completó 50 attach/detach de renderer. El renderer libera inicialización/lotes incompletos y valida slots. Falta validar click de ratón (los intentos no activaron el control), 3 oficiales, HUD de producto/transparencia/popups, resize/HiDPI/suspensión, errores inducidos, readbacks medidos y soak/memoria. | ABI y sincronización aún no tienen gate; Wayland nativo no está implementado. Cincuenta ciclos confirman apertura/cierre básico, no todos los modos de lifecycle. |
| **G2 — Verdad visual y timeline** · Core/presentación | **Implementado parcialmente:** replay v7 guarda roster IDs y orientación/angular velocity, clona identidad al capturar, rechaza roster duplicado, valida versión/cardinalidad/índice y exige el fixture correcto. La proyección valida roster/slots, reutiliza huecos de suplentes y oculta ausentes; Hermite usa `dt` real para movimiento horizontal y vertical, sin umbrales espaciales de corte. El stream marca epochs tipados por cambio de fase, participación o tick regresivo; el muestreo y `Bracket` no cruzan epochs, el seek hacia atrás reinicia la ventana y poses descartadas/underruns quedan contados. Los eventos autoritativos viajan por un ledger separado de poses con secuencia estable y lectura incremental; sobreviven al overflow de poses. Tests cubren identidad/duplicados, sustitución, epochs/seek/underrun/eventos, tangentes verticales y cadencia 30/60/144 Hz. Falta consumo visual sincronizado de eventos y hash integral de SceneFrame/Core conectado/desconectado. | Replay v7 es incompatible intencionalmente con formatos previos; seek es una discontinuidad detectada por tick regresivo, no todavía un API de navegación de replay grabado. Los tests de cadencia no comparan hash GPU. |
| **G3 — Pipeline y vertical humano** · arte técnico + animación | Un humano profesional completo, variantes local/visitante/portero/oficial, balón y campo calibrado; rig, clips locomoción/pase/remate/cabeceo/caída/portero con contactos. Importer deja de exigir 22 raíces por nombre. Manifest íntegro, exports dobles idénticos, validación dura y publish offline. Revisar 0.25×/1× y métricas de contacto. | Acceso/licencias, retopología, rig y clipping. GLB con mallas no pasa el gate de personaje. |
| **G4 — Calidad gráfica y estadio** · gráficos + dirección artística | **Primer paso:** renderer usa MSAA 4× con resolve y attachments transitorios; se eliminó la modulación sinusoidal de alta frecuencia que añadía un patrón fijo al césped. `docs/evidence/MATCHVIEWER_G1_XWAYLAND.md` conserva capturas 1×/4× y la corrección del césped. Restan PBR/texturas, iluminación/color gestionado, sombras/contacto, líneas sin z-fighting, redes/porterías a escala, estadio modular/público prehorneado; instancing, LOD y presupuestos. Cámaras broadcast/táctica/fija; capturas reales objetivo y clips aprobados frente a referencia. | El coste del 4× aún no está medido; falta verificar pacing/memoria y opciones fallback/perfil. Fill-rate/sombras, aliases residuales y oclusión de balón. Más polígonos no equivalen a calidad; medir G4 contra presupuesto desde el primer pase. |
| **G5 — Broadcast e inteligencia** · presentación/UX + Core | Eventos/VAR/repeticiones/audio sincronizados; overlays causales, minimapa, comentario localizado y comandos con estado/aplicación. Layouts estrechos y anchos, teclado/lector, contraste y movimiento reducido. Prueba de tareas y alineación temporal de §7. | Inferencias sin evidencia, desincronía HUD/timeline, saturación visual. Nunca camuflar un defecto de simulación con animación o relato. |
| **G6 — Release y mantenimiento** · QA/plataforma + release | Soak/performance/plataformas completos, packs y publish limpios reproducibles, diagnósticos de fallo y recuperación. Eliminar módulos/PNG/probes de producción obsoletos y formatos sustituidos; retener tests/probes útiles. Revalidar Core/Client y capturas de juego real. | Fugas, instalación limpia/RID y regresiones de dependencias. Ninguna plataforma se anuncia por compilar solamente. |

Después de G6: ampliar rigs, estadios, clima visual y calidad de cámara usando los mismos contratos/gates. Toda nueva feature explica qué hecho representa, qué asset consume y qué coste añade; los fenómenos que afecten al fútbol necesitan primero semántica autoritativa en Core.

## 7. Mediciones y matriz de aceptación

Los siguientes son **presupuestos propuestos**, no medidas alcanzadas. Observación G0 del 28-09-2026: CachyOS Linux x86_64 (kernel 7.2.2), sesión KDE Wayland con display X.Org `:0`; AMD Ryzen 5 5600X (6C/12T), 15 GiB RAM visible; RX 5500 XT Navi 14 con 8 GiB, Mesa 26.2.2, OpenGL 4.6 acelerado; .NET SDK 10.0.302 / runtime 10.0.10, RID `linux-x64`; Blender 5.2.1 LTS. `glxinfo`/`vulkaninfo` enumeraron drivers, pero la ruta WebGPU del juego y su consumo aún no se midieron. Esto identifica una máquina de referencia candidata, no una matriz aprobada ni un benchmark.

| Dimensión | Protocolo y umbral |
| --- | --- |
| Frame pacing | 1080p Balanced, 22 jugadores + 3 oficiales + balón + estadio/público + HUD; 60 s warmup y 10 min de corpus, 3 ejecuciones. Intervalo de presentación p95 ≤16.7 ms y p99 ≤25 ms; CPU de presentación+render p95 ≤4 ms, GPU p95 ≤12 ms, medidos separadamente sin sumarlos ingenuamente. Registrar frames >33.3 ms, resolución física, VSync y cola. |
| Coste/memoria | Registrar draw calls, triángulos efectivos, uploads/frame, allocations/GC, RAM y VRAM pico; techo inicial visor: 1 GiB RAM incremental y 1 GiB VRAM en Balanced. Tras warmup, crecimiento retenido ≤5% en 30 min y tras 50 ciclos; cero recursos GPU/suscripciones propios vivos después de dispose, excluyendo caches globales explícitos. |
| Latencia | Input local→feedback p95 ≤100 ms. Estado aceptado/aplicado incluye tick real. Presupuesto live visual inicial ≤200 ms de retraso respecto a frontera en régimen normal; underflow contabilizado. Eventos, animación y HUD alineados dentro de un tick autoritativo al evaluar un tiempo fijo, sin revelar marcador futuro. |
| Verdad y contactos | Casos de pase, tiro, bote, cabeceo, parada, gol/VAR, reinicio, roja/sustitución/cambio de lado. Cero entidades fantasma o cruces interpolados de discontinuidad. Vertical humano: pie plantado deriva ≤5 cm durante apoyo; contacto visual ≤10 cm y ±1 tick respecto al marcador/evento; medir en coordenadas de mundo y revisar vídeo lento. Fallo se corrige en contrato/clip, no moviendo física. |
| Legibilidad/calidad | Capturas 1536×1024 y 1920×1080 y secuencias 0.25×/1×/4×; revisión de escala, silueta, balón, sombra, césped, grada, HUD y ausencia de popping. Rubrica 1–5 con todos los ejes ≥4 por dos revisores; no sustituye métricas. Prueba formativa con ≥5 usuarios: ≥90% de tareas correctas al identificar posesión, reinicio, presión/cobertura y estado de orden en ≤5 s por tarea. Registrar errores y tiempos, no generalizar a superioridad comercial. |
| Accesibilidad | HUD sin información solo por color; texto normal con contraste ≥4.5:1, foco/teclado, escalado y locales largos; comandos disponibles bajo 1300 px. No ocultar acción decisiva con panel/corte. |

### Pruebas requeridas

- **Puras/contratos:** unidades/ejes y matrices Blender→glTF→GPU; 105×68 m como fixture de calibración, no constante escondida del importer; identidad variable y roles; eventos idempotentes; bytes tipados/versión rechazada; límites/corrupción de pack; bracketing y discontinuidades; clips/IK/cámara reconstruibles por tick.
- **Deterministas:** misma grabación/config/pack produce hash de SceneFrame a ticks seleccionados con render 30/60/144 Hz, pausas y orden de scheduling distinto. Replay lineal y seek dan la misma escena. Core con mismo seed/comandos por tick produce mismos hashes/eventos con visor desconectado, lento o fallando. No comparar comandos por tiempo de pared.
- **GPU:** validación de shaders/features, materiales de referencia, color/depth/alpha, orientación y escalas. Goldens por backend con tolerancias registradas; lectura GPU solo para captura/test explícito. Contadores prueban cero readbacks en camino normal.
- **Lifecycle:** 50 abrir/cerrar/volver a Home; attach/detach/rebind; resize continuo, tamaño cero, minimizar/restaurar, cambio de monitor/escalado, suspensión/reanudación; surface/device-loss y OOM simulados; fallo a mitad de carga y ausencia/corrupción de asset. Sin crash, deadlock, render después de dispose ni pérdida autoritativa; fallo fatal deja estado recuperable y mensaje útil.
- **Plataformas:** Linux X11 **y Wayland nativo por separado**, Windows y macOS como objetivo. G0 decide matriz de lanzamiento, RID/arquitectura y GPU mínima; cada celda requiere publish e instalación limpia, librerías nativas, captura/HUD/input, DPI 100/125/150/200%, lifecycle y performance del perfil prometido. XWayland solo acredita XWayland. Plataforma excluida se declara no soportada, no aprobada implícitamente.
- **Escenarios sostenidos:** partido completo y 30 min de stress con cambios de velocidad, sustituciones, reinicios, prórroga/penaltis cuando el Core los soporte, público máximo y transiciones de UI. Escenarios aún no soportados permanecen bloqueados, no se simulan como hechos de partido.
- **Captura real:** ampliar workflow `Shots` para fijar tick/fracción/cámara/pack/viewport y barrera de frame presentado. Validar captura de ventana completa con superficie GPU y HUD; `RenderTargetBitmap` por sí solo no pasa. Guardar imagen, clip, hashes, log y metadatos juntos. Nunca usar captura de escritorio entero como evidencia del layout.

Comandos existentes de partida (desde raíz; no se afirma que hayan pasado en esta auditoría):

```bash
dotnet build FootballEngine.Client/FootballEngine.Client.fsproj
dotnet run --project Tools/MatchViewer3D/StadiumRendererProbe.fsproj
dotnet fsi --exec Tools/MatchViewer3D/AssetProbe.fsx FootballEngine.Client/Assets/MatchViewer/stadium.glb
dotnet run --project FootballEngine.Client.Tests/FootballEngine.Client.Tests.fsproj -- --list-tests
dotnet run --project FootballEngine.Core.Tests/FootballEngine.Core.Tests.fsproj -- --list-tests
```

Elegir después los prefijos completos de Expecto obtenidos de la lista; no `dotnet test --filter`. Los actuales `--shots`, `--shots-size`, `--shots-pages`, `--match-video` y `--match-seed` ayudan a reproducir, pero no reemplazan la futura barrera temporal/de composición ni son gates GPU ya certificados.

## 8. Cierre y gobernanza de entrega

Un gate se cierra con revisión técnica y visual, tests/métricas, evidencias persistentes y hashes de fuente/pack/toolchain; nunca con «compila», una imagen de Blender o un probe sintético solamente. La entrega final exige trazabilidad de todos los assets, reproducción offline, matriz de plataformas explícita, cero incompatibilidades silenciosas y un único camino de presentación.

**Siguiente acción al implementar:** cerrar la trazabilidad/autorización del asset y generar una captura real reproducible; seguir G1 con interacción de ratón, ciclo de vida/composición GPU+HUD y matriz XWayland/Wayland. En G2 conectar el ledger fiable de eventos con efectos visuales sincronizados y validar hashes reproducibles antes de afirmar continuidad completa. La actualización del add-on y los slices de código no cierran esos gates. Este documento queda como backlog maestro: actualizar aquí estado, decisiones y evidencias de cada gate sin duplicar planes.
