# Match replay

Watch any played match again and jump to any moment, forward or backward. The capture tool uses the same
mechanism, so it stops scanning a live match in 5 second steps and stops spawning a second process to record.

## Decision

A replay is a deterministic re-simulation of the recorded execution (`WorldClock.MatchExecutions[matchId]`:
the `MatchInput` and its commands). No frames are stored.

Measured on seed 7 (Release, one core):

| What | Cost |
|---|---|
| Whole match, `Fast` | 4.7 s |
| Whole match, `Live`, nothing published | 5.6 s |
| Every-tick snapshot archive | 869 MB of poses + 169 MB of motion evidence |

Storing frames is out. Seeking re-simulates without publishing up to the target and presents from there; the
end-of-match events hash check already proves the re-simulation is the recorded match.

* **Forward seek** (target after the worker's frontier): the running worker re-simulates silently to the target
  and restarts the presentation there. This is what `SkipAhead` becomes, for live matches too.
* **Backward seek:** the replay runtime is closed and a new one opened with `startAt = target`; it re-simulates
  silently from kickoff. At most about 6 s for the last minute of a match.

## Contracts

### Core

1. `Match/Contracts/MatchSimulationTypes.fs`: rename `MatchRunMode.Replay of snapshotEvery` to
   `MatchRunMode.Archive of snapshotEvery`, doc comment: records presentation snapshots every N ticks into
   `MatchOutcome.Replay` for tests and tools; never used to watch a match. Follow the compiler everywhere
   (`MatchSession` read/write mode keeps the numeric tag 2, `WorldRunner`, tests, benchmarks). No behaviour change.
2. `Match/Simulator/MatchSession.fs`:
   * `advanceSilentlyTo target session = advanceToCore false target session`, public, with a why comment: a seek
     has no viewer to feed; in `Live` mode the motion journal is still drained into the session archive, so the
     next published boundary continues it.
   * `motionCursor (session: MatchSession) : uint64 = session.MotionCursor`: the evidence sequence the next
     published boundary starts at.
   * `restore` replays its frontier with `advanceSilentlyTo` instead of `advanceTo` (it discards the output).

### Client, animation and presentation

3. `Animation/MotionRuntime.fs`: constructor gains `?startTick: int` and `?startCursor: uint64`. Initial
   `frontier = startTick - 1` (default `-1`) and `cursor = startCursor` (default `1UL`). Why comment: a runtime
   opened by a seek starts at the first boundary it will receive.
4. `AppState/MatchPresentation.fs`:
   * New case `PresentationDiscontinuity.Seeked`.
   * `member Reset(startTick: int<subtick>, startCursor: uint64)`, under the lock, ignored when closed:
     clear every slot, `count <- 0`, `writeIndex <- 0`, `events.Clear()` (sequence counters stay monotonic so
     reader cursors remain valid), `clockStarted <- false`, `underrunActive <- false`, `pendingSeek <- true`,
     motion recovery request, in-flight and error cleared. Replace the motion runtime by a new
     `MotionRuntime(AnimationConfig.defaultConfig, identity, startTick = int startTick, startCursor = startCursor)`
     with the old runtime's identity (close the old one). Set `archiveEnabled <- false`.
   * `Publish`: when `pendingSeek` is set, the pose's `EpochStart` is `Some Seeked` (epoch increments as for any
     discontinuity) and the flag clears.
   * `PublishMotion` appends motion checkpoints only while `archiveEnabled`: the checkpoint archive must be
     contiguous from kickoff, and a reset timeline is not.
   * Handle `Seeked` in every match over `PresentationDiscontinuity` (compiler lists them).

### Client, live contract and runtime

5. `Core/Match/Contracts/LiveMatch.fs`:
   * `LiveMatchStatus.Seeking`: re-simulating to a requested moment; the viewer holds its last pose.
   * `LiveMatchMsg.SemanticReset of revision * LiveClock * (int * int) * TeamStats * TeamStats * MatchEvent list`:
     like `SemanticReceived`, but `Events` and `Commentary` are rebuilt from the given events instead of appended
     (`applyEvents fixture events { current with Events = []; Commentary = [] }`), because a seek jumps the story.
     Same revision guard and status rule as `SemanticReceived`.
   * `SkipAhead` doc: the worker re-simulates unpublished up to the tick and the viewer restarts there.
