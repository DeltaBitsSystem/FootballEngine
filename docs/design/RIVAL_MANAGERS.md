# Técnicos rivales — diseño

> Pilar 2 (rivales que aprenden) de `GAME_DEFINITION.md`. Estado de partida (2026-11-14): todos los técnicos IA
> juegan `Balanced` con instrucciones por defecto, nunca cambian de plan antes del partido, en el partido aplican
> las mismas reglas fijas, y `CurrentSkill`/`PotentialSkill` del técnico no influyen en nada.

## Qué tiene que sentir el jugador

- **Cada rival es alguien.** Antes del partido ves quién es: "El Copión", "El Estratega", "El Temperamental"... y
  cómo te va a jugar. En el partido lo ves reaccionar a su manera.
- **Te estudian, en tiempo realista.** Tu receta funciona unas fechas; después los técnicos que saben leer la
  descubren y le buscan la respuesta. No reaccionan al instante ni hacen trampa: leen tus partidos (los que salen
  por TV) y aprenden a su velocidad.
- **El técnico bueno se nota.** Un CA alto lee mejor el partido, reacciona antes, elige mejores cambios y rota
  cansados. Un CA bajo se equivoca: a veces se tira atrás cuando estaba dominando.
- **Rivalidades que nacen solas.** El técnico al que humillaste se acuerda — aunque cambie de club.

## Modelo

### Personalidad (por técnico, generada, persistida)

| Rasgo | Rango | Qué cambia |
|---|---|---|
| Filosofía | Posesión · Presión · Contra · Directo · Fortaleza | su plan base y su formación preferida |
| Adaptabilidad (ya existe en `Mental`) | 1–20 | si contrarresta tu receta o juega a lo suyo |
| Audacia | 1–20 | cuándo sale a buscar el partido, cuándo se cierra, cuánto arriesga de visitante |
| Temperamento | 1–20 | reacción inmediata y emocional (doble cambio al entretiempo) vs calma |
| Orgullo | 1–20 | rencor: después de una goleada en contra juega la revancha más agresivo |
| Imitación | 1–20 | copia la táctica que le ganó |

El **arquetipo** se deriva de los rasgos (no se guarda): Copión, Temperamental, Estratega, Romántico, Jugador,
Conservador, Pragmático. Es la etiqueta que ve el usuario.

### Inteligencia = CA y PA

- **Lectura** = CA/200. Ruido al percibir el partido y la fuerza del rival, demora para reaccionar, calidad de los
  cambios (rol adecuado vs "el de mejor promedio"), rotación de cansados, precisión del contra-plan.
- **Aprendizaje** = PA/200. Cuántos partidos tuyos necesita para descubrir tu receta (alto PA ≈ 2, bajo ≈ 5).
  Además el CA crece hacia el PA al final de cada temporada (y cae con la edad).
- En los partidos de fondo (IA vs IA) el CA del técnico suma un poco de fuerza: los buenos técnicos rinden por
  encima de su plantel.

### Adaptación antes del partido (plan de juego)

Solo el rival del club del usuario prepara un plan (los partidos IA vs IA se resuelven con el modelo de fondo).

1. **Estudio del usuario.** El mundo guarda tus últimos 10 partidos jugados en el motor (formación, instrucciones,
   patrón de juego, resultado). Tu *receta dominante* es la firma que más usaste; su *amenaza* sube con los puntos
   que te da y con cuántas veces el rival te la vio (curva de aprendizaje por PA).
2. **Enfoque**, elegido por personalidad, en este orden:
   - **Copia**: te perdió con la receta X la última vez, Imitación alta, y su plantel puede jugarla → juega tu X.
   - **Revancha**: lo goleaste y su Orgullo es alto → su identidad, más audaz.
   - **Contra**: tu receta amenaza y su Adaptabilidad lo permite → la neutraliza (cerrar el medio si atacás por
     el centro, abrirse si atacás por fuera, bajar la línea si jugás directo, presionar o meter bloque bajo si
     tenés la pelota), con una intensidad = amenaza × lectura.
   - **Inferioridad**: sos mucho más fuerte → cauto se cierra y sale de contra; audaz apuesta a presionar.
   - **Identidad**: su filosofía.
