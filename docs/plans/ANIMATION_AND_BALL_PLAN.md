# Animación de jugadores, física del balón e interacción jugador–pelota — plan

Fecha: 2026-11-09. Reemplaza la decisión "100 % procedural" de `PROCEDURAL_CHARACTER_MOTION.md` §1. Esa
infraestructura (pista de movimiento, checkpoints, IK con límites y foot-lock) se conserva y pasa a ser la **capa
de corrección**. `BALL_FOOT_CONTACT_MOTION.md` está implementado en lo esencial: toques intermitentes, Magnus,
rebote, spin y ciclo de vida del contacto. Este plan lo extiende.

## 1. Objetivo y vara de medida

Objetivo: que un partido visto de cerca se lea como fútbol real, y que en esto supere a Football Manager. FM
reproduce clips de mocap con mezclas visibles, pies que patinan y una pelota que no siempre toca el pie. Nuestra
ventaja posible es la coherencia: cada toque, pase y rebote ocurre donde y cuando el cuerpo lo hace, porque el
motor y la animación comparten el mismo reloj de zancada y el mismo punto de contacto.

Aceptación medible (corpus de secuencias cercanas, 60 Hz, cámara lenta incluida):

| Métrica | Umbral |
|---|---|
| Patinaje de pie apoyado | < 2 cm por apoyo, p95 |
| Error espacial bota/cabeza ↔ pelota en el contacto | < 3 cm, p95 |
| Error temporal del contacto | ≤ 1 frame a 60 Hz |
| Pops (discontinuidad de velocidad angular articular) | 0 por encima del umbral en todo el corpus |
| Pelota que cambia de dirección sin contacto visible | 0 |
| Repetición visible del mismo clip en 10 s cercanos | 0 |
| CPU de animación, 22 jugadores + 3 árbitros | ≤ 1,0 ms p95 por frame (Ryzen 5 5600X) |

Además, revisión artística humana de clips cercanos lado a lado con una referencia de FM. Los números no
alcanzan para declarar "mejor".

## 2. Diagnóstico (2026-11-09)

- El cuerpo hoy: `BodyPose.fs` (48 líneas) solo inclina torso y cadera y gira la cabeza. Los pies se resuelven
  bien (soporte, foot-lock, IK); los brazos se balancean por fórmula. Resultado visible en
  `artifacts/viewer-engine/c2b/giant/scene3-closeup-dribble.png`: jugadores rígidos, erguidos y con los brazos
  colgando. El procedural puro no tiene datos de cómo se mueve un humano. Ese es su techo.
- El rig es ProAthlete, de 60 joints. Esa calidad de esqueleto ya alcanza para mocap.
- `PresentationDelay` = 250 ms. Motion matching necesita conocer la trayectoria futura (~1 s).
- Balón en Core: Magnus (`ω×v`), spin, rebote con fricción, rodadura y toques de conducción intermitentes. Faltan:
  - El arrastre es `AirDrag ** dt`, es decir exponencial y lineal en v. El real es cuadrático, con crisis de
    arrastre alrededor de 12–15 m/s.
  - No hay postes, travesaño ni red como geometría de colisión: un tiro al palo no puede existir.
  - La pelota no choca con cuerpos (bloqueos, rebotes en piernas): solo hay contactos decididos.
  - No hay superficies de contacto (empeine, interior, exterior, punta, planta, muslo, pecho, cabeza) con efecto
    propio sobre impulso, spin y error.
  - Los toques de conducción siguen un intervalo fijo (`CarryTouchInterval`) y no la zancada: el toque puede caer
    con el pie en el aire.

## 3. Fuente de movimiento: datasets libres (decisión del usuario)

Elegido por el usuario el 2026-11-09. Esto contradice la regla de AGENTS.md de que las animaciones finales sean
originales. Por eso:

- Cada clip lleva procedencia: dataset, toma y licencia, en el `manifest.json` y en `LICENSES.md`.
- Los clips quedan marcados como **reemplazables**: el formato de la base de movimiento no depende de su origen,
  y una grabación propia futura entra por el mismo pipeline sin cambiar código.

Candidatos:

| Dataset | Licencia | Uso |
|---|---|---|
| CMU Graphics Lab Motion Capture | Uso libre, incluido comercial; no se pueden revender los datos crudos | **Base principal**: locomoción, giros, desplazamiento lateral, retroceso, saltos, algunas patadas |
| Ubisoft LaFAN1 | CC BY-NC-ND | Descartado: no comercial |
| Bandai Namco Research Motion | CC BY-NC | Descartado: no comercial |
| AMASS / SMPL | No comercial | Descartado |
| Mixamo | Licencia Adobe que permite uso en juegos, sin redistribuir crudo | Solo si hay huecos que CMU no cubra; requiere cuenta Adobe del usuario |

