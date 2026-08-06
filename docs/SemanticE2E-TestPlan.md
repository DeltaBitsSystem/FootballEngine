# Plan de Tests Semánticos E2E (v2)

## Estado del motor post-balanceo

El benchmark (`FootballEngine.Benchmarks`) produce **0-0 siempre** en ~1.7s/partido.
Esto confirma que los fixes de Fase 1 no alcanzan por sí solos — el problema
también está en los stats generados, la inicialización, o la cadena de decisiones
completa. Este plan construye la capa de tests que detecta exactamente dónde se rompe.

---

## Performance (medida real)

| Métrica | Valor |
|---------|-------|
| Partido completo (228k ticks) | **~1700ms** |
| Memoria por partido | ~40-53 MB |
| Framework | Expecto 10.2.3 |
| Constraint de la suite | **< 10 segundos total** |

### Estrategia

- **Nivel 1** (unitarios de decisión): < 1ms c/u. 5 tests = ~5ms
- **Nivel 2** (colectivos, 2000-5000 ticks): ~100-400ms c/u. 4 tests = ~1.5s
- **Nivel 3** (partidos completos): ~1.7s c/u. Ya usa `Array.Parallel.init` en
  `StatisticalContractsTests.fs` — 20 partidos en paralelo = ~1.7s. Agregar 4
  escenarios nuevos a ese test = ~1.7s más total.

**Total estimado: ~3.5s** — bien dentro del constraint.

### Seed determinista

`Stats.setSeed(int)` / `Stats.clearSeed()` en `Engine/Stats.fs`.
Útil para debug, pero los tests de nivel 3 deben pasar con CUALQUIER seed.

---

## Archivos a crear/modificar

| Archivo | Acción | Qué contiene |
|---------|--------|--------------|
| `Tests/Layer2_Systems/SemanticE2ETests.fs` | **NUEVO** | Tests de nivel 1 y 2 |
| `Tests/Layer2_Systems/StatisticalContractsTests.fs` | **EXTENDER** | Tests de nivel 3 (nuevos escenarios) |
| `Tests/FootballEngine.Tests.fsproj` | **MODIFICAR** | Agregar SemanticE2ETests.fs |

**Nivel 3 va en StatisticalContractsTests.fs** porque ya tiene la infraestructura:
`loadGame()`, `MatchSimulator.trySimulateMatchFull`, conteo manual de eventos,
`Array.Parallel.init`, assertions con mensajes descriptivos. No se duplica.

**Nivel 1 y 2 van en archivo nuevo** porque usan `buildStandardMatch()` y
`advanceTicks` (no necesitan base de datos) y tests de DecisionTrace.

---

## Helpers a crear en SemanticE2ETests.fs

### `countMatchEvents` — reemplaza `MatchMetrics.extract` (que no existe)

```fsharp
type MatchStats =
    { mutable Goals: int
      mutable Shots: int
      mutable ShotsOnTarget: int
      mutable PassCompleted: int
      mutable PassTotal: int
      mutable Corners: int
      mutable Fouls: int
      mutable LongBalls: int
      mutable DribbleSuccess: int
      mutable TackleSuccess: int }

let countMatchEvents (events: MatchEvent list) (clubId: ClubId) : MatchStats =
    let stats = { Goals = 0; Shots = 0; ShotsOnTarget = 0; PassCompleted = 0
                  PassTotal = 0; Corners = 0; Fouls = 0; LongBalls = 0
                  DribbleSuccess = 0; TackleSuccess = 0 }
    for e in events do
        if e.ClubId = clubId then
            match e.Type with
            | Goal -> stats.Goals <- stats.Goals + 1
            | ShotLaunched | ShotOnTarget | ShotOffTarget | ShotBlocked ->
                stats.Shots <- stats.Shots + 1
                if e.Type = ShotOnTarget then stats.ShotsOnTarget <- stats.ShotsOnTarget + 1
            | PassCompleted _ -> stats.PassCompleted <- stats.PassCompleted + 1; stats.PassTotal <- stats.PassTotal + 1
            | PassMisplaced _ | PassIntercepted _ | PassDeflected _ -> stats.PassTotal <- stats.PassTotal + 1
            | Corner -> stats.Corners <- stats.Corners + 1
            | FoulCommitted -> stats.Fouls <- stats.Fouls + 1
            | LongBall _ -> stats.LongBalls <- stats.LongBalls + 1
            | DribbleSuccess -> stats.DribbleSuccess <- stats.DribbleSuccess + 1
            | TackleSuccess -> stats.TackleSuccess <- stats.TackleSuccess + 1
            | _ -> ()
    stats
```

