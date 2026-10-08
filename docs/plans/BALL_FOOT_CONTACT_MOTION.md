# Ball movement and foot contact

## Outcome and scope

Replace tethered dribbling, immediate foot strikes and clock-invented visual rotation with deterministic physical motion and an explicit contact timeline shared by engine and viewer. Keep one current contract; bump the replay format and reject older versions rather than retaining a legacy branch. Sporting results remain Core-owned. FIFA is a quality reference, not an unverified claim of superiority.

## Existing failures

- Controlled balls bypass physics and are moved toward `(playerX + attackingDirection * 0.4, playerY)` on every possession tick. Turning, real foot choice and momentum cannot explain their path.
- All kick producers replace velocity immediately; no reach check, preparation or recoverable contact record exists. Arrival search can grant contact several metres away and ignores ball height.
- Magnus bends world Y regardless of shot direction. Ground gravity can manufacture recurring tiny bounces. Spin does not produce a persistent orientation.
- Character manifests already contain left/right foot contact markers, but the renderer only samples locomotion and maps kicking to idle. Ball yaw is synthesized from match time.

## Design

### Core contracts

Use a world-space angular velocity and unit quaternion orientation. Preserve the documented ball-position height convention (height above ground at the ball's underside); physical contact points use the centre at `height + radius`. Kick requests create one pending strike and a foot-motion envelope: actor, foot, kind, start/contact/recovery ticks and world contact point. No per-player lists or tick history are needed. Only committed contacts apply impulses and update last touch. Cancellation clears preparation without applying a kick.

All behaviour constants belong to BallConfig: radius, inertia ratio, slip friction, rolling resistance, restitution/bounce friction, contact reach/height, windup/recovery, touch cadence and dribble lead. Validate finite/range constraints. Time comes from SimulationClock.

### Scheduling and kicks

The sole scheduler table owns the order. Ball integration runs every movement tick; a ball-owned foot-strike system then commits due requests after validating participation, ownership and geometry. Losing possession, leaving reach or entering a stoppage cancels the request. Retarget from the actual ball position at contact; never teleport to the request origin. Shift arrival hints by windup. Offside assessment belongs to actual pass contact, not the request tick. Existing action producers use this one request path. Throw-ins are hand actions and must not be labelled foot kicks.

### Continuous motion and dribbling

Controlled outfield balls also integrate. The possession system applies bounded intermittent touches, not position constraints; velocity, body facing and technique determine the lead. A ball outside controllable reach becomes loose. Goalkeepers holding a ball are explicitly excluded from foot dribbling.

Flight uses direction-correct `omega cross velocity` Magnus, drag and gravity. Ground impacts resolve at time of impact inside the step; low-energy landings settle without gravity-induced jitter. Tangential impulse couples translation and rotation using sphere inertia and Coulomb friction. Rolling resistance removes energy and a ball at rest stays at rest. Quaternion integration and normalization are constant-size work, without render-rate dependence.

### Replay and visual motion

Replay captures orientation plus pending/recovering foot motion and serializes them in the new format. Projection uses the same motion envelope and contact marker. Contact-aware ball interpolation must not smooth a velocity discontinuity through the foot or invent pre-contact acceleration. Quaternion interpolation preserves phase during pause and seek.

Characters use authored left/right pass/shot/receive clips aligned to their manifest contact times. A bounded two-bone leg solve aligns the striking boot with the authoritative contact point near impact; anatomical segment lengths remain fixed. IK affects only presentation and the existing small joint palette; GPU instancing/skinning remains unchanged. No visual code changes ball position or sporting outcomes.

## Gates

1. Build/test Core physics and config: rest, impact TOI, dissipative rolling/sliding, opposite-direction Magnus, normalized orientation, deterministic replay.
2. Build/test contact lifecycle: no pre-contact impulse, exactly one contact, unreachable/lost-owner/inactive cancellation, real contact origin, dribble touches and loose-ball transition, height/reach constraints and actual-contact offside.
3. Migrate replay/Client and test serialization rejection/round trip, contact interpolation, pose markers and bone lengths. Build after each contract gate.
4. Inspect native close-up contact sequences and a real match; screenshots alone cannot prove temporal quality. Run renderer/per-tick diagnostics, full Core/Client suites and diff checks. Preserve unrelated shared-worktree changes.

## Performance boundaries

One pending action and one motion envelope per ball; no per-tick allocation of candidate histories. Analytic integration and fixed-size quaternion/IK work; no generic rigid-body engine or mesh collisions on the simulation thread. The foot is an explicit reachable effector proxy, not an exact triangle collision solver. Future whole-body collisions, deformable boots, net simulation and asset-detail changes are separate measured features.
