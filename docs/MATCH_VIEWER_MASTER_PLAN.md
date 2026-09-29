# FootballEngine - Live Match Day UI Master Plan

## 1. Vision

Build the best live football-management match experience: a real-time coaching workspace where the pitch, tactical decisions, match intelligence, commentary and emotional narrative form one coherent product.

This is not a replay viewer with management controls attached. Match Day is a live operating environment. The user watches the team think, diagnoses what is happening, intervenes through tactics and substitutions, and sees the consequences emerge from the simulation.

The quality target is above Football Manager in clarity, causality and interaction. The interface must feel authored, broadcast-grade and unmistakably FootballEngine, not like a dashboard assembled from generic cards.

This document replaces the previous Match Viewer plan. There is no backward-compatible replay UI, timeline, scrubbing workflow or legacy playback toolbar in the target product.

## 2. Product Contract

1. **Live first.** The canonical experience consumes an active match session, not a completed replay.
2. **The manager acts, not merely watches.** Tactics, roles, instructions, substitutions and touchline communication are primary controls.
3. **Every visible claim has a cause.** Statistics, commentary and advice must derive from authoritative events or state.
4. **The pitch remains the visual anchor.** Surrounding components support the match and never reduce it to a small dashboard tile.
5. **Information has rhythm.** Persistent information stays quiet; urgent information earns attention temporarily.
6. **Left, center, right and bottom have stable meanings.** Users build spatial memory during pressure.
7. **Competition identity changes presentation, not usability.** League-inspired scoreboards and accents may vary; interaction and hierarchy remain stable.
8. **No compatibility layer.** Delete replay-only state, controls and routes when their live replacements land.
9. **Desktop quality is the primary target.** Compact layouts recompose deliberately; they do not squeeze desktop panels.
10. **A successful build is not visual acceptance.** Every gate requires deterministic captures and human review.

## 3. Experience Pillars

### 3.1 Read The Match

Within three seconds the user must understand:

- Score, clock, period and competition.
- Possession and territorial pressure.
- Current formation and shape deviations.
- Recent danger and momentum without fake aggregate drama.
- Injuries, cards, fatigue and substitution constraints.
- What the commentator considers important and why.

### 3.2 Make A Decision

The user must be able to:

- Change mentality, team instructions and formation.
- Change a player's role, duty and individual instructions.
- Prepare and confirm substitutions.
- Compare candidates without leaving Match Day.
- Issue touchline instructions with explicit duration/cooldown.
- Pause when permitted, queue changes and understand when they take effect.

### 3.3 Observe Consequences

After an intervention the UI must expose:

- Pending, accepted and applied command states.
- The match tick or restart at which a change became active.
- Shape and role changes on the pitch.
- Relevant event and statistical changes without claiming causation that the engine did not establish.

### 3.4 Feel The Occasion

Competition theme, crowd context, commentary cadence, event treatment and scoreboard should create identity while preserving restraint. Drama comes from football events, not constant animation.

## 4. Information Architecture

