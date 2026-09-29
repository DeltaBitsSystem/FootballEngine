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
| `Views/Components/MatchProjection.fs`, `MatchVisualTypes.fs` | Replay v6 aporta `PlayerId` por slot; proyección valida cardinalidad/nombres y asigna `VisualSlot` a los titulares activos. Suplentes activos reutilizan determinísticamente huecos de titulares; sin número de camiseta inventado. | Cortes todavía usan distancia/flow/ocupación, no `ContinuityEpoch`/eventos de contacto tipados; altura de balón usa tangentes cero pese a disponer de `BallVz`; oficiales se proyectan, no se animan. |
| Transformaciones GPU | Usa `VisualSlot` validado para las 22 raíces; ausentes se trasladan fuera de cámara, nunca a posiciones de formación fabricadas. Mundo `(X,0,-Y)`, balón `(X,0.11+BallZ,-Y)`; yaw vuelve a orientación fija con poca velocidad. Ignora `RenderFrame.Officials`. | Resolver orientación sostenida y añadir modelos/renders de árbitros; definir altura/radio de balón y contratos de contacto. |
| Cámara | Renderer usa cámara fija del GLB, actualizada al cambiar tamaño. `MatchCamera.fs` sigue definiendo `Camera2D` y perfiles sin implementación 3D equivalente. | No existe aún director broadcast ni seguimiento 3D integrado. |
| `AppState/MatchPresentation.fs`, `MatchRuntime.fs` | Worker live y canal semántico separados de ventana visual acotada; reloj visual con Stopwatch, pausa, 1×/2×/4× y hold en frontera. Poses publicadas por runtime. | Base útil, pero una ventana de poses no es archivo de replay ni timeline de eventos fiable; alinear HUD, animación y audio al mismo tiempo presentado. |
| `Core/Match/Simulator/Replay.fs` | Formato breaking v6: roster IDs estables en el header, además de snapshots de posición/velocidad/altura/spin, activos, intención, árbitros y flow. Sin `ContinuityEpoch` ni contrato completo de animación/contacto. | G2 parcial: añadir continuidad/eventos y contrato de contacto; no reparar en shaders lo que falte en Core→presentación. Los readers previos no son compatibles por decisión. |
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

## 4. Blender y adquisición profesional: herramientas realmente descubiertas

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
| **G2 — Verdad visual y timeline** · Core/presentación | **Implementado parcialmente:** replay v6 guarda roster IDs por partido, clona identidad al capturar, rechaza roster duplicado, valida versión/cardinalidad/índice y exige el fixture correcto. La proyección valida roster/slots, reutiliza huecos de suplentes y oculta ausentes; Hermite usa `dt` real para movimiento horizontal y vertical, sin umbrales espaciales de corte. El stream marca epochs tipados por cambio de fase, participación o tick regresivo; el muestreo y `Bracket` no cruzan epochs, el seek hacia atrás reinicia la ventana y poses descartadas/underruns quedan contados. Los eventos autoritativos viajan por un ledger separado de poses con secuencia estable y lectura incremental; sobreviven al overflow de poses. Tests cubren identidad/duplicados, sustitución, epochs/seek/underrun/eventos, tangentes verticales y cadencia 30/60/144 Hz. Falta consumo visual sincronizado de eventos y hash integral de SceneFrame/Core conectado/desconectado. | Replay v6 es incompatible intencionalmente; seek es una discontinuidad detectada por tick regresivo, no todavía un API de navegación de replay grabado. Los tests de cadencia no comparan hash GPU. |
| **G3 — Pipeline y vertical humano** · arte técnico + animación | Un humano profesional completo, variantes local/visitante/portero/oficial, balón y campo calibrado; rig, clips locomoción/pase/remate/cabeceo/caída/portero con contactos. Importer deja de exigir 22 raíces por nombre. Manifest íntegro, exports dobles idénticos, validación dura y publish offline. Revisar 0.25×/1× y métricas de contacto. | Acceso/licencias, retopología, rig y clipping. GLB con mallas no pasa el gate de personaje. |
| **G4 — Calidad gráfica y estadio** · gráficos + dirección artística | PBR/texturas, iluminación/color gestionado, sombras/contacto, AA, líneas sin z-fighting, redes/porterías a escala, estadio modular/público prehorneado; instancing, LOD y presupuestos. Cámaras broadcast/táctica/fija; capturas reales objetivo y clips aprobados frente a referencia. | Fill-rate/sombras, memoria, aliases y oclusión de balón. Más polígonos no equivalen a calidad; medir G4 contra presupuesto desde el primer pase. |
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
