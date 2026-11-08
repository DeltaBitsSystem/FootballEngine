# Match Viewer 3D — plan WebGPU "noche de estadio"

Objetivo: un visor 3D que se vea mejor que el de Football Manager en una captura lado a lado y, sobre
todo, en movimiento. Todo lo que se ve es nuestro o tiene procedencia honesta y autoría conservada.
El visor **explica** el partido; Core sigue siendo la única autoridad de posiciones, contactos y resultados.

Este documento reemplaza la parte de render/assets de `docs/MATCH_VIEWER_3D_PLAN.md` (auditoría de
septiembre, desactualizada). El plan de UI del día de partido (`MATCH_VIEWER_MASTER_PLAN.md`) sigue vigente.

## 1. Punto de partida (auditado hoy)

| Área | Hoy | Falta para "nivel siguiente" |
|---|---|---|
| Backend | Silk.NET + wgpu-native (Vulkan en Linux), ventana hija Xlib | Perfilado GPU, tiers de calidad, refactor del renderer de 2183 líneas en pasadas |
| Imagen | HDR Rgba16f, IBL prefiltrado offline, tonemap ACES-fit, 1 sombra direccional 1 cascada, sin AA (MSAA=1) | AA, sombras en cascada suaves, AO, bloom, grading, AA especular |
| Jugadores | ProAthlete (base MakeHuman CC0 adaptada), skinning en vertex shader con paleta calculada en CPU | Variedad física/caras/pelo, kits por club con dorsal/nombre, LOD, skinning por compute, árbitros |
| Estadio | `stadium.glb` 38 MB **sin autoría acreditada** (solo desarrollo, no distribuible), cartelería "FOOTBALL ENGINE" | Estadio propio generado, uno por club |
| Público | Figuras estáticas | Hinchada animada, reactiva a eventos, con colores del club |
| Césped | Texturas ambientCG CC0 + pasto procedural instanciado | Material propio, rayado por club, desgaste real, clima |
| Cielo/luz | Cielo diurno original de Blender | Atardecer y noche con focos, varias sombras por jugador |
| Cámara | Cámaras fijas seleccionables | Director de TV, repeticiones con DOF/motion blur |

Base que se aprovecha: HDR + IBL correctos, pipeline de mips offline, contrato de rig y contactos,
retained GPU presentation, pasto instanciado, Blender con MCP y skill `footballengine-blender`.

## 2. Por qué esto le gana a FM (la tesis)

FM tiene buenos modelos y una presentación TV correcta, pero estadios genéricos, público repetido, césped
plano y luz uniforme. Ganamos en cinco cosas que WebGPU hace baratas y que encima **nacen de la simulación**:

1. **Cada club tiene su estadio.** Generador paramétrico propio (Geometry Nodes) alimentado por reputación,
   división, colores del kit y semilla del club. Un ascenso se nota en la cancha.
2. **La hinchada es un actor.** 30–60 mil personas animadas en GPU, vestidas con los colores locales y
   visitantes, que reaccionan a los eventos semánticos reales (gol, atajada, roja, palo, tiempo cumplido).
3. **La cancha tiene memoria.** El desgaste del césped se acumula con las trayectorias autoritativas de
   Core (áreas, banda del lateral que más corre, punto penal). A los 80' el campo cuenta el partido.
4. **La noche de estadio.** Focos con cuatro sombras suaves por jugador, halo atmosférico, lluvia que moja
   el césped y el balón. Es la imagen que todos asocian al fútbol televisado y donde FM es más débil.
5. **Calidad de imagen limpia.** Líneas de cal sin dientes, sombras suaves, AO de contacto, bloom
   controlado: el ojo nota la diferencia antes de saber por qué.

## 3. Restricciones

- **Hardware objetivo:** Ryzen 5 5600X + RX 5500 8 GiB (RDNA1), 1080p. Objetivo 120 FPS en tier Alto en el
  partido en vivo; mínimo 60 FPS en Ultra y en repeticiones con efectos. Medido con timestamps de GPU.
- **Verdad:** posiciones, balón, contactos, goles y tiempos vienen de Core. El visor puede *interpolar*,
  *suavizar dentro de tolerancias* (pies plantados, IK de manos al lateral) y *ambientar* (público, clima),
  nunca inventar contacto o alterar trayectorias. Un test compara el resultado con y sin visor.
