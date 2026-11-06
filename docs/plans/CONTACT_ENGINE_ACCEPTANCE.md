# Aceptación del contacto causal

## Baseline reproducible

Antes de sustitución construir solución, Client.Tests y Benchmarks. Separar errores de warnings; corregir errores de compilación de baseline antes del reemplazo y documentar cambios mínimos. El agente principal inició esos builds con logs en `/tmp/opencode/contact-baseline/`; son diagnóstico inicial, no aceptación ni permiso para omitir repetirlos si el árbol cambia. Registrar SDK, commit, rama, estado sucio, hash de diff y no seguidos relevantes, hardware, configuración, semilla, comandos y tamaños de corpus.

Antes de editar capturar timings del baseline con Release calentado para semillas 11, 23, 47, 101 y 313 (fixtures idénticas copiadas/snapshot, configuración inicial documentada). Ejecutar 3 repeticiones por semilla; separar warmup. Guardar eventos/estadísticas resumidos para explicar cambios, no exigir idéntico resultado a una versión físicamente distinta. Si la herramienta real exige otro comando/fixture, documentarlo y fijarlo antes de medir versión nueva. No inventar mediciones.

## Matriz de pruebas obligatoria

### Geometría y solver puros

- Separación/no contacto; roce tangencial; choque frontal y lateral; centros coincidentes; velocidades nulas; cuerpo saltando sobre otro.
- Barrido rápido a velocidad máxima soportada sin atravesamiento; impacto cerca de inicio/final del tick; límites de tolerancia inclusivos explícitos.
- Distintas masas, restitución y fricción: conservación de momento en sistema pasivo aislado; energía no aumenta por choque pasivo ni por corrección de penetración; tangente acotada.
- Simultáneos de 3+ cuerpos, presión contra límite del campo, todos activos congestionados, contacto persistente y límite de iteraciones.
- Invariantes tras cada paso: finitud, estabilidad acotada, no penetración mayor que tolerancia numérica configurada (objetivo <= 1 cm para fixtures no inicializadas penetradas), corrección limitada; overlap inicial converge sin explosión con criterio y plazo fijados en test.
- Reflejo geométrico y cambio Home/Away; permutación de almacenamiento conservando identidades no cambia resultado físico más allá de tolerancia. Empates exactos no ofrecen ventaja Home sistemática; claves de identidad no sustituyen evidencia física.

### Causalidad futbolística

- Mismo escenario/semilla, cambiar solo instante de entrada: llega, falla o impacta cuerpo conforme a geometría.
- Mismo impulso con apoyo estable frente a giro/vuelo/fatiga: diferencias esperadas de perturbación/recuperación, no caer siempre por atributo.
- Shield permite proteger desde posición adecuada y fracasa si deja pelota expuesta; rival no alcanza a través del cuerpo sin contacto.
- Entrada limpia con desvío sin posesión; robo controlado; toque dividido; entrada fallida con recuperación; carga lateral legal; falta ofensiva y defensiva.
- Pase/tiro rápido que cruza cuerpo o efector entre muestras produce contacto y cambia trayectoria antes de resultado de gol/salida. Rebote en torso/pierna/cabeza sin regalar control.
- Recepción suave/fuerte, frontal/lateral/de espaldas y fatigada: geometría/velocidad residual y probabilidades de ejecución explicables. No salto de posición ni pelota magnética.
- Conducción/giro brusco puede exponer pelota; toque preparado se cancela al perder alcance/participación. Un solo impulso y una sola evidencia confirmada por toque.
- Portero recibe de pie fuera del área sin manos; captura/parada válida respeta restricciones; pelota desviada antes de meta no conserva resultado del tiro original; manos accidentales tienen tratamiento reglamentario sin control inventado.

### Reglas y consecuencias

- Roce sin infracción; carga legal; careless/reckless/excessive; pelota primero pero impacto peligroso; juego peligroso sin contacto; obstrucción; falta ofensiva.
- Infractor/víctima correctos, lugar y lado del penal correctos para ambos sentidos de ataque.
- DOGSO/SPA con y sin defensores capaces de intervenir, posesión probable y dirección; distinciones aplicables a penal/disputa legítima; no tarjeta por semilla de coordenadas.
- Ventaja realizada/no realizada, recuperación de falta, tarjeta diferida y expulsión; una sanción y un reinicio por incidente. Reinicios/descanso/fin cancelan acciones apropiadas sin perder sanciones pendientes.
- Métricas/eventos/evidencia coherentes: un tackle no equivale siempre a robo y contacto no equivale a falta.

