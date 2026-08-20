# Plan de Diseño — Playbook de Entrenamiento (jugadas ensayadas)

> Estado: **diseño** — propuesta no implementada. Objetivo: entrenamiento
> realista de jugadores y equipo que produzca *comportamiento colectivo visible*
> en el match, sin agobiar al usuario. Complementa
> [ROBOCUP_DEEP_RESEARCH.md](ROBOCUP_DEEP_RESEARCH.md) (punto 7: formación
> aprendida / HELIOS set plays).
>
> La idea en una frase: FM entrena atributos (subir números); esto entrena
> **comportamiento colectivo** — el equipo aprende a jugar *una cosa*, y la
> calidad de ejecución depende de cuánto la ensayó. Encaja con la tesis del
> proyecto: *el manager moldea al equipo con cada decisión; los resultados son
> consecuencias, no inputs*.

## 1. Por qué encaja en la arquitectura actual

Las piezas ya existen — no se inventa maquinaria nueva, se conecta la existente:

| Pieza existente | Cómo se reutiliza |
|---|---|
| `SetPieceRoutines` (Phase 201, rutinas planificadas) | Prueba del concepto "rutina planificada" ejecutada en-match; la jugada de ataque es la misma idea en juego abierto |
| `CoordinationMemory` (Domain) | Plantilla del almacén de proficiency por jugada |
| Patrón R6 (`passMemoryPenalty`, `duelMemoryBias`) | Demuestra que memoria por jugador modula comportamiento vía utilidades |
| `TeamPerceive` → blackboard | Lugar donde vive la señal "¿se puede lanzar la jugada X?" |
| `Formation.fs` + samples | La jugada se define como *offsets sobre la formación*, no posiciones fijas |
| Patrón señal→utilidad (señal al blackboard o término en utilidad) | La ejecución entra sin tocar un solo contrato |

## 2. Definición formal de "jugada"

```fsharp
Play = { Name : string
         Trigger : condiciones de inicio   // zona del balón + fase + score diff
         Moments : (SlotRole * TargetOffset * Timing) list   // 3-6 pasos
         Risk : float }                    // 0..1, qué tan agresiva es
```

Ejemplo — "sarrieta por izquierda":
- *Trigger:* balón en zona media-izquierda, fase transición
- ① el 8 se ofrece al espacio
- ② el 6 supera por banda
- ③ el 11 corta al segundo palo
- ④ el 9 se va al primer palo

Cada momento es un *offset sobre la formación* — la jugada fluye con el juego,
no es un guion fijo (anti-pattern estilo FIFA).

## 3. Ejecución en el match (sin tocar contratos)

El patrón señal→utilidad ya probado en F1–F6/R6:

1. **Señal nueva en el blackboard** (`TeamPerceive`): "¿se puede lanzar la
   jugada X?" — zona del balón, fase, alineación de los roles involucrados.
2. **Lanzamiento** (`TeamDecide`/`TeamPosition`): los targets de forma se
   desvían por momento — cada paso es un offset sobre la formación, no una
   posición fija.
3. **Bias de utilidad** (`PlayerDecision`/`Intent`): el jugador cuyo rol
   participa del momento recibe un peso extra hacia el espacio del momento — no
   se le ordena, se le *inclina*.
4. **Eventos nuevos**: `PlayLaunched` / `PlayCompleted` / `PlayFailed` →
   visible para el viewer (regla 7 de AGENTS.md: si no emite un evento, no
   existe).

## 4. Cómo se aprende (lo que FM no tiene)

Una `PlayProficiency` por (equipo, jugada), tipo `CoordinationMemory`:

- **Crece** al ejecutar la jugada bien en partidos reales (pase completado
  dentro del momento, remate, gol).
- **Decae** si no se usa (jugada no ensayada se olvida).
- **Efecto en runtime**: la proficiency NO es "más probabilidad de gol"
  (aburrido), sino **precisión de ejecución**:
  - ventanas de timing más anchas,
  - pases más ajustados dentro de la jugada,
  - menos chance de que un momento se rompa/desincronice.
- Proficiency baja = ejecución tosca, pasos desincronizados, jugada legible
  como fallida. Proficiency alta = fluida.

## 5. Entrenamiento sin agobiar (la ventana sobre FM)

- **Un tablero, no un calendario.** El usuario dibuja la jugada en la cancha
  (flechas + roles, como el editor de táctica existente). Cero planificación
  diaria — eso es lo que ahoga a FM.
- **Asignación de foco semanal**: "ensayar jugada A × 2, mantener jugada B × 1,
  condición individual" — 3 decisiones, no 30.
- **Dos profundidades**:
  - *Modo simple*: elegir qué ensayar de una lista de jugadas.
  - *Modo avanzado*: dibujar jugadas propias, definir triggers.
  - El hardcore de FM queda contento; el casual no se pierde.
- **Individual vs equipo**: la parte individual es la *antesala* — un jugador
  con "pase al primer toque" entrenado individualmente ejecuta mejor *cualquier*
  jugada que lo involucre; la jugada es donde el equipo convierte eso en fútbol.

## 6. Balance realismo/diversión

La jugada no puede ser un win-button:

- **Riesgo real**: jugada fallida = equipo descolocado (los jugadores del
  momento quedaron fuera de posición → contraataque rival). Es la palanca de
  drama: ver la sarrieta perfecta o regalarla.
- **Contras**: pressing alto la rompe, la trampa de offside la corta (ya
  existe), rival que la conoce la anticipa (el rival también entrena).
- **Determinismo**: mismo seed → misma ejecución. La proficiency introduce la
  varianza legítima: cuanto mejor ensayada, más *consistente* (menos varianza),
  no más probable.
- **Diversión visible**: comentario + eventos reconocibles ("¡la sarrieta!"),
  drama del resultado.

## 7. Por qué es la ventana sobre Football Manager

- FM no puede tener esto: su match engine es un generador de probabilidades —
  no hay "espacio" donde una jugada pueda ocurrir.
- FootballEngine tiene posición, física, memoria y formación aprendida: la
  jugada es el pegamento que une todo.
- El jugador hardcore de FM (50 atributos, 20 instrucciones) valorará un sistema
  donde el entrenamiento produce *fútbol visible* en vez de +2 en un número.

## 8. Fases sugeridas (test-first, patrón de siempre)

1. **PlayBook estático + ejecución en match** — jugadas hardcodeadas de ejemplo,
   bias de utilidad, eventos `PlayLaunched/Completed/Failed`. Probar que el
   equipo realmente ejecuta la jugada.
2. **Proficiency** — aprendizaje post-partido + efecto en precisión +
   decaimiento.
3. **Editor en UI** — tablero de jugadas + foco semanal (modo simple +
   avanzado).
4. **Rivales que entrenan** — los managers AI del World Engine también eligen
   jugadas → variedad táctica.

---

*Documento de diseño — FootballEngine. Sigue las convenciones de `AGENTS.md`
(nada fuera de `BalanceConfig`, un solo lugar obvio, determinismo, tests espejo
de la estructura del código).*