### 4.1 Desktop Wide Layout

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ Match header: competition · score · clock · phase · pause/settings          │
├───────────────┬───────────────────────────────────────┬──────────────────────┤
│ LEFT RAIL     │                                       │ RIGHT RAIL           │
│ Manager Desk  │             LIVE PITCH                │ Match Intelligence   │
│               │                                       │                      │
│ Tactics       │                                       │ Commentary           │
│ Shape         │                                       │ Live stats           │
│ Instructions  │                                       │ Event stream         │
│ Subs          │                                       │ Analysis/advice      │
├───────────────┴───────────────────────────────────────┴──────────────────────┤
│ Live control dock: speed · pause · pending actions · context alerts         │
└──────────────────────────────────────────────────────────────────────────────┘
```

The pitch receives at least 58% of usable width at 1600x900 and at least 62% at 1920x1080. Rails are fixed within bounded ranges and may collapse; they never grow until the pitch becomes secondary.

### 4.2 Spatial Responsibilities

**Top header**

- Competition-themed scoreboard.
- Match clock, period, stoppage and aggregate/leg context.
- Connection/runtime health only when degraded.
- Pause and settings controls.

**Left rail: Manager Desk**

- `Overview`: mentality, formation, team shape and active instructions.
- `Tactics`: in-possession, transition and out-of-possession controls.
- `Players`: role/duty changes and individual instructions.
- `Subs`: bench, condition, performance, cards and substitution workflow.
- `Touchline`: short-lived communication commands.

**Center: Live Pitch**

- Match action, scoreboard and transient event presentation.
- Selected-player context and optional tactical overlays.
- No permanent statistics cards over the playing surface.

**Right rail: Match Intelligence**

- Human-readable live commentary.
- Compact live-stat comparison.
- Chronological event stream.
- Assistant observations separated from factual commentary.
- Contextual detail for selected events or players.

**Bottom dock: Live Control**

- Pause/resume and supported live speeds only.
- Current simulation status.
- Pending tactical changes and next application point.
- Urgent prompts: injury, dismissal, substitution confirmation, VAR or halftime.
- No frame counter, percentage, seek bar, snapshot index or replay step controls.

### 4.3 Responsive Modes

- **Wide, >= 1500 px:** both rails visible; pitch dominant.
- **Standard, 1100-1499 px:** one rail visible, the other opens as a non-modal drawer; user chooses pinned side.
- **Compact, 800-1099 px:** pitch plus bottom tab bar; side content becomes full-height drawers.
- **Below 800 px:** not a shrunk desktop. Use a dedicated compact composition with pitch, scoreboard and one active workspace.

Panel state persists per save and resolution class.

## 5. Visual System

### 5.1 Direction

Use a dark broadcast workspace with a natural, vivid pitch as the luminous center. Surrounding surfaces use one ink hierarchy, restrained borders and depth created through tonal separation rather than stacked rectangles and heavy shadows.

### 5.2 Surface Hierarchy

- `Canvas`: darkest application background.
- `Rail`: one continuous surface per side, not card soup.
- `Section`: spacing and typography first; dividers only where needed.
- `Interactive`: clearly raised or tinted on hover/focus.
- `Urgent`: event-specific accent, temporary and localized.

No more than three simultaneous background tones around the pitch.

### 5.3 Typography

- Bundle a licensed professional family with tabular numerals.
- Score and clock have dedicated numeric styles.
- Section headings use compact uppercase sparingly.
- Body/commentary text prioritizes reading speed.
- Minimum desktop text is 12 physical pixels at reference scale; critical controls target 14-16.

### 5.4 Competition Identity

Each competition owns a `BroadcastIdentity`:

- Stable identity key independent of display name.
- Surface, panel, accent, text and warning colors.
- Scoreboard composition family and metrics.
- Motion timings and event sting style.
- Optional licensed/original icon assets.

Identity belongs in game data/mod metadata, not string matching in the renderer. Built-ins ship identities for every built-in competition. Mod validation rejects malformed identities. Unknown competitions receive a deterministic generated identity as a product feature, never the old generic scoreboard.

League themes are inspired by regional broadcast character but must not copy protected logos, exact trade dress, animations or proprietary typefaces.

### 5.5 Motion

- Panel transitions: 140-220 ms ease-out.
- Score changes: restrained number transition and short accent pulse.
- Urgent alerts: one entrance, persistent calm state, one exit.
- Commentary entries do not animate continuously.
- Reduced-motion mode removes movement while preserving state changes.

## 6. Component Specifications

### 6.1 Match Header And Scoreboard

- Competition identity and actual fixture metadata.
- Full useful club names with deterministic shortening rules.
- Score, clock, period, stoppage, aggregate and shootout states.
- Red-card indicators and substitution count where appropriate.
- Centering uses the score block as the visual axis, not total component width.
- Layout tests cover long names, 3-digit minutes, two-digit scores and localization.

### 6.2 Manager Desk

The rail is one workspace with tabs, not independent floating cards.

**Overview**

- Current formation mini-pitch.
- Mentality selector.
- Active instruction chips grouped by phase.
- Pending-change summary.

**Tactics**

- Phase tabs: In Possession, Transition, Out of Possession.
- Controls explain the football effect, current value and pending value.
- Apply bar shows affected commands and validation errors.
- Undo is available only before the command is accepted by the match session.

**Substitutions**

- Starting XI and bench in a comparison workflow.
- Condition, sharpness, match rating, cards, injury risk and role fit.
- Select outgoing, compare replacements, choose role/duty, confirm.
- Enforce competition rules before command submission.
- Multi-sub windows are prepared as one command batch.

### 6.3 Match Intelligence Rail

**Commentary**

- Primary live narrative written from semantic events.
- Distinguish play-by-play, tactical observation and officiating.
- Resolve actual player names; never display placeholders such as `Player`.
- Important entries may pin briefly; routine entries remain quiet.

**Live stats**

- Start with score-context metrics: shots, shots on target, xG, possession, field tilt, corners, fouls and cards.
- Show paired bars only when comparison helps.
- Values update at a bounded cadence to avoid visual noise.
- Clicking a metric opens its event evidence where available.

**Event stream**

- Goals, cards, injuries, substitutions, VAR and major chances.
- Timestamp, actor, team and outcome.
- No replay seeking in the live product; selecting an event explains it.

**Assistant insight**

- Clearly labelled opinion, never presented as fact.
- Includes evidence and confidence.
- Avoid repetitive or mechanically timed advice.

### 6.4 Live Control Dock

- States: Connecting, Preparing, Live, Paused, Awaiting Decision, Halftime, Finished, Failed.
- Supported speeds are product decisions, not arbitrary replay multipliers.
- Default: `1x`; optional fast live simulation: `2x` and `4x` only.
- Automatically return to `1x` or pause for configured critical events.
- Pending commands show `Queued`, `Accepted`, `Applied` or `Rejected`.

### 6.5 Context And Alerts

- Injury and red-card decisions can open a focused decision layer without replacing the whole page.
- Halftime opens a deliberate interval workspace with team talk, tactical review and substitution preparation.
- Fulltime transitions to a post-match summary route; it does not expose replay controls in Match Day.

## 7. Live Architecture

### 7.1 Authoritative Session

Replace `MatchState` replay semantics with a live session contract:

```text
LiveMatchState
  SessionId
  FixtureContext
  RuntimeStatus
  LatestSnapshot
  PresentationSnapshot
  LiveMetrics
  EventFeed
  CommentaryFeed
  PendingCommands
  DecisionPrompt
  UiWorkspace
