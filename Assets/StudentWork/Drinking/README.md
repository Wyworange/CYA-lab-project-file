# Chunzi — Drinking module (2026-10-09)

This is an additive handoff module, not a replacement Unity project. Namespace:
`DigiPhant.StudentWork.Drinking`. No Python, sockets, new packages or asmdef.
The existing local GroupPerformance scene and old drinking implementation are preserved.
Do NOT run the old drinking implementation and this component together.

## 1. Trigger / performer

Attach `DrinkingInteraction` to the existing **DigiPhant Controls** GameObject.
`performer` is public, defaults to **1 (Chunzi)**; the integrator assigns the claimed role.
Read ONLY the shared controller's calibrated `LeftHandHeight` and `RightHandHeight`
through `TryReadMovement`; this retains its tracking confidence / freshness checks.
Both values must be **> +0.10 relative to calibrated rest** for **2 valid seconds**.
Crossing wrists, elbow tracking and Python X flags are not required.
One raised hand does not count. Invalid tracking up to 0.20 s pauses the hold and
adds no time; longer loss resets it. A valid lowered hand resets the hold immediately.
BOTH values must be <= +0.05 for 0.40 valid seconds between drinks. Release during
the active sequence is recorded, so the next deliberate raise can start after it finishes.
An uninterrupted raised pose never retriggers. Cancel still requires neutral. Short tracking loss pauses neutral; longer loss resets it.
No additional timed cooldown. After an interrupted pre-trigger hold, retry directly.
`HoldProgress`, `IsRecognizing`, `AwaitingNeutral`, `SequenceStarts`, `Status` are available
for the integrator's UI. The manual debug button deliberately bypasses pose/rearming
and calibration, but still checks the rig, water distance and action lock.

## 2. Sequence and restoration

Uses the working local manual animation's timing and easing, with a stronger post-drink motion (2026-10-09):
1. Lower head +28 degrees / extend trunk -16 degrees, SmoothStep, 1 s.
2. Hold 2 s.
3. Raise head to -25 degrees / lift trunk -28 degrees per joint (forward/up, not toward chin), SmoothStep, 1 s.
4. Trunk sway +/-5 degrees, 3 cycles with eased envelope, 1.5 s.
5. Smooth return, 1 s. Total **6.5 s**.
Local pitch/sway axes are captured from the travel root right/up at entry,
so turning toward the water does not reverse head lowering or lifting. Snapshot the entire rig's local positions/rotations and the travel
root's world pose. During the action restore the snapshot then apply the head/trunk
pose, at LateUpdate execution order 1100. Completion, Cancel, and OnDisable restore
it exactly and release the acquired gate once. No bone writes while inactive.
The sequence completes even if the gesture is released. No Animator bool/clip change.
No teleport in TryStart. Water is never moved by the action.
DrinkingWaterSpray is added automatically with the interaction. It reads IsSprayingWater
(3.75–5.50 s), uses the last two assigned trunk bones for nozzle position/direction,
and emits world-space blue water droplets with gravity. It never writes rig/root poses.
The spray stops before the return phase; remaining droplets fade within 0.85 s.
URP particle Unlit shader required. Verify rendering if making a player build.

## 3. Preconditions / shared ownership (integration REQUIRED)

Public contract: `bool IsActive`, `bool TryStart()`, `void Cancel()`, `string Status`.
Assign `travelRoot`, `rigRoot`, `head`, unique `trunk` bones, and `water`.
The context menu **Assign existing DigiPhant rig** copies references from the existing
controller's Head turn and Trunk curl controls and locomotion travel root/Animator.
Check those references in the target project; the component does not guess other rigs.

Maximum horizontal distance from travel root to water is **6 m** (field configurable).
This is a coarse near-station check, NOT IK or a guarantee the trunk tip touches water.
Too far => `Too far from the water`; missing references => explicit setup message.

The `actionGate` MonoBehaviour must implement **IDrinkingActionGate**:
- `CanRecognize(owner)`: false while fruit, wood or another action owns the elephant.
- `TryAcquire(owner)`: synchronously acquire the SINGLE team action lock; return false
  if busy. Pause locomotion/gait and other body/trunk overlays BEFORE returning true.
- `Release(owner)`: release only this owner's lock and restore normal input processing.

The component does not create a shared lock or rely on FruitInteraction's mutable
pause flags. Missing gate rejects start with `Shared action lock not connected`;
busy gate rejects with `Another action owns the elephant`.
The shared controller must check **IsRecognizing** to reserve walking/steering
while both hands are raised, including while awaiting neutral; release the reservation
when the gesture is released. Only Chunzi left-hand input drives forward; confirmed
both-hands-raised input belongs to drinking. Do NOT require a valid resting right wrist
for left-hand travel: seated framing often loses that wrist, and this caused a local regression.
Because the detector runs at 1100, observe the previous frame's reservation before
gait, or call a centrally scheduled input stage in the final integration. The shared
lock must suppress root motion and all competing writers for the whole active sequence.