- **Autoría:** todo asset final se autoría en Blender o se genera por código propio. Se pueden usar
  herramientas y bases con licencia que nos deje la propiedad del resultado (CC0, MIT), registrando la
  procedencia: un CC0 transformado es "derivado de X (CC0)", no "100% nuestro".
  Nada de mallas, texturas, HDRIs, fotos de nubes o mocap de terceros como contenido final. Sin servicios
  generativos pagos. Los assets sin autoría acreditada (`stadium.glb`) salen del producto.
- **Sin backward compatibility:** se reemplazan contratos de assets y del renderer cuando conviene.
- **Comentarios explican el porqué; números de comportamiento visual en configuración validada**
  (`ViewerQualityConfig`), no sueltos en shaders.

## 4. Arquitectura de render

### 4.1 Forma del frame (Forward+ con MSAA)

Forward+ (clustered) en vez de deferred: MSAA 4× funciona nativo (líneas de cal y bordes de jugadores
limpios sin el borroneo de TAA sobre un balón chico y rápido), y la noche necesita decenas de luces.

```
compute:  skinning (22 jugadores + 4 oficiales, una vez por frame)
          crowd animation + culling → indirect draws
          culling GPU de estadio/props por instancia (frustum + Hi-Z del frame anterior) → indirect
          clustering de luces (froxels 16×9×24)
          wear/clima del césped (cada N frames)
shadow:   3 cascadas direccionales (sol) o atlas de focos (noche) + sombras de cápsula por jugador
prepass:  depth + normales (MSAA 4×)
GTAO:     media resolución, bilateral
opaque:   pitch → stadium → players → crowd → ball (HDR Rgba16f MSAA 4×, resolve)
transp:   redes, lluvia, partículas, halos de focos
post:     bloom (cadena de mips) → exposición auto suave → grading LUT 3D → tonemap → (repeticiones: DOF + motion blur)
hud:      composición existente
```

### 4.2 Lo que WebGPU nos da y vamos a usar

- **Compute shaders:** skinning (sale de 3 pasadas de vertex skinning a 1), animación del público por VAT,
  culling GPU, clustering de luces, desgaste del césped, partículas de clima, red del arco (masa-resorte).
- **Indirect draws + storage buffers:** todo lo instanciado (público, butacas, pasto, props) se dibuja
  con `drawIndexedIndirect` escrito por compute; la CPU no recorre instancias.
- **Render bundles:** pasadas estáticas grabadas una vez para que el envío desde F# cueste poco.
- **Timestamp queries:** cada pasada medida en GPU, visible en el HUD de diagnóstico y en los reportes.
- **Compresión BC7/BC5** para todas las texturas de estadio/jugadores (8 GiB alcanzan, pero el ancho de
  banda de la RX 5500 no sobra).
- Verificar en F0 que wgpu-native expone en esta GPU `timestamp-query`, `texture-compression-bc` e
  indirect-first-instance; si alguna falta, ese punto cae al camino sin la feature y queda registrado.

### 4.3 Refactor previo imprescindible

`WebGpuStadiumRenderer.fs` (2183 líneas) se parte en un **grafo de pasadas** simple: cada pasada declara
qué recursos lee/escribe y su presupuesto. Nada de framework genérico: una lista ordenada de módulos
(`ShadowPass`, `DepthPrepass`, `AoPass`, `OpaquePass`, `CrowdPass`, `PostChain`...) con recursos compartidos
en un `FrameResources`. Esto hace posible tiers de calidad (apagar/abaratar pasadas) y medir cada una.

### 4.4 Presupuesto de GPU, tier Alto, 1080p, RX 5500 (8,3 ms = 120 FPS)

| Pasada | ms |
|---|---|
| Compute (skinning, público, culling, luces) | 0,7 |
| Sombras (3 cascadas / atlas noche + cápsulas) | 1,1 |
| Prepass | 0,5 |
| GTAO media resolución | 0,5 |
| Opacos (césped + estadio + jugadores + balón) | 2,4 |
| Público | 1,0 |
| Pasto instanciado | 0,5 |
| Transparentes / clima | 0,3 |
| Post (bloom, grading, tonemap) | 0,6 |
| HUD + margen | 0,7 |

Tiers Bajo/Medio/Alto/Ultra cambian cascadas, densidad de público, AO y MSAA. Ultra suma sombras de
contacto y más densidad de pasto, con 60 FPS como piso.

## 5. Assets propios (Blender)

Todo sigue el ciclo de la skill `footballengine-blender`: contrato → bloqueo a escala → fuente editable →
texturas propias → export reproducible con hashes → validación en la app con capturas nativas.