```

Remove `ActiveReplay`, `Snapshot`, `FrameCount`, replay stepping, seek percentage and `PlaybackSpeed` values intended for completed-match playback.

### 7.2 Command Boundary

UI never mutates match state directly. It submits typed commands:

- `ChangeMentality`
- `ChangeFormation`
- `SetTeamInstruction`
- `SetPlayerRole`
- `SetPlayerInstruction`
- `PrepareSubstitutions`
- `ConfirmSubstitutions`
- `IssueTouchlineInstruction`
- `SetLiveSpeed`
- `PauseMatch` / `ResumeMatch`

Every command carries session ID, expected revision and command ID. The runtime responds with accepted, rejected and applied events. Stale commands are rejected explicitly.

### 7.3 Application Timing

The engine defines safe application points. The UI displays them; it does not fake immediate application.

- Shape/instruction changes: next permitted cognition boundary.
- Substitutions: next legal stoppage and substitution window.
- Touchline instruction: accepted immediately, behavioral effect at the engine-defined boundary.
- Halftime changes: applied before second-half kickoff.

### 7.4 Read Models

Build dedicated immutable projections:

- `LivePitchScene`
- `ManagerDeskModel`
- `SubstitutionModel`
- `MatchIntelligenceModel`
- `LiveControlModel`
- `BroadcastHeaderModel`

Views consume read models and emit intents. They do not traverse `GameState` or infer competition/fixture identity by searching maps during rendering.

### 7.5 Module Ownership

```text
AppState/LiveMatch/
  Types.fs
  Commands.fs
  Projection.fs
  Update.fs

Views/MatchDay/
  MatchDayPage.fs
  MatchDayLayout.fs
  MatchHeader.fs
  ManagerDesk.fs
  TacticsWorkspace.fs
  SubstitutionWorkspace.fs
  MatchIntelligence.fs
  CommentaryFeed.fs
  LiveStats.fs
  EventStream.fs
  LiveControlDock.fs
  DecisionOverlay.fs

Views/MatchPitch/
  VisualTypes.fs
  Projection.fs
  Camera.fs
  RenderResources.fs
  PitchRenderer.fs
  EntityRenderer.fs
  BroadcastRenderer.fs
  MatchPitchControl.fs
