# Mantenibilidad, rendimiento y DX — plan

Objetivo: un proyecto que una persona entienda y cambie rápido, con un motor más profundo que Football Manager y
lo bastante rápido para simular un mundo entero. F# idiomático por defecto; código de alto rendimiento solo donde
el perfil lo pide, aislado y con su porqué. Sin compatibilidad hacia atrás.

## 1. Línea base medida (2026-11-11, Release, Ryzen local)

| Medida | Valor | Por qué importa |
|---|---|---|
| Un partido completo (headless) | 12,8 s, 48,9 µs/tick, ~2 GB asignados | Temporadas, tests y calibración escalan con esto |
| Reparto del partido | ContactDynamics 31 %, PlayerDecision 29 %, TeamPosition 16 %, PlayerIntent 10 % | Cuatro sistemas son el 86 % |
| Asignaciones sospechosas | ContactDynamics 756 MB, RestartController 458 MB (casi sin trabajo), PlayerDecision 510 MB | Basura por tick en caminos calientes |
| Suite Core | 4 min de pared, 46 min de CPU; las pruebas de temporada se llevan la mayor parte | Feedback lento: se evita correrla |
| Build incremental Core + Client | ~33 s | Ciclo de edición |
| Warnings | Core 4, Client 91, Core.Tests 13 | Ruido que esconde problemas reales |
| Archivos gigantes | ModSystem 4441, Settings 2606, UI 1965, ContactDynamics 1700 líneas | Difíciles de leer y de revisar |
| Documentación de arquitectura | AGENTS.md describe carpetas que ya no existen | Engaña a quien llega (persona o agente) |
| Decisión de jugador | Utilidades ad hoc con pesos opacos (`WeightSections.generated`) | Difícil de razonar; p. ej. 7 % de remates de afuera vs ~40 % real |

## 2. Fases (en orden)

### F1 — Motor rápido (desbloquea todo lo demás)

Meta: **≤ 1,5 s por partido** (×8) y **≤ 100 MB asignados por partido**, con resultados idénticos (mismos eventos
con la misma semilla) salvo donde un cambio se justifique y recalibre.

1. Cero asignaciones por tick en los cuatro sistemas calientes y en RestartController: buffers en el scratch del
   mundo, structs, sin listas/closures/tuplas en bucles.
2. Índice espacial compartido por tick (grilla del campo) para `opponentsNear`, pases candidatos y presión, en
   lugar de recorridos O(n²) repetidos por sistema.
3. PlayerDecision: candidatos y evaluaciones por jugador cacheados mientras su percepción no cambia.
4. Gate de rendimiento: `Training profile` con presupuesto por sistema y BenchmarkDotNet para micro-casos; una
   regresión > 10 % se ve en el PR.
   Hecho: `scripts/dev.fsx perf` con `perf/budget.txt`. El rediseño para bajar de ~5,4 s está en
   `F1_ENGINE_REDESIGN.md`.

Aceptación: hash de eventos idéntico en el corpus de semillas; perfil antes/después en el commit.

Progreso (2026-11-12): 12,8 s → ~5,0 s por partido. Refactors puros con hash idéntico; el único cambio numérico
(2 segmentos de contacto en vez de 4) validado held-out con 48 partidos, referencia nueva `12d27136516b9ad0`.
Antes de ese cambio, 5,8 s:
ContactDynamics 2,2 s / 181 MB, PlayerDecision 0,94 s, TeamPosition 0,94 s, RestartController 0,15 s / 43 MB.
Suite Core 4:04 → 1:59 (837/837). Lo que queda pide cambios de estructura, no micro-ajustes: menos segmentos de
contacto por tick con la misma precisión, índice espacial compartido, planner sin `Set`/closures.