6. `AppState/MatchRuntime.fs`:
   * Publication `SemanticReset of LiveRuntimeSemantic`; `AppState` maps it through a new
     `MatchSlice.applySemanticReset` (same as `applySemantic` but with `SemanticReset`).
   * `MatchRuntime` takes `source: LiveMatchSource` and `startAt: int<subtick>` as constructor arguments; the
     source is no longer derived from the run mode. `buildJob` uses `prepareReplayJob` for `RecordedReplay`. Both
     sources run `MatchRunMode.Live LiveExecutionPolicy.defaultPolicy`.
   * A local `seekTo (target: int<subtick>)` in the worker:
     1. `enqueue dispatch (StatusChanged Seeking)`.
     2. Loop while tick < target, flow is not `MatchEnded`, no failure and not cancelled:
        `receiveRecordedCommands ()`, then `MatchSession.advanceSilentlyTo stop` with
        `stop = min target (next recorded command tick) (tick + 4000)`; the 4000 tick cap keeps cancellation
        responsive.
     3. `presentation.Reset(tick + 1, MatchSession.motionCursor session)`, publish `Replay.capture session.World`.
     4. Enqueue `SemanticReset` with the clock, score, stats, evidence and the last 128 entries of
        `session.Events`; reset `lastSemanticTick`, `lastSemanticScore`, `lastEvidence`, `pendingEvents`.
     5. Status back to `Paused` or `Live speed`; `nextAdvance <- now`.
     6. If the flow reached `MatchEnded`, run the same end-of-match handling as after a normal advance (factor it
        into one local function used by both paths).
     7. The silent part stops two presentation delays (2 s) before the target, and the worker then publishes the rest
        unpaced, even when paused. Motion matching reads a second of future trajectory and the viewer runs a second
        behind it; a seek that landed exactly on its target left the viewer with no motion to animate, and a held
        replay never presented a frame.
   * `SkipAhead untilTick`: accept immediately and `seekTo (min untilTick MatchSession.maxTicks)`; a target at
     or before the frontier is accepted and does nothing. Remove `skipUntil`, `skipping` and every branch that
     depended on it.
   * Startup: when `startAt > 0`, call `seekTo startAt` after motion configuration and before the loop; the
     starting-snapshot publish stays for `startAt = 0`.
   * `RecordedReplay` never pauses at half time and, at `MatchEnded`, after the events hash check, publishes
     `StatusChanged Finished` instead of `Finished result`: there is nothing to commit.
   * `MatchRuntime.createReplay matchId gs clock saveOpt (startAt: int<subtick>)` consumes the armed initial hold
     like `createUserMatch`, and creates the runtime with `RecordedReplay` and `startAt`. Live creation passes
     `LiveSession` and `0<subtick>`.

### Client, MVU and views

7. `AppState/ReplayMoments.fs` (new, compiled before its users):
   `type ReplayMomentKind = Goal | Save | Woodwork | Penalty | FreeKick | Corner | YellowCard | RedCard |
   Substitution`, `type ReplayMoment = { Tick: int<subtick>; Kind: ReplayMomentKind; Side: ClubSide option }`,
   `ofEvents: MatchEvent list -> ReplayMoment list` sorted by tick, with the same `EventData` cases the capture
   tool logs today (`Goal`, `Save`, `Woodwork`, `SetPieceAt` Corner, FreeKick and Penalty, `Card`,
   `SubstitutionApplied`). `leadSeconds = 8`: a moment is watched from its build up.
8. `Msg.fs`: `LiveMsg.OpenReplay of MatchId * startAt: int<subtick>` and `LiveMsg.SeekReplay of int<subtick>`.
   * `OpenReplay`: close any runtime, `navigateTo (Route.Match(matchId, MatchPhase.Replay))`, create the replay at
     `startAt`, keep the selected camera (`clearState` resets it), start it.
   * `SeekReplay target`: target after `live.Clock.Elapsed` submits `SkipAhead target` through the same command
     path as `TogglePause`; otherwise dispatch `OpenReplay(matchId, target)`.
   * `ConfirmFinish` with a `RecordedReplay` live state: close the runtime, clear the match slice and navigate to
     `Route.Match(matchId, Postmatch)`, before the pending result check.