```

`MatchViewer.fs` must be dismantled; no new large multipurpose replacement is allowed.

## 8. Commentary System

Commentary is a first-class product system, not a string helper.

- Input: typed semantic events plus authoritative context.
- Output: structured entry with category, importance, actors, timestamp, localization key and evidence IDs.
- Deterministic wording selection from seeded variants.
- Competition and locale may affect tone, never factual content.
- Tactical observations come from analysis systems and are labelled separately.
- Commentary rendering virtualizes long feeds and bounds retained entries.

## 9. Performance Contract

- Stable 60 FPS pitch rendering at 1920x1080 Balanced.
- UI response to manager input <= 50 ms.
- Live-stat projection cadence <= 4 Hz unless an event forces an update.
- Commentary/event append must not rebuild the pitch or entire page.
- Steady pitch playback target: zero native allocations and <= 1 KB managed allocations per frame.
- Rails virtualize feeds and avoid rebuilding hidden workspaces.
- Opening tactics or substitutions must not pause the engine unless the user or rules request it.

## 10. Accessibility

- Keyboard navigation across all manager actions.
- Explicit focus order by region.
- Screen-reader labels include current and pending values.
- Team identity never depends only on color.
- Text scaling does not cover the pitch or hide confirm controls.
- Reduced motion, color-vision-safe palettes and configurable critical-event pause.
- Commentary remains available as text independent of animation/audio.

## 11. Implementation Gates

### Gate 0 - Evidence And Deletion Map

- Capture current wide, standard and compact Match Day.
- Inventory replay-only state, messages, controls and routes.
- Record performance and state-flow baseline.
- Produce an explicit deletion list.

Exit: evidence exists and every legacy path has an owner and deletion gate.

### Gate 1 - Live Session Contract

- Introduce live state, fixture context, revisions and typed command lifecycle.
- Add safe tactical/substitution application points in the runtime.
- Add deterministic command tests.

Exit: live commands can be accepted, rejected and observed as applied without UI inference.

### Gate 2 - Read Models And Page Shell

- Build dedicated projections and the four-region responsive shell.
- Implement stable panel sizing and drawer behavior.
- Preserve pitch quality while removing old surrounding UI.

Exit: wide/standard/compact captures pass layout review.

### Gate 3 - Manager Desk

- Implement overview, tactics, players, substitutions and touchline workflows.
- Add validation, pending states and command feedback.

Exit: a user can manage a full match without leaving Match Day.

### Gate 4 - Match Intelligence

- Implement commentary pipeline, event stream, live stats and assistant insights.
- Eliminate placeholder names and fabricated metrics.

Exit: every displayed item traces to authoritative evidence.

### Gate 5 - Broadcast System

- Move competition identity to typed game/mod data.
- Implement and review every built-in competition identity.
- Validate long names, periods, stoppage, aggregate and shootouts.

Exit: competition screenshot matrix passes human review.

### Gate 6 - Live Control And Critical Moments

- Replace replay toolbar with live controls and command queue.
- Implement injury, dismissal, VAR, halftime and fulltime transitions.

Exit: no replay control remains in the live route.

### Gate 7 - Modularization And Performance

- Split the monolith according to ownership.
- Establish resource lifetime and render/update isolation.
- Meet frame, allocation and input-latency budgets.

Exit: profiling artifacts and lifecycle tests pass.

### Gate 8 - Final Deletion

- Delete replay-only Match Day state, messages, timeline, step controls, snapshot counters and old composition.
- Delete string-based competition theme matching.
- Delete obsolete fallback UI and dual paths.

Exit: searches prove the old route is gone; solution, tests, formatting and screenshot matrix pass.

## 12. Validation Matrix

Required deterministic scenarios:

- Normal live play at 0', 45+', halftime, 90+' and fulltime.
- Goal, disallowed goal, VAR, card, red card, injury and substitution.
- Pending tactical change before and after application.
- Single and multi-substitution workflows, including rejection.
- Long club and competition names.
- League, domestic cup, continental group and two-leg knockout context.
- Wide, standard, compact and HiDPI layouts.
- Keyboard-only, text scaling and reduced motion.
- Runtime disconnect/failure and stale command rejection.

Each scenario requires state assertions plus visual capture. Golden updates require human approval.

## 13. Quality Bar

Reject the implementation if any of the following is true:

- The pitch looks embedded in a generic admin dashboard.
- Rails are collections of unrelated cards.
- The user must leave Match Day for routine tactical or substitution decisions.
- A control changes visually before the runtime accepts it without showing pending state.
- Commentary uses placeholder identities or invents causes.
- Different competitions are merely recolors of one scoreboard.
- Compact mode is the desktop layout scaled down.
- Replay terminology or controls remain in the live route.
- A component is declared complete without reference captures and human visual review.

## 14. Definition Of Done

Match Day is complete only when:

1. The live session, not replay data, drives the page.
2. Tactics, roles, instructions and substitutions work end to end through authoritative commands.
3. Left rail, pitch, right rail and bottom dock form one coherent visual system.
4. Commentary, stats, events and advice are factual, legible and useful.
5. Every built-in competition has a reviewed original broadcast identity.
6. Wide, standard and compact compositions pass visual review.
7. Critical match moments have deliberate interaction flows.
8. Accessibility and performance contracts pass.
9. The monolithic viewer and replay-only UI paths are deleted.
10. Release build, Core tests, Client tests, Fantomas, diff checks and deterministic screenshot matrix pass.

No milestone, component or system should be called complete before its exit evidence exists.

## 15. Implementation Reference

This section is normative. Implement these contracts unless the compiler or measured evidence proves a concrete defect. Do not replace them with looser strings, tuples or mutable UI state.

F# records and discriminated unions are used deliberately: immutable records model snapshots/read models, while unions make command and lifecycle states exhaustive under pattern matching.

### 15.1 IDs, Runtime Status And Clock

```fsharp
namespace FootballEngine.LiveMatch

