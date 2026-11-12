# Football Engine — Match Engine & Match Viewer
### Documento Arquitectural

> **Para quién es esto:** para vos en el futuro, para colaboradores humanos, y para cualquier IA que trabaje en este codebase. Todos leen esto antes de tocar algo.
>
> **Qué es:** el norte del proyecto. No describe el código hoy. Describe lo que el código tiene que llegar a ser y por qué. Cuando el código y este documento se contradicen, este documento gana.
>
> **Qué no es:** un roadmap. Las tareas concretas viven en otro lado. Este documento describe principios que no tienen fecha de vencimiento.

---

## La meta

Construir algo que no existe todavía.

No un Football Manager mejor. No una simulación más fiel a la realidad. Algo distinto: un sistema donde la **inteligencia colectiva emerge**. Donde el equipo piensa como unidad. Donde el manager moldea esa unidad con cada decisión. Donde el drama, el momentum, y los resultados son consecuencias — no inputs.

Un partido donde el resultado de 2–1 tiene causas reales y encadenadas. El lateral cometió el error porque estaba agotado. Estaba agotado porque el técnico no lo sustituyó. El técnico no lo sustituyó porque el sistema de momentum indicaba dominio. Nadie programó esa cadena. El sistema la produjo.

Eso es imposible de terminar. Siempre hay más profundidad posible. Esa es la razón para abrir este archivo todos los días.

La meta tiene dos dimensiones en tensión permanente:

- **Realismo:** los resultados tienen causas. La táctica importa. La fatiga importa. Las decisiones del manager importan.
- **Diversión:** el usuario quiere ver el siguiente partido. Hay drama. Hay sorpresas que tienen sentido.

No hay una fórmula para ese balance. Hay criterio, y este documento es donde vive ese criterio.

---

## El horizonte — lo que siempre puede ser más profundo

Estos son los territorios que nunca se terminan. Son la razón por la que el sistema nunca llega a un estado "listo".

### Inteligencia colectiva e individual — el ciclo

La inteligencia individual y la colectiva no son territorios separados. Son el mismo sistema en dos escalas. Un jugador con Vision 18 ve un pase que uno con Vision 10 no ve — eso es inteligencia individual. Pero ese pase solo existe porque el `TeamOrchestrator` posicionó al receptor en ese espacio — eso es inteligencia colectiva. Las dos se necesitan.

La profundidad es infinita en el ciclo entre las dos: cómo una instrucción táctica se propaga y se interpreta distinto por cada jugador según sus atributos, cómo las decisiones individuales acumuladas modifican el estado que el equipo lee para coordinarse, cómo la química afecta cuánto confía un jugador en la intención de su compañero sin verlo.

### El canal Manager → Equipo → Jugador

El cambio táctico del manager no es un comando instantáneo. Es una señal que se propaga. El manager decide, `TeamOrchestrator` actualiza la directiva, y cada jugador la adopta a su ritmo según `TeamWork`. Este canal tiene tres propiedades que se pueden profundizar indefinidamente: la velocidad de propagación, la fidelidad con que cada jugador interpreta la directiva, y cómo el manager aprende a anticipar ese lag para dar instrucciones antes de que las necesite.

### El manager como agente real

El manager es el sistema que observa el partido desde afuera y lo modifica. No es una interfaz de usuario. Es un agente con su propio modelo del partido, su propia lectura del momentum, sus propios errores y aciertos.

Un manager agresivo que no puede leer cuándo su equipo está agotado. Un manager conservador que no reacciona a tiempo cuando el partido se le va. Un manager que entiende los datos pero no los momentos. Estas son dimensiones de inteligencia que se pueden modelar indefinidamente.

La pregunta que guía el desarrollo de este agente: ¿puede el manager perder un partido por sus propias decisiones, independientemente de los jugadores? Si la respuesta es no, el agente no es suficientemente inteligente todavía.

### Momentum y estados del partido

