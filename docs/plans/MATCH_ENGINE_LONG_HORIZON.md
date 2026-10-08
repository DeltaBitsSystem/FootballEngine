# Match Engine — evolución de largo horizonte

Estado: **implementación iniciada; G0/O1/G1 parciales, G2–G8 pendientes**. Propuesta: 2026-10-02. Evidencia de ejecución: [MATCH_ENGINE_IMPLEMENTATION.md](../evidence/MATCH_ENGINE_IMPLEMENTATION.md). No es certificación del motor actual.

## 1. Objetivo y decisiones de producto

Construir un motor offline, determinista, realista, rápido y divertido dentro del **monolito modular existente en F#/.NET**, conectado al cliente Avalonia/WebGPU. Un partido debe tener causas deportivas explicables y resultar creíble al verlo completo, acelerado o resumido. Las decisiones del entrenador deben transformar lo que ocurre en el campo, no modificar un resultado prefabricado.

La ambición frente a Football Manager y al modo carrera de FIFA/EA Sports FC se traduce en cuatro ventajas verificables: inteligencia colectiva legible, causalidad física, decisiones de entrenador relevantes y una experiencia de observación convincente. No se promete superar esos productos por cantidad de módulos o por adoptar 3D.

### Restricciones explícitas

- **Cero backward compatibility**: proyecto stealth, sin usuarios publicados. Se pueden romper contratos, formatos, configuraciones, fixtures y APIs internas. No crear adaptadores legacy, migraciones de saves, modos viejo/nuevo, campos deprecados ni flags temporales para conservar implementaciones descartadas.
- Mantener el stack y la aplicación integrada. No microservicios, motor externo, segundo simulador, framework ECS genérico ni reescritura total por principio.
- Sustitución directa por cortes verticales comprobables. Una etapa termina con el nuevo camino funcionando y el anterior eliminado; no con dos fuentes de verdad.
- No implementar todo este documento en una única operación gigante. Las dependencias y gates son deliberados; cada corte mantiene un repositorio compilable y verificable.
- El 3D es una representación espacial autoritativa del fútbol, **no** una simulación biomecánica de cada músculo. Táctica y navegación pueden seguir usando el plano del césped cuando corresponda; contacto, alcance y arbitraje no pueden ignorar altura y volumen.
- Reglas oficiales, condiciones de competición y parámetros de comportamiento son categorías distintas. No calibrar una regla para mejorar goles por partido.
- Realismo y diversión se trabajan juntos. No rubber-banding, goles forzados, hándicaps secretos, teletransportes correctivos ni resultados elegidos antes de jugar.
- **Observable y explicable por diseño**: el software debe poder explicar qué sucede y sus causas deportivas sin depender de logs masivos ni comprometer los budgets de simulación. La explicación es un consumidor de evidencia, nunca una segunda lógica del partido.

## 2. Punto de partida verificado

Lectura dirigida del worktree actual; no es una auditoría exhaustiva ni una nueva ejecución de toda la suite. El árbol tiene trabajo compartido sin commit. El desarrollo del cuerpo/equipamiento queda en pausa y no forma parte de esta implementación.

| Área | Evidencia actual | Consecuencia para el plan |
|---|---|---|
| Núcleo separado | `FootballEngine.Core/FootballEngine.Core.fsproj`, carpetas `Match/Contracts`, `Stepper`, `Systems`, `Restart`, `Simulator` | Evolucionar esta organización, no la arquitectura antigua descrita en partes de `AGENTS.md`. |
| Una sesión | `Contracts/MatchBoundary.fs` contiene `MatchSession`; `Simulator/Simulator.fs` delega en ella | Conservar un único stepping para batch/live/replay; eliminar fachada de excepción y argumento `weightsPath` ignorado cuando se sustituyan sus callers. |
| Scheduling | `Stepper/SystemsTable.fs` contiene la tabla `Scheduler.systems`; `Stepper/Stepper.fs` ejecuta el pipeline | Documentar ownership y visibilidad temporal antes de reordenar; cambiar orden cambia simulación. |
| Física 3D existente | `Systems/Ball/Physics.fs`: impulso común, gravedad, drag, Magnus, rebote y cruce del arco | No empezar de cero; reforzar contacto continuo, volumen del balón, marcos y resolución causal. |
| Resultados todavía abstractos | `Systems/Ball/Outcome.fs`, `resolveShot`: bloqueo y parada probabilísticos al cruce; xG interviene en probabilidad de gol | Eliminar el segundo veredicto que contradice la trayectoria/contactos; xG será análisis, no autoridad del gol. |
| Contacto parcialmente planar | `Systems/Ball/Contact.fs`: estimaciones de llegada por distancia XY y velocidad, variantes locales del cálculo | Un modelo compartido de alcanzabilidad con orientación, aceleración, altura y tiempo; no adjudicar balón por cercanía proyectada. |
| Cuerpo inicial | `Systems/Players/BodyPhysics.fs`: salto vertical y aterrizaje, condición afecta salto | Ampliar a balance, soporte, alcance y compromisos de acción; no confundir salto con cuerpo 3D completo. |
| Estado sobrecargado | `Systems/Referee/VAR.fs` usa `RunLookup`, `Phase = 250`; `Systems/Players/Decision.fs` documenta marcador de decisión `Phase = 200` | Sustituir marcadores por estados tipados con dueño propio. |
| Arbitraje heurístico | `Systems/Referee/Fouls.fs`: ventaja por progresión/compañero, tarjetas probabilísticas y constantes locales | Separar evidencia, calificación legal, percepción arbitral y sanción; revisar casos, no sólo ajustar probabilidades. |
| Reglas de competición iniciales | `Contracts/MatchContext.fs`: `CompetitionSubstitutionRules`, `IsKnockout` | Ampliar un ruleset validado: booleano de knockout no describe todos los formatos/protocolos. |
| Replay visual separado | `Simulator/Replay.fs`: proyección cuantizada, posiciones/velocidades/altura/orientación | Conservar la frontera, añadir timeline de acción/contacto y discontinuidades; replay visual no es checkpoint autoritativo. |
| Continuación actual | `MatchBoundary.fs`, restore vuelve a avanzar hasta la frontera del checkpoint | Medir coste; sustituir por snapshot completo si se necesita seek/restore acotado. No afirmar que hoy restaura directamente todo el estado. |
| Tests existentes | `FootballEngine.Core.Tests/FootballEngine.Core.Tests.fsproj`: contratos, física, contactos, reinicios, inteligencia, sesiones/live | Reutilizar escenarios e invariantes útiles; nombres o cantidad de tests no certifican cobertura de fútbol real. |

