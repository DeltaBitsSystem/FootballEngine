# ProAthlete — jugador semi-realista reproducible

Reemplaza el cuerpo "Landmark-Cage" autorado por secciones numéricas (que tras más de 20 iteraciones seguía viéndose
no humano) por una base anatómica paramétrica, manteniendo intacto el contrato de runtime
(`CharacterAsset`, `RigDefinition.conform`, atlas 4×4, schema 3 / rig 2, 18 clips).

## Reproducir

Requisitos: Blender 5.2 LTS con la extensión **MPFB 2.0.x** (GPL; sólo herramienta) y el pack
`makehuman_system_assets_cc0.zip` (CC0, SHA-256 `b542127a8e25547c7c29c19f2d1d2adb9a664c80396ecd694095dbc8028a0107`,
de https://files.makehumancommunity.org/asset_packs/makehuman_system_assets/) extraído en
`~/.config/blender/5.2/extensions/.user/blender_org/mpfb/data/`. Python 3 con NumPy y Pillow.

```sh
# desde la raíz del repo; FE_ASSET_ROOT apunta al checkout que contiene la carpeta de staging (ignorada por git)
FE_ASSET_ROOT=$PWD sh Tools/MatchViewer3D/ProAthlete/build_all.sh          # body → kit → uv → paint → pack
FE_ASSET_ROOT=$PWD python3 Tools/MatchViewer3D/ProAthlete/promote.py        # instala en Assets/MatchViewer con backup
```

Todo corre en procesos Blender aislados; nunca abre ni modifica la escena interactiva. La salida queda en
`FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Pro-Athlete/`; `promote.py` verifica hashes del manifiesto
y copia los `player*` vivos a `PlayerBodyRework/Live-Backup-<fecha>/` antes de reemplazarlos.

| Paso | Script | Qué hace |
|---|---|---|
| body | `build_body.py` | Humano MPFB (macros en `athlete.json`), rig `game_engine` de MPFB mapeado a los 60 huesos de runtime, pesos con correctivos de media rotación, muslo/pierna exactos al contrato, pies bajo la cadera, máscaras anatómicas y landmarks. |
| kit | `build_kit.py` | Short, camiseta (cortes planos, escote elíptico con tira de cuello, caída sin relieve muscular), medias, botines con suela plana y punta definida, pelo con línea de nacimiento anatómica; oculta piel cubierta y hunde el cuero cabelludo. |
| uv | `build_uv.py` | UV por tile del atlas; la cara recibe 3× densidad, manos 1,5×; exporta `surface-sample.npz`. |
| paint | `paint_atlas.py` | Pinta albedo y normal/rugosidad 4096 desde posiciones 3D: piel, rubor, barba, labios, cejas, pestañas, poros, uñas, iris, pelo, telas, escudo, franjas, botines; local y visitante. |
| pack | `build_pack.py` | 18 clips de `player_authoring.py` (sin offsets heredados), GLB (1 skin, 60 joints), manifiesto schema 3, mips y `rigGeometry`. |

`review.py` + `contact_sheet.py` generan vistas de revisión (reposo, cara, poses de estrés, clips). Son previews
EEVEE, no evidencia de runtime.

## Procedencia

- **Base anatómica y rig de referencia:** MakeHuman/MPFB, CC0 (sin atribución obligatoria). La malla del cuerpo, sus
  pesos `game_engine` y los ojos low-poly derivan de esos assets; se registra honestamente como derivado CC0.
- **Original del proyecto:** mapeo al rig de 60 huesos y correctivos, toda la ropa, botines y pelo (generados desde
  la superficie del cuerpo), todos los píxeles de las texturas (procedurales en `paint_atlas.py`, sin fotos ni
  texturas de MakeHuman), escudo, colores, y los clips (generador del proyecto).
- No se usan texturas, pelo, ropa ni animaciones de MakeHuman en el paquete final.

## Físico

`athlete.json` usa macros `muscle 0.92 / weight 0.52` más targets regionales de MPFB con predominio de piernas
(cuádriceps 0.70, gemelos 0.60, deltoides 0.50, brazos 0.45, dorsal 0.40, cuello 0.40, pectoral 0.35…): atlético
marcado, no fisicoculturista. Para estudiar otros físicos sin tocar el paquete vivo:
`FE_ATHLETE_PARAMS=<json> FE_ATHLETE_OUT=Pro-Athlete-X` compila en otra carpeta de staging.
El escote se dimensiona con el cuello medido y el dobladillo con la entrepierna medida, así cualquier físico conserva
la camiseta; la anatomía genital se suaviza (siempre bajo el short). `build_kit.py` falla si alguna prenda tiene una
arista ≥ 10 cm (púas de Solidify en pliegues; se limitan con `thickness_clamp`).

## Medidas (paquete actual)

- 1,80 m (+12 mm de suela); muslo 0,44026 m, pierna 0,43526 m (contrato 0,4402556 / 0,4352585).
- ≈36k triángulos en total (piel ≈17k tras ocultar piel cubierta). GLB 2,2 MB (sin imágenes embebidas).
- Probe nativo WebGPU, misma cámara/frame: primer plano 167 → 201 FPS, transmisión 188 → 212 FPS (lecturas del
  overlay, no benchmark).
- Client tests: 200/202 con los tests actuales; 202/202 aplicando `client-tests.patch` (ver abajo). Mutación de
  socket +1 cm detectada por el test actualizado.

## Tests que cambian (`client-tests.patch`)

Dos tests fijaban constantes del botín anterior (suela a 0,005 m, huella en z=0,0875, punta en z=0,275 y una franja
de X). El contrato que declaran es "los sockets salen de la geometría del botín vivo"; el parche los compara contra un
oráculo independiente (vértices más bajos y punta más adelantada de cada pie en el GLB), no contra el manifiesto.
Se generó contra el estado sin commitear de esos tests: `git apply Tools/MatchViewer3D/ProAthlete/client-tests.patch`.

## Límites conocidos

- Semi-realista: cara sin blendshapes ni dientes visibles; cejas y pestañas pintadas; pelo como casco esculpido.
- La piel entera comparte un tile de 1024² (cara ≈ 450 px); los clips son los procedurales existentes.
- No se auditó la interpenetración de ropa en todos los clips ni el arquero con brazos extremos.

## Camino a realismo máximo

1. Atlas: tile dedicado de cara (o 8192) — `paint_atlas.py` pinta desde 3D, sólo cambia la resolución/UV.
2. Pelo y cejas por tarjetas: el shader ya tiene MSAA 4×; activar alpha-to-coverage en el pipeline del atleta.
3. Detalle de piel: subir `vnoise` de poros y añadir arrugas por máscaras; normal map desde fuente MPFB subdividida.
4. Blendshapes faciales desde los targets de expresión de MPFB (CC0) cuando el runtime soporte morphs.
