# Plan — Entrenador IA Adaptativo (Fase 1: memoria entre partidos)

> **Fecha:** 19 ago 2026 · **Estado:** Fase 1 implementada
>
> **Idea:** llevar el efecto de un entrenador adaptativo (una IA que aprende de tus patrones y te contrarresta) al motor. La IA rival estudia cómo jugaste en partidos anteriores, guarda ese conocimiento en la DB y ajusta su plan de partido antes de enfrentarte. El humano recibe la misma herramienta, pero sesgada. La dificultad emerge de la inteligencia de la IA, no de modificadores artificiales.
>
> **Decisiones de diseño tomadas (con el autor del proyecto):**
> 1. **Fase 1 = memoria entre partidos.** La adaptación en vivo queda para la Fase 2.
> 2. **Scouting asimétrico:** el staff humano genera informes sesgados (con ruido); la IA lee la verdad siempre. Así la IA es inteligente sin romper el juego.
> 3. **La IA debe comprar bien SIEMPRE.** El balance de traspasos de la IA es P0: comprar por necesidad real, no por reputación. (Síntoma que la comunidad de FM reporta: compras redundantes de 29-31 años que se pudren.)
> 4. **El contrarresto es tenaz:** si el usuario repite formación e instrucciones, la IA lo contrarresta; si el usuario cambia mucho, la IA igual lee el último partido y ajusta. Siempre un paso adelante.
> 5. **Sin "bono de sorpresa" artificial.** La sorpresa emerge: si cambiás de patrón, el rival queda des-contado y tu eficiencia sube sola.

---

## 0. Por qué este plan encaja en la arquitectura actual

El motor ya tiene casi todas las piezas. Este plan las conecta, no inventa:

| Pieza existente | Dónde | Qué aporta al plan |
|---|---|---|
| `MatchMetrics.extract` | `FootballEngine.Core/Domain/MatchMetrics.fs` | Patrón a seguir para el extractor de patrones (fold puro sobre `MatchEvent list`) |
| `CoordinationMemory` + `updateClubCoord` | `Domain/Club.fs`, `World/Phases/MatchPhase.fs:313` | Patrón de memoria por club actualizada post-partido — la `ScoutingMemory` replica esta mecánica |
| `MatchContextBuilder.build` | `World/Phases/MatchContextBuilder.fs` | Único punto de inyección del plan de partido: ahí se aplica el contrarresto pre-partido |
| `TacticsConfig` | `Match/Contracts/TacticsConfig.fs` | Contrato que ya consume `Decide.fs` y `TeamEmergent` — el contrarresto ajusta estos campos, sin tocar el motor de decisión |
| `Lineup` (Formation + Instructions) | `MatchContextBuilder.fs:44` | La firma táctica (formación + instrucciones) ya es capturable pre-partido, sin eventos |
| `TargetSelection` / `SquadAnalysis` | `World/Agents/ManagerAgent.fs` | Base del `SquadPlanner` (AI que compra bien) |
| Página `Scouting` | `AppState/Types.fs` (caso `Scouting`), `Program.fs:68` (`failwith "todo"`) | El informe sesgado para el humano tiene página reservada |
| `BalanceConfig` | `Match/Contracts/BalanceConfig.fs` | Todo número de comportamiento vive acá: ruido de scout, decaimiento, fuerza de contrarresto |

**Determinismo:** el contrarresto es función pura del estado persistido (los partidos anteriores del usuario son inputs del sistema). Misma seed + mismo historial → mismo partido. Los replays no se rompen.

---

## 1. Fase 1 — Memoria entre partidos

### 1.1 Extractor de patrones (`PatternExtractor`)

Nuevo módulo `FootballEngine.Core/Domain/PatternExtractor.fs`, al lado de `MatchMetrics`, mismo estilo: fold puro y determinista sobre `MatchEvent list`.