Un partido de fútbol no es una secuencia de eventos independientes. Tiene estados. El equipo que acaba de recibir un gol está en un estado diferente al que acaba de anotar.

Modelar el momentum no es agregar un número llamado "momentum". Es que el estado del partido emerja de la acumulación de eventos reales — posesión, xG, presión territorial, fatiga diferencial — y que ese estado afecte el comportamiento de todos los agentes, especialmente el manager.

---

## Los dos sistemas

El Match Engine y el Match Viewer son inseparables. Lo que pasa adentro del engine tiene que ser visible afuera en el viewer. Un engine que simula bien pero no se puede observar no cumple la meta.

### Match Engine

Simula un partido completo. Corre en dos modos:

| Modo | Qué produce | Cuándo |
|---|---|---|
| `runFast` | Estado final + eventos clave (goles, tarjetas, lesiones) | Liga, simulación masiva de fondo |
| `runFull` | Snapshots periódicos + todos los eventos | El partido que ve el usuario |

**La propiedad más importante:** los agentes no saben en qué modo están corriendo. Un agente que se comporta distinto en `runFast` que en `runFull` es un bug de arquitectura, no una optimización.

```
Simulator.runFast  →  loop sin snapshots  →  MatchStepper.updateOne
Simulator.runFull  →  loop con snapshots  →  MatchStepper.updateOne
                                               ↑
                                misma función, mismo contrato
```

### Match Viewer

Consume el output de `runFull` y lo hace visible con Skia en 2D — círculos para jugadores, pelota con física real. Tiene dos modos:

- **Tiempo real:** el partido corre y el viewer lo muestra a medida que ocurre.
- **Replay:** el usuario puede rebobinar, avanzar, pausar. Los snapshots periódicos lo hacen posible.

**El contrato entre engine y viewer es el evento.** El viewer consume `MatchEvent list`. No sabe cómo funciona el engine. El engine no sabe cómo funciona el viewer. Se hablan por eventos.

```
Engine  →  MatchEvent list  →  Viewer

Agregar un evento nuevo al engine = agregar algo visible al viewer.
Mejorar el viewer = nunca tocar el engine.
```

Las métricas derivadas (xG, heatmaps, PPDA, estadísticas) se calculan en post-proceso sobre los eventos, nunca dentro de la simulación. Esto mantiene `runFast` sin overhead y el engine sin responsabilidades del viewer.

---

## Los principios — lo que no se negocia

Estas son las reglas que gobiernan cualquier decisión de diseño. Si una decisión viola uno de estos principios, la decisión está mal, no el principio.

### 1. La inteligencia individual y la colectiva se alimentan mutuamente

No son opuestas ni compiten. Son el mismo sistema visto en dos escalas distintas.

`TeamOrchestrator` establece el marco — la directiva, la forma, los roles. `PlayerAgent` decide dentro de ese marco. Pero las decisiones individuales acumuladas son lo que `TeamOrchestrator` lee para ajustar la directiva en el siguiente ciclo.

```
TeamOrchestrator  →  TeamDirective  →  PlayerAgent
        ↑                                    |
        └──────────── SimState ←─────────────┘
```

El ciclo es el diseño. El manager observa este ciclo desde afuera y lo moldea.

### 2. Agregar inteligencia nueva tiene que tener un lugar obvio

Si alguien quiere que el equipo presione mejor en transición, tiene que haber exactamente un lugar en el código donde eso vive. Si no es obvio, el diseño está mal.

La consecuencia: se prioriza claridad de capas sobre optimización prematura. Siempre.

### 3. Los agentes no se llaman entre sí

La comunicación entre agentes pasa por `SimState`. Un agente lee del estado, hace su trabajo, escribe en el estado o devuelve eventos. Nunca llama a otro agente directamente.

### 4. Ningún número de comportamiento vive fuera de BalanceConfig

Si hay un número que afecta cómo se comporta el juego, vive en `BalanceConfig` con un nombre que explica qué hace. Sin excepción.