| Asset | Técnica | Autoría |
|---|---|---|
| **Estadio por club** | Generador Geometry Nodes modular: tribunas por tramos (1–3 bandejas), techos (sin techo, voladizo, cerrado), torres de focos o anillo en techo, colores de butacas desde el kit, palcos, túnel, bancos. Parámetros = reputación, división, semilla del club. Export offline de 3 LOD + impostor | 100% propio, reemplaza `stadium.glb` |
| **Público** | Persona low-poly derivada de nuestro cuerpo a LOD bajo, 8 variantes de silueta, ropa por máscaras; animaciones propias (sentado, de pie, aplauso, salto en gol, manos a la cabeza, insulto al árbitro) horneadas a **vertex animation textures**; impostores a distancia | Propio (base corporal ya registrada) |
| **Jugadores** | ProAthlete se mantiene con procedencia honesta (base MakeHuman CC0). Se suman: variedad por shape keys desde altura/peso reales del jugador + semilla, tonos de piel, 12–16 peinados propios en *hair cards*, botines por color, 3 LOD | Derivado CC0 + trabajo propio, documentado |
| **Kits** | Textura generada en runtime desde `ClubKit` (patrón, colores) con dorsal y apellido; tipografía de números autorada por nosotros en Blender (no fuente de terceros) | Propio |
| **Oficiales** | Árbitro y asistentes sobre el mismo rig, con bandera; kit propio | Propio |
| **Balón** | Modelo y paneles propios, normal/rugosidad horneados; variante de invierno | Propio |
| **Arcos y red** | Malla propia; la red es un tejido masa-resorte en compute que se infla con el impacto autoritativo del gol | Propio |
| **Césped** | Material propio horneado en Blender (hojas, tierra, cal) que reemplaza ambientCG; patrones de corte por club | Propio, reemplaza CC0 |
| **Cartelería LED** | Paneles animados con contenido generado (nombre de clubes, marcador, auspiciantes ficticios propios) | Propio |
| **Cielos** | Además del día actual: atardecer, nublado y noche, mismo pipeline de cielo original | Propio |

Assets con procedencia ajena que quedan: base corporal MakeHuman (CC0, documentada). Salen: `stadium.glb`,
texturas ambientCG y Poly Haven.

## 6. Fases

Cada fase termina con: build y tests verdes, capturas nativas de las **escenas fijas** (§7) comparadas con
la fase anterior, tiempos por pasada con timestamps y un commit revisable. Ninguna fase se cierra con un
render lindo de Blender que el juego no reproduce.

| Fase | Contenido | Aceptación |
|---|---|---|
| **F0. Medir** | Timestamps por pasada, HUD de perfilado, `ViewerQualityConfig` validada, harness de escenas fijas reproducibles (semilla + segundo del partido + cámara), verificación de features wgpu en la RX 5500, línea base | Reporte de línea base con FPS y ms por pasada; las escenas salen idénticas en dos corridas |
| **F1. Imagen limpia** | Refactor en pasadas; MSAA 4×; 3 cascadas con PCF suave y fundido; GTAO; bloom; grading LUT; AA especular; BC7/BC5; filtrado anisotrópico | Capturas A/B de las 6 escenas; sin dientes en líneas de cal a 1080p; presupuesto de F1 cumplido |
| **F2. GPU-driven** | Skinning compute, culling GPU + indirect, render bundles, LOD de jugadores | Tiempo de CPU de envío < 1 ms; mismo resultado visual que F1 (diferencia de imagen bajo umbral) |
| **F3. Estadio propio** | Generador GN, variantes por club, focos, LED, túnel, bancos; borrado de `stadium.glb` | 3 clubes de reputación distinta generan estadios reconocibles; el juego carga el del local; procedencia completa |
| **F4. Hinchada** | VAT + instancias + impostores, colores por kit, reacción a eventos semánticos, densidad según reputación/importancia | 40k espectadores dentro del presupuesto; reacción visible y correcta en un gol real de la semilla de prueba |
| **F5. Jugadores y oficiales** | Variedad, kits con dorsal/nombre, pelo, oficiales, movimiento secundario de la tela, pies plantados dentro de tolerancia | Dos equipos con kits de patrón distinto legibles a cámara TV; ningún dorsal o apellido incorrecto (test contra el plantel) |
| **F6. Cancha viva y clima** | Desgaste por trayectorias reales, corte por club, lluvia/nieve en compute, césped y balón mojados, red del arco | El mapa de desgaste de un partido corresponde a las posiciones de Core (test); lluvia sin caída fuera de presupuesto |
| **F7. Noche** | Cielos atardecer/noche, focos clustered, sombras múltiples (atlas + cápsulas), halo volumétrico barato | Escena de noche con 4 sombras por jugador a 120 FPS en Alto |
| **F8. Dirección TV** | Director de cámaras por evento (TV principal, cerrada, detrás del arco, spidercam), repeticiones automáticas con DOF y motion blur, transición a gráficos | Una jugada de gol produce corte + repetición coherente; el reloj y el marcador nunca se adelantan a Core |

