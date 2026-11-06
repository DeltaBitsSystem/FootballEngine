# Integración y ownership de contacto

## Exclusividad de implementación

Una sesión Luna Max implementa esta unidad en el checkout compartido. El agente principal no edita archivos de implementación mientras esa sesión trabaja. Antes de empezar registrar `git status --short` y guardar diff/lista de no seguidos fuera del repo o en evidencia propia con nombre no colisionante. Preservar contenido previo; no usar checkout/reset/stash para esconderlo. No hacer checkpoint commit sin autorización. Si otro proceso cambia los mismos archivos, detener esa edición, registrar diferencias y coordinar.

## Mapa de cambios obligatorio

### Core contratos

`FootballEngine.Core/Match/Contracts/`: añadir contratos físicos y acciones de contacto; integrar TeamFrame/World, BallState/BallImpulse, BalanceConfig y validación, MatchEvent/EventKind/flags, Observation, MotionEvidence y fronteras de sesión. Reutilizar identidades y PhysicalMorphology. Inspeccionar tipos reales antes de editar: varios se agrupan en `MatchSimulationTypes.fs`. Actualizar constructores/clones/builders y orden de compilación en `FootballEngine.Core.fsproj`.

Cada número de comportamiento pertenece a secciones Contact/Balance/ContactAction de BalanceConfig (o agrupación coherente existente): dimensiones, masa fallback, fricción/restitución por superficie, resistencia de apoyo, aceleración/esfuerzo, estabilización, límites solver, error de trayectoria, duraciones, estabilidad y recuperación, umbrales arbitrales. Validar finitud, signos, rangos, relaciones geométricas y capacidad. Quitar configuraciones probabilísticas obsoletas y actualizar codecs, muestras, herramientas y entrenamiento que las lean. No conservar campos ignorados para que compile.

### Core sistemas y scheduler

`Systems/Players/Steering.fs`: objetivos físicos desde intención, sin integración posicional/repulsión secuencial. `BodyPhysics.fs`: reutilizar salto en integración única; evento de aterrizaje una vez. `Decision.fs`, `Intent.fs`, `Actions.fs`, `Actions/Duel.fs`: reemplazar duelo binario por propuestas temporales sin inventar nueva arquitectura de agentes. Reconectar Planner/branch simulation para que evaluaciones no dependan de una física antigua ni compartan arrays mutables.

`Systems/Ball/Physics.fs`, `FootContact.fs`, `Contact.fs`, `Possession.fs`, `Outcome.fs` y BallDynamics: integrar ruta de contacto común, geometría, TOI, toque/control y cruces. Mantener física de vuelo/spin/suelo existente donde sea válida. Auditar GK, disparos, rebotes, ArrivalGate y PendingStrike; ningún gate ni etiqueta Struck/Aimed protege de obstáculos previos. Reemplazar comentarios que prometen un comportamiento antiguo.

Crear módulos puros de geometría/solver y controller ContactDynamics bajo `Match/Systems/` con un hogar inequívoco, sin dependencias hacia Client. `Stepper/SystemsTable.fs` sigue siendo tabla única. `Stepper/EventApply.fs`: reducer único de consecuencias, nueva disciplina/incidentes y deduplicación sin repetir impulso. Revisar triggers, flags, rutas de interrupción y causalidad a través del tick de drenaje. Actualizar foundation con el nuevo orden implementado y explicado.

`Systems/Referee/Fouls.fs`, Cards y VAR: reemplazar evidencia insuficiente en esta unidad, conservar fronteras administrativas. El árbitro no llama a agentes ni reejecuta geometría para inventar otro resultado. Actualizar estadísticas/scouting/comentarios para distinguir tackle intentado, pelota tocada, robo controlado y falta; sin duplicar métricas por episodio persistente.

### Sesión, persistencia y proyecciones

`Simulator/MatchSession.fs`, MatchDump, MatchAdapter, checkpoint/clones/proyecciones y contratos de boundary: incluir nuevos estados y evidencia, fast/live comparten física; las políticas de publicación no cambian resultados. Revisar codecs Core y Client, archives de movimiento, hashes/identidad del runtime y saves que almacenen versiones de partido. Hacer inventario de todos los consumidores con búsqueda de símbolos, no solo de los archivos enumerados aquí.

### Client

`FootballEngine.Client/Animation/`: Contracts, MotionRuntime, TaskPlanner, SupportPlanner, BodyPose, PoseEvaluation, ConstraintSolve, MotionValidation, RigDefinition si hace falta, MotionIdentity y codecs de checkpoint. Consumir nuevos tipos sin conocer RNG ni modificar Core. Reusar apoyo/IK/reacción procedural existentes, no reescribir locomoción ajena. Actualizar Match3D/View projections que construyan raíces/evidencia, capturas deterministas y tests. No editar binarios, texturas ni fuentes Blender.

### Tests y Tools

`FootballEngine.Core.Tests/`: fixtures/builders, contactos nuevos y actualización semántica de DuelContact/Observation/MatchSession; no borrar tests incómodos ni mantener nombres que afirmen física que ya no prueban. `FootballEngine.Client.Tests/`: MotionRuntime/Timeline, CharacterAnimation y codecs/replay para nuevas reacciones. `FootballEngine.Benchmarks/` y `Tools/MatchDiagnostics`/`Tools/CollectiveIntelligence`: probes de corpus y coste físico con salida fuera del tick. Modificar proyectos compartidos en un único flujo exclusivo.

## Ciclo de vida que debe cerrar la implementación

- Nueva acción, cancelación antes/después de compromiso y recuperación.
- Balón perdido durante windup y dos contactos en mismo subtick.
- Contacto persistente y varios rivales simultáneos.
- Caída, aterrizaje, levantada y balón aún accesible.
- Reinicio autorizado, pausas, descanso, fin de partido y ventaja pendiente.
- Lesión, expulsión, sustitución y cambio de generación.
- Clone del planner, checkpoint a mitad de contacto y replay desde frontera.
- Consumidor lento/ausente, journal desbordado, render a distintas tasas.
- Datos/config inválidos y archivo antiguo rechazado sin pérdida de datos.

## Cambios fuera del listado

Están autorizadas modificaciones mínimas a consumidores necesarios descubiertos por compilador/búsqueda, no un rediseño adicional. Registrar el inventario final en evidencia. Resolver decisiones locales sin pedir aprobación por cada fichero; elevar solo contradicciones reales del contrato con evidencia y propuesta concreta.