`docs/MASTER_PLAN.md` es un plan específico de reinicios, no un plan global del motor. Sus ideas de dueño único, causa e impulso común se integran aquí; no ejecutar instrucciones históricas ni duplicar lo que ya está implementado. En G0 se reconciliarán los planes existentes y se actualizará la guía de arquitectura a las rutas reales.

## 3. Arquitectura objetivo: responsabilidades, no infraestructura nueva

```text
World/carrera → MatchInput inmutable + comandos fechados
                         ↓
                  MatchSession (único runner)
                         ↓
                  Stepper / orden explícito
                         ↓
  Cognición → intenciones → acciones → locomoción/contactos
       ↑                       ↓              ↓
  Memoria/equipo       hechos físicos → reglas/árbitro
       ↑                                      ↓
       └────────── eventos confirmados / flujo ┘
                         ↓
   Resultado + estadísticas + observación/replay + diagnóstico
                         ↓
            World/carrera y cliente (consumidores)
```

Las flechas describen flujo de datos, no llamadas entre agentes. Sesión conoce stepper; stepper conoce sistemas; sistemas usan contratos y servicios puros. Ningún sistema depende del cliente, del runner o de SQLite. El cliente nunca escribe directamente en `World` del partido.

### Dueños únicos

| Estado/decisión | Dueño | Otros consumidores |
|---|---|---|
| Tiempo simulado, comandos y avance | MatchSession/Stepper según contrato explícito | Presentación, manager, clock |
| Integración del balón | BallPhysics | Contactos, reglas, observación |
| Resolución ordenada de contactos | ContactResolver | Acciones, posesión, faltas |
| Pose física reducida, soporte y desplazamiento | PlayerBody/Locomotion | Acciones, contactos, percepción, cliente |
| Ciclo de acción | ActionController | Decision propone; locomoción ejecuta; contacto confirma |
| Control de balón | Possession/Control | Equipo y jugador; no equivale a pegar balón al root |
| Calificación de infracción y sanción | Rules/Referee | Flow, Restart, stats, viewer |
| Fases globales y periodos | MatchFlow/MatchClock, con límites no solapados | Stepper y presentación |
| Preparación/autorización/cierre de reinicio | RestartController | Agentes cumplen roles y objetivos legales |
| Estructura táctica colectiva | Team | Jugadores deciden dentro del marco |
| Plan individual y creencias | Player cognition | Team recibe señales agregadas, no mente omnisciente |
| Cambios tácticos y participación | Manager + aplicación de comandos validada | World recibe resultado confirmado |
| Métricas | Proyecciones de hechos/eventos confirmados | No modifican física ni marcador |
| Animación, cámara, audio, UI | Cliente | Consumen observación; no arbitran contactos |

Un módulo tiene una razón deportiva clara y una superficie pequeña. Evitar `Common`, `Helpers` o `Shared` como depósito de lógica sin dueño. Compartir algoritmos puros reales, no crear una abstracción por cada par de funciones similares.

### Estado y contratos

- Identidades tipadas para acción, contacto, incidente, reinicio y evento; secuencias locales estables, sin depender de hash estructural para unicidad.
- Reemplazar marcadores mágicos por DUs/records: `ActionState`, `OffsidePhase`, `AdvantageState`, `ReviewIncident`, `BodyState`, `ControlState`. Los nombres finales seguirán las convenciones locales.
- Distinguir dato autoritativo, creencia, caché derivada y proyección visual. Una caché declara de qué revisión depende y se invalida explícitamente.
- Mantener arrays/SoA en hot paths útiles, con acceso slot-indexado encapsulado. `PlayerId` no es el índice; sustitución y expulsión no reutilizan identidad deportiva por accidente.
- No almacenar la misma orientación, posesión, línea de fuera de juego o fase en múltiples lugares editables. Snapshots inmutables para consumidores, no referencias a arrays que siguen mutando.
- `BalanceConfig` aloja comportamiento con unidades y validación; `Ruleset` contiene obligaciones legales; `Environment` parámetros físicos resueltos. Agrupar config por dueño y eliminar parámetros sin lector o con efectos duplicados.

## 4. Determinismo, tiempo y causalidad

### Contrato que debe durar

Mismo input resuelto, build/versión de motor, configuración, reglas, entorno, semilla y secuencia de comandos efectiva → mismo resultado y traza autoritativa, independientemente de cámara, FPS, pausa de visualización, rapidez de reproducción o modo de salida.

No se exige conservar hashes entre cambios del motor: no hay compatibilidad hacia atrás. Versiones/hashes existen para rechazar datos incompatibles y reproducir bugs del mismo build, no para cargar formatos antiguos. Inicialmente garantizar bit-exact en la plataforma/runtime soportados; ampliar a otras arquitecturas sólo después de medir diferencias de coma flotante. No introducir fixed-point por dogma.

- Tick físico fijo elegido con pruebas de estabilidad/coste; evaluar 60/120 Hz en G0/G2, no atarlo al FPS. Substeps o swept tests locales para contactos rápidos si hacen falta, con límites deterministas.
- Cognición a cadencia acotada y eventos relevantes; física continua cada tick. Reaccionar a un evento no significa latencia cero ni reevaluar todo a cada frame.
- Fijar visibilidad por fases: comandos validados → percepción/decisión con latencia → acciones y movimiento → candidatos de contacto/cruce → resolución cronológica → reglas/flujo → publicación. La secuencia definitiva se decide con pruebas, no copiando este esquema sin analizar el pipeline actual.
- Dentro de un intervalo: ordenar por tiempo de ocurrencia, prioridades físicas/legales explícitas y desempate estable por identidad. Un gol posterior a una falta, una salida anterior a un contacto o dos toques simultáneos no dependen del orden Home/Away en un loop.
- RNG separado por dominios/ocurrencias cuando se justifique: presentación nunca consume RNG de partido; eliminar efecto mariposa de agregar un consumidor irrelevante. Especificar streams y su snapshot antes de usarlos.
- Presupuesto de planificación por número de operaciones/nodos, nunca por tiempo de CPU; iteration order estable. Paralelizar partidos independientes primero, no introducir carreras dentro de un partido.
- Un hecho se aplica una vez; usar relaciones de causa, no inferir causalidad de proximidad temporal. Gol provisional, anulación y gol confirmado tienen consecuencias diferentes.
- Safety guards terminan con error estructurado y dump reproducible; nunca fabrican un final reglamentario ni un impulso para destrabar.