**Solo preview** checkbox is only for camera-free isolated testing with ALL competing
actions disabled. It permits no-gate start and restores the rig/root after other local
writers, but is NOT a team lock and does NOT stop upstream input processing. Default
false; never enable in the presentation with fruit/wood active. This module alone
cannot prove three-action mutual exclusion before Yuwen connects the shared lock.

## 4. Props / placement

`Prefabs/WaterPool.prefab` contains the existing basin's copied mesh geometry and new
URP/Lit materials under this folder, with no external Assets dependencies.
Root name **Student drinking station**. Parent: scene root (identity transform).
World position **(9.557159, 0.010151863, -21)**; rotation **(0,0,0)**; scale (1,1,1).
Assign `water` to its root Transform. It is inside the team's 30 m stage and away from
the known feeding bush at (-15,0,-5.1). Aurora's wood placement is not supplied; Yuwen
must check that location for overlap. Do not add a second basin if one already exists.
Placement screenshot: `Placement.png` (existing GroupPerformance test arrangement).

Optional MANUAL setup for a drink-only test (not done by TryStart): Elephant Travel
position (9.56,0.01015,-16), rotation (0,180,0). Local test camera position
(15.877946,4.7986894,-7.983191), rotation approximately (16.714,-138.652,0).
No scene file is included. These camera coordinates are optional and scene-specific.

## 5. Camera-free test

In a clean project copy, disable the old drink action and other fruit/wood actions.
Instantiate the prefab; add DrinkingInteraction to DigiPhant Controls and assign rig.
Use **Test sliders** with sliders zero. Enable **Solo preview** on this component,
then **Debug: drink** (bottom-right panel, 265x220).
Expected: one START log, static legs/root, full 6.5 s sequence, one END log, restored rig.
During the sequence click Debug again: no restart. Start again, click Cancel:
restored pose immediately. Move away >6 m: explicit distance rejection.
Turn Solo preview OFF before team testing; wire the gate, then repeat with the shared
lock and test fruit/wood blocking in both directions.

Editor menu **DigiPhant > Chunzi Delivery > Validate drinking module** runs isolated
camera-free state/rig tests without modifying the open scene. Exit Play first.
The build menu is for Chunzi's local project only (uses GroupPerformance as prop source).

## 6. Export / import

Select only Assets/StudentWork/Drinking, Export Package, UNCHECK Include dependencies.
Do not export Elephant, DigiPhant, scenes, Python, ProjectSettings or Packages.
Unity generates all .meta files. The module uses Assembly-CSharp and IMGUI, no legacy Input.
Target handoff: Unity 6000.6.3f1 / URP 17.6.0. Local validation: 6000.6.4f1 / URP 17.6.0.
The patch-version difference needs a test on Yuwen's machine; do not overwrite her settings.

## 7. Requested shared changes (Yuwen applies; NOT included in this package)

- `Assets/DigiPhant/Runtime/DigiPhantController.cs`, ApplyControls / gesture evaluation:
  suppress all additive bone inputs while the central action lock is held; reserve
  locomotion during DrinkingInteraction.IsRecognizing. Disable/remove the legacy local
  `drinking.Update`/`drinking.Apply` path IF present. Do not import Chunzi's modified controller.
- `Assets/DigiPhant/Runtime/DigiPhantLocomotion.cs`, Evaluate: honor the central lock
  before translating/rotating or evaluating gait/root motion. Existing fruit pause flags
  alone are insufficient because FruitInteraction rewrites them each frame.
- Integrator's shared action lock and role assignment: provide the IDrinkingActionGate
  adapter, arbitrate fruit/wood/drink, assign claimed Chunzi slot to performer (P1 default),
  and wire the actionGate. No exact line numbers for not-yet-created integrator code.
- Scene setup only: add this component, prefab/references and placement; do not merge
  GroupPerformance.unity or overwrite the integrator's stage, camera or start settings.
- Shared camera lifecycle: cancel a running action explicitly on session teardown if
  desired; do not treat a brief landmark loss as cancellation.

## 8. Known limits / honest validation status

The supplied Yuwen reference snapshots were read only, not imported or modified.
No live three-person camera test, role-claim test, final shared-lock test or wood/fruit
coexistence test has been performed. Chunzi confirmed local left-hand travel and drinking in live camera testing on 2026-10-09.
The local core spray was visually verified in Unity. The exported independent module
is editor-validated; its three-person integration and target-machine spray need testing.
The personal gesture pretraining/Python branch is not shipped or needed.
This is procedural rig animation, not water-contact IK. Target rig/pitch axes need
visual confirmation. Distance/placement alone does not make a physical drinking simulation.