### `makeTestLineup` — alineación con stats uniformes

```fsharp
let makeTestLineup (baseSkill: int) : Player[] * (float * float)[] =
    let gk = makeGk 1 (baseSkill + 2) (baseSkill + 2) baseSkill
    let players = [| 2 .. 11 |] |> Array.mapi (fun i pid ->
        let pos = [| DL; DC; DC; DR; DM; MC; MC; AML; AMC; ST |].[i]
        makePlayer pid pos baseSkill)
    Array.append [| gk |] players, standardHomePositions ()
```

### `makeConfiguredMatch` — match con config customizada

```fsharp
let makeConfiguredMatch (homeSkill: int) (awaySkill: int) =
    let hPlayers, hPos = makeTestLineup homeSkill
    let aPlayers, aPos = makeTestLineup awaySkill
    let ctx, state = buildSimState hPlayers hPos aPlayers aPos
    ctx, state
```

---

## Nivel 1 — Invariantes individuales (DecisionTrace)

Van en `SemanticE2ETests.fs`. Cada test activa `Diagnostics.TraceDecisions = true`,
corre 1 tick, y lee el trace devuelto por `PlayerDecision.decide`.

**Nota:** `PlayerDecision.decide` devuelve `OnBallIntent option * DecisionTrace option`.
El caller en `ActionResolver.fs` actualmente descarta el trace con `_trace`. Para
estos tests hay que llamar a `PlayerDecision.decide` directamente (no a través de
ActionResolver) o modificar ActionResolver para coleccionar traces.

**Approach propuesto:** Llamar directamente a `PlayerDecision.decide` con un
`AgentContext` construido manualmente (como ya hace `PlayerScorerTests.fs`).

### Escenario: ST solo ante el arco desde 12m elige Shoot

**Nivel:** 1
**Qué verifica:** Un delantero elite en posición de gol toma la decisión correcta.
**Por qué:** Si un ST con skill 20 a 12m del arco no elige tirar, el motor está roto.
**Setup:** Construir `AgentContext` con ST en zona de ataque, `DistToGoal = 12m`,
`Zone = AttackingZone`, ball controlled. Activar trace.
**Assertions:**
- `trace.ChosenAction = "Shoot"`
- `trace.Shoot.FinalScore > 0.5`
- `trace.Shoot.Blocked = false`
**Señal de falla:** shootScore mal calibrado o QualityGate bloquea tiros desde cerca.

### Escenario: Jugador en zona defensiva con presión alta no elige Dribble

**Nivel:** 1
**Qué verifica:** Un defensor bajo presión no intenta regatear.
**Por qué:** Regatear en tu propio campo con presión es mal fútbol.
**Setup:** `AgentContext` con DC en `DefensiveZone`, `ImmediatePressure > 0.7`.
**Assertions:**
- `trace.ChosenAction <> "Dribble"`
- `trace.Dribble.Blocked = true`
**Señal de falla:** Modificadores de zona no aplican.

### Escenario: Jugador con mejor opción de pase elige Pass sobre Shoot

**Nivel:** 1
**Qué verifica:** Si hay un compañero mejor posicionado, el jugador pasa.
**Por qué:** Un jugador egoísta que tira cuando hay un teammate solo es mal fútbol.
**Setup:** `AgentContext` con `BestPassTargetIdx = ValueSome 8` (AM a 8m del arco),
ST a 25m.
**Assertions:**
- `trace.ChosenAction = "Pass"`
- `trace.Pass.FinalScore > trace.Shoot.FinalScore`
**Señal de falla:** passScore sub-calibrado.

### Escenario: GK con balón elige distribución (no Shoot)

**Nivel:** 1
**Qué verifica:** El portero no tira a puerta con el balón en las manos.
**Por qué:** Ningún GK tira a puerta desde sus manos.
**Setup:** `AgentContext` con GK, `BallState.Control = Controlled(HomeClub, gkId)`.
**Assertions:**
- `trace.ChosenAction <> "Shoot"`
**Señal de falla:** Bug crítico en ActionSystem — GK no debería llegar a PlayerDecision.