```fsharp
type PatternVector =
    { Corridors: float * float * float          // izquierda / centro / derecha (fracción, suma = 1)
      PassLength: float * float * float         // corto / medio / largo (fracción)
      CrossRate: float                          // centros por 100 posesiones
      ShotZones: float * float * float          // lejos / área / cerca (fracción)
      Directness: float                         // 0..1: proporción de pases hacia adelante
      PossessionStyle: float                    // 0..1: posesión vs transición
      Formation: string                         // firma táctica: "4-3-3"
      Instructions: TacticalInstructions }      // firma táctica: instrucciones
```

**Trabajo previo obligatorio — enriquecer eventos con posición.** Hoy `EventData.Pass`, `Shot`, `Dribble` y `CrossAttempt` no llevan posición, y el pasillo (izquierda/centro/derecha) no se puede derivar sin ella. Se añaden campos de posición a esos casos de `EventData` (`Match/Contracts/MatchEvent.fs`). Dos beneficios colaterales:
- Los **pass maps / heatmaps** del roadmap de estadísticas avanzadas dejan de requerir trabajo extra (mismo dato).
- El viewer puede trazar la procedencia de un gol (rebobinado causal).

Nota: `EventKind` tiene su tabla de bits congelada (P1) — **no se toca**; solo cambia `EventData`, que no tiene esa restricción. Los tests de determinismo cubren la regresión.

**Extracción:** se corre en `MatchPhase.runFixtures`, después de `MatchMetrics.extract`, para cada equipo de cada partido (AI vs AI incluido: la IA aprende de todos, no solo del usuario).

### 1.2 Memoria persistida (`ScoutingMemory`)

Cada club guarda su lectura de cada rival que enfrentó:

```fsharp
type OpponentMemory =
    { OpponentId: ClubId
      Vector: PatternVector
      MatchesSeen: int
      LastSeen: System.DateTime
      Weighted: PatternVector }   // vector con decaimiento temporal aplicado

// en Club:
ScoutingMemory: Map<ClubId, OpponentMemory>
```

- **Decaimiento temporal:** al actualizar, los matches viejos pesan menos (factor `exp(-Δdías / λ)` con `λ = ScoutConfig.MemoryHalfLife`). Esto es el "límite de memoria": equipos chicos olvidan rápido, equipos grandes recuerdan (mismo factor por ahora; la diferenciación por club llega con las personalidades en Fase 3).
- **Persistencia:** `Club` ya se serializa en `Database/Entities.fs` + `Mappers.fs` + `Serializers.fs`. Se agrega la columna/tabla de `ScoutingMemory` siguiendo el patrón exacto de `CoordinationMemory`.
- **Límite de tamaño:** poda de rivals sin enfrentamientos en > 1 temporada (evita crecimiento infinito en saves largos; regla con justificación en config).

### 1.3 Informe asimétrico (el corazón de la diversión)

```
Verdad (PatternVector real) ──► IA: informe = verdad (sin ruido)
                            └─► Humano: informe = verdad + ruido del staff
```

- **IA perfecta:** los clubes AI leen `Weighted` directo. Sin sesgo, sin ruido. Config flag `ScoutConfig.AiReportNoise = 0.0`.
- **Humano sesgado:** el informe que ve el usuario se genera con ruido controlado por el atributo de scouting del staff:
  - patrones por debajo de un umbral de confianza se omiten (`ScoutConfig.MinConfidenceToReport`),
  - la magnitud de un patrón reportado se distorsiona con ruido gaussiano proporcional a `(1 - staff.JudgingPlayerAbility/100)` (los atributos del staff ya existen en la DB),
  - el informe tarda en "descubrir" patrones nuevos: la primera vez que el rival usa una táctica nueva, el staff la reporta con confianza baja.