3. **Once**: el mejor para la formación, y un técnico de CA alto descansa a los cansados.

El plan es determinista por partido: lo que ves en la previa es lo que va a hacer.

### Adaptación en el partido

El técnico rival **lee** (marcador, minuto, xG y dominio de los últimos minutos con ruido según CA, rojas, cambios
restantes) en momentos de revisión: cada 5', y tras gol, roja o entretiempo con una demora que baja con el CA y con
el temperamento. Entre decisiones deja pasar unos minutos (no cambia en cada jugada).

| Movida | Cuándo | Personalidad |
|---|---|---|
| Ir a buscarlo | pierde y llegó su minuto | audaz ~55', cauto ~78' |
| Todo o nada | pierde por poco al final | solo audaces |
| Cuidar la ventaja | gana y llegó su minuto | cauto antes; el muy cauto estaciona el micro |
| Aguantar el temporal | lo están dominando y no pierde | lectura buena lo ve a tiempo; mala, tarde o al revés |
| Tomar la iniciativa | domina con empate | audaces |
| Volver al plan | la causa de la movida desapareció | — |
| Reacción a la roja | expulsado propio | — |

Cada movida viene con cambios coherentes (delantero por volante si busca, defensor si cuida) y **siempre emite un
evento**: el partido se detiene en "el rival cambió" y dice qué hizo y por qué.

Tu propio equipo no tiene piloto automático nuevo: conserva el comportamiento actual.

### Rivalidades

Cada técnico guarda su historial contra cada técnico (no contra el club): PJ, G, E, P, goles y el último margen.
Viaja con él cuando cambia de club. La previa muestra tu historial contra él.

## Qué ve el usuario

- **Previa (Inicio, próximo partido)**: tarjeta del técnico rival — nombre, arquetipo, lectura en estrellas,
  historial contra vos y "cómo te va a jugar" con el motivo ("Te cierra el medio: tu 4-3-3 lleva 5 victorias",
  "Copió tu 4-2-3-1", "Busca revancha del 4-0").
- **Partido**: el momento clave "cambio del técnico rival" dice qué movida hizo.

## Fuera de alcance (después)

Fichajes y personal compitiendo por personalidad, despidos por vestuario, noticias de rivalidad, sesgo lateral
del motor (no existe el campo), que tu asistente te sugiera movidas.

---

# Implementation spec (for the implementer)

All behaviour numbers live in a new `RivalManagerConfig` inside `BalanceConfig` (defaults with one-line why,
validated in `ConfigValidation`, read from config, never `defaultConfig`). Determinism: every random draw comes
from a seeded `Rng` stream (pre-match: seed from execution seed + fixture id + coach id; in-match: the system rng).

## Unit 1 — Core

1. **Domain types** (`Domain/Staff.fs`, before `Staff`): `CoachPhilosophy = Possession | Pressing | Counter |
   Direct | Fortress`; `RivalRecord = { Played; Won; Drawn; Lost; GoalsFor; GoalsAgainst; LastMargin: int;
   LastMet: DateTime }`; `ManagerApproach = OwnIdentity | CounterRecipe of recipe: Formation | CopyRecipe of
   Formation | Revenge | Underdog`; `GamePlanNote = { OpponentClubId; Date; Formation; Approach; Threat: float }`;
   `ManagerMind = { Philosophy; Boldness; Temperament; Pride; Imitation: int (1..20); Rivals: Map<StaffId,
   RivalRecord>; LastPlan: GamePlanNote option }`. `Staff` gets `Mind: ManagerMind`.