Orden recomendado: F0 → F1 → F2 son la base técnica (sin ellas el resto no entra en presupuesto).
F3 y F4 pueden ir en paralelo (Blender vs código). F7 depende de F1 (cascadas/clustered) y de F3 (focos).

## 7. Escenas fijas de validación

Seis escenas de una semilla de partido fija, siempre las mismas cámaras y segundos:

1. Saque inicial, cámara TV principal (densidad de estadio y público).
2. Córner, cámara elevada detrás del arco (sombras, AO, aglomeración de jugadores).
3. Primer plano de conducción (jugador, kit, dorsal, pelo, balón).
4. Gol con red y festejo de la hinchada.
5. Minuto 80 desde arriba (desgaste del césped).
6. Noche con lluvia (focos, sombras múltiples, césped mojado).

Comparación con FM: capturas de FM tomadas por el usuario con encuadres equivalentes, guardadas fuera del
repo como referencia interna. Nunca se copian assets ni se publican como prueba.

## 8. Cómo se implementa

- Yo diseño y escribo el F#/WGSL y valido cada fase (capturas nativas, perfil GPU, tests).
- El trabajo de Blender (estadio GN, público, pelo, oficiales, cielos) se hace con Blender MCP sobre
  escenas nuevas, sin tocar la escena viva del usuario; la skill permite subagentes **solo** para Blender,
  con especificación cerrada por asset.
- Tests: contrato de assets (procedencia, hashes, LOD, rig), determinismo del visor (con/sin visor),
  desgaste del césped contra trayectorias, kits/dorsales contra planteles, presupuesto por pasada en
  la máquina objetivo como evidencia registrada (no como test de CI).

## 9. Decisiones tomadas (8-11-2026)

1. **Calidad primero.** Se desarrolla en tier **Ultra** sin techo de FPS; después se optimiza y de Ultra se
   derivan Alto, Medio y Bajo. El presupuesto de §4.4 pasa a ser el objetivo del tier Alto, no un límite de
   desarrollo.
2. **La noche va antes.** Orden: F0 → F1 → F2 → F3 → **F7 (noche)** → F4 → F5 → F6 → F8.
3. **Estadio procedural según el club.** Un club grande (reputación alta, primera) tiene un estadio mayor y
   más sofisticado que uno de segunda, y ambos deben verse realistas.
4. **Jugadores ficticios** por ahora.
5. **Reparto:** el código (F#/WGSL) lo implementa Sonnet con especificaciones cerradas por unidad; los assets
   de Blender los autoría el agente principal, que además revisa cada unidad de código.

### 9.1 Estadio: kit modular + ensamblado en runtime

Generar un `.glb` por club no escala (miles de clubes × decenas de MB). El estadio se divide en:

- **Kit (Blender, offline):** piezas de alta calidad a escala real con materiales propios: butaca, baranda,
  vomitorio, escalera, frente de tribuna, cerchas y chapa de techo, torre de focos, barra de focos de techo,
  fachada exterior, palco, banco de suplentes, túnel, arco con red, banderín, cartel LED, pantalla, grúa de
  cámara. Cada pieza con LOD y puntos de anclaje en metadatos. Export: `Assets/MatchViewer/StadiumKit/`.
- **Ensamblado (F#, determinista):** `StadiumLayout.fromClub` (reputación, división, kit, semilla del club)
  decide forma (cuatro tribunas sueltas / bowl), bandejas (1–3), filas por bandeja, techo (ninguno,
  parcial, total), focos (torres / anillo en techo), capacidad y colores de butacas. Genera la geometría de
  gradas escalonadas y coloca piezas del kit e instancias de butacas; las posiciones de butaca son también
  los asientos del público (F4). Mismo club → mismo estadio.

## 10. Preguntas resueltas

1. **Objetivo de rendimiento:** ¿120 FPS en Alto (plan) o preferís 60 FPS con todo al máximo?
2. **Prioridad de la noche:** ¿F7 antes que F5/F6 para tener la imagen icónica antes?
3. **Alcance de estadios:** ¿un estadio por cada club del mundo (generado) o por club de primera división y
   genéricos por categoría para el resto?
4. **Caras:** jugadores ficticios con caras generadas por variedad; ¿confirmás que no queremos parecidos a
   jugadores reales?