open FootballEngine.Domain
open FootballEngine.Core.Match

[<Struct>]
type LiveMatchSessionId = LiveMatchSessionId of uint64

[<Struct>]
type MatchCommandId = MatchCommandId of uint64

type LiveSpeed =
    | Normal
    | Fast
    | VeryFast

type LiveMatchStatus =
    | Connecting
    | Preparing
    | Live of speed: LiveSpeed
    | Paused
    | AwaitingDecision of DecisionPrompt
    | HalfTime
    | Finished
    | Failed of message: string

and DecisionPrompt =
    | InjuryDecision of playerId: PlayerId
    | DismissalDecision of playerId: PlayerId
    | SubstitutionRequired of reason: string

type MatchPeriod =
    | FirstHalf
    | HalfTimeInterval
    | SecondHalf
    | ExtraTimeFirstHalf
    | ExtraTimeInterval
    | ExtraTimeSecondHalf
    | PenaltyShootout
    | FullTime

type LiveClock =
    { Elapsed: int<subtick>
      DisplayMinute: int
      StoppageMinute: int option
      Period: MatchPeriod }
```

Do not derive `Period` or stoppage display inside a view.

### 15.2 Fixture Context

```fsharp
type ClubMatchIdentity =
    { ClubId: ClubId
      Name: string
      ShortName: string
      PrimaryColor: uint32
      SecondaryColor: uint32 }

type FixtureContext =
    { MatchId: MatchId
      CompetitionId: CompetitionId
      CompetitionName: string
      BroadcastIdentityKey: string
      Home: ClubMatchIdentity
      Away: ClubMatchIdentity
      Round: Round option
      Leg: int option
      AggregateScore: (int * int) option
      SubstitutionRules: CompetitionSubstitutionRules }
```

Construct this once when the session starts. Delete map searches by club pair from render/view code.

### 15.3 Command Contract

```fsharp
type TacticalPhase =
    | InPossession
    | Transition
    | OutOfPossession

type LiveMatchCommandPayload =
    | ChangeMentality of Mentality
    | ChangeFormation of Formation
    | SetTeamInstruction of phase: TacticalPhase * instruction: TacticalInstruction
    | RemoveTeamInstruction of phase: TacticalPhase * instruction: TacticalInstruction
    | SetPlayerRole of playerId: PlayerId * role: PlayerRole * duty: PlayerDuty
    | SetPlayerInstruction of playerId: PlayerId * instruction: PlayerInstruction
    | PrepareSubstitutions of changes: SubstitutionChange list
    | ConfirmSubstitutions of changes: SubstitutionChange list
    | IssueTouchlineInstruction of TouchlineInstruction
    | SetLiveSpeed of LiveSpeed
    | PauseMatch
    | ResumeMatch

type LiveMatchCommand =
    { CommandId: MatchCommandId
      SessionId: LiveMatchSessionId
      ExpectedRevision: uint64
      IssuedAt: int<subtick>
      Payload: LiveMatchCommandPayload }

type CommandRejection =
    | StaleRevision of expected: uint64 * actual: uint64
    | InvalidMatchState of LiveMatchStatus
    | IllegalSubstitution of reason: string
    | InvalidTacticalChange of reason: string
    | CommandNotPermitted of reason: string

type ApplicationPoint =
    | Immediate
    | NextCognitionBoundary
    | NextStoppage
    | BeforeNextKickOff

type CommandLifecycle =
    | Queued
    | Accepted of applicationPoint: ApplicationPoint
    | Applied of revision: uint64 * appliedAt: int<subtick>
    | Rejected of CommandRejection

type PendingCommand =
    { Command: LiveMatchCommand
      Lifecycle: CommandLifecycle }
```

The runtime owns legality and application. Client-side validation improves feedback but never authorizes a command.

### 15.4 Live State

```fsharp
type MatchDayWorkspace =
    | Overview
    | Tactics of TacticalPhase
    | Players
    | Substitutions
    | Touchline