Corrección deportiva (2026-11-12): las reanudaciones congelaban ~1/3 del partido (un penal con un defensor
proyectado dentro del área por redondeo, un compañero parado sobre el balón que cancelaba el tiro libre ~30 000
veces, el saque de arco puesto a 6,5 m mientras el plan mandaba al ejecutor a 5,5 m, expulsados que bloqueaban
reglas). Juego vivo 129k → 202k ticks por partido; eventos 158k → 87k. Recalibrado (`ShotErrorBase` 0,26,
`HandError` 0,16): held-out 48 partidos con 7/8 métricas en banda; los córners (39/partido, ya 31 antes) son el
siguiente problema deportivo. Hash nuevo `8f3dbf11178e6bb6`. Con más juego vivo el partido cuesta ~6,5 s y
PlayerDecision pasa a 1,4 s / 772 MB: es el próximo objetivo de F1.

Planner (2026-11-12): balones rodados reutilizados por hilo, un `ChainState` por rama y pases leídos de una tabla
interpolada cada 5 cm (`Planner.PassTableResolution`, error acotado por test). Hash sin cambios
`b7f74f92b9fb2bb2`; held-out idéntico. Partido 5,7 s → 5,45 s; PlayerDecision 0,78 s / 441 MB. Lo que queda:
ContactDynamics (1,6 s, trabajo repartido: `findEarliest` 0,54 s, cotas 0,26 s, escritura 0,17 s) y
TeamPosition (0,87 s, 15 µs por llamada). La meta de 1,5 s pide bajar la frecuencia de contacto o paralelizar
por partido; ambas cosas necesitan validación held-out.

Después de F3.2 (2026-11-13): 7,2 s por partido. PlayerDecision 2,1 s / 1,45 GB, ContactDynamics 1,8 s,
TeamPosition 0,9 s. Contado: 54 932 decisiones de portador por partido (re-decide cada ~8 ticks: cadencia 4 +
deliberación 4) y 66 evaluaciones del planner por decisión; la búsqueda cuesta 1,6 s (29 µs por decisión). Espaciar
las re-decisiones ahorraría como mucho ~0,8 s y es un cambio deportivo; la meta pide además el contacto
(segmentos por tick) y construir estados del planner solo para las ramas que sobreviven al haz.

Partidos de fondo (2026-11-12): las ligas que el usuario no sigue se juegan con `QuickMatch` (sin ticks:
Poisson de goles por fuerza media del once, goleadores/asistentes por rol y atributo, tarjetas, faltas,
lesiones y cambios), del orden de microsegundos por partido. Los partidos de competiciones donde juega el club
del usuario siguen en el motor completo. `Training quick-fit` ajusta `QuickMatch` contra el motor y
`quick-validate` compara ambos por brecha de fuerza; mientras el motor no premie la calidad, `defaultQuickMatch`
usa valores de ligas reales.

Validez deportiva (2026-11-12): ajustar `QuickMatch` reveló que el motor no respondía a la calidad. Medido con
plantillas reales de un mundo generado (`Training side-counts`, `side-flow`, `contrast`, `contrast-groups`):
- `MatchWorld.offsideLine` arrancaba el pliegue en `-HalfLength` para los dos lados: el visitante nunca quedaba
  en offside, el local pasaba el 60 % de su posesión en su tercio y el visitante marcaba el doble. Ahora arranca
  en mitad de cancha (nadie está en offside en su campo). El planner proyectaba la línea con el *segundo*
  defensor de campo; ahora usa la misma regla que el árbitro.
- El efecto del tiro era en coordenadas del mundo: el visitante curvaba hacia afuera del arco.
- Las utilidades de decisión sumaban atributos como preferencia: un jugador mejor en todo prefería pasar (cuatro
  términos de habilidad) a tirar (dos) o encarar (ninguno), y el equipo débil tiraba más. Ahora cada habilidad se
  lee contra la media propia del jugador (`Decision.ownLevel`); el nivel absoluto actúa en la ejecución.
- La ejecución casi no dependía de la técnica: error de tiro 0,26 + 0,25·(1−definición) pasa a 0,14 + 0,5·(1−def.)
  (mismo promedio), y la dispersión de pase se duplica con `TechniqueShare` 0,9.
