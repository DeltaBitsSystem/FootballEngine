# A3: stride clock

Phase A3 of `docs/plans/ANIMATION_AND_BALL_PLAN.md`. Core gets a stride clock, ball carry touches are timed on it, and
the matcher uses it as a feature and to lock its playback rate. The goal is for the ball and the feet to share a
cadence: Core touches the ball with the foot that is swinging under the hips while the other one supports.

## What changed

**Core**
- `GaitConfig` in `BalanceConfig`, with validation:
  - Cadence (steps/s) = `StepFrequencyBase + StepFrequencyPerSpeed·v`, capped at `StepFrequencyMax` and scaled by
    `sqrt(ReferenceLegLength / leg)`.
  - Default: 2.0 + 0.22·v, which is ~2.7 steps/s at 3 m/s, ~3.2 at 5.5 and ~3.9 at 8.5. That fits the literature
    and the CMU corpus, which measures 1.4–1.8 cycles/s between 3 and 5 m/s.
- `TeamFrame.GaitPhase` in [0, 1): the first half has the left foot in support, the second the right.
  - It advances in `ContactDynamics`, where bodies are actually integrated. `PlayerBodyPhysics` is not in the
    scheduler table.
  - It holds while airborne or below `StandingSpeed`, so landing or starting off resumes the stride where it was.
- `Gait` module (`Shared.fs`): one cadence law, used by integration and by carrying.
- Carry touches (`Possession.fs`):
  - The first touch (reception) is immediate.
  - After that, a touch needs at least `CarryTouchInterval` since the previous one, and the phase must cross
    `CarryTouchPhase` (0.08) after the support foot's strike.
  - The touching foot is the swinging one (right in the first half, left in the second).
  - The impulse aims for the ball to sit `CarryOffset` ahead of where the carrier will be at the next touch, within
    boot reach. It integrates the rolling ball (two corrections), so it is not aimed at a fixed lead time.
    `CarryLeadTime` was removed.
  - Standing still, or in the air, the interval alone governs.
  - **Announced touches.** Each touch computes the next one: tick, foot and predicted ball. It publishes it as a
    `Prepared` Dribble task, stored in `Ball.AnnouncedTouch`. That touch is a commitment: it fires exactly on its
    tick, and the confirm reuses the TaskId with the contact on the aimed face. Loss of control, a kick, another
    touch or staleness withdraws it (`Cancelled`). The viewer then has a swing to plan instead of an instant to react
    to.
- `MovementConfig.CarryEffort` (0.4): a carrier uses 40 % of the jog-to-sprint range. Before, carrying steered at
  full sprint.
- `MotionRootSample.GaitPhase` is published in the motion stream and validated in [0, 1). Replay goes to
  `formatVersion` 12.

**Viewer (matcher)**
- Capture phase taken from the contact labels: left strike = 0, right strike = 0.5, linear in between. Gaps of more
  than a second are pauses (NaN).
- Feature vector of 29 dimensions: adds sin/cos of the phase (`PhaseWeight` 2). It is masked when Core is not
  striding.
- Playback rate:
  - Running (≥ `RunningSpeed` 2.5 m/s), it follows Core's cadence plus a correction that closes the phase gap in
    `PhaseLockSeconds` (0.3 s).
  - Walking, it follows speed, with the same correction.
  - The rate changes at most `RateSlewPerSecond` per second.

**Viewer (task planner)**
- A revision of the same TaskId keeps the swing in flight, so a confirm does not restart it.
- A task is selected only once its `StartedAt` arrives.
- The landing is not retargeted before recovery starts. This keeps the overlay pool at 128 within the 2 MiB budget.

## How to reproduce

```bash
dotnet fsi scripts/test.fsx --project Core --filter "FootballEngine.Core.FootballEngine.Core.Systems.Ball"
dotnet fsi scripts/test.fsx --project Client --filter "FootballEngine.Client.Motion"
dotnet build Tools/MatchViewer3D/StadiumRendererProbe.fsproj -c Release
dotnet Tools/MatchViewer3D/bin/Release/net10.0/StadiumRendererProbe.dll --motion-corpus --matched docs/evidence/motion-corpus/matched-a3.json
```

## Unit acceptance

- Core (`PossessionTests`):
  - A running carrier touches only on a crossing of the touch point, with the swinging foot (right in left support,
    left in right support).
  - In flight there is no touch, and a crossing one tick old is not repeated. Standing still, the interval governs.
  - A carry touch announces the next one. That touch fires on the announced tick with the announced boot and
    confirms the same task. Losing the ball cancels whatever is announced.
  - Cadence rises with speed, falls with leg length, is zero when standing, and stays within the literature at
    3 and 5.5 m/s.
- Matcher (`MotionMatching`): a 4.5 m/s run with Core's clock offset by 0 and by 0.5 cycles. Both must be
  followed, so a coincidence fails.
  - Mean gap < 0.08 cycles, maximum < 0.2.
  - At ≥ 90 % of the touch points the support foot is planted in the capture.
- The existing matcher tests still pass: pops, planted sole, cold evaluation and budget.

## Real play (seed 1, first half, `matched-a3.json`)

Carry touches over the whole half, judged at Core's contact instant:

| Measure | Value |
|---|---|
| Carry touches | 1,180 |
| Announced ahead | 1,161 (98 %) |
| Capture phase within 0.1 cycle of Core's | 544 (46 %) |
| Support foot down in the capture (label) | 468 (40 %) |
| Support lock ≥ 0.5 | 203 (17 %) |
| Swing foot locked (touch with a planted foot) | 7 (0.6 %) |
| Boot to contact point, median / p90 / p95 | 1.25 cm / 3.3 cm / 19.3 cm |
| Same, announced touches with an active overlay, median / p90 / p95 | 1 / 2 / 5 cm |

Before the announcement, the boot was 0.59 m from the contact point (median). Windows (same meter as A2): 4 pops and
0.13 m of skating over 1,620 frames (A2: 10 and 0.04 m).

## Honest limits

- **No capture above ~5.5 m/s.** The shipped CMU corpus has 12 frames above 6 m/s, and the one fast clip runs at
  5 steps/s. About 30 % of Core's touches happen at ≥ 7 m/s, where neither phase nor support can be followed
  within ±25 % of playback rate. Sprint capture is A5 work.
- **The p95 contact error misses the 3 cm target.** About 43 touches had no overlay active at the commit, so the
  boot stays on the clip. The cause is not yet isolated: an effector mismatch or an overlay reservation.
- **Support down at only 40 % of touches.** The phase is aligned at 46 % of touches in real play, against ≥ 90 %
  in the controlled run. Turns, accelerations and the cut-off above 5.5 m/s break the lock. The lock also yields
  under slip between the root and the capture stride.
- **No video.** Numbers come from the meter; a 60 Hz visual review on real play is still pending.
