# Ejecución del plan del motor

## Estado

G0/O1/G1 en curso. **El plan completo no está terminado y O1 aún no está cerrado.** Los gates posteriores conservan sus criterios; no se han reemplazado por mejoras cosméticas. Este documento conserva la evidencia histórica por etapa: sus recuentos de tests y pausas de trabajo no representan todos el estado final. La implementación y los assets están ahora agrupados en commits de desarrollo; consultar la [guía de review](../reviews/DEVELOPMENT_STACK_2026_10_07.md) para el estado y la validación actuales.

## Implementado

- `Match/Contracts/Observation.fs`: evidencia tipada con identidades de ocurrencia, buffer circular de 4096 entradas por partido, retención cronológica, contadores de expulsión y pérdida de enlaces.
- Decisión → impulso mediante scope explícito; impulso → eventos de recepción/resultado mediante asociaciones que sobreviven al diferimiento del reducer. Reinicios enlazados por `RestartId`. No se atribuyen causas por mera proximidad temporal.
- Captura de decisiones, impulsos, probabilidades de recepción/parada, evaluación arbitral, reinicios, eventos confirmados y comandos efectivos. El texto identifica resoluciones probabilísticas como tales, no como contactos físicos medidos.
- Captura selectiva de alternativas ya evaluadas por actor e intervalo mediante API; no vuelve a ejecutar el planner ni consume RNG.
- Sustitución directa de `DecisionTrace`: fuera strings/listas de texto y desplazamiento del array durante ticks. Sin adaptador legacy.
- Publicación de snapshots al cliente y panel `Why?` con expansión de causas y aviso de contexto expulsado. Formateo fuera del tick.
- `--match-evidence` permite repetir el escenario de screenshots del inspector.
- Consultas tipadas por actor/acción/evento/intervalo y exportación JSON de inspección con identidades, enlaces y contadores de pérdida. Este JSON no sustituye al input/checkpoint reproducible.
- Corregida la lista exhaustiva de eventos del test de flags: faltaba `PlayerBodyFlight`, presente en el motor del baseline.
- G1: eliminados `Phase=200/250` y claves artificiales `slot+1000`. `PendingPlayerAction`/`PlayerActionChoice` pertenecen al equipo; las evidencias VAR medidas y fechadas pertenecen a `World.Reviews`. `RunLookup` ya no alberga decisiones ni revisiones. Sin adaptador legacy. Canonical hash de diagnóstico incluye ambos estados nuevos.
- Test de aislamiento verifica que una revisión no pisa carreras ni propuestas de acción, y limpiar Home conserva la revisión Away. Se mantiene la precisión float32 de la evidencia VAR anterior; este refactor no certifica la regla de offside.
- G1: fachada `Simulator.runMatch` única con `Result`; eliminadas fachada de excepción, `runMatchResult` duplicada y `weightsPath` ignorado. Callers tests/benchmarks actualizados; warmups/profiles no descartan errores. Test de política live inválida confirma error tipado sin excepción.
- O1: publicación incremental por cursor de ocurrencia. Cliente conserva una ventana acotada; tests de consumidor retrasado, publicación duplicada y stale. Dumps explícitos conservan snapshots completos. Detectado y corregido coste del transporte full-copy (~8% en el corpus corto).
- El inspector detectó que el kickoff se etiquetaba como balón cruzando la línea de gol. Reducer ahora diferencia inicio, nuevo periodo y gol concedido; fixtures cubren las tres causas. No se cambió la física ni la regla del gol.
- G0: guía actual del repositorio y contrato de coordenadas/relojes/ownership/visibilidad/RNG/budgets en `MATCH_ENGINE_FOUNDATION.md`.
- G0: edición normativa IFAB 2026/27 confirmada en fuente oficial; inventario artículo a artículo y protocolos en `IFAB_2026_27_COVERAGE.md`. Lectura contrastada de leyes 2, 9, 10 y 12; demás artículos permanecen pendientes de auditoría semántica. Ninguna fila se certifica sólo por enumerarla.
- O1: prueba adicional satura las cuatro posiciones de la cola diagnóstica, comprueba rechazo inmediato de exportación/selección y resolución explícita de todas las peticiones retenidas al cerrar. LiveMatch 20/20 tras este añadido; última suite Core 508/508.
- O1: controles reales de selección de jugador, captura de alternativas por los siguientes 60 segundos de simulación (intervalo fijado en el worker), detener detalle y exportar. Cola diagnóstica acotada a cuatro peticiones; cierre/fin resuelven tareas pendientes sin dejarlas colgadas.
- Exportación reproducible usando `JsonCodec` existente: input completo/config/lineups/rules/seed, checkpoint binario con comandos y RNG, evidencia original. Codec probado por roundtrip y rechazo de formato incompatible. I/O de archivo fuera del worker deportivo. `Tools/MatchDiagnostics` reconstruye el frontier por la ruta única actual, no por otro simulador.
- Prueba manual de UI: selector, captura y exportación activados en aplicación real. Archivo generado en `$XDG_DATA_HOME/FootballEngine/Diagnostics` (o equivalente de LocalApplicationData). Verificación del archivo real: match 1736, seed 4609114385319, frontier 1628, hash 3301712737038092047. Screenshot `match-evidence-controls-live.png`.
- Evidencia de falta → decisión/reinicio enlazada por IDs explícitos; scope de reducer aislado y probado. Reinicios `SetPieceAt` ya no se etiquetan todos como cruce de banda.
- G1: máscara agregada de flags derivada una vez de la DU real; bits asignados explícitamente, independientes del orden de declaración. Test runtime verifica unicidad y un bit por kind. Se mantiene el límite actual de 64 kinds.