Resultado: +20/−20 en todos los atributos pasa de 0,67–2,33 (invertido) a 2,08–1,08. Pendiente: el pase sigue
sin depender del pasador en completitud ni intercepción (91 % vs 89 % con ±20 técnico), y el equipo técnico
recicla en su tercio; hace falta que la factibilidad y la velocidad del pase lean el Passing/Vision. El corpus
de `validate` usa `TestBuilders.defaultCtx`, cuyas posiciones base están amontonadas en un cuadrante: la
calibración debería medirse sobre plantillas generadas.

Seguimiento (2026-11-13): el corpus de `validate`/`sweep`/`hash` ya es un mundo generado
(`SimulatorRunner.corpusContext`); held-out 6/8 en banda (fuera: % de pase 91 y córners). Por atributo
(`contrast-attributes`), Tackling no se usaba en ningún partido y Dribbling no pesaba; ahora cada acción de contacto
usa su habilidad y la conducción tiene término de Dribbling. Passing y Vision estaban invertidos porque subían la
utilidad de cada pase y dejaban los tiros bajo el umbral: el que pasa mal tiraba 22,6 veces contra 5,3. Sin esos
términos de preferencia, +20/−20 en todo da 1,28–1,01 (antes 0,88–1,24): el signo es correcto, el efecto sigue
chico. Probado sin efecto y revertido: elegir primero el tipo de acción y después el destino, valorar el tiro
inmediato con su xG en el planner, peligro del planner en escala 0–1, velocidad de pase por Passing, mal juicio
del pasador. La causa de fondo es la decisión por suma de utilidades: es F3.2, el próximo paso, con la aceptación
"+10 en todo gana por ≥ 0,6 goles en 144 partidos". `QuickMatch` se ajusta después.

F3.2 (2026-11-13): la decisión es por valor esperado en probabilidad de gol (`DecisionValue`, `ExpectedValueConfig`):
pase = completitud por la carrera de intercepción × amenaza del receptor − pérdida × amenaza concedida; tiro = xG
creído (ahora con los rivales dentro del triángulo pelota-postes); conducción, centro y despeje en la misma escala, y
el planner valora el final de la cadena igual. Al quitar la barra de tiro apareció lo que tapaba: la base ya entraba
al área 44 veces por equipo, porque en repliegue nadie salía a la pelota y el portador (6,4 m/s) le sacaba ventaja
a cualquier defensor. Ahora un repliegue a menos de 35 m de su arco manda al defensor que llega primero a contener
(`Contain`: a pleno esfuerzo, entra solo cuando el toque deja la pelota más cerca suyo que del portador), la
conducción va a 5,5 m/s, y quien no fue enviado a presionar tampoco mete el pie con la pelota pegada al portador.
El centro solo se elige si hay rematador (antes se reintentaba en bucle). Held-out 48: 7/8 en banda, error 4,8
(antes 7,0); fuera solo offsides 0,5, porque casi no hay desmarques a la espalda. Embudo de tiro real: 31 % al
arco, 11 % de conversión, 65–70 % atajadas. Aceptación: +10 en todo gana por 0,64 de local y 0,46 de visitante en
144 partidos (promedio 0,55; error estándar ~0,14): en el borde. Atributos ±20: Finishing, Dribbling, Composure y
Pace correctos; Passing neutro; Anticipation levemente al revés, dentro del ruido. `CarrySkillWeight` es 0 a
propósito: sumar la habilidad a la creencia hacía que los equipos hábiles retuvieran la pelota de más e invertía
Dribbling y Tackling. `WeightSections.generated`, `weights.json` y el AutoTuner están borrados: los pesos de estilo
viven en `ExpectedValueConfig` y el resto en su sección (Manager, Match, Actions); hash de eventos sin cambio
(6b23c022b8d5a2c8). Queda abierto: desmarques a la espalda para los offsides.

### F2 — Feedback rápido y limpio (DX)

