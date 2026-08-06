# Diagnóstico: Por qué el balanceo está roto

## Resumen Ejecutivo

El motor tiene **7 problemas estructurales** que causan los 0-0, la falta de pases, y la fragilidad al tocar cualquier cosa. No es un problema de "pesos mal calibration" — es un problema de **arquitectura de las fórmulas**.

---

## Problema 1: Normalización destruida en shootScore

**Archivo:** `PlayerScorer.fs:162-175`

```fsharp
let maxPossible =
    d.ShootFinishingWeight      // 0.35
    + d.ShootLongShotsWeight    // 0.15
    + d.ShootComposureWeight    // 0.20
    + d.ShootDistNormWeight     // 0.20
    + d.ShootPosDirectnessWeight // 0.10
    + d.ShootPosDepthWeight     // 0.08
    + d.ShootSTBonus            // 0.20
    + d.ShootDecisionsWeight    // 0.15
    + d.ShootBraveryMod         // 0.12
    + d.ShootConcentrationMod   // 0.08
    + d.ShootXGWeight           // 0.20
    + d.ShootBraveryPressureMod // 0.20
    + 1.0                       // ← AQUÍ ESTÁ EL BUG
```

El `+ 1.0` infla el denominador en **50%**. Un striker elite en posición de gol (scoreRaw ≈ 1.0) obtiene:

| Sin `+1.0` | Con `+1.0` |
|-------------|------------|
| 1.0 / 2.03 = **0.49** | 1.0 / 3.03 = **0.33** |

El threshold de shoot es 0.30. Con el `+1.0`, un striker elite barely pasa el threshold. Un MC promedio no lo pasa nunca.

**Impacto:** Los tiros se reducen drásticamente. Los mediocampistas nunca tiran.

---

## Problema 2: maxPossible de passScore usa valores negativos

**Archivo:** `PlayerScorer.fs:322-338`

```fsharp
let maxPossible =
    ...
    + tempoBias       // NEGATIVO: t.Tempo * (-0.03)
    + abs directBias  // POSITIVO pero variable
    ...
```

`tempoBias` es negativo (`t.Tempo * iw.Pass.AttackPhasePenalty` = `0.5 * -0.03` = `-0.015`). Está siendo restado del denominador, lo que **infla** el score normalizado artificialmente.

Pero en el cálculo del score real, `tempoBias` también es negativo y reduce el numerador. El efecto neto es impredecible y depende de la táctica.

**Impacto:** Los pases tienen scores inflados artificialmente, lo que debería significar que se pasan más. Pero el problema real es que la normalización no es lineal — ciertas combinaciones de tácticas producen scores absurdos.

---

## Problema 3: IndividualWeights hardcodeado a defaultConfig

**Archivo:** `PlayerScorer.fs:77, 244, 369, 444`

```fsharp
let iw = BalanceConfig.defaultConfig.Individual  // SIEMPRE default
```

Aparece **4 veces** en las 4 fórmulas de scoring (shoot, pass, dribble, longBall). Esto significa que:

- Los pesos entrenados en `weights_trained.json` **NUNCA se aplican** al scoring individual
- El pipeline de entrenamiento calibra pesos que luego son ignorados
- Si cambias `Individual.Shoot.FinishingWeight` en el config entrenado, no tiene efecto

**Impacto:** El 50% del trabajo de calibración se pierde. Los pesos entrenados son ignorados.

---

## Problema 4: Colectivo no afecta decisiones individuales con balón

**Flujo actual:**
```
CollectiveModifiers → MovementScores (pressBall, coverSpace, supportAttack)
                    → NO afecta ActionScores (shoot, pass, dribble)
```

Los modificadores colectivos (urgencia, debilidad, rest defense, opponent shape) solo afectan el **movimiento sin balón**. Cuando un jugador tiene el balón, la decisión de tirar/pasar/driblar es **100% individual**.

**Lo que debería pasar:**
- Un equipo con pressing alto debería tirar más (urgency)
- Un equipo con posesión debería pasar más
- Un equipo en transición debería driblear más
- Un equipo con bajo bloque debería tirar de larga distancia

**Impacto:** Las tácticas no afectan el juego ofensivo. Todos los equipos juegan igual con el balón.

---

## Problema 5: Shot Quality Gate demasiado restrictivo

**Archivo:** `ShotAction.fs:85`

```fsharp
if quality < sc.QualityGate then  // QualityGate = 0.35
    // ShotOffTarget - gol automático fallido
```

Con `NormalisationDistance = 30m`:

| Distancia | Quality | ¿Pasa gate? |
|-----------|---------|-------------|
| 10m | 0.567 | Sí |
| 15m | 0.450 | Sí |
| 20m | 0.333 | **NO** |
| 25m | 0.217 | **NO** |

**Todos los tiros desde 20m+ son automáticamente off-target.** En el fútbol real, los goles desde 20-25m son comunes (~15% de los goles).

**Impacto:** Los goles de larga distancia son imposibles. Solo se puede meter gol desde dentro del área.

---

## Problema 6: xG hardcoded a PlacedShot

**Archivo:** `PlayerScorer.fs:135`

```fsharp
let xgModel =
    { ...
      ShotType = ShotType.PlacedShot  // HARDCODEADO
      BodyPart = "foot"               // HARDCODEADO
      ... }
```

El modelo xG en el scoring siempre asume `PlacedShot`. Pero el jugador podría estar tirando de cabeza, de volea, o de primera. El xG real de un tiro de cabeza es ~0.7x el de un tiro colocado.

**Impacto:** El bonus de xG en la decisión de tiro es inexacto. Los tiros de cabeza reciben demasiado peso.

---

## Problema 7: fragilidad — tocar una cosa rompe otra

El problema de fragilidad viene de que **las fórmulas están acopladas por implícito**:

1. `PlayerScorer` usa `BalanceConfig.defaultConfig.Individual` (hardcoded)
2. `PlayerDecision` usa los scores normalizados de `PlayerScorer`
3. `ShotAction` tiene su propio cálculo de calidad separado del scoring
4. `xGCalculator` se llama desde 3 lugares con diferentes parámetros
5. `MovementScorer` tiene sus propias constantes hardcoded (12m, 25m, 45m)
6. Los umbrales en `PlayerDecision` dependen de los outputs de `PlayerScorer`

Cuando calibras un peso en `BalanceConfig`:
- Cambia el score en `PlayerScorer`
- Pero `ShotAction` tiene su propio gate que no depende de ese peso
- Y `PlayerDecision` tiene thresholds que podrían bloquear el tiro aunque el score suba
- Y `MovementScorer` tiene constantes que no cambian

**No hay un flujo de datos claro.** Las fórmulas están dispersas y cada una hace su propia cosa.

---

## Cadena de Causa-Efecto del 0-0

```
1. shootScore normalizado muy bajo (problema 1)
   ↓
2. shootThreshold = 0.30 barely superado por striker elite
   ↓
3. MC/AM nunca superan threshold → nunca tiran
   ↓
4. Shot Quality Gate bloquea tiros desde 20m+ (problema 5)
   ↓
5. Solo tiros desde <15m pasan el gate
   ↓
6. Para llegar a <15m necesitás pasar o driblear
   ↓
7. Pass score inflado artificialmente (problema 2) → pases sí se hacen
   ↓
8. Pero el xG de un tiro desde 15m es bajo (~0.15)
   ↓
9. GK save probability es alta para tiros predecibles
   ↓
10. Resultado: 0-0
```

## Cadena de Causa-Efecto de "no pasan la pelota"

```
1. Colectivo no afecta on-ball decision (problema 4)
   ↓
2. Todos los jugadores deciden individualmente
   ↓
3. Softmax temperature = 0.25 (razonable)
   ↓
4. Pero los scores están tan comprimidos (normalización rota)
   ↓
5. Las diferencias entre shoot/pass/dribble son mínimas
   ↓
6. Softmax se vuelve casi aleatorio
   ↓
7. Resultado: patean porque shoot tiene slight bias por urgency bonus
```

---

## Por qué tocar una cosa rompe otra

El sistema tiene **3 capas de gating** que interactúan de forma no obvia:

```
Capa 1: PlayerScorer (normalización rota)
   ↓ score [0, 1]
Capa 2: PlayerDecision (thresholds dependen del output de Capa 1)
   ↓ blocked/unblocked
Capa 3: ShotAction/PassAction (quality gates independientes)
   ↓ éxito/fallo
```

Cada capa tiene sus propias constantes y fórmulas. Cuando calibras un peso en Capa 1:
- El score cambia
- Los thresholds de Capa 2 podrían bloquear/desbloquear acciones
- Los gates de Capa 3 no cambian

Esto crea efectos no lineales e impredecibles. Un cambio de +0.05 en `ShootFinishingWeight` podría:
- No tener efecto (threshold ya se pasaba)
- Habilitar tiros que antes estaban bloqueados
- O cambiar el ranking en softmax de forma caótica

---

## Plan de Corrección

Ver `FormulaAST-Plan-v2.md` para el plan de arquitectura correcto.