## Validación ejecutada

| Prueba | Resultado |
|---|---|
| Baseline Core Release | 491/492; fallo de lista de flags descrito arriba |
| Core Release tras cambios | **526/526** |
| Client tests | **100/100**, incluyendo LiveMatch y CLI textual |
| Observation tests | **19/19**: retención, snapshots, enlaces explícitos, pérdida de contexto, selección, RNG/eventos/estado, resultado terminal on/off, consultas/JSON, transporte delta, kickoff, falta/reinicio, ventaja, cadenas profundas y auditoría integral |
| Client build | Sin errores; permanecen warnings del árbol |
| Screenshot determinista y captura de ventana | Generadas e inspeccionadas; escenario real con panel de evidencia |

Captura de ventana: [match-evidence-inspector.png](match-evidence-inspector.png).

Comandos reproducibles:

```bash
dotnet run --project FootballEngine.Core.Tests/FootballEngine.Core.Tests.fsproj -c Release
dotnet run --project FootballEngine.Client.Tests/FootballEngine.Client.Tests.fsproj
dotnet fsi Tools/MatchViewer3D/ObservationBench.fsx
dotnet run --project FootballEngine.Client/FootballEngine.Client.fsproj -- --shots /tmp/opencode/match-evidence --shots-pages match --shots-settle 18000 --match-evidence
```

## Medición provisional

`ObservationBench.fsx`: Release, Ryzen 5 5600X, 12 procesadores lógicos; 24 ejecuciones alternadas on/off, tres seeds, 12000 ticks por ejecución, dos warmups. La última ejecución aislada dio:

| Métrica (mediana) | Off | On |
|---|---:|---:|
| Tiempo total | 133.856 ms | 134.422 ms |
| p99 de avance de un tick | 116.375 µs | 116.184 µs |
| Bytes asignados por hilo | 60401192 | 60566224 |

Tiempo total: +0.42%; asignaciones: +0.27%. La pequeña mejora aparente del p99 es ruido, no una optimización demostrada. Hashes de eventos idénticos por seed. El benchmark mide `advanceTo` de un tick, no un profiler interno del stepper.

**No certifica el presupuesto completo:** falta medir publicación de snapshots y consumo UI, partidos completos, ráfagas de eventos, captura selectiva máxima, memoria y GC. Una primera ejecución concurrente con tests mostró mayor variación; no se usa para declarar cumplimiento. JSON completo en `observation-benchmark.json`.

