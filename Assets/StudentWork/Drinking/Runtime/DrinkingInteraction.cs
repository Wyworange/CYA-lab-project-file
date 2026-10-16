using System;
using System.Collections.Generic;
using UnityEngine;

namespace DigiPhant.StudentWork.Drinking
{
    // Implement this on the integrator's shared lock; this module creates no competing lock.
    public interface IDrinkingActionGate
    {
        bool CanRecognize(DrinkingInteraction owner);
        bool TryAcquire(DrinkingInteraction owner);
        void Release(DrinkingInteraction owner);
    }

    [RequireComponent(typeof(DrinkingWaterSpray))]
    [DefaultExecutionOrder(1100)]
    public sealed class DrinkingInteraction : MonoBehaviour
    {
        [Range(1, 4)] public int performer = 1;
        public DigiPhantController controls;
        public Transform travelRoot, rigRoot, head;
        public Transform[] trunk = Array.Empty<Transform>();
        public Transform water;
        public MonoBehaviour actionGate;
        [Tooltip("Solo preview ONLY. Never enable with fruit/wood actions or the old drinking controller.")]
        public bool soloTesting;
        public bool recognizeCamera = true, showDebugUI = true;
        [Min(.1f)] public float maximumWaterDistance = 6;
        public bool IsActive { get; private set; }
        public bool IsRecognizing => !IsActive && (handsRaised || held > 0);
        public bool IsSprayingWater => IsActive && elapsed >= 3.75f && elapsed < 5.5f;
        public bool AwaitingNeutral => awaitingNeutral;
        public float HoldProgress => held;
        public int SequenceStarts { get; private set; }
        public string Status { get; private set; } = "Ready; assign rig, water and shared lock";
        public const float HoldSeconds = 2, RaiseMargin = .10f, ReleaseMargin = .05f;
        public const float SequenceSeconds = 6.5f;
        readonly Dictionary<Transform, Pose> snapshot = new Dictionary<Transform, Pose>();
        readonly Dictionary<Transform, Vector3> pitchAxes = new Dictionary<Transform, Vector3>();
        readonly Dictionary<Transform, Vector3> swayAxes = new Dictionary<Transform, Vector3>();
        IDrinkingActionGate acquiredGate;
        Pose rootPose;
        bool awaitingNeutral;
        bool handsRaised, releasedDuringSequence;
        float sequenceNeutralHeld;
        float held, gap, neutralHeld, neutralGap, elapsed;
        bool finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        void Awake() { if (!controls) controls = GetComponent<DigiPhantController>(); }
        [ContextMenu("Assign existing DigiPhant rig")]
        public void AssignExistingRig()
        {
            controls = GetComponent<DigiPhantController>();
            var motion = GetComponent<DigiPhantLocomotion>();
            if (motion) { travelRoot = motion.travelRoot; if (motion.elephantAnimator) rigRoot = motion.elephantAnimator.transform; }
            if (!controls) return;
            foreach (var c in controls.controls)
            {
                if (c.label == "Head turn" && c.bones != null && c.bones.Length > 0) head = c.bones[0];
                if (c.label == "Trunk curl" && c.bones != null) trunk = (Transform[])c.bones.Clone();
            }
        }
        bool RigValid()
        {
            if (!travelRoot || !rigRoot || !head || trunk == null || trunk.Length == 0)
            { Status = "Assign travel root, rig root, head and trunk bones"; return false; }
            var unique = new HashSet<Transform> { head };
            if (!head.IsChildOf(rigRoot)) { Status = "Head is outside rig root"; return false; }
            foreach (var bone in trunk)
                if (!bone || !bone.IsChildOf(rigRoot) || !unique.Add(bone))
                { Status = "Missing, duplicate or incorrectly parented trunk bone"; return false; }
            return true;
        }
        public bool TryStart()
        {
            if (IsActive) return false; // Never overwrite the active sequence's status or restart it.
            if (!RigValid()) return false;
            if (!water) { Status = "Assign water Transform"; return false; }
            Vector3 offset = water.position - travelRoot.position; offset.y = 0;
            if (offset.magnitude > maximumWaterDistance)
            { Status = "Too far from the water"; return false; }
            var gate = actionGate as IDrinkingActionGate;
            if (!soloTesting && gate == null) { Status = "Shared action lock not connected"; return false; }
            if (gate != null && !gate.TryAcquire(this)) { Status = "Another action owns the elephant"; return false; }
            acquiredGate = gate;
            snapshot.Clear(); pitchAxes.Clear(); swayAxes.Clear();
            rootPose = new Pose(travelRoot.position, travelRoot.rotation);
            foreach (var bone in rigRoot.GetComponentsInChildren<Transform>(true))
                snapshot[bone] = new Pose(bone.localPosition, bone.localRotation);
            CaptureAxes(head);
            foreach (var bone in trunk) CaptureAxes(bone);
            elapsed = held = gap = 0;
            releasedDuringSequence = false; sequenceNeutralHeld = 0;
            IsActive = true;
            Status = "Drinking: lower head and extend trunk";
            SequenceStarts++;
            Debug.Log("[Chunzi Drinking] START #" + SequenceStarts + " P" + performer
                + (soloTesting ? " SOLO PREVIEW" : " shared lock acquired"), this);
            return true;
        }
        void CaptureAxes(Transform bone)
        {
            pitchAxes[bone] = bone.InverseTransformDirection(travelRoot.right).normalized;
            swayAxes[bone] = bone.InverseTransformDirection(travelRoot.up).normalized;
        }
        public void Cancel()
        {
            if (!IsActive) return;
            Finish("Cancelled; lower both hands to rearm");
        }
        void RestorePose()
        {
            if (travelRoot) travelRoot.SetPositionAndRotation(rootPose.position, rootPose.rotation);
            foreach (var pair in snapshot)
                if (pair.Key) pair.Key.SetLocalPositionAndRotation(pair.Value.position, pair.Value.rotation);
        }
        void Finish(string status)
        {
            RestorePose();
            IsActive = false;
            awaitingNeutral = !releasedDuringSequence || status.StartsWith("Cancelled", StringComparison.Ordinal);
            held = gap = neutralHeld = neutralGap = 0;
            var gate = acquiredGate; acquiredGate = null;
            gate?.Release(this);
            snapshot.Clear(); pitchAxes.Clear(); swayAxes.Clear();
            Status = status;
            Debug.Log("[Chunzi Drinking] END #" + SequenceStarts + " " + status, this);
        }
        void LateUpdate()
        {
            Tick(Time.unscaledDeltaTime, Time.realtimeSinceStartup);
        }
        // Public for camera-free editor validation; callers must not also tick from another loop.
        public void Tick(float dt, float now)
        {
            if (!finite(dt) || dt < 0 || !finite(now)) return;
            dt = Mathf.Min(dt, .1f);
            if (IsActive)
            {
                if (recognizeCamera && controls && controls.inputMode == InputMode.Camera && controls.IsCalibrated)
                {
                    bool l = controls.TryReadMovement(performer, Movement.LeftHandHeight, now, out float activeLeft);
                    bool r = controls.TryReadMovement(performer, Movement.RightHandHeight, now, out float activeRight);
                    ObserveHands(l && r, activeLeft, activeRight, dt);
                }
                elapsed += dt;
                RestorePose(); // Last writer during SOLO preview; shared lock must pause gait upstream.
                ApplySequence(elapsed);
                if (elapsed >= SequenceSeconds) Finish(releasedDuringSequence
                    ? "Finished; ready for raised hands" : "Finished; lower both hands to rearm");
                return;
            }
            if (!recognizeCamera) return;
            if (!controls) { Status = "Body controller missing"; handsRaised = false; held = gap = neutralHeld = 0; return; }
            if (controls.inputMode != InputMode.Camera || !controls.IsCalibrated)
            { Status = "Select Camera and calibrate neutral"; handsRaised = false; held = gap = neutralHeld = 0; return; }
            var gate = actionGate as IDrinkingActionGate;
            if ((!soloTesting && gate == null) || (gate != null && !gate.CanRecognize(this)))
            { handsRaised = false; held = gap = neutralHeld = 0; Status = gate == null ? "Shared action lock not connected" : "Another action owns the elephant"; return; }
            bool leftOk = controls.TryReadMovement(performer, Movement.LeftHandHeight, now, out float left);
            bool rightOk = controls.TryReadMovement(performer, Movement.RightHandHeight, now, out float right);
            ObserveHands(leftOk && rightOk && finite(left) && finite(right), left, right, dt);
        }
        public void ObserveHands(bool valid, float left, float right, float dt)
        {
            if (!finite(dt) || dt < 0) return;
            dt = Mathf.Min(dt, .1f);
            valid &= finite(left) && finite(right);
            bool raised = valid && left > RaiseMargin && right > RaiseMargin;
            bool neutral = valid && left <= ReleaseMargin && right <= ReleaseMargin;
            handsRaised = raised;
            if (IsActive)
            {
                sequenceNeutralHeld = neutral ? sequenceNeutralHeld + dt : 0;
                if (!releasedDuringSequence && sequenceNeutralHeld >= .4f)
                {
                    releasedDuringSequence = true;
                    Debug.Log("[Chunzi Drinking] RELEASE during sequence; next hold allowed after completion", this);
                }
                return; // Input observation never modifies an active animation.
            }
            if (awaitingNeutral)
            {
                if (neutral) { neutralHeld += dt; neutralGap = 0; }
                else { neutralGap += dt; if (valid || neutralGap > .2f) neutralHeld = 0; }
                Status = (neutral ? "Neutral detected " : "Waiting for neutral ") + neutralHeld.ToString("0.00")
                    + "/0.40s" + (!valid ? " | wrist missing / low confidence" : "");
                if (neutral && neutralHeld >= .4f)
                { awaitingNeutral = false; neutralHeld = neutralGap = 0; Status = "Ready for raised hands"; }
                return;
            }
            if (raised)
            {
                held += dt; gap = 0;
                Status = "Hands hold " + held.ToString("0.00") + "/2.00s | L " + left.ToString("0.00") + " R " + right.ToString("0.00");
                if (held >= HoldSeconds) { held = 0; TryStart(); }
            }
            else if (held > 0 && !valid && (gap += dt) <= .2f)
                Status = "Wrist tracking briefly lost; hold paused";
            else
            {
                held = gap = 0;
                Status = !valid ? "Wrist missing / low confidence" : "Ready: raise BOTH hands above rest";
            }
        }
        void ApplySequence(float time)
        {
            // Same sequence timings and easing; stronger post-drink head lift and trunk shake.
            float h, t, sway = 0;
            if (time < 1)
            { float u = Mathf.SmoothStep(0, 1, time); h = 28 * u; t = -16 * u; Status = "Drinking: lower head and extend trunk"; }
            else if (time < 3) { h = 28; t = -16; Status = "Drinking: hold at water"; }
            else if (time < 4)
            { float u = Mathf.SmoothStep(0, 1, time - 3); h = Mathf.Lerp(28, -25, u); t = Mathf.Lerp(-16, -28, u); Status = "Drinking: raise head and curl trunk"; }
            else if (time < 5.5f)
            { float u = (time - 4) / 1.5f; h = -25; t = -28; sway = 5 * Mathf.Sin(6 * Mathf.PI * u) * Mathf.Sin(Mathf.PI * u); Status = "Drinking: head raised and trunk shake"; }
            else
            { float u = 1 - Mathf.SmoothStep(0, 1, (time - 5.5f)); h = -25 * u; t = -28 * u; Status = "Drinking: return to rest"; }
            Rotate(head, h, 0);
            foreach (var bone in trunk) Rotate(bone, t, sway);
        }
        void Rotate(Transform bone, float pitch, float sway)
        {
            if (!bone || !snapshot.ContainsKey(bone)) return;
            bone.localRotation = snapshot[bone].rotation * Quaternion.AngleAxis(pitch, pitchAxes[bone])
                * Quaternion.AngleAxis(sway, swayAxes[bone]);
        }
        void OnDisable() { Cancel(); }
        void OnGUI()
        {
            if (!showDebugUI) return;
            GUILayout.BeginArea(new Rect(Mathf.Max(0, Screen.width - 275), Mathf.Max(0, Screen.height - 230), 265, 220), GUI.skin.box);
            GUILayout.Label("Chunzi · Drinking · P" + performer);
            GUILayout.Label(Status);
            if (GUILayout.Button("Debug: drink")) TryStart();
            if (GUILayout.Button("Cancel drinking")) Cancel();
            if (!IsActive) soloTesting = GUILayout.Toggle(soloTesting, "Solo preview (no other actions)");
            GUILayout.EndArea();
        }
    }
}