Los términos exactos de CMU se verifican y se citan textualmente en la fase A1, antes de descargar nada (la
descarga requiere tu aprobación). La cobertura de fútbol de CMU es pobre: casi no tiene tackles, cabezazos,
estiradas de arquero ni recepciones. Esos huecos se rellenan derivando clips en Blender a partir de bases CMU
(misma procedencia) y la cobertura se mide antes de comprometer fases posteriores.

## 4. Arquitectura

```text
Core (autoridad deportiva, sin huesos)
  raíz + trayectoria · reloj de zancada (GaitPhase) · tareas de contacto con superficie · física del balón
      │  MotionBoundary (ya existe) + GaitPhase + superficie/punto de contacto
      ▼
Client/Animation (presentación, determinista con los mismos datos)
  1. Locomoción: motion matching sobre la base de movimiento (CMU retargeteado)
  2. Acciones: clip de acción con time-warp para que su frame de contacto caiga en CommitAt
  3. Corrección: foot-lock + IK con límites (existente) al punto de contacto autoritativo
  4. Capas: mirada, inclinación por aceleración, fatiga, estilo por jugador
  5. Inercialización (Holden 2018) en cada cambio de clip: sin crossfade visible
      ▼  PosePalette 60 joints
Match3D: skinning GPU (sin cambios de contrato)
```

Reglas que no cambian: la animación nunca mueve la pelota ni la raíz deportiva. Si el cuerpo no puede alcanzar un
contacto, se registra un residual y se corrige el proxy en Core.

### 4.1 Reloj de zancada compartido (la idea central)

Core calcula para cada jugador una fase de zancada determinista: frecuencia en función de velocidad y longitud
de pierna (`PhysicalMorphology`), y el pie que está en apoyo. Con eso:

- **Core**: los toques de conducción ocurren en un apoyo real del pie elegido; la cadencia sale de la zancada, no
  de un intervalo fijo.
- **Animación**: el motion matching incluye la fase como feature, así que el pie que toca la pelota en Core es el
  que está en el lugar correcto en pantalla.

Esto elimina la clase entera de defectos "la pelota se mueve sola". Es poco código en Core, pero cambia un
comportamiento deportivo: va con regresión del corpus deportivo.

### 4.2 Motion matching

- **Features por frame**: posiciones y velocidades de ambos pies, velocidad de cadera, trayectoria futura de la
  raíz (posición y dirección a 0,33 / 0,66 / 1,0 s), fase de zancada. Normalizadas por desvío estándar y con pesos
  en `AnimationConfig`.
- **Búsqueda**: fuerza bruta SIMD sobre una base de ~30–60 k frames, cada 0,1 s por actor o antes si la
  trayectoria cambia bruscamente. Los actores lejanos usan LOD: menos frecuencia de búsqueda y base reducida. Un
  árbol de búsqueda solo si el perfil lo pide.
- **Futuro**: sube `PresentationDelay` de 250 ms a ~1,0 s en la vista de partido. Para un juego de manager un
  segundo de retraso es invisible, y da trayectoria real en vez de predicha. La alternativa, predecir desde la
  intención de Core, queda descartada porque introduce errores que luego hay que corregir visualmente.
- **Determinismo**: la búsqueda es pura sobre (base, entradas, checkpoint), así que replay y seek reproducen la
  misma pose, usando los checkpoints existentes.

### 4.3 Acciones con contacto

Una biblioteca de acciones por familia (pase, tiro, recepción, conducción, tackle, cabezazo, arquero, saque de
banda), anotada en el pipeline con frame de contacto, efector, superficie y punto de contacto relativo a la raíz.
Para cada tarea de Core:

1. Se elige el clip por familia, superficie, pie y distancia y altura del contacto (matching de acción).
2. Se aplica time-warp entre preparación y contacto para que el frame de contacto caiga exactamente en `CommitAt`.
3. IK acotada lleva la bota al punto autoritativo. Si hace falta más de un umbral, es un residual: falla el gate
   y no se estira el cuerpo.
4. La recuperación vuelve a motion matching por inercialización.

### 4.4 Física del balón y del contacto (Core)

- **Aerodinámica**: arrastre cuadrático con coeficiente dependiente de Reynolds (crisis de arrastre),
  sustentación Magnus con coeficiente de spin acotado y decaimiento de spin por arrastre del aire. Las constantes
  van a `BallConfig` con validación. Referencias: valores publicados de túnel de viento para balones de 32 y menos
  paneles.