## Pendientes explícitos

- Cerrar G0: reconciliar documentación/ownership con arquitectura actual y ampliar baseline reproducible.
- Cerrar O1: auditar cobertura de todas las consecuencias, no sólo ejemplos completos, y multicausalidad restante. Ventaja recordada ya enlaza incidente original y disparador. Corpus textual guardado y consultas UI por acción/evento/intervalo son operativos. No confundir exportar o un ejemplo sin links ausentes con certificar causalidad/legalidad completas.
- Extender medición de deltas al worker/UI real y partidos completos: las medianas cortas oscilan alrededor del objetivo de 2%; no declarar certificación con una ejecución favorable.
- Revisar cobertura de enlaces de causalidad: la ausencia de enlace sigue siendo ausencia de evidencia, no autorización para reconstruir historias.
- G1 pendiente: contratos completos de reglas/control, identidades secuenciales deportivas y separación restante de codec/comandos del runtime. Contratos/job/sesión/proyección ya están separados; flags tienen asignaciones estables verificadas. Ampliarlos más allá de 64 kinds requiere otro corte.
- No modificados todavía: resultados probabilísticos al cruce del arco, contactos planares, aplicación incompleta de instrucciones tácticas, ruleset IFAB, checkpoint directo, inteligencia colectiva y gates G2–G8.

## Revalidación tras el corte tipado de G1

Core 501/501; Client 90/90. Benchmark aislado: Off 131.826 ms / On 132.116 ms (+0.22%); p99 112.571 / 112.851 µs (+0.25%). Tres seeds mantienen sus hashes tanto on/off como respecto al baseline anterior al refactor (12000 ticks, no certificación de partidos completos). Artefacto separado: `observation-benchmark-g1.json`.

## Transporte incremental y fachada única

## Corpus de partidos completos y validación de exportación

### Contrato de desempate (G1)

### Autoridad de instrucciones tácticas (G1)

`TeamWorld.Tactics` es el único estado efectivo mutable, inicializado desde el input de kickoff. Comandos actualizan ese estado; TeamDecide, TeamEmergent y PlayerDecision consumen instrucciones efectivas en vez del `BaseTactics` inmutable. Se eliminó `DefensiveLine * 100` al aplicar comandos: el productor World ya usa metros (20 + slider×10), y TeamDecide los consume como metros. No se cambia la escala para rescatar el error previo. Intensidades no finitas/fuera de [0,1] y línea no finita/fuera de la longitud del campo se rechazan antes de entrar en la cola. Hash diagnóstico incluye las tácticas efectivas.

Fixture verifica línea 44 m y amplitud 0.7 tras una orden y tras otra decisión colectiva: ya no se sobrescriben con la instrucción inicial. Restore reconstruye la orden efectiva y el mismo RNG. Inspector aclara que la formación todavía no remapea roles; almacenar su nombre no es ejecutar el cambio. Core completo 511/511, Client 93/93; nueva comprobación de restore incluida en MatchSession 14/14. Batería causal de intervenciones y transiciones de formación siguen pendientes de G5.

Identidad de input reforzada: hash de frontera recorre el input resuelto completo (alineaciones, atributos, posiciones, tácticas, reglas y configuración) con orden estable, tipos y representación exacta de floats; no usa el hash aleatorio de strings del runtime ni entra en ticks. `MatchJob` incorpora esta identidad en su hash de commit. Checkpoint versión 3 la incluye y rechaza otro contexto incluso en frontier cero, antes de que eventos/RNG puedan detectar diferencias. Fixtures prueban cinco cambios deportivos manteniendo IDs/seed/revisión, y equivalencia de inputs construidos independientemente. FNV es identidad operacional, no firma criptográfica. No hay lector legacy de checkpoint v2.