### 5. El compilador hace cumplir el diseño

Cambiar una firma y dejar que el compilador muestre todos los sitios que rompen es la forma correcta de evolucionar este sistema. No hay backward compatibility artificial — si la arquitectura mejora, se migra.

### 6. La fatiga que solo afecta la velocidad no es fatiga real

Un jugador agotado en el minuto 85 percibe peor, decide con menos calidad, y tiene intenciones más conservadoras — además de correr más lento. La `Condition` tiene que propagarse por todo el pipeline.

### 7. Si algo no emite un evento, no existe para el viewer

Antes de implementar cualquier comportamiento nuevo en el engine, la pregunta es: ¿qué evento emite esto? Si la respuesta es "ninguno", el comportamiento es invisible.

### 8. Los agentes viven en el tiempo de los eventos, no en el tiempo del reloj

El tiempo físico existe para la pelota y para el viewer. Los agentes no se despiertan porque pasaron N ticks — se despiertan porque algo relevante para ellos ocurrió. Esta separación es lo que hace que la causalidad sea legible: cada decisión tiene un evento que la disparó.

---

## El modelo de tiempo — dos capas que no se mezclan

### Capa física — continua

La pelota tiene física real: aerodinámica, spin, efecto Magnus, rebotes en postes. Corre cada tick. Es lo que el viewer necesita para que el movimiento se vea fluido. Esta capa no tiene opinión sobre fútbol — solo sobre física.

```fsharp
BallPhysics.update : PhysicsConfig -> dt -> BallPhysicsState -> BallPhysicsState
```

### Capa semántica — por eventos

Los agentes viven acá. No ven ticks — ven `SemanticEvent`.

```fsharp
type SemanticEvent =
    | BallSecured             of side: ClubSide * playerId: PlayerId
    | BallLost                of side: ClubSide * playerId: PlayerId
    | BallLoose
    | PassLaunched            of from: PlayerId * target: PlayerId
    | ShotLaunched            of shooter: PlayerId
    | FoulOccurred            of fouler: PlayerId * fouled: PlayerId
    | GoalScored              of side: ClubSide * scorer: PlayerId option
    | SetPieceAwarded         of kind: SetPieceKind * team: ClubSide
    | PlayerConditionCritical of playerId: PlayerId * condition: int
    | RedCardIssued           of playerId: PlayerId
    | MomentumShifted         of toSide: ClubSide
```

### EventRouter — quién corre y cuándo

```fsharp
module EventRouter =
    let route (events: SemanticEvent list) (subTick: int) (clock: SimulationClock) : AgentActivation list
```

Transforma eventos semánticos en activaciones de agentes. No hay frecuencias arbitrarias en ningún otro lugar del sistema. La pregunta no es "¿toca correr el árbitro este tick?" sino "¿ocurrió algo que el árbitro necesita juzgar?".

```fsharp
type AgentActivation =
    | ActivatePhysics
    | ActivateCognition
    | ActivateAction    of triggeredBy: SemanticEvent
    | ActivateReferee   of triggeredBy: SemanticEvent
    | ActivateTeam      of triggeredBy: SemanticEvent
    | ActivateManager   of triggeredBy: SemanticEvent
```

### SimulationClock

```fsharp
type SimulationClock =
    { SubTicksPerSecond: int }
```

El reloj no tiene frecuencias de agentes. Su única responsabilidad es convertir entre subticks y segundos reales.

### TickTrace — debuggeo por tick

```fsharp
type TickTrace =
    { SubTick        : int
      SemanticEvents : SemanticEvent list
      Activations    : AgentActivation list
      MatchEvents    : MatchEvent list }
```

En modo debug, cada tick produce un `TickTrace`. En `runFast` no existe — cero overhead. Podés preguntarle al engine "¿qué pasó en el minuto 67?" y la respuesta es una lista legible de eventos y qué se activó como consecuencia.