### Modos de observación

`Fast`, `Live`, replay y highlights comparten simulación. Cambian publicación/almacenamiento, no leyes ni decisiones. Speed-up aumenta avance por tiempo real o reproduce más rápido; **no aumenta dt**. Reducir FPS visual no elimina contactos ni eventos. Highlights seleccionan intervalos del partido sucedido, no resimulan oportunidades hasta obtener escenas interesantes.

Pausa de reproducción y pausa para decidir como manager se definen separadamente. Los comandos se aceptan con tick efectivo/revisión; nunca retroactivamente sobre el tramo ya confirmado. Backpressure tiene política explícita: se pueden coalescer poses intermedias, no perder semántica ni compartir estado mutable con UI.

## 5. Modelo físico deportivo 3D

### Balón y geometría

- Geometría de campo parametrizada legalmente, lados y cambio de campo explícitos; radio del balón incluido en cruces completos de líneas.
- Barrido de esfera contra suelo, postes/travesaño y proxies de contacto. Resolver primera colisión válida y trayectoria restante; prevenir tunneling y doble rebote del mismo contacto.
- Rebote, fricción, rodadura, drag y spin consistentes con unidades y disipación. Viento/superficie/humedad sólo con efecto modelado y testeable; no multiplicadores cosméticos de resultado.
- El balón no busca al receptor ni se curva hacia el arco por un destino declarado. Target/trajectory intent es intención o pista de búsqueda, no autoridad física.
- Gol por cruce completo legal y confirmado; poste y parada pueden impedir ese cruce **antes**, no una tirada probabilística después de cruzar. Gol en propia puerta y desvío se atribuyen con política estadística separada de las reglas de validez.
- xG/post-shot xG son estimadores observacionales; el jugador puede estimar valor de una opción con incertidumbre, pero ninguna métrica impone gol/no gol.

### Cuerpo y locomoción reducidos

- Root, orientación, velocidad, aceleración, soporte de pies, balance, radio/volumen y ventanas de alcance. Proxies funcionales de pie, cabeza, torso, manos del arquero y brazos relevantes para infracciones; no colisionar toda la malla renderizada.
- Aceleración, frenado, giro y cambio de dirección con límites ligados a perfil y fatiga. No rotación instantánea ni velocidad lateral equivalente a sprint frontal.
- Alcanzabilidad incorpora velocidad actual, orientación, reacción, preparación, altura, salto y recuperación. Una sola consulta pura compartida por planificación, intercepción y ejecución, con incertidumbre de percepción donde corresponda.
- Navegación/reserva de espacio, evitación local y contacto físico tienen dueños distintos. Evitar que dos solvers empujen al jugador dos veces o que un clamp visual esconda una penetración autoritativa.
- Choques plausibles, shielding, tropiezo, recuperación y salto disputado; límites de penetración y energía. Contactar a un rival no es automáticamente falta.
- Pies/cabeza de contacto procedimentales autoritativos de bajo coste, luego retarget visual. No integrar esqueleto completo en cada tick como requisito previo para buen fútbol.

### Acción como compromiso físico

Contrato común: propuesta → factibilidad → preparación → compromiso/contacto → recuperación o interrupción. Persisten objetivo, ejecutante, parte corporal, pie dominante, timing, alcance e intención de impulso. Un intento fallido también es observable.

- Pases rasos/elevados, centro, tiro, volea, cabezazo, despeje y remate de primera usan el mismo camino de contacto/impulso, con diferencias de preparación y técnica.
- Error técnico se aplica a timing, dirección, fuerza, spin o superficie corporal bajo presión/fatiga. No añadir otra moneda de éxito después de resolver el contacto.
- Conducción por toques y control alcanzable; recepción puede amortiguar, orientar, rebotar o escapar. No conceder posesión de una pelota físicamente inalcanzable.
- Entradas de pie/deslizantes, protección y duelos aéreos producen evidencia de contacto y riesgo. Acciones incompatibles, cooldown físico y cancelación están en el ciclo, no en múltiples timers dispersos.
- Arquero: colocación, reacción, pasos, elección de alcance/parada, dive, bloqueo, catch, parry, caída y distribución. Rechazar saves fuera de alcance, respeto a área/manos/back-pass y posesión temporal.
- Animación comparte identidad y reloj del contacto; IK corrige visualmente dentro de tolerancias, no mueve el balón para hacer coincidir un clip. Sin contacto físico → no evento de patada/parada consumada.

## 6. Inteligencia de fútbol y diversión causal

### Percepción y decisión individual

Campo de visión, escaneo, oclusión funcional, memoria reciente, confianza, anticipación y atención limitada. El jugador no conoce posiciones futuras reales ni todos los atributos enemigos. Fatiga afecta atención, ejecución, reacción, intención y movilidad con consecuencias diferenciadas.

Selección acotada de opciones factibles: mantener/controlar, pase, regate, remate, centro, despeje, protección y movimiento sin balón. Evaluación de valor/riesgo/coste con personalidad y rol; no elegir uniformemente la mejor opción omnisciente. Compromiso/histéresis evita oscillation de intención; cadencias y thresholds están configurados.

Planificación corta y ramificada con predicción aproximada compartida, no clones de `World` y simulaciones completas por cada jugador/tick. Registrar motivos y opciones descartadas mediante modo diagnóstico opt-in.

### Equipo como unidad

- Fases: ataque posicional, defensa organizada, transición ofensiva/defensiva, balón parado. Responsabilidades concretas por rol y situación, no sólo perseguir coordenadas de formación.
- Distancias entre líneas, amplitud/profundidad, ocupación escalonada, superioridades, tercer hombre, apoyos y rupturas, rotaciones y lado débil.
- Presión con triggers, primer presionante, coberturas, sombra de pase, trap y salida; contra-presión con riesgo y límite de esfuerzo. Defensa de descanso evita que todos ataquen simultáneamente.
- Marcaje zonal/individual mixto, seguimiento/entrega de marca, línea defensiva y anticipación del fuera de juego basados en creencias y timing.
- Química/familiaridad mejoran coordinación y entendimiento, no agregan un bonus invisible de gol. Adaptación surge de evidencia reciente con ruido y demora; no omnisciencia sobre táctica rival.
- Rutinas de reinicio ofrecen opciones legales y coordinación física; ejecutante conserva capacidad de adaptar/cancelar ante una situación que cambió.