`MatchContext.IsKnockout` eliminado y sustituido por `MatchTieResolution`: AllowDraw, ExtraTimeThenPenalties y PenaltiesAfterRegulation. EventApply aplica el contrato explícito al cierre de periodo; World elige el formato actual equivalente para sus fixtures. Dumps, builders y benchmarks actualizados sin adaptador. Batería de 12 transiciones cubre tres formatos × regulación/prórroga × empatado/no empatado. Core 509/509, Client 93/93 y benchmarks compilados. Reglas de agregado/away-goals y protocolos completos siguen pendientes, no se infieren del nuevo contrato.

### Cadenas de resolución verificadas

El filtro de jugador del inspector usa ahora la consulta causal, no sólo el actor directo del registro: incluye consecuencias enlazadas a resolución/impulso/decisión. Consulta construye índices de fuentes una vez por snapshot en el consumidor; no vuelve a escanear toda la ventana por cada ancestro. Fixtures adicionales verifican ascendencia explícita, pérdida de contexto por eviction, ausencia de enlace por proximidad temporal y terminación ante ciclos malformados. Core 514/514, Client 93/93. Workflow de screenshot ejecutado y ventana real inspeccionada (`match-evidence-causal-filter-live.png`); la selección visual individual de consecuencias no quedó comprobada en esa captura y no se declara verificada sólo por compilar.

Rechazos de ejecución también son evidencia tipada: distancia real/límite configurado y rechazo por la heurística actual de offside antes de lanzar. Un rechazo enlaza la decisión activa; el evento de offside enlaza el rechazo concreto. La explicación explicita que no hubo impulso ni participación offside físicamente medida. Se trasladó el límite previo de 65 m a `Actions.MaxPassExecutionDistance`, con unidades y validación finita/positiva, conservando el valor efectivo. Fixture verifica que rechazar un pase no modifica el balón ni consume RNG. Core 512/512 y Client 93/93. Este corte no certifica Law 11: la heurística prematura sigue registrada como deuda de G4.

### Exportación desacoplada y captura selectiva prolongada

El worker sólo toma input/checkpoint/evidencia inmutables; serialización de checkpoint y JSON se ejecutan en el consumidor. Fixture verifica que avanzar 600 ticks más y aplicar un comando pendiente no modifica el snapshot ya capturado. El paquete conserva `ObservationSnapshot` tipado completo, no sólo explicaciones textuales; no hay adaptador para el paquete anterior. `MatchDiagnostics --actor/--action/--event` consulta esa evidencia sin reconstruir el partido. Las consultas siguen exclusivamente ancestros explícitos retenidos, de modo que un evento con `ResolutionCause` aparece al consultar su acción/actor; ciclos se acotan por IDs visitados.

Corpus ampliado: 24 partidos completos, cuatro modos (off, mínimo, publicación, detalle selectivo de un atacante durante todo el partido), tres seeds y dos repeticiones alternadas. Todos los hashes coinciden por seed. Medianas pareadas: captura +0.47%, publicación +0.08%, detalle/publicación +0.27%; p99 mínimo +0.41%, publicación +0.24%. `observation-benchmark-selective-full-matches.json` conserva filas, GC, asignaciones, p50/p95/p99 y resultados. No certifica UI ni todos los escenarios densos; la variabilidad observada permanece visible.

**Defecto baseline observado, no corregido:** seed 11 acaba 0–0 con sólo un remate. `observation-seed-11-stalled.json` conserva la cadena: falta en tick 148272 → free kick/planning en 148273–148274 → ningún evento de autorización/ejecución antes del full time en 254521. La ventana completa sólo tuvo 300 registros con detalle, frente a 31705/31266 en seeds 12/13. Se debe investigar el bloqueo de positioning/readiness usando el estado de reinicio, sin forzar autorización ni teletransportar jugadores. Seeds 12/13 producen 132/152 remates totales: deuda de realismo para G2–G5/calibración, no una distribución aprobada.