---

## Las cuatro capas del engine

```
┌─────────────────────────────────────────────────────┐
│  A — Contrato fundacional                           │
│  SimState · MatchContext · BalanceConfig            │
│  SemanticEvent · MatchFlow · SimulationClock        │
├─────────────────────────────────────────────────────┤
│  B — Simulador                                      │
│  runFast · runFull                                  │
├─────────────────────────────────────────────────────┤
│  C — Stepper                                        │
│  MatchStepper.updateOne · EventRouter               │
├─────────────────────────────────────────────────────┤
│  D — Agentes                                        │
│  BallAgent · RefereeAgent · TeamOrchestrator        │
│  PlayerAgent · ManagerAgent                         │
└─────────────────────────────────────────────────────┘
```

Las capas solo se comunican hacia abajo. B conoce C, C conoce D. Ninguna capa conoce a la que está por encima.

---

## CAPA A — Contrato fundacional

### SimState

El estado mutable del partido en un instante. Es la única cosa que los agentes pueden leer y escribir.

```fsharp
type SimState() =
    member val SubTick               : int
    member val Ball                  : BallPhysicsState
    member val Home                  : TeamSimState
    member val Away                  : TeamSimState
    member val Flow                  : MatchFlow
    member val Momentum              : float
    member val PendingSemanticEvents : SemanticEvent list   // se drenan cada tick
    member val MatchEvents           : ResizeArray<MatchEvent>
```

### MatchFlow

```fsharp
type MatchFlow =
    | Live
    | SetPieceSetup  of SetPieceSetupState   // jugadores se posicionan, física corre, pelota quieta
    | GoalPause      of GoalPauseState
    | VARReview      of VARFlowState
    | InjuryPause    of InjuryPauseState
    | RestartDelay   of RestartPlan           // kickoff y casos sin posicionamiento visible
    | HalfTimePause  of remainingTicks: int
    | FullTimeReview
    | MatchEnded
```

`SetPieceSetup` es el estado donde los jugadores se mueven a sus posiciones antes del set piece. Durante este estado la física sigue corriendo — el viewer muestra el movimiento. La pelota no se mueve.

---

## CAPA C — Stepper

**Orden fijo dentro de cada tick:**

```
1. Física         — BallPhysics.update, MovementEngine
2. BallAgent      — posesión, bounds, trayectorias, emite SemanticEvents
3. Drenar eventos — SimStateOps.drainSemanticEvents
4. EventRouter    — qué agentes corren este tick
5. Agentes        — en el orden que el router determina
6. MatchClock     — halftime, fulltime, stoppage
```

---

## CAPA D — Agentes

### BallAgent

**Responsabilidad:** física del balón y emisión de eventos semánticos.

1. Physics, Spin, Magnus effect
2. Interception
3. Bounds
4. Control — transiciones `Free / Controlled / Receiving / Airborne / Contesting`
5. SemanticEmission — `BallSecured`, `BallLost`, `BallLoose`, `PassLaunched`, `ShotLaunched`

---

### RefereeAgent

**Responsabilidad:** aplicar las reglas del fútbol.

1. FoulAnalysis, Handball
2. Cards — emite `RedCardIssued`
3. Goals — emite `GoalScored`
4. VAR

`RefereeApplicator` es privado dentro de `RefereeAgent`. Ningún otro módulo aplica efectos del árbitro directamente.

---

### TeamOrchestrator

**Responsabilidad:** inteligencia colectiva del equipo.

Pipeline de cinco pasos:

1. **Read** — cohesión, tasas de evento, condición promedio. Puro.
2. **DirectiveResolver** — qué `DirectiveKind` corresponde ahora
3. **Plan** — SlotRoles, posiciones de soporte, runs, shape defensivo
4. **EmergentLoops** — ajusta estado emergente según patrones observados
5. **Write** — única función que muta. Escribe en `TeamFrame` y `TeamDirective`.