2. **`Domain/ManagerMind.fs`** (new, after Staff.fs): `reading`, `learning` (clamped CA/200, PA/200),
   `ManagerArchetype` DU + `archetypeOf` with the ordered rules: Imitation≥15 Copycat; Temperament≥15 Hothead;
   Adaptability≥14 && CA≥140 Strategist; Adaptability≤7 && Philosophy∈{Possession,Pressing} Romantic; Boldness≥15
   Gambler; Boldness≤6 || Fortress Conservative; else Pragmatist. `recordResult` (updates both coaches' Rivals),
   `prune` (drop LastMet older than config years), `developSeason` (CA toward PA by rate while age < peak end;
   decline after; never above PA, never below a floor).
3. **Generation** (`WorldGeneration/StaffGen.fs`): every staff member gets a Mind (assistants can be promoted).
   Philosophy uniform; Boldness/Pride ~N(10,4); Temperament ~N(9,4); Imitation skewed so ~15% ≥ 15; clamp 1..20.
   Every other constructor of `Staff` (Pressure/JobMarket hiring, tests builders) gets a valid Mind.
4. **Persistence** (Client `Database/SnapshotRepository.fs` + any GameState codec): Staff with Mind and the new
   `GameState.UserStudy` round-trip; bump the schema and reject older saves explicitly with a clear message, never
   silently drop data.
5. **User study** (`GameState.UserStudy: UserMatchRecord list`, newest first, capped by config `StudyWindow`=10):
   `{ Date; OpponentClubId; Formation; Instructions: TacticalInstructions; Pattern: PatternVector; GoalsFor;
   GoalsAgainst }`, appended where the managed club's engine matches commit (MatchPhase / WorldRunner, the same
   places `recordPattern` runs; reuse the extracted pattern). Rivals are recorded for every fixture (engine and
   background) between the two head coaches.