**Corrección posterior verificada:** `RestartReadinessProbe.fsx` reprodujo la barrera inválida: destino (13.461,-34) para spot (7.318,-32.045), dentro de 9.15 m. La proyección radial se recortaba contra la banda. `RestartPlanner.awayFrom` ahora selecciona un destino interior con distancia suficiente cuando el recorte invalida la proyección; no cambia árbitro, no fuerza autorización y no teletransporta. Tests cubren seis posiciones cercanas a bordes y la seed completa hasta tick 160000, donde el reinicio 7 ya terminó. Core 508/508. El ejemplo anterior se conserva como evidencia pre-corrección.

Corpus repetido tras corrección: 24 partidos, hashes iguales entre cuatro modos por seed. Seed 11 ahora termina con 81 remates y 17163 registros mínimos (17919 con detalle), no con un único remate. Sigue siendo 0–0, sin forzar marcador. Medianas pareadas de overhead: captura +0.28%, publicación +0.46%, selectivo/publicación +0.48%; p99 +0.26%/+0.39%. Artefactos: `observation-benchmark-restart-fixed.json`, `observation-seed-11-restart-fixed.json`. Los hashes cambian respecto del motor con deadlock; no existe obligación de conservar esos resultados defectuosos.

Recepción, bloqueo, parada y rebote de marco tienen un tipo de resolución explícito. Los eventos de pase/remate/parada/gol apuntan a la ocurrencia concreta de resolución; ésta apunta al impulso ejecutado. La asociación genérica del impulso no sobrescribe la causa más precisa. Fixtures verifican recepción→pase y parada/gol→resolución→impulso. El texto mantiene explícito que son adjudicaciones estocásticas, no contactos físicos. Un fixture adicional verifica la aplicación de línea táctica y la advertencia de campos todavía no ejecutados. Suite Core completa: 506/506. Esto refuerza O1, pero no reemplaza la física pendiente de G2 ni certifica reglas completas.

Última medición Release: 18 partidos completos (tres seeds, dos repeticiones por seed y tres modos alternados), dos warmups, 254722–264842 ticks por partido. Todos los modos conservan el hash por seed. Comparación **pareada por seed/repetición**, para no confundir diferencias entre partidos con overhead: mediana captura +0.23%, captura/publicación +0.42%; p99 +0.52%/+0.47%. p99 absoluto ~34 µs, por debajo de 6.25 ms (25% de dt). Asignaciones agregadas ~991.8/995.1/997.2 MB; siguen siendo altas y pertenecen a G7, no sólo a observación.

Artefacto completo y filas individuales/outliers: `observation-benchmark-full-matches.json`. Las medianas agregadas no pareadas difieren y algunos runs muestran ruido; los datos se conservan, no se eliminan outliers. Estos resultados sostienen el budget mínimo en este corpus, **no** una certificación de hardware/población/rendimiento UI general. Soak y captura selectiva extrema siguen pendientes.

```bash
FOOTBALL_OBSERVATION_BENCH_TICKS=400000 FOOTBALL_OBSERVATION_BENCH_REPETITIONS=6 dotnet fsi Tools/MatchViewer3D/ObservationBench.fsx
dotnet run --project Tools/MatchDiagnostics/MatchDiagnostics.fsproj -- <archivo-exportado.json>
```

La restauración del archivo real exportado se verificó además del fixture de tests. Sigue reconstruyendo desde kickoff hasta checkpoint; restauración autoritativa directa/seek acotado sigue pendiente.

### Medición anterior del transporte

Core 504/504; Client 90/90; Benchmarks build sin errores. Screenshot determinista del inspector con deltas generado e inspeccionado; RenderTargetBitmap no captura la superficie externa WebGPU (la captura de ventana previa sí la captura).

Antes del cambio, publicación full-copy: Off 129.960 ms, captura 131.072 ms, captura/publicación 140.487 ms; asignaciones subían a 103.9 MB. Tras deltas: última ejecución Off 123.955 ms, captura sin publicación 127.099 ms, captura/publicación 123.586 ms; 60.4/60.6/60.8 MB. La diferencia no monotónica confirma ruido de medición: **no se da por certificado el target 2%**. p99 112.340/113.482/112.370 µs. Se conserva identidad on/off. Artefacto: `observation-benchmark-deltas.json`. Pendiente corpus largo y medidas de cola/UI/GC.