type IntelligenceWorkspace =
    | Commentary
    | Statistics
    | Events
    | Assistant

type MatchDayUiState =
    { ManagerWorkspace: MatchDayWorkspace
      IntelligenceWorkspace: IntelligenceWorkspace
      SelectedPlayerId: PlayerId option
      SelectedEventId: MatchEventId option
      DraftSubstitutions: SubstitutionChange list
      PinnedRail: MatchDayRail option }

and MatchDayRail =
    | ManagerRail
    | IntelligenceRail

type LiveMatchState =
    { SessionId: LiveMatchSessionId
      Fixture: FixtureContext
      Revision: uint64
      Status: LiveMatchStatus
      Clock: LiveClock
      LatestSnapshot: Replay.ReplaySnapshot option
      PresentationSnapshot: Replay.ReplaySnapshot option
      Metrics: TeamStats * TeamStats
      Events: MatchEvent list
      Commentary: CommentaryEntry list
      PendingCommands: Map<MatchCommandId, PendingCommand>
      Ui: MatchDayUiState
      Error: string option }
```

`ReplaySnapshot` may remain temporarily as the engine-to-presentation frame format, but replay ownership, seeking and completed-match playback must not leak into `LiveMatchState`.

### 15.5 Messages And Effects

```fsharp
type LiveMatchMsg =
    | SessionConnected of LiveMatchSessionId * FixtureContext * revision: uint64
    | FrameReceived of revision: uint64 * Replay.ReplaySnapshot * TeamStats * TeamStats
    | EventsReceived of revision: uint64 * MatchEvent list
    | CommandSubmitted of LiveMatchCommand
    | CommandAccepted of MatchCommandId * ApplicationPoint
    | CommandApplied of MatchCommandId * revision: uint64 * appliedAt: int<subtick>
    | CommandRejected of MatchCommandId * CommandRejection
    | RuntimeStatusChanged of LiveMatchStatus
    | SelectManagerWorkspace of MatchDayWorkspace
    | SelectIntelligenceWorkspace of IntelligenceWorkspace
    | SelectPlayer of PlayerId option
    | SetDraftSubstitutions of SubstitutionChange list
    | SubmitDraftSubstitutions
    | DismissError

type LiveMatchEffect =
    | SendCommand of LiveMatchCommand
    | ConnectSession of MatchId
    | DisconnectSession of LiveMatchSessionId
    | NoEffect
```

Do not put functions such as `(TacticalInstructions -> TacticalInstructions)` in live messages. Commands must be serializable, loggable and deterministic.

### 15.6 Reducer

```fsharp
module LiveMatchUpdate =

    let private updatePending commandId lifecycle state =
        let pending =
            state.PendingCommands
            |> Map.tryFind commandId
            |> Option.map (fun current -> { current with Lifecycle = lifecycle })

        match pending with
        | Some value ->
            { state with
                PendingCommands = state.PendingCommands |> Map.add commandId value }
        | None -> state

    let update msg state : LiveMatchState * LiveMatchEffect =
        match msg with
        | FrameReceived(revision, snapshot, homeStats, awayStats) when revision >= state.Revision ->
            { state with
                Revision = revision
                LatestSnapshot = Some snapshot
                PresentationSnapshot = Some snapshot
                Metrics = homeStats, awayStats },
            NoEffect

        | FrameReceived _ -> state, NoEffect

        | CommandSubmitted command ->
            let pending =
                { Command = command
                  Lifecycle = Queued }

            { state with
                PendingCommands = state.PendingCommands |> Map.add command.CommandId pending },
            SendCommand command

        | CommandAccepted(commandId, point) ->
            updatePending commandId (Accepted point) state, NoEffect

        | CommandApplied(commandId, revision, appliedAt) ->
            updatePending commandId (Applied(revision, appliedAt)) { state with Revision = revision }, NoEffect

        | CommandRejected(commandId, rejection) ->
            updatePending commandId (Rejected rejection) state, NoEffect

        | SelectManagerWorkspace workspace ->
            { state with Ui = { state.Ui with ManagerWorkspace = workspace } }, NoEffect

        | SelectIntelligenceWorkspace workspace ->
            { state with Ui = { state.Ui with IntelligenceWorkspace = workspace } }, NoEffect

        | SelectPlayer playerId ->
            { state with Ui = { state.Ui with SelectedPlayerId = playerId } }, NoEffect

        | SetDraftSubstitutions changes ->
            { state with Ui = { state.Ui with DraftSubstitutions = changes } }, NoEffect

        | RuntimeStatusChanged status -> { state with Status = status }, NoEffect
        | DismissError -> { state with Error = None }, NoEffect
        | SessionConnected _
        | EventsReceived _
        | SubmitDraftSubstitutions -> state, NoEffect