9. `Views/Components/CareerJourney.fs` `review`: a "Watch replay" button when there is no pending result and
   `state.WorldClock.MatchExecutions` has the match; it dispatches `OpenReplay(matchId, 0<subtick>)`.
10. `Views/Components/ReplayTimeline.fs` (new): a full width bar shown above the dock when the source is
    `RecordedReplay`. Total length is the tick of the fixture's last recorded event. Markers per moment, colour by
    kind as `EventTimeline.eventColor`; a playhead at `live.Clock.Elapsed`. Clicking the bar seeks to that tick;
    clicking a marker seeks to `tick - leadSeconds`. Previous and next moment buttons seek to the nearest moment
    start before and after the playhead. The moments come from the played fixture's events in `GameState`.
11. `MatchViewer.fs`: pause and speed are enabled for both sources (`live.Status <> Finished`); manager tools
    stay `LiveSession` only. `statusText` and `statusColor` handle `Seeking`.
12. Localization, English and Spanish: `WatchReplay` ("Watch replay", "Ver repetición"), `MatchSeeking`
    ("Seeking", "Buscando"), `ReplayPreviousMoment` ("Previous moment", "Momento anterior"), `ReplayNextMoment`
    ("Next moment", "Momento siguiente").

### Capture tool (`Shots/Shots.fs`)

13. Every `--video-*` flow first plays the match once: open the live match as today, `SkipAhead maxTicks`, wait
    for `Finished`, dispatch `ConfirmFinish` and wait until the fixture is played.
    * `--video-find-goals`: moments from the played fixture's events through `ReplayMoments.ofEvents`; write
      `goals.txt` (goal seconds) and `moments.txt` in today's line format; no 5 second scan.
    * Recording (`--video-goal-at`, `--video-goals`): for each clip start, `armInitialHold start`, dispatch
      `OpenReplay(matchId, toTick start)`, wait until the status is no longer `Seeking` or `Preparing` and the
      pitch is presented, reselect the camera, then release the hold while ffmpeg records the 3D surface's X11
      window by id (`x11grab -window_id`) for the clip's length: a clip costs its own length. The old frame loop with
      `HoldAt` and one `import` per frame (0.55 s a frame, 2.5 min a clip) stays behind `--video-stepped`. A screen
      region does not work under a Wayland desktop: the root window shows nothing of the viewer. Clips may come in
      any order. Remove `recordInFreshRun`.
    * The viewer scene flow is unchanged.

## Out of scope

Slow motion speeds, storing replays in saves, the viewer scene flow, key-moment pacing, `MatchOutcome.Replay`.

## Acceptance

Core (`FootballEngine.Core.Tests`):

* A silent advance reaches the same match: two `Live` sessions on one input, one advanced with `advanceTo` in 400
  tick slices to 4000, the other with `advanceSilentlyTo 4000`; equal events hash, RNG and tick; both then finish
  with equal final events hash. The silent one's next `advanceTo` returns motion whose first boundary is tick 4001
  and whose first evidence sequence, if any, is the `motionCursor` read before it.

Client (`FootballEngine.Client.Tests`):

* A `MotionRuntime` opened at `startTick = T, startCursor = c` accepts the boundary at `T` and rejects `T - 1` and
  `T + 1` with `NonCanonicalBoundary`.
* After `Reset`, `TryReadAfter 0UL` returns no earlier pose, the next published pose has `EpochStart = Some
  Seeked`, `Sample` holds it, and `ReadEventsAfter` from the old cursor returns only events published after the
  reset.
* `SemanticReset` replaces events, commentary and score; a later `SemanticReceived` appends again.
* `ReplayMoments.ofEvents` turns goals, saves, woodwork, set pieces, cards and substitutions into moments at their
  ticks, sorted; contacts and passes produce none.
* Runtime: a replay opened at 30 s (1200 ticks) publishes its first pose at tick 1200 and a `SemanticReset` whose
  clock is 1200; a `SkipAhead` in a live runtime lands with a `Seeked` epoch. Use the existing runtime test
  fixtures.

Capture tool: `--video-find-goals --match-seed 7` finishes in under a minute and writes `goals.txt`; one
`--video-goal-at` clip encodes an mp4.

Gate: `dotnet fsi scripts/test.fsx` green, Fantomas clean, `FootballEngine.Benchmarks` builds.