La deuda táctica detectada es visible en las explicaciones: el executor actual actualiza tipo de directiva/línea/presión/anchura; aceptar una instrucción no prueba que todos sus campos afecten al juego.

## Continuación local: ownership de frontera e inspector por intervalo

- `Contracts/MatchBoundary.fs` contiene los contratos de input/comandos/checkpoint/resultados, no el runner. `Simulator/MatchJob.fs` posee la identidad de jobs; `Simulator/MatchSession.fs` posee sesión/avance/restauración; `Simulator/MatchAdapter.fs` proyecta el resultado usando esa única sesión. El adapter es la frontera existente con carrera, no compatibilidad legacy ni un simulador alternativo. Namespace/callers siguen coherentes por compilación, sin copia de la implementación anterior.
- `MatchInput.snapshot` copia todos los arrays de alineación, participantes y ventanas al entregar un job y al crear la sesión. Tests mutan el input del caller después de entregar el trabajo y comprueban estabilidad de identidad/checkpoint/RNG/proyección física. Maps y records inmutables no se duplican innecesariamente.
- Órdenes para sesiones terminadas o en/al otro lado del límite de seguridad se rechazan sin tocar journal/evidencia. El orden se verifica contra el journal completo, no sólo la cola pendiente. Safety limit no equivale a final reglamentario.
- Inspector: consultas combinables por jugador, acción, evento y ticks inclusivos; reset de acción/evento/intervalo conserva selección de jugador. Cantidad total de coincidencias visible antes del límite de 24 filas. Selecciones de IDs expulsados permanecen explícitas; no se reconstruye su contexto perdido. Tests verifican que filtros no emiten comandos deportivos ni cambian eventos/revisión y rechazan intervalos invertidos.
- Se detectó un **bloqueo real durante edición de intervalo** en `/tmp/opencode/match-evidence-query-final-interval.png`: proceso vivo con CPU alta y memoria creciente, no excepción registrada en stderr/journal. Se detuvo exclusivamente el proceso de prueba. Rebinding podía redisparar updates idénticos; callbacks ignoran valores sin cambio, reducer normaliza/compara la consulta y `MatchSlice` conserva la instancia ante un no-op. Regresión comprueba identidad del estado tras consulta/selección repetida. No se atribuye al renderer una causa sin stack capturado.
- Reproducción posterior acotada: `DOTNET_GCHeapHardLimit=0x40000000 timeout --kill-after=5s 75s dotnet run --no-build --project FootballEngine.Client/FootballEngine.Client.fsproj -- --shots /tmp/opencode/match-evidence-crash-verify --shots-pages match --shots-settle 45000 --match-evidence`. Edición real a `Until=100`, Tab: **3 coincidencias en [0,100]**, reloj avanzando a 00:27, proceso a los 45 s con RSS 727596 KiB; captura automática final generada y proceso salió normalmente antes del timeout. Imagen de ventana WebGPU inspeccionada: [match-evidence-interval-recovery.png](match-evidence-interval-recovery.png). Es una reproducción positiva acotada, no soak ni certificación de rendimiento de UI.
- Validación: Core Release **518/518** tras el guard; Client completo **94/94** antes del guard y LiveMatch **21/21** después. Builds Core/Client y `git diff --check` aprobados. Los warnings previos del worktree permanecen. O1/G1 todavía requieren auditoría de cierre; G2–G8 no se declaran implementados por estos cambios.

## Observabilidad textual y validación posterior de frontera