### Determinismo, lifecycle y persistencia

- Semilla/inputs/commands idénticos => estado deportivo/eventos/RNG idénticos, captura on/off y consumidor ausente/lento incluidos.
- Fast/live y distintas políticas de publicación producen la misma autoridad deportiva.
- Checkpoint/restauración a mitad de windup, contacto persistente, caída y ventaja: siguiente secuencia idéntica, IDs/cursors correctos, sin impulso duplicado.
- Clones del planner no mutan original; acciones y equilibrio se proyectan con misma física.
- Sustitución/expulsión/lesión cancelan generación anterior; replay no aplica reacción a jugador nuevo del mismo slot.
- Esquemas anteriores rechazados explícitamente sin borrado; roundtrip nuevo sin campos perdidos; config NaN/infinita/negativa/relaciones imposibles rechazada con Result.
- Overflow observable/acotado de evidencia, sin crecimiento ilimitado ni cambio deportivo.

### Presentación real

- Fixtures deterministas de carga, protección, entrada, desvío, tropiezo/caída y recuperación en viewer real. Capturar imágenes con workflow existente y abrirlas/inspeccionarlas; comprobar raíces, apoyo, alcance y momento de contacto. Tests de geometría de pose además de capturas estáticas.
- Misma grabación a 30/60/144 FPS y tras buscar/restaurar checkpoint: mismos contactos deportivos, tareas sin duplicación y poses finitas; no evidencia usada antes de AvailableAt.
- No exigir animación de calidad cinematográfica ni nuevo asset, sí consecuencia reconocible, sin penetraciones graves persistentes o root teleport para simular impacto. Registrar paths de capturas y límites visuales.

## Rendimiento y memoria

Medir Release, mismo entorno/corpus baseline; 5 semillas x 3 repeticiones, warmup excluido, mediana/p95/p99 de tick y duración total, bytes asignados por tick/partido, máximo de memoria/scratch/journals y saturación solver. Probe adicional congestionado con 22 jugadores por al menos 10 000 subticks, tras warmup, para comprobar acotación/no NaNs/no runaway.

Presupuesto: p99 total < 6.25 ms (25% de dt=25ms) en máquina registrada; regresión mediana de duración de partido <=15% respecto de baseline funcional bajo mismo workload; scratch/journals acotados, sin asignación proporcional a pares por tick en núcleo geométrico/solver tras inicialización. Medir por separado asignaciones preexistentes de agentes/eventos, no prometer cero global. Captura mínima adicional debe mantener los objetivos foundation (<=2% total, <=5% p99 frente a misma física con captura desactivada); informar variabilidad y más muestras si el ruido impide conclusión.

Si baseline ya incumple un límite absoluto, registrar deuda previa y demostrar coste incremental, sin declarar aprobado ese límite. No bajar Hz, quitar colisiones, ignorar estados, reducir participantes ni desactivar evidencia para cumplir. Si tras optimización local medida no cumple, entregar evidencia del cuello y bloqueo concreto; no autoampliar a rediseño ajeno ni maquillar cifras.

## Comandos y cierre

- `dotnet build FootballEngine.slnx`
- `dotnet build FootballEngine.Client.Tests/FootballEngine.Client.Tests.fsproj`
- `dotnet build FootballEngine.Benchmarks/FootballEngine.Benchmarks.fsproj`
- Listar suites Expecto con `dotnet run --project <proyecto> -- --list-tests`; ejecutar prefijos completos descubiertos, nunca asumir que `dotnet test --filter` las ejecuta.
- Ejecutar pruebas relevantes incrementales y suites completas Core.Tests y Client.Tests al final; registrar cantidad y fallos. Ejecutar pruebas/tools adicionales afectados.
- `git diff --check`; Fantomas check limitado a F# tocado, sin reformatear baseline ajeno. Cumplir reglas de indentación de AGENTS.
- Buscar símbolos obsoletos para verificar eliminación real de caminos probabilísticos/bypasses y compatibilidad muerta.
- Evidencia final `docs/evidence/CONTACT_ENGINE.md`: matriz pass/fail/block con pruebas concretas, resultados de builds/tests, resultados crudos de benchmark/probes, archivos de capturas, cambios de esquema y límites. Nunca sobrescribir informes anteriores ajenos.
- Marcar pasos completados en plan únicamente cuando la evidencia los sustente. No parar después de scaffolding, un solver aislado o un build. Resultado completo para revisión manual; revisión independiente del agente principal posterior, no auto-certificación del implementador.