### Escenario: MatchSituation ShootMod visible perdiendo al min 80

**Nivel:** 1
**Qué verifica:** El modificador situacional afecta las decisiones.
**Por qué:** Equipo perdiendo 0-1 al min 80 debe tener urgencia de tiro.
**Setup:** `buildStandardMatch`, setear score 0-1, minuto 80, ball en zona de ataque.
Llamar `MatchSituationBuilder.compute HomeClub state clock` y leer el resultado.
**Assertions:**
- `sit.IsLosingLate = true`
- `sit.ShootMod > 1.1`
**Señal de falla:** MatchSituationBuilder no computa bien.

---

## Nivel 2 — Invariantes colectivos

Van en `SemanticE2ETests.fs`. Usan `buildStandardMatch()` + `advanceTicks`.

### Escenario: DirectPlay genera más tiros que pases en zona de ataque

**Nivel:** 2
**Qué verifica:** DirectPlay tira más de lo que pasa.
**Por qué:** DirectPlay = pelota rápida al frente, menos posesión.
**Setup:**
```fsharp
let ctx, state = buildStandardMatch ()
SimStateOps.setTactics state HomeClub TeamTactics.DirectPlay
state.Flow <- MatchFlow.Live
let _, events = advanceTicks 5000 ctx state
let homeEvents = events |> List.filter (fun e -> e.ClubId = ctx.Home.Id)
```
**Assertions:**
- `shotCount / passCount > 0.3` (DirectPlay), vs `~0.15` (Balanced)
**Señal de falla:** Tácticas no afectan scoring.

### Escenario: MatchSituation diferencia ganando vs perdiendo

**Nivel:** 2
**Qué verifica:** Los modificadores situacionales son diferentes según el marcador.
**Por qué:** Ganando 2-0 = más pases, perdiendo 0-2 = más tiros.
**Setup:**
```fsharp
let ctx, state = buildStandardMatch ()
let clock = SimulationClock.defaultClock
let stateA = // copia con score 2-0
let sitA = MatchSituationBuilder.compute HomeClub ctx stateA clock
let stateB = // copia con score 0-2
let sitB = MatchSituationBuilder.compute HomeClub ctx stateB clock
```
**Assertions:**
- `sitA.PassMod > sitB.PassMod`
- `sitB.ShootMod > sitA.ShootMod`
**Señal de falla:** MatchSituationBuilder no diferencia estados.

### Escenario: Posesión mayor genera más pases completados

**Nivel:** 2
**Qué verifica:** Stats más altos = más pases precisos.
**Por qué:** Skill difference debería traducirse en diferencia de pases.
**Setup:**
```fsharp
let ctx, state = makeConfiguredMatch 16 10
state.Flow <- MatchFlow.Live
let _, events = advanceTicks 5000 ctx state
```
**Assertions:**
- Tasa de pases del team 16 > tasa del team 10
**Señal de falla:** Stats de Passing no afectan Enough.

### Escenario: Equipo con presión recupera balón en campo rival

**Nivel:** 2
**Qué verifica:** HighPress produce recuperaciones en campo rival.
**Por qué:** Gegenpressing = recuperar rápido, no retroceder.
**Setup:**
```fsharp
let ctx, state = buildStandardMatch ()
SimStateOps.setTactics state HomeClub TeamTactics.Pressing
state.Flow <- MatchFlow.Live
let _, events = advanceTicks 5000 ctx state
```
**Assertions:**
- Contar `TackleSuccess` en campo rival (ball X > 52.5 para home)
- Debe ser mayor que tackles en campo propio
**Señal de falla:** Pressing no afecta comportamiento defensivo.

---

## Nivel 3 — E2E de partido completo

**Se agregan 4 escenarios NUEVOS a `StatisticalContractsTests.fs`.**
Ya existe el test base con 20 partidos, conteo de eventos, y assertions.

### Patrón de conteo (reutilizar el existente)

El test ya tiene variables mutables para todos los conteos. Los nuevos escenarios
replican el mismo patrón pero con diferentes configs de entrada.

### Nuevo escenario 1: Dominador vs dominado

