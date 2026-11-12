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

### F2 — Feedback rápido y limpio (DX)

1. Warnings a cero y `TreatWarningsAsErrors` en todos los proyectos.
2. Niveles de test explícitos: **rápido** (< 60 s de pared, default local) y **completo** (gate). El rápido
   no reduce cargas en silencio: agrupa los tests unitarios y causales; los de temporada quedan en completo.
3. Con F1, la suite completa debería bajar de 4 min a < 1 min sin tocar cargas.
4. `dotnet fsi scripts/dev.fsx` único: build, fast tests, format y profile con un comando.

Estado (2026-11-12): 1 hecho (0 warnings en todos los proyectos y Tools, `TreatWarningsAsErrors` global); 2 hecho
(`--fast`: 863 casos en ~30 s de ejecución); 4 hecho (`scripts/dev.fsx`). 3 pendiente de F1: la suite completa
tarda 1:45.

### F3 — Simple y entendible

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

Con el motor rápido, simular **todas** las ligas cargadas con el motor completo (sin un modo "rápido" con otra
física), y usar el sobrante para inteligencia: lectura táctica del rival en vivo, aprendizaje de jugadores
entre partidos. Se detalla al cerrar F1–F3, con mediciones reales de throughput de temporada.

## 3. Reglas

- Cada paso: medir antes, cambiar, medir después, tests afectados; suite completa al cerrar cada fase.
- Alto rendimiento solo con perfil que lo justifique; el resto, F# idiomático (records, DUs, pipelines).
- Ningún cambio deportivo sin regresión del corpus y recalibración held-out.