- `Tools/MatchDiagnostics`: CLI headless con `--summary`, `--chain <ObservationId>`, selectores combinables `--actor/--action/--event/--from/--until`, límite explícito de filas, `--verify` y JSON limpio por stdout. La verificación deportiva utiliza la sesión única; los mensajes de verificación van a stderr cuando se pide JSON. Opciones inválidas/duplicadas y combinaciones que no se consumirían se rechazan antes de I/O. Cuatro tests Expecto protegen el parser, incluyendo doce solicitudes inválidas.
- `Observation.chain` recorre ancestros retenidos con stack explícito y conjuntos de visitas; reporta links ausentes, ciclos y fuentes futuras. Consultas por identidad también excluyen fuentes posteriores, sin atribuir actor a un registro anterior mediante evidencia futura. Pruebas positivas, negativas y de archivo malformado: **14/14 Observation**.
- `RefereeFouls` enlaza sus consecuencias a la ocurrencia de evaluación arbitral, que conserva el evento original. No altera RNG, orden ni decisión deportiva. Un reinicio directo ahora explica falta → evaluación → respuesta/reinicio. Ventaja recordada sigue siendo deuda explícita de contexto de incidente/disparador.
- [Corpus textual guardado](causal-corpus/README.md): seis partidos completos, seeds 11/12/13 con una orden efectiva en tick 6000, 16840/24094/18769 ocurrencias en los capturados. Hash/estado/RNG iguales en cada par on/off. Artefacto incluye SHA-256 de la DLL, runtime/arquitectura y budget externo de 131072 registros/partido; publicación contigua verificada. Los ejemplos elegidos de pase/pérdida/remate/falta/orden no tienen links ausentes, ciclos ni fuentes futuras. No implica cobertura exhaustiva ni demuestra que la táctica causó el resultado.
- `CaptureFixture.fsx` generó `/tmp/opencode/match-current.json` sin GUI. CLI verificó frontier 4000, hash 6104987999638426024 y RNG/comandos; `--chain 17` muestra las cuatro ocurrencias reales decisión → impulso → recepción actual → pase. Consulta jugador 2 / ticks [148,173] retorna cinco registros; `--verify --json` se parseó como JSON válido. El archivo UI de contrato anterior se rechazó por el campo `MaxPassExecutionDistance` faltante; no se añadió lector legacy/default.
- Nueva corrida Release: **24 partidos completos**, tres seeds, cuatro modos y dos repeticiones por seed, hashes deportivos iguales. Medianas pareadas: captura −0.088% (ruido, no mejora demostrada), publicación +0.277%, selectivo/publicación +0.510%; p99 captura +0.308%, publicación +0.628%, absoluto ~38 µs. Artefacto: [observation-benchmark-text-corpus.json](observation-benchmark-text-corpus.json). Sostiene budgets de captura en este corpus; no certifica UI/soak. Asignaciones ~1.08 GB/partido siguen siendo deuda de G7.
- Frontera G1: input inválido retorna `InvalidMatchInput` antes de construir `World`: slots/arrays inconsistentes, identidad duplicada/compartida, clubes iguales, geometría/velocidad no finita o build vacío. Context y EffectiveConfig deben concordar; ya no se sobrescribe silenciosamente la configuración del input. Esto valida estructura/identidad, no toda la elegibilidad o las leyes.
- Journal limitado explícitamente a 100000 comandos, igual que el codec de checkpoint; overflow rechaza sin cambiar cola/journal/evidencia. **20/20 MatchSession**, **521/521 Core**, **98/98 Client** y diff limpio en la última validación. El objetivo completo sigue pendiente; no se declara cerrado O1/G1 ni implementados G2–G8.

## Incidente de ventaja autoritativo y causalidad de dos fuentes