```

The omitted cases receive focused handlers in the same module; do not use a wildcard branch in the production reducer.

### 15.7 Runtime Command Handling

```fsharp
let handleCommand command session =
    if command.SessionId <> session.Id then
        Error(CommandNotPermitted "Command belongs to another match session")
    elif command.ExpectedRevision <> session.Revision then
        Error(StaleRevision(command.ExpectedRevision, session.Revision))
    else
        match CommandValidation.validate session.World command.Payload with
        | Error reason -> Error reason
        | Ok applicationPoint ->
            let queued = CommandQueue.enqueue applicationPoint command session.Commands
            Ok({ session with Commands = queued }, applicationPoint)
```

At each safe engine boundary, drain only commands assigned to that boundary, apply them in command-ID order and emit `CommandApplied` with the new revision.

### 15.8 Read Models

```fsharp
type BroadcastHeaderModel =
    { CompetitionIdentityKey: string
      CompetitionName: string
      HomeName: string
      AwayName: string
      HomeScore: int
      AwayScore: int
      Clock: LiveClock
      AggregateScore: (int * int) option
      HomeRedCards: int
      AwayRedCards: int }

type LiveControlModel =
    { Status: LiveMatchStatus
      SelectedSpeed: LiveSpeed
      CanPause: bool
      CanResume: bool
      Pending: PendingCommand list
      Prompt: DecisionPrompt option }

type MatchDayModel =
    { Header: BroadcastHeaderModel
      Pitch: LivePitchScene
      ManagerDesk: ManagerDeskModel
      Intelligence: MatchIntelligenceModel
      Controls: LiveControlModel
      Ui: MatchDayUiState }

module MatchDayProjection =
    let project gameState liveState : MatchDayModel =
        { Header = BroadcastHeaderProjection.project liveState
          Pitch = LivePitchProjection.project gameState liveState
          ManagerDesk = ManagerDeskProjection.project gameState liveState
          Intelligence = MatchIntelligenceProjection.project gameState liveState
          Controls = LiveControlProjection.project liveState
          Ui = liveState.Ui }
```

Projection is the only place allowed to combine `GameState` and live session state for Match Day.

### 15.9 Broadcast Identity In Game Data

```fsharp
type ScoreboardComposition =
    | Ribbon
    | Split
    | Capsule
    | Continental

type BroadcastPalette =
    { Surface: string
      Panel: string
      Accent: string
      Text: string
      AccentText: string
      Warning: string }

type BroadcastIdentity =
    { Key: string
      Composition: ScoreboardComposition
      Palette: BroadcastPalette
      EnterDurationMs: int
      ScorePulseDurationMs: int }

type Competition =
    { Id: CompetitionId
      Name: string
      BroadcastIdentity: BroadcastIdentity
      // Existing authoritative fields remain here.
    }
```

The `.femod` schema requires `broadcastIdentity`; increment the schema and delete the old decoder. Validation must reject invalid hex colors, duplicate keys, non-positive timings and unsupported composition values.

### 15.10 Commentary Contract

```fsharp
type CommentaryCategory =
    | PlayByPlay
    | Officiating
    | TacticalObservation
    | MatchContext

type CommentaryImportance =
    | Routine
    | Notable
    | Critical

type CommentaryEntry =
    { Id: MatchEventId
      Tick: int<subtick>
      Category: CommentaryCategory
      Importance: CommentaryImportance
      LocalizationKey: string
      Arguments: Map<string, string>
      ActorIds: PlayerId list
      EvidenceEventIds: MatchEventId list }

let fromEvent context event =
    match event.Data with
    | EventData.Goal goal ->
        { Id = event.Id
          Tick = event.Tick
          Category = PlayByPlay
          Importance = Critical
          LocalizationKey = "match.commentary.goal"
          Arguments =
            Map [ "scorer", context.playerName goal.ScorerId
                  "team", context.clubName goal.ClubId ]
          ActorIds = [ goal.ScorerId ]
          EvidenceEventIds = [ event.Id ] }
    | _ -> CommentaryRules.fromNonGoalEvent context event
```

Template selection is seeded by event ID. The view receives final localized text and never resolves player names.

### 15.11 Responsive Layout

```fsharp
type MatchDayLayoutMode =
    | Wide
    | Standard
    | Compact

