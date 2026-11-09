# A0 — Línea base de animación sobre juego real

Fase A0 de `docs/plans/ANIMATION_AND_BALL_PLAN.md`. Mide la animación procedural actual sobre movimiento real de
partido. Los números de esta página son el punto de partida contra el que se acepta A2.

## Cómo se reproduce

```bash
dotnet build Tools/MatchViewer3D/StadiumRendererProbe.fsproj -c Release
dotnet Tools/MatchViewer3D/bin/Release/net10.0/StadiumRendererProbe.dll --motion-corpus docs/evidence/motion-corpus/baseline.json
```

`Tools/MatchViewer3D/MotionCorpus.fs`:

- Simula la semilla 1 (contexto por defecto de los tests) durante el primer tiempo, dos veces.
  - La primera pasada elige una ventana de ±1,5 s por familia. Toma el primer caso después del minuto 1, sin
    solapes entre ventanas.
  - La segunda pasada ingiere cada `MotionBoundary` en `MotionRuntime` y evalúa poses a 60 Hz dentro de cada ventana.
- Es determinista: misma semilla, mismas ventanas, mismo informe.
- Sale con código 2 si alguna familia no aparece.

Métricas por ventana:

| Métrica | Definición |
|---|---|
| Patinaje | Deriva horizontal de una suela en apoyo entre frames de 60 Hz, sumada cuando supera 0,5 m/s |
| Pops | Frames en que alguna articulación supera 400 m/s². Un pie en sprint llega a ~250 m/s²; 400 m/s² son ~11 cm de desvío de su propia trayectoria en un frame |
| Error de contacto | Distancia entre el socket de pelota de la bota y el objetivo del overlay en su frame de contacto |
| Residuales | Apoyos o contactos que la IK declaró inalcanzables |

## Línea base (2026-11-09, Core con B1)

| Familia | Actor | Ticks | Patinaje (cm) | Vel. máx. suela (m/s) | Acel. máx. (m/s²) | Pops | Contactos | Error contacto (cm) | Residuales |
|---|---|---|---|---|---|---|---|---|---|
| Start | 111 | 19880–20000 | 52 | 3.72 | 771 | 30 | 0 | 0 | 20 |
| Stop | 103 | 3174–3294 | 5 | 0.91 | 883 | 14 | 0 | 0 | 32 |
| Turn | 7 | 2373–2493 | 0 | 0.45 | 670 | 9 | 0 | 0 | 4 |
| Sprint | 105 | 2494–2614 | 45 | 3.02 | 1712 | 88 | 0 | 0 | 40 |
| Dribble | 102 | 4220–4340 | 4 | 1.2 | 1990 | 17 | 1 | 7 | 14 |
| Pass | 101 | 3684–3804 | 3 | 0.97 | 1549 | 10 | 3 | 59 | 27 |
| Shot | 9 | 2842–2962 | 0 | 0 | 3176 | 39 | 3 | 154 | 25 |
| Reception | 102 | 4040–4160 | 13 | 3.16 | 2173 | 55 | 2 | 237 | 22 |
| Tackle | 105 | 2666–2786 | 71 | 5.31 | 2730 | 92 | 4 | 213 | 70 |
| Goalkeeper | 1 | 50203–50323 | 2 | 0.81 | 864 | 6 | 4 | 51 | 30 |

Totales: 1,95 m de patinaje y 360 pops en 1 800 frames (20 %).

## Lectura

- **Contactos.** Con el alcance de pierna de B1 (0,7 m), Core ya contacta donde está la pelota. El cuerpo
  procedural no llega: hay errores de 0,5 a 2,4 m en tiro, recepción y tackle. Es el defecto visual más grande y
  lo que A4 debe resolver con acciones de clip, time-warp e IK hasta el punto autoritativo.
- **Pops y patinaje** se concentran en sprint, tackle y recepción, donde la raíz de Core cambia de velocidad
  bruscamente. Es la clase de defecto que ataca A2 (motion matching e inercialización).
- **Familias ausentes.** En un tiempo completo Core no produce evidencia de cabezazo ni de salto; las raíces nunca
  se despegan del suelo. El arquero sólo aparece porque B1 emite `KeeperDive`. Son huecos de Core, no del medidor:
  van a B2/A5.

## Límites honestos

- La raíz se interpola linealmente entre boundaries de Core. El visor real interpola snapshots de replay, así que
  los pops de raíz pueden diferir un poco de los de pantalla.
- Falta el video cercano a 60 Hz. El harness actual renderiza jugadores sintéticos, no un partido. Conectar la
  sesión real al harness es parte de A2, donde se compara antes contra después.
- Los umbrales sirven para comparar entre versiones; no son una vara artística. La aceptación de A2 se fija contra
  estos números (menos patinaje y menos pops en la misma semilla), no contra cero.
