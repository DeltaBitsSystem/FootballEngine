# Player motion: runtime rig and action integration

## Scope and authority

Improve the current original 60-joint player in the real match viewer. Keep the
mesh, clothing, textures and physical proxy as the development baseline. Core
owns root position, facing, sporting contacts and their times. Presentation may
pose joints but must never move the sporting root, delay a kick, or alter a result.
No imported clips, stock meshes, paid generation or facial remodeling.

This is a design for implementation and verification, not evidence of completion.

## Findings from the current implementation

- `MatchPitchControl` uses `MotionPaletteEvaluator`, which calls
  `PoseEvaluation`; its final pose does not sample the 18 GLB clips.
- `BodyPose` starts from bind pose. Procedural IK targets, not authored clip
  changes, determine the live limbs.
- The source authoring script applies half rotations to hip, knee, elbow and
  axilla support bones. Their runtime counterparts are siblings of the driven
  joints, so rotating a source joint's descendant subtree does not reproduce
  those corrections. The current evaluator has no corrective-joint pass.
- `TaskPlanner` retains contact reservations, but the sealed `ActorPlan` and
  evaluator do not use them. There is no visible action-contact override in
  the final palette.
- Both feet are oriented flat at every evaluation; pivot/roll and swing-foot
  articulation are absent. Arms follow fixed hand-height targets.
- The live rig's foot pivots are approximately Y=0.057769 m in bind space.
  Boot geometry spans Y=0.005..0.1643 m and Z=-0.075..0.275 m. The loader's
  hardcoded world-offset sole Y=-0.095 m therefore does not describe this rig's
  sole (approximately -0.052769 m below its pivot).
- Independent evaluation using the preserved baseline executable confirms the
  visible consequence: each actual boot sole reference point is Y=0.042231448 m
  in idle at standard stature, while both leg residuals are `ValueNone`.
  The existing socket residual therefore misses about 4.22 cm of sole float.
  Reproducer: `/tmp/opencode/motion-ground-check/Check.fsproj`; results:
  `/tmp/opencode/motion-ground-check.log`. This is a baseline measurement,
  not evidence that the new implementation has fixed the offset.
- Native baseline inspected: `/tmp/opencode/motion-design-before.png`.
  Blender MCP is unavailable; any Blender comparison must use isolated batch
  execution against preserved sources.

## Chosen implementation

### 1. Asset-derived contacts and corrective joints

Define explicit left/right ground-sole and ball-contact sockets from the actual
boot geometry in bind space. Ground sockets lie on the lowest sole plane with
fore/aft position inside the sole footprint; ball sockets lie on the forward
boot surface. Preserve the authored coordinate system, report the chosen
points and validate against the geometry. Generate metadata reproducibly;
do not silently reuse the old player's offsets. Convert positions through the
inverse bind transform, not just inverse rotation. Keep the skeleton's physical
limb lengths unchanged.

Represent corrective relationships explicitly in the rig: support node, source
node and half-rotation weight, with parent/bind validation. After final limb IK,
derive source rotation relative to its bind orientation in its parent's space,
apply half that delta to the support's bind rotation, and propagate its own
descendants. Do not rotate the source twice. Compare runtime matrices against
known poses evaluated in the existing Blender source, including rotated parents
and mirrored sides. Resolve matrix/quaternion order from those checks.

### 2. Coherent procedural locomotion

Retain canonical sequential support planning and immutable retained plans.
World-space stance anchors must stay fixed while supported. Use continuous
Hermite swing trajectories; preserve position/velocity when retargeting and
bound target changes during turns. Derive placement from actual velocity and
facing, so lateral/backward travel does not assume forward walking. Account for
angular change when deciding a foot must step even with negligible translation.
Alternate release ownership deterministically. Stop into a balanced stance;
do not reset both feet or phase on every speed threshold.

Allow bounded visual pelvis adjustment to keep both leg targets feasible, with
an explicit residual when impossible. Keep actual leg lengths and root unchanged.
Use bounded heel/toe roll and swing orientation instead of forcing every foot
flat. Arm swing should counter the leg cycle, blend continuously with speed,
and retain plausible elbows; idle must not retain the bind A-pose. Put new
visual tuning in validated `AnimationConfig`, separate from sporting config.

### 3. Consume actual contact evidence

Apply prepared/confirmed/cancelled foot-task reservations to the actor's sealed
plan at canonical ingestion, before retention. Use `AvailableAt` to prohibit
future knowledge; never rewrite a previously published plan. Prepared contacts
may anticipate `PredictedPoint`; confirmation uses the actual point only when
available. Late or unreachable contacts produce explicit residuals rather than
moving the root, inventing preparation, or changing the ball.

The acting foot follows continuous preparation/contact/recovery segments using
the ball-contact socket. The other foot retains a feasible support. Cancellation
and task end return continuously to locomotion. Preserve actor generation and
task identity, bounded reservation capacity, deterministic arbitration and
checkpoint recovery. This unit handles foot strikes, reception and carry
evidence; it does not claim new keeper dives, headers or throw-in animation.

#### Contact boundary details

- A late confirmation must not replace the sporting commit time with `now +
  epsilon`. If the foot was not at the confirmed point at that time, preserve
  the current continuous pose, report the miss/insufficient preparation, and
  recover. Do not claim a retrospectively successful contact.
- Start cancellation recovery once with a fixed finish time. Subsequent ticks
  reuse it or retarget with the same finish and continuous position/velocity;
  extending the finish on every tick would prevent recovery from terminating.
- Ground and ball sockets are different points on the boot. Do not switch
  abruptly between them when an overlay becomes active/inactive. Use a smooth
  socket blend from ground at preparation start to ball at commit and back to
  ground at recovery end, with zero blend derivative at the boundaries. The
  target trajectories describe that blended point; the local socket passed to
  IK uses the same blend. Initialization must evaluate the currently active
  blended point so starting or replacing a task does not move the ankle.
  A direct socket switch would produce a jump even if the target curve is C1.

## Acceptance and evidence

- Numerical scenarios: idle, start/walk/run/sprint/stop, side/back movement,
  90/180-degree turns and pivots, both feet striking/receiving, cancellation,
  late confirmation, unreachable target and actor replacement.
- Check stance drift and sole penetration, segment-length preservation,
  quaternion/matrix validity, joint limits, continuity at all phase boundaries,
  actual socket-to-contact residual and corrective-joint reference agreement.
  Never suppress impossible-target residuals to improve a metric.
- Evaluate retained poses in different render orders/rates and after checkpoint
  restore; identical canonical input/time must give identical poses. Root and
  sporting state must be unchanged by animation.
- Inspect native moving evidence at matching camera/scenario/time before/after,
  including near views and match cameras. Fix the synthetic probe's crossing
  actors for isolated animation review rather than claiming their root overlap
  is a skinning defect. Also inspect a real deterministic live-match sequence.
- Measure warmed Release ingestion and palette evaluation for 22 actors with the
  same live rig and inputs, reporting median/p95/p99 and allocations. Preserve
  allocation-free warmed pose evaluation; do not promise 120 FPS from CPU timings.
- Build/tests must cover animation, character contracts, motion replay and client
  integration. Preserve shared engine work and historical evidence. Record any
  animation configuration/checkpoint format changes explicitly and reject old
  incompatible motion checkpoints rather than introducing silent fallbacks.

Terra implements this design in medium/Goal Mode. Sol reviews numerical results,
actual pictures and moving evidence before accepting the runtime integration.