6. **Game planner** (`World/Agents/GamePlanner.fs` or Domain if compile order requires; pure):
   `prepare cfg seed gs fixture clubId : (Lineup * GamePlanNote) option`, plus `prepareForFixtures` that runs it
   only for the AI opponent of the managed club in each fixture, writes the lineup to that coach
   (`Attributes.Coaching.Lineup`) and `Mind.LastPlan`, and never touches the managed club. Call it before
   `LineupOps.ensureForFixtures` at both world call sites **and on the client live-match path** (find where the
   user's live match builds its `MatchContext`; the live match is the main place this matters). Also expose
   `preview` (same function, same seed) for the UI.
   - Recipe = (formation, coarse instruction bands) most used in `UserStudy`; `recipeMatches`; `ppg`; mean pattern.
   - `awareness = 1 - exp(-recipeMatches * learning * StudyRate)`;
     `threat = awareness * clamp01((ppg - ThreatPpgFloor) / (3 - ThreatPpgFloor))`.
   - Opponent strength perceived with gaussian noise σ = `StrengthNoise * (1 - reading)`.
   - Approach order as in the design: Copy (last record vs the user's coach a loss, the user played formation F in
     that match, Imitation ≥ `CopyImitationMin`, draw < Imitation/20, squad suitability of F ≥
     `CopySuitabilityMin` × suitability of its best formation) → Revenge (Pride ≥ `RevengePrideMin`, LastMargin ≤
     −`HumiliationMargin`) → Counter (threat ≥ `CounterThreatMin`, draw < Adaptability/20; pattern-targeted deltas
     scaled by threat × reading; formation switch only to one the squad suits) → Underdog (perceived gap <
     −`UnderdogGap`) → OwnIdentity (philosophy preset instructions in config; preferred formation if set and suited,
     else best suited).
   - XI: per-slot score = `TacticalSuitability.stars` × (1 − `RotationWeight` × reading × (1 − condition/100)).
7. **Counter plan scaling** (`MatchContextBuilder`): the existing `CounterPlan` deltas scale by
   `(Adaptability/20) × reading` of that club's head coach.
8. **Match contract** (`Match/Contracts/MatchContext.fs`): `ClubRef.Manager: ManagerProfile` =
   `{ Autonomous: bool; Reading; Boldness; Temperament; Adaptability: float32 (0..1) }`, built in
   `MatchContextBuilder` (Autonomous = not the managed club). Test builders default to `Autonomous = false`.
9. **Event**: `EventData.ManagerShift of side: ClubSide * shift: ManagerShift` (`ChaseGame | AllOut | ProtectLead |
   WeatherStorm | TakeInitiative | BackToPlan | RedCardReshape`), its `EventKind`, hash, description, no-op apply.
10. **In-match brain**: `Match/Systems/Manager/ManagerBrain.fs` (pure `decide cfg profile read noise -> move
    voption`, unit-testable without a match) and a `ManagerBrainState` in `TeamWorld` owned by `ManagerSystem`
    (last review minute, pending review minute, last move minute, xG/shots snapshot at last review, current shift).
    - Non-autonomous side: the current code path, unchanged.
    - Autonomous side: reviews every `ReviewIntervalMin`; a goal, red card or half-time schedules a review after
      `MaxReactionDelayMin × (1 − reading) × (1 − Temperament × TemperamentHaste)`; `MinMinutesBetweenMoves`
      (shorter for hotheads) except after a goal or red card. Make sure the system actually sees goal/red/half-time
      events (check how `buffer.Applied` relates to the minute cadence; add `OnEvent` triggers if needed).
    - Momentum = (ΔmyXg − ΔtheirXg)/(ΔmyXg + ΔtheirXg + ε) since the last review, plus gaussian noise
      `ReadNoise × (1 − reading)`.
    - Rules per the design table, with minutes interpolated by Boldness from config.
    - Moves set directive kind and params (as today) and are paired with substitutions by intent: high reading
      picks a role-matched bench player and a sensible outgoing (attacking: weakest tired DM/DC → ST/AM/Winger;
      defensive: tired forward → defender/DM; card risk: booked defender after 60' when reading ≥
      `CardRiskReadingMin`); low reading keeps today's crude choice. Respect windows and `SubsUsed`. Hothead losing at
      half-time makes a double change.
    - Every move emits `TeamInstruction side` and `ManagerShift(side, shift)`.
11. **Background model** (`QuickMatch.rates`): strength gap +=
    `CoachStrengthPoints × (homeReading − awayReading)`; keep the default modest and justify it.
12. **Season**: `developSeason` and `prune` run once per season rollover for all staff.

### Acceptance tests (Core.Tests, registered in Program.fs)

- Generation: 300 generated coaches over fixed seeds → every archetype ≥ 3%, none > 45%; deterministic per seed.
- Learning curve: same dominant user recipe; awareness after 1 match < after 4; a high-PA coach crosses
  `CounterThreatMin` in fewer matches than a low-PA one.
- Approaches: stubborn (Adaptability 3, Imitation 2) plays identity vs a strong recipe; adaptive high-CA counters
  a centre-heavy pattern with narrower width than its identity plan; copycat who lost to F433 copies it when the
  squad suits F433 and does not when it doesn't; proud coach after a 0–4 plays Revenge with higher mentality.
- Planner never modifies the managed club's lineup; `preview` equals `prepare` for the same state and seed.
- Rotation: high-reading coach benches a 55%-condition starter whom a low-reading coach keeps.
- Brain (pure): bold chases earlier than cautious in the same losing read; cautious parks the bus where bold only
  defends; high-reading reacts sooner after conceding than low-reading; no move inside `MinMinutesBetweenMoves`
  without a goal/red; perceived-noise A/B: low reading makes the wrong storm/initiative call more often over seeds.
- Full match (autonomous away side, losing script or corpus of seeds): every directive change on that side is
  paired with `TeamInstruction` + `ManagerShift`; the non-autonomous side's event sequence for a fixed seed is
  identical to a run where both sides are non-autonomous only in what that side does (or: both non-autonomous
  reproduces the pre-change event hash — prove the old path is untouched).
- QuickMatch: identical squads, high vs low reading coach → higher expected goals for the better coach; equal
  coaches → identical rates to before.
- Rivals: results update both coaches' records; record follows the coach to a new club; prune drops stale ones.
- Season: CA grows toward PA for a young coach, never exceeds PA; old coach declines.
- Persistence (Client.Tests): Staff Mind + UserStudy round-trip through a temporary SQLite save; an old-schema
  save is rejected with an explicit error.

## Unit 2 — Client

- Home next-match: rival-coach card (name, archetype ES/EN with one-line description, reading as 5 stars, record vs
  the user's coach, planned approach + reason from `GamePlanner.preview`).
- KeyMoments / match banner: `ManagerShift` text ES/EN for the rival ("El rival sale a buscarlo", ...).
- Shots of the home page with the card; Client tests for the projection.