1. Warnings a cero y `TreatWarningsAsErrors` en todos los proyectos.
2. Niveles de test explícitos: **rápido** (< 60 s de pared, default local) y **completo** (gate). El rápido
   no reduce cargas en silencio: agrupa los tests unitarios y causales; los de temporada quedan en completo.
3. Con F1, la suite completa debería bajar de 4 min a < 1 min sin tocar cargas.
4. `dotnet fsi scripts/dev.fsx` único: build, fast tests, format y profile con un comando.

Estado (2026-11-12): 1 hecho (0 warnings en todos los proyectos y Tools, `TreatWarningsAsErrors` global); 2 hecho
(`--fast`: 863 casos en ~30 s de ejecución); 4 hecho (`scripts/dev.fsx`). 3 pendiente de F1: la suite completa
tarda 1:45.

Medición (2026-11-13): Core 2:13 de pared en 12 núcleos, ~1 500 s de CPU sumada; los casos más largos (72 s) son
temporadas y partidos completos en serie. La pared ya está limitada por CPU, así que < 1 min solo llega bajando el
costo del partido (F1); agrupar o paralelizar tests no alcanza y reducir cargas está prohibido.

### F3 — Simple y entendible

Estado (2026-11-13): 2 hecho (ver F3.2 arriba). 4 hecho: todo partido completo arranca en `Simulator.start`
(valida la config, sella el `MatchInput` con `MatchInput.create`/`engineVersion` y abre la `MatchSession`); carrera,
Training, benchmarks y el adaptador de comandos juegan esa sesión. `Stepper.step`/`stepInto` quedan solo para
escenarios armados a mano (tests y sondas), que corren el mismo tick sin sesión.

Estado (2026-11-12): 1 hecho (AGENTS.md según el árbol real; scripts muertos y visor 2D Skia eliminados). 3 hecho
fuera del motor: ModSystem en siete módulos por etapa, Settings en cuatro, PlayerView y Display fuera de UI.
ContactDynamics se parte dentro del rediseño de F1 (`F1_ENGINE_REDESIGN.md`), que separa su estado. 2 y 4
pendientes; 4 también depende de ese rediseño.

1. AGENTS.md reescrito según el árbol real; un mapa de una página por subsistema.
2. Decisión de jugador por **valor esperado** (xG del remate vs valor de mantener/pasar) con parámetros con nombre
   en `BalanceConfig`; se elimina `WeightSections.generated`. Recalibración contra las bandas y semillas
   held-out.
3. Partir archivos gigantes por responsabilidad (ModSystem, Settings, UI, ContactDynamics), sin cambiar
   comportamiento.
4. Contratos únicos: un solo camino para correr un partido (hoy hay Stepper.step, stepInto y runners de
   Training/tests).

### F4 — Poder: profundidad que FM no tiene

Decisión del usuario (2026-11-12): el fondo debe ser rápido; lo que el usuario sigue puede ser lento. Por eso las
ligas de fondo usan `QuickMatch` (~35 µs por partido, ajustado al motor con `quick-fit`/`quick-validate`) y el
motor completo queda para las competiciones del club del usuario. F4 ya no busca correr todo con el motor
completo, sino:
1. Mantener `QuickMatch` fiel al motor: reajustar con `quick-fit` cada vez que cambie la respuesta del motor a la
   calidad, y vigilar que `quick-validate` siga dentro del error por brecha de fuerza.
2. Usar el motor completo, en las competiciones seguidas, para la inteligencia que el modelo rápido no tiene:
   lectura táctica del rival en vivo y aprendizaje de jugadores entre partidos, con su evento visible.
3. La meta de F1 (≤ 1,5 s) sigue valiendo para el partido del usuario, los tests y la calibración.

## 3. Reglas

- Cada paso: medir antes, cambiar, medir después, tests afectados; suite completa al cerrar cada fase.
- Alto rendimiento solo con perfil que lo justifique; el resto, F# idiomático (records, DUs, pipelines).
- Ningún cambio deportivo sin regresión del corpus y recalibración held-out.