**Nivel:** 3
**Qué verifica:** Equipo élite gana la mayoría vs equipo débil.
**Por qué:** En fútbol real, Barcelona vs tercera gana > 70%.
**Setup:** 20 partidos con `Array.Parallel.init`, home=clubs[0] (élite), away=clubs[1] (débil).
Usar los clubs generados por `loadGame()` — si los primeros 2 clubs son similares,
forzar una diferencia con `makeConfiguredMatch 18 9` (requiere modificar el setup).
**Assertions:**
| Métrica | Rango | Por qué |
|---------|-------|---------|
| Victorias home | >= 12/20 (60%) | Debería ganar la mayoría |
| Goles home promedio | > 1.5 | Dominio ofensivo |
| Goles away promedio | < 1.5 | Poca capacidad ofensiva |
**Señal de falla:** Stats no afectan resultado.

### Nuevo escenario 2: Tácticas extremas — DirectPlay vs Possession

**Nivel:** 3
**Qué verifica:** Estilos opuestos producen patrones diferentes.
**Por qué:** DirectPlay = menos pases, más tiros; Possession = lo contrario.
**Setup:** 20 partidos. Antes de cada uno, `SimStateOps.setTactics`.
**Assertions:**
| Métrica | Rango | Por qué |
|---------|-------|---------|
| Pases home (Direct) | < pases away (Possession) | Estilos opuestos |
| LongBalls home | > LongBalls away | DirectPlay usa pelotas largas |
**Señal de falla:** Tácticas no afectan juego.

### Nuevo escenario 3: Partido de presión — perdiendo al min 70

**Nivel:** 3
**Qué verifica:** Equipo perdiendo genera más ofensiva al final.
**Por qué:** Presión tardía es patrón real de fútbol.
**Setup:** Correr 70 min, forzar marcador 0-1, correr 20 min más.
**BUG CORREGIDO:** Usar `midState` (el resultado de advanceTicks 100800), no `state`:
```fsharp
let ctx, state = makeConfiguredMatch 13 13
state.Flow <- MatchFlow.Live
let midState, _ = advanceTicks 100800 ctx state  // 70 min
midState.HomeScore <- 0
midState.AwayScore <- 1
midState.HalfTimeHandled <- true
let finalState, _ = advanceTicks 33600 ctx midState  // ← midState, no state
```
**Assertions:**
| Métrica | Rango | Por qué |
|---------|-------|---------|
| Tiros home últimos 20 min | >= 3 | Presión ofensiva |
| Tiros home últimos 20 min | > tiros home primeros 70 min / 3 | Más ofensiva al final |
**Señal de falla:** MatchSituation ShootMod no funciona.

### Nuevo escenario 4: Portero débil encaja más goles

**Nivel:** 3
**Qué verifica:** GK débil = más goles encajados.
**Por qué:** Calidad del portero es determinante.
**Setup:** 20 partidos con `makeConfiguredMatch` usando GK débil (skill 8) vs GK élite (skill 16).
**Assertions:**
| Métrica | Rango | Por qué |
|---------|-------|---------|
| Goles encajados GK débil | > goles GK élite | GK importa |
| Diferencia promedio | >= 0.3 goles/partido | GK decente evita goles |
**Señal de falla:** GK save logic no depende de stats.

---

## Señales de falla por categoría

| Si falla... | Significa... | Dónde buscar |
|-------------|--------------|--------------|
| Nivel 1: ST no tira desde área | shootScore mal calibrado | PlayerScorer.fs |
| Nivel 1: GK tira a puerta | Falta check en ActionSystem | ActionResolver.fs |
| Nivel 1: ShootMod = 1.0 perdiendo | MatchSituation no computa | MatchSituationBuilder.fs |
| Nivel 2: DirectPlay = Balanced | Tácticas no afectan scoring | PlayerScorer / CollectiveModifiers |
| Nivel 2: Presión no aumenta | Urgency no propaga | MatchSituation / PlayerDecision |
| Nivel 3: 0-0 siempre | Bug sistémico en cadena | QualityGate, shootScore, passScore |
| Nivel 3: Goles < 1 promedio | Tiros bloqueados o QualityGate | BalanceConfig.Shot.QualityGate |
| Nivel 3: Stats no importan | Skills no afectan scores | PlayerScorer normalización |
| Nivel 3: GK no importa | GK save logic rota | GKAction.fs / ShotAction.fs |
