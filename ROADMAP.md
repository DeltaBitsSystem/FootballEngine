# Roadmap — FootballEngine

> **Fecha:** 19 ago 2026 · Reescrito sobre el código actual (`FootballEngine.Core`).
> Este documento reemplaza a `REWRITE_PLAN.md` (obsoleto: referenciaba rutas que ya no existen).
> Las quejas de la comunidad de Football Manager alimentan los requisitos: ver `docs/USER_FEEDBACK_ROADMAP.md` (investigación, 18 temas con citas verbatim).

---

## Misión

Un simulador de fútbol determinista y offline donde **los resultados tienen causas encadenadas reales** y la dificultad **emerge de la inteligencia de la IA**, no de modificadores artificiales. La IA debe jugar al mismo juego que el humano: misma información, mismos incentivos, mismas consecuencias.

## Principio rector del roadmap

> La IA debe aprender de ti — y comprar bien, siempre.

Cada feature de IA se implementa con su **test de ventana en CI**: el balance es un dato, no una discusión de foro.

---

## P0 — Entrenador IA Adaptativo (Fase 1)

Memoria entre partidos: la IA estudia tus patrones (pasillos, longitud de pase, formación, instrucciones), los persiste en la DB con decaimiento temporal y **contrarresta antes del partido**. El humano recibe la misma herramienta, pero **sesgada** (staff con ruido); la IA lee la verdad.

Detalle técnico completo: `docs/ADAPTIVE_AI_PLAN.md`

| # | Trabajo | Dónde |
|---|---|---|
| N1 | Enriquecer `EventData` con posición (desbloquea pass maps y heatmaps) | `Core/Match/Contracts/MatchEvent.fs` |
| N2 | `PatternExtractor` — vector de patrones por partido (fold puro como `MatchMetrics`) | `Core/Domain/PatternExtractor.fs` |
| N3 | `ScoutingMemory` por club + persistencia en SQLite (patrón `CoordinationMemory`) | `Core/Domain/Club.fs`, `Database/` |
| N4 | `CounterPlan` aplicado en el armado del partido (tenaz: contrarresta siempre, incluso si cambiaste todo) | `Core/World/Phases/MatchContextBuilder.fs` |
| N5 | Informe asimétrico (AI perfecta / humano sesgado) + página `Scouting` (hoy `failwith "todo"`) | `Client/AppState/Slices/`, `Client/Views/Pages/` |
| N6 | **AI de traspasos que compra bien siempre**: `SquadPlanner` con necesidad por rol, minutos reales y disciplina de valor; test de 10 temporadas | `Core/World/Agents/ManagerAgent.fs`, tests Layer 1 |

## P0 — Calibración y datos

| # | Trabajo | Estado actual |
|---|---|---|
| C1 | Ventanas estadísticas reales en CI (goles/partido, xG, posesión) sobre 1.000 partidos | Parcial (proyecto `Training/`: `validate`, `sweep`, `quick-fit`) |
| C2 | Balance de dificultad como test: "ascenso improbable" (presupuesto mínimo vs AI adaptativa, 100 temporadas) | No existe |
| C3 | Ventanas de contrarresto (ver plan: nunca < 20% de victoria) | No existe |

## P1 — Fase 2: adaptación en vivo

- La IA detecta cambios de patrón durante el partido con delay = `AdaptationSpeed` del manager rival.
- Nuevo `EventKind.AdaptationShift` + barra de "escáner" en `MatchViewer` ("El rival está descubriendo tu táctica (75%)").
- La sorpresa es **emergente** (cambiar de pasillo deja al rival des-contado), sin buffs artificiales.
- Ajustes sobre los canales `TeamEmergent` y `Directive` existentes.

## P1 — AI de managers (de `USER_FEEDBACK_ROADMAP.md`)

| # | Trabajo | Queja origen |
|---|---|---|
| M1 | Filosofías de manager en datos + anti-convergencia (entropía mínima de sistemas a 10 temporadas) | "every other manager uses 4-4-2" |
| M2 | Moral causal por eventos + promesas como contrato de estado | "morale goes from way up to the floor" |
| M3 | Valor de mercado causal (edad, contrato, minutos, forma) | "Kudus worth 90M with 6 g/a" |
| M4 | Lesiones como consecuencia de carga real | "89 injuries in 46 games" |
| M5 | Newgens con arquetipos modernos | "newgen CBs can't pass" |

## P2 — Profundidad de juego

| # | Trabajo | Nota |
|---|---|---|
| D1 | Estadísticas avanzadas: xG, xA, pass maps, heatmaps | Desbloqueado por N1 (posición en eventos) |
| D2 | Finanzas: presupuesto, ingresos de partido, patrocinios | Datos existen, UI pendiente |
| D3 | Valoraciones de jugador por partido y rendimiento | — |
| D4 | Contratación de staff y renovaciones | — |
| D5 | Diseñador de balones parados | — |
| D6 | Cantera con generación anual | — |

## P3 — Comunidad y producto

| # | Trabajo |
|---|---|
| E1 | Más mods de ligas (JSON, schema ya validado) |
| E2 | API de datos de partido exportables |
| E3 | Documentación para autores de mods |
| E4 | Temas de UI y micro-funciones de datos (comparador, colores de atributos, atajos) |

---

## Cómo se mide el avance

- Cada fila de este roadmap se cierra con un test en `FootballEngine.Core.Tests` (nunca "a ojo").
- Los saves largos (10-15 temporadas) son tests de CI, no anécdotas: el motor es determinista y rápido.
- `Training/` recalibra pesos contra ventanas reales; cada calibración cambia defaults con nombre en `BalanceConfig`.