- **Por qué esto funciona:** la IA nunca es imbécil (siempre contrarresta de verdad), pero el usuario tiene que confiar en su propio staff — que se equivoca. El juego no se gana con una receta: se gana leyendo mejor que el staff rival, como en la vida real.
- **Implementado como:** `Confidence = min 1.0 ((MatchesSeen - 1) / ConfidenceMatches)` — el primer partido es el descubrimiento, no una confirmación; con `ConfidenceMatches = 3` hacen falta 4 avistamientos para confianza plena. La confianza la modula el ruido (es cuántas veces lo viste, no cuánto mide).

### 1.4 Contrarresto pre-partido (`CounterPlan`)

En `MatchContextBuilder.build`, antes de armar el `TacticsConfig` del club AI contra el usuario (y del usuario contra la AI, en ambos sentidos):

```fsharp
type CounterPlan =
    { SideBias: float          // -1..1: empujar el bloque hacia un pasillo
      PressBoost: float        // sumado a PressIntensity
      LineDrop: float          // resta a DefensiveLine (bloque bajo)
      WidthTighten: float      // resta a Width (cerrar pasillos)
      TempoShift: float        // resta a Tempo (no dejar jugar rápido)
      DirectnessShift: float } // contrarresto del estilo

// regla de decisión (tenacidad):
//   contrarresta SIEMPRE que la firma táctica del rival coincida con la última
//   vista, o que la memoria no haya registrado firma (no es un cambio). Solo
//   una formación DISTINTA a la memorizada escala el contrarresto por
//   SignatureChangePenalty — las instrucciones (tempo/directness) se ignoran
//   en la firma: son tácticas intercambiables sin cambiar identidad, y tratarlas
//   como identidad haría que el contrarresto se rebajara por haber hecho su
//   propio trabajo. Si el rival cambió TODO, la IA lee el último partido y
//   contrarresta sobre él.
```

- El `CounterPlan` se aplica **sumando** sobre los campos de `TacticsConfig` (nunca reemplazando la identidad del club: un equipo de bloque bajo no se vuelve gegenpress).
- La fuerza del contrarresto está acotada por `CounterConfig.MaxBias` etc. — el contrarresto **nunca** convierte un partido en derrota asegurada; mueve la ventana de resultados.
- El módulo que convierte `OpponentMemory` → `CounterPlan` es una función pura y se testea en aislamiento (Layer 3).
- Sin cambios en el motor de decisión: `Decide.fs` y `TeamEmergent` ya consumen `TacticsConfig`. El marcaje hombre-a-hombre puntual (`ManMark`) queda para Fase 2 (requiere plomería nueva en el motor).

### 1.5 Página de Scouting para el humano

La página `Scouting` ya existe como caso de `Page` (`AppState/Types.fs`) y hoy es `failwith "todo"` en `Program.fs:68`. Se implementa:
- Lista del próximo rival + informe sesgado (patrones, formación, instrucciones, confianza por ítem).
- Formato: barras de confianza por patrón (el germen de la barra del "escáner" de la Fase 2).
- Slice `ScoutingSlice` nuevo en `AppState/Slices/`, vista en `Views/Pages/Scouting.fs`.

### 1.6 AI de traspasos que compra bien (P0 aparte)

El usuario lo marcó como el problema #1 del balanceo: "la IA hace compras de mierda; debe comprar bien siempre". El `ManagerAgent` ya tiene `SquadAnalysis.assessNeeds` + `TargetSelection.selectTargets`; el plan los fortalece en un `SquadPlanner`:

1. **Necesidad por rol con minutos reales:** hueco = minutos esperados por rol − cobertura actual (edad + minutos jugados la temporada anterior). Un 29-31 años solo se ficha si el hueco es "ganar ahora" (presión de directiva alta).
2. **Nunca apilar:** prohibición suave de tener > 2 jugadores de 25+ por rol con < 10 partidos/año (el síntoma "Tel en Bayern" como invariante de sistema).
3. **Disciplina de valor:** no pagar sobre el valor de mercado salvo presión de directiva; usar la misma función de valor que el mercado (`MarketValue` causal del roadmap).
4. **Rotación causal:** como el desarrollo depende de minutos jugados, un club que compra un wonderkid y no lo juega destruye su activo — la AI rota porque el incentivo es causal (ver `USER_FEEDBACK_ROADMAP.md` tema 6 y R2).