- **Arcos**: postes, travesaño (cilindros) y red (volumen que absorbe energía) como colisión analítica. Un tiro al
  palo emite un evento propio para el visor y el relato.
- **Cuerpos**: cápsulas de piernas y torso de cada jugador, derivadas de `PhysicalMorphology` y la fase de zancada.
  Choques no intencionales producen rebotes y bloqueos con evento. Es colisión analítica de 22 × pocas cápsulas,
  sin motor rígido genérico.
- **Superficies de contacto**: cada superficie define una función de impulso, spin y dispersión por técnica,
  equilibrio y presión, con números en `BalanceConfig`. Un pase de exterior lleva spin lateral; uno de empeine,
  potencia y menos precisión.
- **Primer control**: amortiguación por superficie y técnica. Un mal control rebota lejos, y la animación lo
  explica porque comparte superficie e instante.

## 5. Fases

Cada fase termina con builds, la suite completa en verde, capturas o video cercano inspeccionados, métricas de §1
y límites honestos.

| Fase | Trabajo | Aceptación |
|---|---|---|
| **A0 — Evidencia** | Corpus de 12 secuencias cercanas fijas (arranque, frenado, giro, conducción, pase, tiro, recepción, tackle, salto) con video 60 Hz desde el harness; medidor de patinaje, error de contacto y pops sobre la paleta | Baseline actual medido y reproducible |
| **A1 — Pipeline de datos** | Términos de CMU citados; descarga aprobada; Blender: BVH → retarget al rig ProAthlete (60 joints) → limpieza → etiquetas de apoyo y contacto → base binaria `motion-db` + loader F# con validación y hash; informe de cobertura por familia | Base cargada en tests; reporte de cobertura; procedencia en manifest |
| **A2 — Locomoción** | Motion matching + inercialización reemplazan la pose de locomoción; foot-lock e IK existentes encima; `PresentationDelay` 1 s | Patinaje y pops dentro de umbral en el corpus; ≤ 1 ms CPU |
| **B1 — Balón en vuelo** (en paralelo, solo Core) | Arrastre cuadrático + crisis, Magnus con coeficiente, postes, travesaño y red | Tests contra trayectorias de referencia analíticas; tiro al palo con evento; regresión del corpus deportivo |
| **A3 — Reloj de zancada** | `GaitPhase` en Core; toques de conducción en apoyo real; feature en matching | Cero toques con pie en el aire; distancia pie–pelota en el toque < 3 cm |
| **B2 — Contacto y cuerpos** | Superficies de contacto, primer control, colisión con cápsulas de cuerpo | Tests causales A/B (superficie → spin y dispersión); rebotes con evento |
| **A4 — Acciones** | Biblioteca de acciones, time-warp a `CommitAt`, IK al punto autoritativo | Error de contacto < 3 cm y ≤ 1 frame en pase, tiro, recepción y tackle |
| **A5 — Cobertura** | Arqueros (estiradas, blocajes, saques), cabezazos, saque de banda, caídas, reacciones y festejos por evento, árbitros y asistentes | Ninguna familia sin animación; ningún gesto que invente fútbol |
| **A6 — Producción** | Estilo por jugador (seed), fatiga en postura y amplitud, LOD, perfilado, revisión artística vs FM | Todos los umbrales de §1; video comparativo |

Orden recomendado: **A0 → A1 → A2** (el salto visual más grande), con **B1** en paralelo porque toca solo Core y no
comparte archivos. Después A3 + B2, luego A4, A5 y A6.

## 6. Riesgos

1. **Cobertura de CMU para fútbol.** Es probable que falten tackles, cabezazos y arqueros. Mitigación: derivar en
   Blender; si la calidad no alcanza, grabación propia más adelante por el mismo pipeline.
2. **Retarget de proporciones.** Los actores de CMU tienen otras proporciones. Mitigación: retarget por
   rotaciones locales más corrección de pies e IK; validación de longitudes de segmento.
3. **Ruido de mocap antiguo** (jitter, pies que flotan). Mitigación: filtrado y re-etiquetado de contactos en el
   pipeline offline, nunca en runtime.
4. **Cambios deportivos** (zancada, aerodinámica, cuerpos). Mitigación: van con justificación y regresión del
   corpus de partidos; no se recalibra contra seeds conocidas.
5. **Latencia de 1 s.** Es aceptable en la vista de partido; los comandos del manager no dependen del frame visual.