### Entrenador, carrera y drama

Órdenes tácticas tipadas con tradeoffs observables; distinguir intención del manager, comprensión del equipo y ejecución. Cambiar presión/anchura/ritmo genera respuesta gradual y coste, no instant teleport ni modifier de marcador. Sustituciones, cambio de arquero, lesión, inferioridad numérica y tiempo añadido alteran roles y capacidad real.

Fatiga aguda y reserva anaeróbica, recuperación, carga acumulada, lesión y estado mental se conectan al input/output de carrera sin que el motor acceda a persistencia. No duplicar lesiones/participación/estadísticas entre partido y World. Registrar intervalos efectivos de participación y carga para reconciliar resultado.

Drama por riesgo, errores plausibles, adaptación, segunda pelota y presión competitiva. Auditar Momentum/WinProbability existentes: conservar como estimador o mecanismo psicológico causal documentado; retirar toda influencia circular del marcador/probabilidad de victoria sobre la probabilidad de gol. Diversión medida por decisiones significativas, diversidad y lectura del juego, no únicamente más goles.

## 7. Reglas: cobertura completa y demostrable

Referencia: IFAB, edición **2026/27** como base propuesta, confirmada disponible en su catálogo oficial el 2026-10-02. Fijar edición, documento y checksum en G0; no depender de `/latest` durante ejecución. Separar protocolos opcionales/ensayos de reglas universales. Un ruleset histórico es contenido elegido para una competición, **no** backward compatibility de software.