**Territorio de crecimiento:**

La situación táctica se calcula pero no llega diferenciada a cada jugador. Las posiciones de soporte no tienen conciencia de zonas presionadas ni triángulos de pase. `EmergentLoops` aprende a nivel equipo pero no por zona del campo. Estos son los tres frentes de crecimiento de inteligencia colectiva.

**Propagación de directiva:**

```
ManagerAgent emite cambio táctico
        ↓
TeamOrchestrator escribe nueva TeamDirective en SimState
        ↓
Cada PlayerAgent la adopta según su DirectiveAdoptionDelay
proporcional a (1 - TeamWork)
        ↓
El cambio se ve gradualmente — no en el siguiente tick
```

---

### ManagerAgent

**Responsabilidad:** observar el partido y tomar decisiones de nivel técnico.

Se activa por eventos semánticos críticos (`RedCardIssued`, `MomentumShifted`, `PlayerConditionCritical`) y por ventanas de tiempo en `BalanceConfig`. No escanea el estado completo en cada tick.

1. TacticsRead, SubstitutionIntelligence, PressureRead, MomentumRead

---

### PlayerAgent

```fsharp
PlayerAgent.tick : AgentContext -> PlayerResult
```

`AgentContext` es una vista filtrada — no recibe `SimState` completo.

**Las 5 capas:**

| Si querés que el jugador... | Va en... |
|---|---|
| Vea más o menos opciones | Perception |
| Se mueva a distintos espacios | Intent |
| Tome mejores decisiones con el balón | Decision |
| Ejecute mejor tiros, pases, regates | Action |
| Se mueva de forma más realista | Steering |

**Capa 5 — Steering:** durante `SetPieceSetup`, lee `TeamFrame.SetPieceTargetX/Y` en lugar de los targets normales. Los jugadores se mueven a sus posiciones antes del remate.

---

## Set Pieces — experiencia visual

Los set pieces son secuencias de momentos visibles, no funciones síncronas.

**Flujo de un córner:**

```
RefereeAgent detecta salida → emite SetPieceAwarded (Corner, HomeClub)
        ↓
MatchFlow → SetPieceSetup
        ↓
SetPiecePositioner.assignTargets — escribe targets en TeamFrame
(near post, far post, box, edge para atacantes / muro, marcaciones para defensores)
        ↓
MovementEngine respeta targets — jugadores se mueven, viewer lo muestra
        ↓
EventRouter activa TeamOrchestrator — coordina posicionamiento
        ↓
RemainingTicks llega a 0
        ↓
SetPieceAgent ejecuta — taker, rutina, lanzamiento
        ↓
BallAgent resuelve llegada — mismo pipeline que cualquier centro
```

`SetPlayAction` solo ejecuta el lanzamiento. Toda la selección y posicionamiento ocurrió durante el setup.

---

## Módulo compartido — Ball/Bounds

El único módulo que cruza fronteras de agentes. Existe una sola vez. La lógica de salida por línea de fondo no puede existir en dos lugares.

---

## Principios para cualquiera que toque el código

**Si agregás un número que afecta el comportamiento del juego, va a `BalanceConfig`.**

**Si dos módulos necesitan la misma lógica, la lógica se mueve a un tercer módulo.**

**Si querés agregar inteligencia colectiva, el lugar es `TeamOrchestrator`.** Si querés agregar inteligencia de manager, el lugar es `ManagerAgent`. Si querés agregar inteligencia individual, mirá las 5 capas de `PlayerAgent`.

**Los agentes emiten `SemanticEvent` cuando algo relevante ocurre.** Si un agente hace algo importante y no emite un evento semántico, ese comportamiento no existe para el resto del sistema.

**Antes de agregar algo, preguntá qué `SemanticEvent` emite.**

**El compilador es el primer test.**

---

*El código converge hacia este documento.*
*El documento no converge hacia el código.*
*El sistema nunca está terminado. Esa es la idea.*