---

## 2. Calibración y tests

Regla del proyecto: **el balance es dato, no sensación.** Cada mecánica nueva lleva ventana calibrada en CI.

| Test | Capa | Invariante |
|---|---|---|
| `PatternExtractor` determinista y correcto (corridors suman 1, etc.) | Layer 3 | Misma lista de eventos → mismo vector |
| Decaimiento temporal | Layer 2 | Un partido de hace 3 meses pesa < uno de ayer; `λ` en config |
| Asimetría del informe | Layer 2 | AI: informe == verdad. Humano: ruido acotado, patrones bajo confianza omitidos |
| Contrarresto | Layer 1 E2E | 500 partidos misma táctica vs AI adaptativa: la tasa de victoria cae con las repeticiones hasta una ventana calibrada (ej. 45–55% para rival de igual plantilla), y **nunca** a < 20% (el contrarresto no rompe el juego) |
| Sorprender funciona | Layer 1 E2E | Cambiar de patrón tras 5+ partidos repetidos recupera la ventaja inicial dentro de la ventana |
| Saves largos (traspasos) | Layer 1 | 10 temporadas simuladas: ningún club termina con > 2 jugadores de 25+ con < 10 partidos/año; los clubes AI mantienen plantillas por rol coherentes |
| Determinismo | Layer 2 | Mismo seed + mismo historial → mismo resultado (los replays siguen funcionando) |

---

## 3. Config nueva en `BalanceConfig`

| Sección | Campos (todos con default calibrado y validación en `ConfigValidation`) |
|---|---|
| `ScoutConfig` | `MemoryHalfLifeDays`, `MinConfidenceToReport`, `AiReportNoise` (=0), `NoiseByAbility` (pendiente), `RivalPruneAfterDays` |
| `CounterConfig` | `MaxSideBias`, `MaxPressBoost`, `MaxLineDrop`, `MaxWidthTighten`, `MaxTempoShift`, `MatchFormationWindow` (cuántos partidos atrás define la "firma táctica actual") |
| `TransferConfig` | `MinMinutesPerRole`, `MaxOver25BenchWarmers` (=2), `OverpayTolerance`, `WinNowAgeThreshold` |

---

## 4. Fases futuras (no incluidas en esta entrega)

- **Fase 2 — Adaptación en vivo:** durante el partido, la IA detecta cambios de patrón con delay = `AdaptationSpeed` del manager rival, ajusta `Emergent` channels/line height en vivo y emite un `EventKind.AdaptationShift` nuevo (el viewer lo muestra: "El rival está descubriendo tu táctica (75%)"). La sorpresa queda emergente: sin buffs.
- **Fase 3 — Personalidades:** atributos por manager: `MemoryDecay`, `AdaptationSpeed`, `PatternBias` (testarudez), `Insight` (calidad de lectura). Diferenciación chico/grande (límite de memoria).
- **Fase 4 — Marcaje hombre-a-hombre:** `ManMark` sobre el creador de juego del rival (plomería nueva en el motor: un agente de marcaje).

---

## 5. Orden de implementación sugerido

1. Enriquecer `EventData` con posición (+ tests de determinismo). *(desbloquea todo lo demás)*
2. `PatternExtractor` + tests Layer 3.
3. `ScoutingMemory` en `Club` + DB (Entities/Mappers/Serializers) + actualización en `MatchPhase`.
4. `CounterPlan` en `MatchContextBuilder` + tests Layer 1/2.
5. Informe asimétrico + página `Scouting` (elimina el `failwith "todo"`).
6. `SquadPlanner` (AI de compras) + test de 10 temporadas.
7. Calibración de ventanas con `Training/` y `weights.json`.

Cada paso compila y se testea solo; el paso 6 no depende del 1-5 (paralelizable).