Fuentes: [documentos oficiales](https://www.theifab.com/laws-of-the-game-documents/), [VAR](https://www.theifab.com/laws/latest/video-assistant-referee-var-protocol/). La revisión de artículos y excepciones se completa en G0/G4; este plan no sustituye el texto normativo.

### Matriz obligatoria de las 17 reglas

Cada artículo/subartículo y protocolo tendrá: fuente/edición, escenario, precondiciones, evidencia física, decisión, reinicio/sanción, evento visible, dueño, tests positivos/negativos y estado. `Implementado`, `pendiente` y `validado` son estados distintos. Ninguna fila se declara completa por tener un enum.

| Regla | Alcance a cubrir |
|---|---|
| 1. Terreno | Dimensiones válidas, marcas/áreas, límites, arcos, cambio de campo, superficie; validación de GLT/VAR e infraestructura según competición. |
| 2. Balón | Dimensiones/propiedades legales, balón defectuoso/reemplazo y reanudación correspondiente; estado externo explícito, no fallo aleatorio sin causa. |
| 3. Jugadores | Mínimo legal, titular/suplente/expulsado, elegibilidad, cambio de arquero, sustituciones y ventanas, entradas/salidas, personas extra e interferencia. |
| 4. Equipamiento | Seguridad, obligatoriedad/colores, corrección y reingreso; equipo visual separado del cuerpo pero validación legal de su estado. |
| 5. Árbitro | Autoridad, ventaja/retorno, disciplina diferida, lesiones, señales, interrupción/suspensión/abandono y límites para cambiar decisiones. |
| 6. Otros oficiales | Asistentes, cuarto árbitro y VAR según disponibilidad, funciones/señales; no meros adornos que contradigan decisiones. |
| 7. Duración | Periodos, intervalos, tiempo perdido, añadido anunciado/extendido, prolongación para penal, prórroga y abandono. |
| 8. Inicio/reinicio | Sorteo/elección inicial, saque inicial y balón a tierra, posiciones/distancias y condiciones para estar en juego. |
| 9. En/fuera | Balón entero fuera, interrupción arbitral, contacto con oficiales y condiciones que requieren balón a tierra. |
| 10. Resultado | Gol válido/anulado, ganador, formato de desempate y tanda: elegibles, turnos, igualdad de jugadores y fin anticipado. |
| 11. Fuera de juego | Snapshot al toque pertinente, cuerpo jugable, balón/penúltimo rival/mitad propia; participación activa, interferencia, ventaja, rebote/desvío vs juego deliberado y excepciones de reinicios. |
| 12. Faltas/conducta | Imprudencia/temeridad/fuerza excesiva, mano contextual, holding/push/obstrucción/juego peligroso, simulación, SPA/DOGSO, disciplina a jugadores/staff y reglas del arquero. |
| 13. Tiros libres | Directo/indirecto, ubicación con excepciones de área, distancias/barrera, señal, gol directo permitido/no permitido, segundo toque e infracciones. |
| 14. Penal | Posiciones, autorización, ejecutante/arquero, invasión con efecto, amago, doble toque y procedimiento/sanción/repetición según caso y edición. |
| 15. Lateral | Lugar/técnica/pies/manos, distancia rival, pelota en juego, segundo toque, gol directo y cuenta regresiva donde el protocolo corresponda. |
| 16. Saque de arco | Lugar, posiciones, entrada rival, pelota en juego, gol directo, segundo toque y demoras/protocolo aplicable. |
| 17. Córner | Cuadrante/distancia, pelota en juego, gol directo, segundo toque, infracciones y asignación reglamentaria correspondiente. |

Para reglas administrativas/no generadas en juego normal, incluir un comando/escenario determinista que ejercite la consecuencia y validación prepartido cuando proceda. No hace falta generar publicidad ilegal o invasiones con frecuencia para cubrir su contrato; tampoco declararlas fuera de alcance silenciosamente.

### Verdad, observación arbitral y revisión

- Hecho físico ≠ percepción arbitral ≠ decisión. Motor de reglas puro determina consecuencias a partir de evidencia; árbitro observa con posicionamiento/latencia/incertidumbre controlados. Nunca errores arbitrarios del texto legal.
- Offside tiene fase tipada desde el toque, candidatos e implicación posterior; no snapshot del rematador al disparo ni `RunLookup` de táctica como archivo VAR.
- Registrar contacto, momento, parte corporal, posesión, dirección/velocidad, posibilidad de jugar balón y contexto. DOGSO/SPA y mano no se reducen a distancia al arco o moneda universal de tarjeta.
- Ventaja contiene incidente, equipo, evaluación, plazo reglamentario y sanción pendiente. Si vuelve la falta, no aplicar dos veces tarjetas/reinicio.
- VAR utiliza incidentes/evidencias inmutables y categorías revisables del protocolo fijado; distinguir check/review/recomendación/decisión final. Sólo correcciones autorizadas, con consecuencia deportiva coherente.
- No rebobinar físicamente el partido para borrar lo ocurrido: decidir provisionalidad y efectos reversibles/confirmados con reglas de flujo; auditar stats, marcador y narrativa tras una anulación.
- Tiempo añadido deriva de intervalos de tiempo perdido con política oficial/configuración de competición, no porcentaje mágico; distinguir tiempo físico, periodo, reloj mostrado y tiempo efectivo.
- Todas las tablas de excepciones de penal, doble toque, gol directo, indirectos y reinicios tienen tests exhaustivos por caso; incluir reglas nuevas del arquero/protocolos de la edición fijada, no asumir texto de años anteriores.

## 8. Presentación de un partido real

Motor publica observaciones tipadas: root/cuerpo, velocidades, orientación, fase de acción, objetivo de contacto, incidente, identidad, reloj y estado arbitral. No nombres de clips ni huesos específicos de un asset en contratos Core.

- Un reloj de presentación interpola snapshots sin cambiar autoría. Marcar discontinuidades: colocación legal de balón detenido, sustitución, cambio de periodo y seek no se interpolan como movimientos sobre el campo.
- Timeline ordenado de acción/contacto permite pie plantado, patada, salto/cabezazo, parada y recuperación en el instante correcto. Correcciones visuales acotadas y medibles.
- Observar cada nuevo comportamiento en replay y visor: contacto, intento fallido, decisión arbitral, gesto y efecto deportivo. No inferir una falta sólo porque dos jugadores se acercan.
- Cámara broadcast contextual, audio/comentarios/público por eventos y niveles de confianza; provisionalidad de gol y VAR coherentes con marcador. No explicar como certeza una intención desconocida.
- Velocidad normal para validar credibilidad; acelerada para validar continuidad y eventos; highlights con contexto anterior/posterior suficiente para entender la jugada.
- Probe sintético sirve para layout/poses, **no** certifica motor. Exigir capturas y secuencias de partidos autoritativos en el cliente real, no sólo renders Blender.
- LOD y optimizaciones visuales independientes de precisión del partido. Cambiar calidad gráfica no cambia contactos, reglas o outcome.

### 8.1 Observabilidad causal y explicaciones del software

Requisito de producto, no herramienta reservada al desarrollador: poder responder «qué ocurrió», «qué lo provocó», «qué intentó el jugador/equipo», «qué observó el árbitro» y «qué efecto tuvo mi instrucción». Distinguir hechos, creencias e inferencias; si no existe evidencia suficiente, decirlo. No inventar motivos a partir del resultado ni presentar correlación como causa.

**Contrato mínimo de evidencia:** identidad y tiempo de ocurrencia, actor/participantes, acción/incidente, causas enlazadas, tipo de consecuencia y valores decisivos disponibles. Las relaciones pueden tener varias causas: una pérdida puede depender de presión, orientación corporal, error de ejecución y falta de apoyo. Conservar contexto en el momento de decidir/contactar, no consultar el estado posterior como si fuera el original.

Ejemplo de explicación verificable: «Intentó un pase al lateral; percibió libre el corredor. El rival entró durante la preparación y tocó el balón antes de la recepción». Los IDs enlazan decisión, preparación, contacto y pérdida. «La presión alta causó el gol» sólo se afirma si se puede mostrar la cadena relevante; el software puede señalar contribuciones sin prometer un contrafactual demostrado.

Tres niveles, con responsabilidades y retención diferentes:

1. **Evidencia deportiva mínima, siempre activa:** reutilizar eventos autoritativos y añadir códigos de razón/campos compactos al confirmar acciones o consecuencias. Conservar lo suficiente para explicar jugadas, reglas y decisiones elegidas. No registrar todas las alternativas ni el estado completo de los 22 jugadores cada tick.
2. **Observación agregada, para usuario y salud del motor:** proyecciones incrementales de fases, presión, líneas, apoyos, fatiga, participación y consecuencias de órdenes. Aprovechar cálculos existentes; publicar a cadencia acotada y por cambios significativos. Cada indicador tiene definición, procedencia y límite de interpretación.
3. **Diagnóstico profundo opt-in:** alternativas evaluadas, utilidades, creencias y candidatos de contacto sólo para jugadores/intervalos/incidentes seleccionados. Puede usar muestreo determinista y anillo de preincidente. No atribuir exhaustividad a datos muestreados; fuera del budget, reducir detalle o rechazar la captura, nunca alterar decisiones.

**Diseño de coste acotado:**

- En hot path: registros compactos tipados/códigos numéricos, buffers preasignados y reutilización de valores ya calculados. Sin strings explicativas, JSON, escritura a disco, llamadas de red, consultas a base de datos ni stack traces por evento normal.
- Fuera del tick: consumidores de snapshots inmutables/bloques publicados hacen agregación, búsqueda causal, localización, texto, overlays y persistencia. Un consumidor lento no mantiene un lock del mundo ni retiene arrays mutables del motor.
- Colas y retención con capacidad, política de overflow y contadores explícitos. La evidencia deportiva mínima no se pierde silenciosamente: dimensionar/publicar por bloques, archivar fuera del tick y, si no alcanza, notificar fallo de captura o aplicar backpressure fuera de la sección crítica según modo. El detalle diagnóstico sí puede descartarse con marcador de pérdida. Memoria finita no implica historial infinito gratis.
- No serializar un grafo completo en cada tick: enlazar IDs y construir índices de consulta en el consumidor. Agregar trazabilidad al producir el hecho cuesta menos y es más fiable que reconstruirla por heurísticas después.
- Instrumentación no consume RNG deportivo, no cambia iteration order, scheduling, comandos ni budgets de planificación. Usar contadores operacionales para decisiones de captura, no deadlines de CPU que afecten el partido.
- Explicaciones deterministas mediante plantillas y evidencia tipada, offline. No exigir un LLM o servicio remoto; cualquier redacción opcional futura queda fuera del motor y no puede agregar hechos.

**Presupuesto y gate:** coste cero no es una garantía técnicamente honesta. G0 mide modo mínimo sin consumidor, con agregación y con captura selectiva en el mismo corpus/hardware. Meta inicial para evidencia mínima: ≤ 2% de incremento en tiempo total de simulación y ≤ 5% de incremento en p99 del tick frente a una build de benchmark equivalente sin captura, siempre dentro del presupuesto absoluto de live. Medir también bytes/partido, pico de memoria, asignaciones y lag del consumidor; usar repeticiones suficientes para distinguir overhead de ruido. Si no se cumple, optimizar la representación/retención, no quitar explicabilidad silenciosamente.

**Pruebas de aceptación:**

- Observabilidad mínima/profunda activada o desactivada → mismo hash autoritativo, marcador y comandos efectivos.
- Cada gol, pérdida, falta, sanción, reinicio, revisión y cambio táctico aplicado tiene evidencia consultable; decisión elegida e intento fallido explicables sin capturar todas las opciones.
- Consultas causales no enlazan incidentes sólo por cercanía temporal; validación de IDs, causalidad temporal, provisionalidad y consecuencias anuladas.
- Pruebas con consumidor lento/ausente, cola llena, captura larga y seek: sin crecimiento no acotado, bloqueo oculto ni pérdida silenciosa. Mostrar intervalos no capturados en lugar de fabricar explicaciones.
- Cliente permite inspeccionar una jugada y sus causas, distinguir intención/ejecución/decisión arbitral y visualizar contexto táctico con definiciones claras. La explicación de una orden no garantiza que haya sido la única causa del resultado.

## 9. Rendimiento y operación offline

Medir primero. G0 fija hardware de referencia, build Release, datasets, número de ticks y modo de salida; publicar p50/p95/p99, memoria máxima, asignaciones/tick, GC, bytes de replay y coste por sistema. Separar arranque/carga, warm-up y ejecución.

Metas iniciales propuestas, **no capacidades actuales**:

- Core live: p99 del tick ≤ 25% del presupuesto de dt elegido, incluyendo escenarios densos; sin crecimiento de memoria por duración fuera del archivo de salida acotado.
- Live 1× estable con simulación fuera del hilo UI; a velocidad objetivo elegida en G0 sin underruns sostenidos. UI objetivo 60 FPS en hardware de referencia; medir CPU y GPU por separado.
- Batch: ≥ 100× tiempo de partido como meta inicial por worker en hardware fijado, sin replay completo; medir mejora respecto a baseline y ajustar explícitamente si incompatible con precisión necesaria.
- Regresión por gate: investigar >10% en throughput o p99 frente al último baseline aprobado; no aprobar silenciosamente por promedio más rápido.
- Replay/seek: elegir en G0 objetivos de almacenamiento y latencia para 90/120 min; restauración no debe crecer linealmente con el minuto si se declara seek interactivo acotado.

Técnicas preferidas: buffers reutilizables, arrays/SoA actuales, consultas de espacio compartidas por tick/revisión, broad phase sólo si profiling lo justifica (22 jugadores no necesitan un framework de física masivo), predicción de interceptación cacheada y budgets de búsqueda. Hot paths sin listas/closures/asignaciones evitables donde el perfil lo demuestre. Evitar logs por tick y JSON continuo.

Límites definidos para eventos, historial de percepción, incidentes, snapshots y colas; overflow explícito, nunca pérdida de regla/evento. Checkpoints incluyen todo estado necesario — RNG, acciones, memorias, reglas, reinicios, participación y comandos pendientes — o reconstrucción equivalente probada. La representación de snapshot no se vuelve segundo modelo editable.

Guardar/restaurar y seek dentro de la misma versión; formatos viejos se rechazan con mensaje claro. Dumps incluyen input/config/ruleset/entorno/semilla/comandos/build/hash y ventana causal suficiente. Preservar integridad de datos sigue siendo necesario aunque no haya usuarios.

## 10. Validación: realismo, leyes, diversión y mantenimiento

### Pirámide de evidencia

1. Contratos/unidades: finitud, rangos, identidades, ownership, configuración resuelta y rechazo de input inválido.
2. Microescenarios: balón/poste, intercepción alta/baja, control, cabezazo, tackles, GK, cada artículo legal y tabla de excepciones.
3. Invariantes/property tests: no contacto fuera de alcance; gol legal por cruce; no doble consecuencia; participación y posesión consistentes; estados terminales legítimos; simetría espacial/teams donde las condiciones sean simétricas.
4. Determinismo: mismo input con chunking distinto, fast/live, FPS/velocidades distintas, checkpoint/resume y comandos equivalentes → mismo hash autoritativo. Cambiar semilla puede cambiar ejecución pero nunca violar leyes.
5. Soak: miles de seeds y formatos, expulsiones/lesiones/prórroga/tanda, reinicios en bordes, scripts adversariales y varias temporadas offline. Minimizar y conservar seeds fallidas, sin parches por seed.
6. Estadística: corpus estratificado por nivel, táctica, fatiga, marcador/tiempo y competición; holdout separado de calibración. Goles/xG/tiros, posesión, pase, faltas, tarjetas, offside, paradas, centros, carreras, distancia, sprints, tiempo efectivo e interrupciones.
7. Experiencia: secuencias completas a 1× y aceleradas, capturas del cliente y evaluación de decisiones tácticas por revisores; contar defectos visibles repetidos y si puede explicarse la causa.

Datos reales con licencia/procedencia, definiciones homogéneas y rangos por población. No inventar una distribución universal ni ajustar a un único promedio. Contrastar también geometría/ritmos: mapas de ocupación, separación entre líneas, secuencias de pases, timings y segundas pelotas.

Tácticas: experimentos emparejados de seeds con una intervención por vez; medir efectos esperados y costes (presión vs fatiga/espacio concedido, línea alta vs profundidad, amplitud vs protección central). No exigir que una táctica gane cada partido. Evaluar sensibilidad de atributos con perfiles controlados, sin garantizar que el mejor siempre gane.

La optimización/calibración cambia parámetros, no leyes ni tests para acomodar el motor. Cada parámetro explica fenómeno, unidad/rango, dueño y evidencia; eliminar knobs redundantes/no identificables. Modelo ML opcional en herramientas offline, sólo si supera baseline interpretable en holdout y exporta artefacto determinista. Ningún servicio externo en ejecución.

## 11. Roadmap por gates y cortes verticales

Dependencia principal: **G0 mínimo → O1 (observabilidad habilitante) → G1 → G2 → G3 → G4 → G5 → G6 → G7 → G8**. **O1 es la primera prioridad de implementación y bloquea los cambios deportivos posteriores.** La auditoría normativa y el inventario completo de G0 pueden continuar mientras se construye O1; no retrasar la capacidad de observar esperando terminar toda la documentación. Rendimiento se mide en todos los gates; G7 consolida y calibra, no descubre por primera vez que el motor es lento.

### G0 — Baseline, contratos reales y cobertura normativa

Entregables:
- Inventario de escritores de estado/eventos y dependencias; mapa de redundancias con evidencia, no borrado indiscriminado.
- Build/suites actuales, lista exacta de tests, corpus de seeds y partidos reales capturados; métricas físicas/tácticas/temporales y baseline Release.
- Edición IFAB fijada y matriz artículo a artículo, rulesets de competiciones objetivo y protocolos opcionales.
- ADRs concisos: coordenadas/lados, dt, alcance de determinismo, orden de fases, RNG, ownership, budgets y provisionalidad.
- Guía del repositorio reconciliada con Core/Client actual y planes de reinicios/viewer; registrar deuda y qué trabajo ajeno está activo.

Salida: baseline reproducible, métricas/objetivos con hardware y cobertura pendientes explícitas. Ningún número de rendimiento sin medir. No rediseñar física antes de acordar autoridad y contratos.

### O1 — Observabilidad primero: desbloquear la evolución

Entrada mínima: build y suites de referencia, corpus pequeño reproducible, medición Release, mapa de productores de eventos y contrato de evidencia de §8.1. No requiere haber completado la revisión de las 17 reglas ni rediseñado el motor.

Cortes en orden:
1. Identidades/causas/códigos de razón capturados en los puntos reales de decisión y consecuencia; conservar RNG, orden y semántica deportiva actuales. Los refactors mínimos indispensables para capturar evidencia pertenecen a O1, no una reescritura anticipada de G1–G5.
2. Buffers acotados, publicación inmutable y consumidor desacoplado; consultas por partido, tick, jugador, acción e incidente. Política de retención, overflow y evidencia incompleta visible.
3. Inspector causal mínimo **utilizable en el cliente ahora**, con timeline y explicación de una jugada real. Mostrar decisión elegida, ejecución, resolución actual y consecuencia. Si hoy un resultado depende de una tirada probabilística, exponerlo honestamente, no describir un contacto físico inexistente.
4. Captura selectiva y dump reproducible para investigar problemas; medición del overhead, equivalencia on/off y pruebas de consumidor lento/ausente.

Salida bloqueante:
- Se puede seguir una cadena completa de pase/pérdida y remate/resultado, una falta/reinicio y una orden táctica aplicada, con IDs y razones verificables. Registrar expresamente qué causas todavía no produce el motor.
- El software explica evidencia actual sin inventar intención, causalidad o legalidad. No hace falta una UI final pulida, sí una herramienta funcional para inspeccionar partidos reales.
- Captura mínima dentro del budget de §8.1 y live; memoria acotada, sin I/O síncrono en tick ni pérdida silenciosa. Hash autoritativo idéntico con consumidores/diagnósticos on/off.
- Baseline de defectos observados y ejemplos de consultas guardados para contrastar cada cambio futuro.

**No iniciar G2–G5 ni calibrar comportamientos a ciegas.** Cada corte posterior extiende la evidencia existente y usa el inspector para demostrar qué cambió, por qué y a qué coste. O1 proporciona infraestructura durable, no un sistema temporal de logs que después se descarta.

### G1 — Fundación sin estado sobrecargado

Cambios: contratos tipados de acciones/incidentes/reglas/control; separación de cache/creencia/verdad; secuencias y causalidad integradas con la observabilidad ya operativa de O1; fachada `Result` única; retirar parámetros ignorados y excepciones como flujo normal. Reorganizar `MatchBoundary` por responsabilidades reales, no por tamaño arbitrario. Update de callers, serialización, tests y fixtures en el mismo corte.

Salida: decision/VAR no usan fases mágicas en RunLookup; owner único por estado; fast/live/chunking equivalentes; no caminos legacy ni parámetro nuevo sin consumidor. Eventos semánticos extensibles sin depender de una tabla de flags frágil de tags implícitos; elegir máscara tipada o mapa estable según necesidad real.

### G2 — Geometría, balón y contacto verdaderamente autoritativos

Cortes: (a) líneas/radio/arco y colisión continua, (b) alcanzabilidad/orden de contacto, (c) control/impulso/desvío, (d) gol y outcomes derivados. Mantener presentación mínima y arbitraje existentes conectados mientras se sustituyen sus consumidores.

Salida: una pelota físicamente dentro no se convierte en parada por una tirada posterior; todos los toques tienen evidencia; no contactos XY imposibles por altura; pruebas de poste/desvío/interceptación/ball-out-before-contact y atribución. Eliminar resolución probabilística duplicada y destino autoritativo tras demostrar equivalencia de obligaciones, no equivalencia del antiguo marcador.

### G3 — Cuerpo, acciones y arquero

Cortes: (a) aceleración/giro/soporte, (b) preparación-contacto-recuperación y conducción/recepción, (c) aerial/tackle/shield, (d) GK completo y salidas/distribución. Cada corte añade payload observable y escenarios reales.

Salida: alcance y temporización consistentes; fallo físico representable; ausencia de patadas/paradas fantasma, dobles contactos o acciones incompatibles. Revisar ciclo entero a 1×, no sólo poses de éxito. Rendimiento dentro del presupuesto fijado.

### G4 — Reglamento y arbitraje completo

Cortes: ruleset/periodos/participación → reinicios directos/indirectos/dropped ball → offside → faltas/mano/ventaja/disciplina → penales/tandas → VAR/protocolos/administración. Aprovechar RestartController actual donde su contrato lo permita; no crear segundo controller.

Salida: matriz 17 reglas y protocolos objetivo validada, con todas las excepciones relevantes y fuente registrada; ninguna fila pendiente para declarar cobertura completa. Árbitro, marcador, stats y replay coinciden después de VAR; mínimo de jugadores, abandono, final de periodo y prolongación por penal correctos. No llamar a este gate completo sólo porque funcionan córners/laterales.

### G5 — Inteligencia colectiva y efecto del entrenador

Cortes: percepción/creencias → opciones factibles y compromiso → roles/fases/coordinación → presión/cobertura/transiciones → adaptación/manager/fatiga/lesión. Eliminar fórmulas duplicadas de llegada y scoring de acciones, mantener aproximación percibida explícita.

Salida: batería de intervenciones tácticas muestra efectos y tradeoffs previstos con incertidumbre; identidades de equipo observables, sin todos persiguiendo balón ni cambios instantáneos de formación. Fatiga modifica más que velocidad. Atributos y coordinación tienen efectos causales diferenciados. Ningún planificador rompe budget o conoce futuro autoritativo.

### G6 — Partido observable, live/replay y carrera reconciliados

Cortes: timeline de contactos/acciones → interpolación/discontinuidades → pausa/speed-up/highlights/seek → comandos y commit de resultado a World. No reabrir trabajo de assets como prerrequisito del motor.

Salida: mismos outcomes con renderer ausente y presente, 1×/4×/8× y chunking distintos; no perder eventos por cola; seek coherente; acción/contacto alineados visualmente; inspector causal de O1 ampliado a replay/seek/highlights y presentación final; resultado aplicado una sola vez a carrera y carga/participación correctas. Capturas reales y grabaciones de secuencias, no sólo probe sintético.

### G7 — Calibración y rendimiento de producción

Cortes: profiler/allocations → caches/lookup/planificador → corpus y ajuste multiobjetivo → holdout/sensitivity → soak y formatos. No convertir fast en una simulación deportiva simplificada para alcanzar throughput.

Salida: métricas de rendimiento aprobadas con hardware/dataset; distributions y variabilidad plausibles por población; sin exploits tácticos dominantes ni parámetros redundantes; miles de partidos sin estados ilegales, safety timeout o crecimiento de memoria inesperado. Errores hallados vuelven a su gate propietario, no se disimulan calibrando.

### G8 — Cierre, mantenibilidad y horizonte de años

Entregables: contratos y dueños actualizados, cero código sustituido/dead config, fixtures y herramientas reproducibles, tests que protegen comportamiento deportivo, baselines, matriz legal completa, catálogo de eventos/presentación y manual de añadir una acción/regla/táctica.

Salida: un cambio futuro tiene exactamente un hogar para su autoridad; docs y compilador concuerdan; clean build, suites Core/Client y soak aprobados. Establecer revisión por cambio de temporada IFAB y por versión de motor, no mantenimiento de formatos viejos. Backlog explícito de mejoras opcionales sin esconder requisitos del objetivo.

## 12. Definition of Done de cada corte

- Escenario deportivo, dueño, contrato, causa y evento definidos antes de implementar.
- Nueva ruta autoritativa única; vieja ruta/campos/config/callers eliminados. No dejar compatibilidad por comodidad.
- Build del menor proyecto afectado después de cambiar contratos y antes del siguiente gate; tests específicos y `git diff --check`.
- Fixture/microescenario positivo, negativo y borde; determinismo y rendimiento comparados con baseline.
- Evidencia y explicación del nuevo comportamiento verificables; overhead de captura medido y acotado, hash idéntico con diagnósticos on/off y políticas de retención/overflow probadas.
- Verificación real en cliente si cambia observación; imagen/secuencia inspeccionada, no simplemente generada.
- Si cambia semántica estadística, actualizar consumidor World y definición de métrica; si cambia regla, actualizar matriz IFAB.
- Evidencia en este plan o en artefacto enlazado: build/versión, comando, input/seed y resultado. Un checkbox sin evidencia no cierra gate.
- Preservar trabajo compartido; no revertir ni borrar archivos no investigados. No commits/push como consecuencia implícita del plan.

Comandos de referencia (verificar opciones reales antes de filtrar):

```bash
dotnet build FootballEngine.Core/FootballEngine.Core.fsproj -c Release
dotnet run --project FootballEngine.Core.Tests/FootballEngine.Core.Tests.fsproj -- --list-tests
dotnet run --project FootballEngine.Core.Tests/FootballEngine.Core.Tests.fsproj
dotnet run --project FootballEngine.Client.Tests/FootballEngine.Client.Tests.fsproj
dotnet build Tools/MatchViewer3D/StadiumRendererProbe.fsproj
git diff --check
```

Expecto: usar el prefijo completo listado por `--list-tests`, no `dotnet test --filter`. El probe es una herramienta complementaria; añadir harness de partidos reales y benchmark headless dentro de Tools/Benchmarks existentes si es necesario, sin crear otra aplicación de producto.

## 13. Riesgos y decisiones para evitar callejones

| Riesgo | Prevención / criterio |
|---|---|
| Reescritura interminable | Cortes verticales con gate; reutilizar física/runner/SoA útiles, eliminar únicamente reemplazos comprobados. |
| 3D caro pero fútbol pobre | Proxies deportivos, contacto y alcance primero; malla visual no controla simulación. |
| Legalidad incompleta tras miles de tests | Matriz por artículo/excepción y protocolos con escenarios dirigidos, no sólo seeds aleatorias. |
| Estadística correcta pero escenas falsas | Microfísica + revisión a velocidad normal + métricas de movimiento/secuencias. |
| Fútbol correcto pero aburrido | Agencia del manager, incertidumbre razonable, riesgo y estilos; presentación clara, sin fabricar remontadas. |
| IA omnisciente o errática | Creencias, atención, latencia y compromiso; errores en percepción/ejecución, no normas aleatorias. |
| Fórmulas redundantes | Catálogo de modelos puros compartidos y caches revisionadas; eliminar copia en contactos/planner/decisión. |
| Dependencia accidental del FPS | Fixed step y clocks separados; pruebas speed-up/chunking/renderer-off obligatorias. |
| VAR destruye stats/flujo | Provisionalidad explícita e incidente causal; resolución confirmada aplicada una sola vez. |
| Rendimiento degrada por capas | Budget desde G0, perfiles por gate; no segundo motor rápido ni deadlines de CPU en decisiones. |
| Formatos/contratos viejos frenan diseño | Reemplazo directo, rechazo de versión incompatible; sin migraciones ni compatibilidad. |
| World y partido discrepan | Input inmutable, participación explícita y commit idempotente; prueba de temporada/lesión/sustitución. |

## 14. Próximo paso recomendado

Empezar por **el baseline mínimo de G0 y O1: observabilidad causal operativa antes de evolucionar el fútbol**. Primero poder inspeccionar y explicar el motor actual con coste acotado; después usar esa evidencia para retirar marcadores mágicos en G1 y sustituir la resolución tardía del remate en G2. La observabilidad desbloquea diagnóstico, refactor, reglas, inteligencia, calibración y rendimiento: no es un entregable postergado hasta G6.

El objetivo final no es «tener más sistemas»: es que **cada consecuencia tenga una causa, cada estado un dueño, cada regla una prueba y cada decisión del entrenador un efecto observable**, con suficiente margen de rendimiento para evolucionar durante años.