let layoutMode width =
    if width >= 1500.0 then Wide
    elif width >= 1100.0 then Standard
    else Compact

let view model dispatch =
    Grid.create
        [ Grid.columnDefinitions "Auto,*,Auto"
          Grid.rowDefinitions "Auto,*,Auto"
          Grid.children
              [ MatchHeader.view model.Header dispatch
                |> Grid.row 0
                |> Grid.columnSpan 3

                ManagerDesk.view model.ManagerDesk dispatch
                |> Grid.row 1
                |> Grid.column 0

                MatchPitch.view model.Pitch dispatch
                |> Grid.row 1
                |> Grid.column 1

                MatchIntelligence.view model.Intelligence dispatch
                |> Grid.row 1
                |> Grid.column 2

                LiveControlDock.view model.Controls dispatch
                |> Grid.row 2
                |> Grid.columnSpan 3 ] ]
```

The real implementation switches complete compositions by `MatchDayLayoutMode`; it must not hide random children inside one oversized tree.

### 15.12 Substitution Validation

```fsharp
let validateSubstitutions rules world changes =
    result {
        do! SubstitutionRules.requireNonEmpty changes
        do! SubstitutionRules.requireDistinctOutgoing changes
        do! SubstitutionRules.requireDistinctIncoming changes
        do! SubstitutionRules.requirePlayersEligible world changes
        do! SubstitutionRules.requireWithinMaximum rules changes
        do! SubstitutionRules.requireWindowAvailable rules world
        do! LineupValidation.requireCompleteAfterChanges world changes
        return changes
    }
```

The confirmation UI displays all validation errors before submit. The runtime executes the same authoritative validation again.

### 15.13 Tests To Write First

```fsharp
testCase "accepted tactical change stays pending until engine application" (fun _ ->
    let state = Fixtures.liveMatchState revision=7UL
    let command = Fixtures.changeMentalityCommand revision=7UL

    let queued, _ = LiveMatchUpdate.update (CommandSubmitted command) state
    let accepted, _ = LiveMatchUpdate.update (CommandAccepted(command.CommandId, NextCognitionBoundary)) queued

    Expect.equal
        accepted.PendingCommands[command.CommandId].Lifecycle
        (Accepted NextCognitionBoundary)
        "UI does not pretend the tactic is active"

    let applied, _ = LiveMatchUpdate.update (CommandApplied(command.CommandId, 8UL, 2400<subtick>)) accepted
    Expect.equal applied.Revision 8UL "applied command advances the authoritative revision")

testCase "stale frame cannot replace current presentation" (fun _ ->
    let state = Fixtures.liveMatchState revision=12UL
    let stale = Fixtures.snapshot ()
    let next, _ = LiveMatchUpdate.update (FrameReceived(11UL, stale, TeamStats.zero, TeamStats.zero)) state
    Expect.equal next.Revision 12UL "out-of-order delivery is ignored")

testCase "competition identity is required" (fun _ ->
    let json = Fixtures.competitionJsonWithoutBroadcastIdentity
    Expect.isError (CompetitionCodec.decode json) "schema has no legacy identity fallback")
```

Add screenshot tests for each layout mode and every scoreboard composition, plus interaction tests for keyboard substitution flow and rejected commands.

### 15.14 Destructive Migration Order

1. Add live contracts and tests in new compile-ordered modules.
2. Add runtime command queue and acknowledgements.
3. Add read models and new Match Day shell behind the existing route, not a feature flag.
4. Replace the route composition in one change.
5. Delete `Step`, snapshot counters, seek percentage, replay speed options and old playback controls.
6. Delete `ActiveReplay`, `Snapshot`, `FrameCount` and replay-only update branches from Client state.
7. Move completed-match replay, if retained as a separate future product, to a separate route and state model; do not share Match Day UI.
8. Split rendering modules and delete the monolithic `MatchViewer.fs` implementation.
9. Move scoreboard identity from renderer string maps into `.femod` data, then delete string matching and generated mutable paint mutation.
10. Run the full validation matrix before calling any gate complete.

### 15.15 Explicit Non-Goals

- No compatibility adapter from old `MatchMsg` to `LiveMatchMsg`.
- No hidden replay timeline in live mode.
- No UI-owned tactical truth.
- No generic dashboard card library imposed on Match Day.
- No fabricated commentary, xG, momentum or advice.
- No per-league implementation copied from protected broadcast packages.
- No completion claim based only on compilation or one screenshot.