- Reemplazo directo del tuple de ventaja por `AdvantageIncident`: evento fuente, tick original, lado, infractor, víctima y coordenadas. `AdvantageStarted` transporta ese contexto; estados pendiente/realizado/recordado/expirado lo conservan. No se incorpora una caché diagnóstica como fuente de verdad ni un lector del contrato anterior.
- Retorno por pérdida de posesión, recall explícito y expiración conservan ambos participantes en `RestartCause.Foul`. Las respuestas de otro equipo no consumen la ventaja; recall repetido no vuelve a conceder reinicio. Se elimina el fallback arbitrario Home del productor de expiración.
- `PairedEvents` enlaza incidente original y disparador real del reinicio. El consumidor recorre ambos y reporta una rama ausente aunque la otra siga retenida. Consultas por evento recorren ambas; causas futuras continúan excluidas. JSON de inspección reemplazado por formato 2 con array `Causes` y roles/IDs tipados separados, sin exportador legacy.
- Dos regresiones adicionales cubren tres rutas de retorno, contexto retenido, participantes, causalidad, rama evictada, consulta por incidente, JSON, lado incorrecto y doble aplicación. Core completo **523/523**, Client **98/98**; tras la última aserción de consulta, Observation **16/16**. Builds Core/Client y `git diff --check` aprobados.
- Corpus regenerado en `causal-corpus/`: seis partidos completos con captura on/off idénticos; añade ejemplos reales de ventaja recordada. Seed 11, raíz #5781: falta #5772 y posesión #5782 como dos fuentes explícitas, seguida de impulso/decisión del disparador. No se inventa que el disparador sea la falta.
- Benchmark actual: 24 partidos completos, medianas pareadas captura −0.056% (ruido), publicación +0.684%, selectivo +0.274%; p99 captura +0.335%, publicación +0.518%, ~39–40 µs absolutos. [Artefacto](observation-benchmark-advantage.json). Las mediciones sostienen el budget de captura en este corpus, no certifican todas las cargas ni toda la UI.
- Pendiente: identidades de evento aún basadas en hash estructural, evaluación legal completa de ventaja/sanción diferida y causalidad exhaustiva. Este corte no certifica G4 ni cierra el plan.

## Cadenas JSON y auditoría del historial completo

- `--chain <ObservationId> --json [--verify]` consume el mismo recorrido tipado que texto. Exporta raíz, ocurrencias reales, causas ausentes tipadas, ciclos y aristas futuras excluidas. Root ausente retorna error; no se fabrica una ocurrencia para representar contexto ausente. Verificación permanece en stderr, JSON limpio en stdout. Formato 2 compartido, sin lector/exportador legacy.
- Índice de fuentes compartido por consultas, recorrido y auditoría, construido una vez por operación fuera del tick. El recorrido deja de escanear todo el snapshot por cada ancestro. Regresión de 4096 ancestros verifica recorrido iterativo completo, sin recursion/stack overflow ni fuentes descartadas.
- `--audit [--json]` inspecciona toda la ventana retenida: IDs duplicados, fuentes ausentes/ambiguas, aristas futuras y ocurrencias sin causa con desglose por tipo. Selector de jugador o chain junto con audit se rechaza, no se ignora. Es auditoría de enlaces directos, no certificado deportivo/legal ni detector integral de ciclos; ciclos se detectan en el recorrido de raíz.
- Corpus regenerado: 18 cadenas JSON y tres auditorías completas, junto con texto y summary/build SHA. Seeds 11/12/13 no tienen enlaces explícitos ausentes/ambiguos/futuros ni ObservationIds duplicados. Sin causa: 12093/17021/13471 ocurrencias, incluyendo decisiones legítimamente raíz **y** consecuencias todavía sin enlazar. Seed 11: 85 faltas, 243 tackles, 398 posesiones, 365 centros, 472 regates y 242 impulsos sin causa, además de otros tipos. No se usa ausencia de links rotos para declarar explicado el partido.
- CLI probado sobre archivo headless: chain #17 contiene cuatro ocurrencias y cero anomalías; `--audit --json --verify` se parsea con 61 registros retenidos y checkpoint frontier 4000/hash 6104987999638426024 verificado. JSON de archivos malformados prueba ciclo, fuente futura, rama ausente y uint64 máximo.
- Última validación: **526/526 Core**, **100/100 Client**, builds afectados y `git diff --check` aprobados. Seis partidos del corpus nuevamente idénticos on/off. Cambios limitados a consumidores de evidencia, sin cambiar física/scheduling/RNG. O1 aún requiere enlazar consecuencias sin fuente y auditar aceptación completa; no se inicia G2–G5 a ciegas.
