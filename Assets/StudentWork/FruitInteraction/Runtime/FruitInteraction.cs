using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace DigiPhant.StudentWork
{
    [Serializable] public class FaceActionFrame
    {
        public int version, sequence, actionId;
        public string kind, action;
        public bool enabled;
        public string error;
        public bool tracked, calibrated, calibrating, mouthOpen;
        public float jawOpen, eyeBlinkLeft, eyeBlinkRight;
        // Role claiming (optional fields): the claimed Face slot and, while claiming, each slot's mouth.
        public int faceSlot;
        public bool claiming;
        public SlotJaw[] slotJaw;
    }
    [Serializable] public class SlotJaw { public int slot; public float jaw; }

    // Runs after gait evaluation and additive gestures; owns the rig only during an action.
    [DefaultExecutionOrder(1000)]
    public class FruitInteraction : MonoBehaviour
    {
        public Transform apple, banana, fruitBranch;
        public int facePort = 5058;
        public bool faceMode;
        [Tooltip("Hide the top-right fruit panel (a small button brings it back).")]
        public bool panelCollapsed;
        [Range(15, 70)] public float rearDegrees = 55;
        public float rearSeconds = 1.2f, trackingTimeout = .5f;
        [Tooltip("Found by name when empty; its bounds are kept clear at the pick station.")]
        public Transform feedingBush;
        [Min(0)] public float stationClearance = .25f;
        public bool showPortal = true;
        [Min(.5f)] public float portalRadius = 1.2f;
        [Tooltip("Distance behind the pick station, about two elephant strides.")]
        [Min(1)] public float portalDistance = 3.5f;
        public Vector3 StationPosition => stationPosition;
        public float StationReachMargin { get; private set; }
        public string Status { get; private set; } = "Debug ready; approach the feeding bush";
        public float RearBlend { get; private set; }
        public bool Picking => phase != 0;
        /// <summary>A fruit action owns the elephant (rising, upright, picking or lowering).</summary>
        public bool IsBusy => snapshotValid || rearRequested || Picking;
        public Transform TrunkTip => tip;
        public Vector3 LeftHindAnchor { get; private set; }
        public Vector3 RightHindAnchor { get; private set; }
        DigiPhantController controls;
        DigiPhantLocomotion locomotion;
        DigiPhantHandGrab handGrab;
        ElephantActionLock actionLock;
        Transform model, leftHind, rightHind, leftFront, rightFront, tip;
        Transform modelParent;
        Transform leftThigh, rightThigh, leftShin, rightShin;
        Transform[] trunk;
        readonly Dictionary<Transform, Pose> pose = new Dictionary<Transform, Pose>();
        readonly Dictionary<SkinnedMeshRenderer, (bool force, bool update)> skinSettings = new Dictionary<SkinnedMeshRenderer, (bool, bool)>();
        Quaternion modelRotation;
        Quaternion leftHindRotation, rightHindRotation;
        Vector3 leftKnee, rightKnee;
        Vector3 modelPosition, pivot, axis, reachOrigin, homeTip, leftAnchor, rightAnchor;
        Transform held;
        Transform appleParent, bananaParent;
        Vector3 applePosition, bananaPosition;
        Quaternion appleRotation, bananaRotation;
        bool initialized, rearRequested, mouthOpen, snapshotValid, awaitFaceNeutral;
        Camera framedCamera;
        float originalFieldOfView;
        int phase, pendingFruit, lastSequence = -1, lastAction = -1;
        float phaseTime, lastFrame = -1000;
        float nextFaceConfiguration;
        UdpClient socket;
        string faceReadout = "Face · waiting";
        readonly Dictionary<int, float> claimJaw = new Dictionary<int, float>();
        float claimJawTime = -1000;
        int faceSlot;
        bool claimingFace;
        /// <summary>Unity's Hands toggle; sent with the face configuration (same allowed command socket).</summary>
        [NonSerialized] public bool handsEnabled;
        /// <summary>Slot whose face drives the actions; 0 = no roles (only a lone performer counts).</summary>
        public int FaceSlot => faceSlot;
        public void SetFaceSlot(int slot) { faceSlot = Mathf.Clamp(slot, 0, 4); nextFaceConfiguration = 0; SendFaceConfiguration(); }
        public void SetFaceClaim(bool claiming) { claimingFace = claiming; claimJaw.Clear(); nextFaceConfiguration = 0; SendFaceConfiguration(); }
        public bool TryGetClaimJaw(int slot, float now, out float jaw)
        { jaw = 0; return claimingFace && now - claimJawTime <= trackingTimeout && claimJaw.TryGetValue(slot, out jaw); }
        Vector3 stationPosition, stationFacing, portalPosition;
        Quaternion stationRotation;
        bool stationAttempted, stationValid, portalArmed;
        float arrivalHoldUntil;
        const float arrivalHoldSeconds = 1.5f;
        GameObject portal;
        readonly List<Material> portalMaterials = new List<Material>();

        public bool Initialise()
        {
            if (initialized) return true;
            controls = GetComponent<DigiPhantController>();
            locomotion = GetComponent<DigiPhantLocomotion>();
            handGrab = FindAnyObjectByType<DigiPhantHandGrab>(FindObjectsInactive.Include);
            actionLock = GetComponent<ElephantActionLock>();
            if (!controls || !locomotion || !locomotion.elephantAnimator || !apple || !banana) return false;
            model = locomotion.elephantAnimator.transform;
            modelParent = model.parent;
            var all = model.GetComponentsInChildren<Transform>(true);
            Transform Find(string name) => Array.Find(all, t => t.name == name);
            leftHind = Find("elephant_l_Toe_bone"); rightHind = Find("elephant_r_Toe_bone");
            leftThigh = Find("elephant_l_Femur_bone"); rightThigh = Find("elephant_r_Femur_bone");
            leftShin = Find("elephant_l_Tibia_bone"); rightShin = Find("elephant_r_Tibia_bone");
            leftFront = Find("elephant_l_Paw_bone"); rightFront = Find("elephant_r_Paw_bone");
            trunk = new Transform[7];
            for (int i = 0; i < 7; i++) trunk[i] = Find("elephant_Trunk" + (i + 1) + "_bone");
            tip = Find("elephant_Trunk7-nub_bone");
            if (!leftHind || !rightHind || !leftThigh || !rightThigh || !leftShin || !rightShin || !leftFront || !rightFront || !tip || Array.Exists(trunk, t => !t))
            { Status = "Fruit rig setup incomplete"; return false; }
            appleParent = apple.parent; applePosition = apple.localPosition; appleRotation = apple.localRotation;
            bananaParent = banana.parent; bananaPosition = banana.localPosition; bananaRotation = banana.localRotation;
            initialized = true;
            return true;
        }
        void OnEnable()
        {
            if (!Application.isPlaying || Application.isBatchMode) return;
            BeginFaceReceiver();
        }
        public void BeginFaceReceiver()
        {
            if (socket != null) return;
            try { socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, facePort)); socket.Client.Blocking = false; }
            catch (Exception e) when (e is SocketException || e is ArgumentOutOfRangeException) { Status = "Face port unavailable: " + e.Message; }
        }
        static bool Unit(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && v >= 0 && v <= 1;
        public bool AcceptPacket(string json, float now)
        {
            if (!Initialise()) return false;
            if (string.IsNullOrEmpty(json) || json.Length > 4096) return false;
            foreach (var name in new[] { "version", "kind", "sequence", "actionId", "tracked", "calibrated", "mouthOpen", "jawOpen", "eyeBlinkLeft", "eyeBlinkRight", "action" })
                if (!json.Contains("\"" + name + "\"")) return false;
            FaceActionFrame f;
            try { f = JsonUtility.FromJson<FaceActionFrame>(json); } catch (ArgumentException) { return false; }
            if (f == null || f.version != 1 || f.kind != "fruitActions" || f.sequence < 0 || f.actionId < 0 ||
                !Unit(f.jawOpen) || !Unit(f.eyeBlinkLeft) || !Unit(f.eyeBlinkRight) ||
                (f.action != "" && f.action != "rearUp" && f.action != "apple" && f.action != "banana")) return false;
            if (now - lastFrame > trackingTimeout) { lastSequence = -1; lastAction = f.actionId; }
            if (f.sequence <= lastSequence) return false;
            lastSequence = f.sequence; lastFrame = now;
            if (f.claiming && f.slotJaw != null) { claimJaw.Clear(); foreach (var item in f.slotJaw) if (item != null && Unit(item.jaw)) claimJaw[item.slot] = item.jaw; claimJawTime = now; }
            faceReadout = f.tracked ? "Face · mouth " + f.jawOpen.ToString("0.00") + " · L " + f.eyeBlinkLeft.ToString("0.00") + " · R " + f.eyeBlinkRight.ToString("0.00") +
                (f.calibrated ? "" : " · calibrating") : "Face · not tracked";
            if (!faceMode || !controls.CameraSessionActive) return true;
            if (!string.IsNullOrEmpty(f.error))
            { SuspendFaceActions(); Status = "Facial unavailable: " + f.error + ". Body camera continues."; return true; }
            if (!f.tracked || !f.calibrated)
            { mouthOpen = false; rearRequested = false; pendingFruit = 0; if (Picking) CancelPickup(); Status = f.calibrating ? "Face neutral calibration: eyes open, mouth closed" : "Face lost / not calibrated: lowering safely"; return true; }
            if (awaitFaceNeutral)
            {
                lastAction = f.actionId;
                if (!f.mouthOpen && f.eyeBlinkLeft < .35f && f.eyeBlinkRight < .35f) { awaitFaceNeutral = false; Status = "Neutral face ready for the next action"; }
                else Status = "Reset: close mouth and open both eyes to re-arm";
                return true;
            }
            mouthOpen = f.mouthOpen;
            if (mouthOpen) rearRequested = true;
            else if (!Picking)
            {
                rearRequested = false; pendingFruit = 0;
                if (RearBlend > 0) { locomotion.actionPoseOwned = locomotion.actionMovementPaused = true; }
            }
            if (f.actionId > lastAction)
            {
                lastAction = f.actionId;
                // The tracker separates winks from blinks over several frames (fruit_actions.py);
                // a single packet's eye values cannot, so they are not re-checked here.
                if (f.action == "rearUp") RequestRear();
                else if (f.action == "apple") RequestPick(true);
                else if (f.action == "banana") RequestPick(false);
            }
            return true;
        }
        void Poll(float now)
        {
            if (socket == null) return;
            try
            {
                var sender = new IPEndPoint(IPAddress.Loopback, 0);
                for (int i = 0; i < 32 && socket.Available > 0; i++)
                {
                    var bytes = socket.Receive(ref sender);
                    if (IPAddress.IsLoopback(sender.Address)) AcceptPacket(Encoding.UTF8.GetString(bytes), now);
                }
            }
            catch (SocketException e) { Status = "Face UDP: " + e.Message; }
        }
        void Snapshot()
        {
            pose.Clear();
            skinSettings.Clear();
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            { skinSettings[skin] = (skin.forceMatrixRecalculationPerRender, skin.updateWhenOffscreen); skin.forceMatrixRecalculationPerRender = true; skin.updateWhenOffscreen = true; }
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) pose[t] = new Pose(t.localPosition, t.localRotation);
            modelPosition = model.localPosition; modelRotation = model.localRotation;
            leftAnchor = modelParent.InverseTransformPoint(leftHind.position);
            rightAnchor = modelParent.InverseTransformPoint(rightHind.position);
            leftHindRotation = Quaternion.Inverse(modelParent.rotation) * leftHind.rotation;
            rightHindRotation = Quaternion.Inverse(modelParent.rotation) * rightHind.rotation;
            leftKnee = modelParent.InverseTransformPoint(leftShin.position);
            rightKnee = modelParent.InverseTransformPoint(rightShin.position);
            RearFrame(out var worldPivot, out var worldAxis);
            pivot = modelParent.InverseTransformPoint(worldPivot);
            axis = modelParent.InverseTransformDirection(worldAxis).normalized;
            snapshotValid = true;
            framedCamera = locomotion.followCamera;
            if (framedCamera)
            { originalFieldOfView = framedCamera.fieldOfView; framedCamera.fieldOfView = Mathf.Max(originalFieldOfView, 50); }
        }
        // Raise the torso over the supporting hips, rather than rotating the whole leg
        // backward around a toe. Hind-limb IK retains the original planted stance.
        void RearFrame(out Vector3 worldPivot, out Vector3 worldAxis)
        {
            worldPivot = (leftThigh.position + rightThigh.position) * .5f;
            worldAxis = (rightThigh.position - leftThigh.position).normalized;
            var front = (leftFront.position + rightFront.position) * .5f;
            if ((Quaternion.AngleAxis(10, worldAxis) * (front - worldPivot)).y < (front - worldPivot).y) worldAxis = -worldAxis;
        }
        float TrunkLength()
        {
            float length = 0;
            for (int i = 0; i < trunk.Length - 1; i++) length += Vector3.Distance(trunk[i].position, trunk[i + 1].position);
            return length + Vector3.Distance(trunk[6].position, tip.position);
        }
        // Team rule: the trunk carries one thing at a time, so no rearing or picking while
        // Aurora's hand grab holds the wood.
        bool CarryingWood => handGrab && handGrab.IsHolding;
        // Chunzi's drinking holds the team action lock; fruit waits for it.
        bool Drinking => (actionLock || (actionLock = GetComponent<ElephantActionLock>())) && actionLock.DrinkActive;
        ElephantSitOnLog sit;
        bool Sitting => (sit || (sit = GetComponent<ElephantSitOnLog>())) && sit.IsBusy;
        string BusyStatus => Drinking ? DrinkingFirst : "The elephant is sitting on the log";
        const string DrinkingFirst = "Chunzi is drinking: wait until she finishes";
        void SuspendHandGrab(bool suspended) { if (handGrab) handGrab.overlaySuspended = suspended || Drinking || Sitting; }
        const string DropWoodFirst = "Drop the wood first: open a hand, then rear";
        public void RequestRear()
        {
            if (!Initialise()) return;
            if (CarryingWood) { Status = DropWoodFirst; return; }
            if (Drinking || Sitting) { Status = BusyStatus; return; }
            // Suspend the hand grab now: buttons call this from OnGUI, after this frame's Tick.
            SuspendHandGrab(true);
            rearRequested = true;
            if (!snapshotValid) Snapshot();
            locomotion.actionPoseOwned = true;
            locomotion.actionMovementPaused = true;
            Status = "Rising onto hind feet; movement paused";
        }
        public bool RequestPick(bool selectApple)
        {
            if (!Initialise() || Picking) return false;
            if (CarryingWood) { Status = DropWoodFirst; return false; }
            if (Drinking || Sitting) { Status = BusyStatus; return false; }
            SuspendHandGrab(true);
            var target = selectApple ? apple : banana;
            if (RearBlend < .98f)
            {
                if (rearRequested) { pendingFruit = selectApple ? 1 : 2; Status = "Wink selection queued until upright"; return true; }
                Status = "Open mouth / Rear first; wait until upright"; return false;
            }
            if (!target.gameObject.activeSelf) { Status = "Fruit already picked; Reset to replenish"; return false; }
            float length = TrunkLength();
            if (Vector3.Distance(trunk[0].position, target.position) > length * 1.05f)
            { Debug.LogWarning("FRUIT_OUT_OF_REACH base=" + trunk[0].position + " target=" + target.position + " length=" + length); Status = "Too far from branch: lower, move closer, then rear again"; return false; }
            held = target; reachOrigin = tip.position; homeTip = tip.position;
            phase = 1; phaseTime = 0; rearRequested = true;
            locomotion.actionPoseOwned = true;
            locomotion.actionMovementPaused = true;
            Status = "Reaching for " + (selectApple ? "apple" : "banana");
            return true;
        }
        void RestorePose()
        {
            foreach (var p in pose) if (p.Key) { p.Key.localPosition = p.Value.position; p.Key.localRotation = p.Value.rotation; }
            model.localPosition = modelPosition;
            model.localRotation = modelRotation;
        }
        void SolveTrunk(Vector3 target)
        {
            for (int pass = 0; pass < 18; pass++)
                for (int i = trunk.Length - 1; i >= 0; i--)
                {
                    var bone = trunk[i];
                    var a = tip.position - bone.position; var b = target - bone.position;
                    if (a.sqrMagnitude < .000001f || b.sqrMagnitude < .000001f) continue;
                    var delta = Quaternion.FromToRotation(a, b);
                    bone.rotation = Quaternion.Slerp(Quaternion.identity, delta, .65f) * bone.rotation;
                }
        }
        static void SolveHindLeg(Transform thigh, Transform shin, Transform foot, Vector3 target, Vector3 preferredKnee, Quaternion footRotation)
        {
            Vector3 hip = thigh.position; float upper = Vector3.Distance(hip, shin.position), lower = Vector3.Distance(shin.position, foot.position);
            Vector3 direction = target - hip; float distance = direction.magnitude;
            if (distance < .0001f || upper < .0001f || lower < .0001f) return;
            direction /= distance;
            distance = Mathf.Clamp(distance, Mathf.Abs(upper - lower) + .00001f, upper + lower - .00001f);
            float along = (upper * upper + distance * distance - lower * lower) / (2 * distance);
            float bend = Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
            Vector3 pole = preferredKnee - hip; pole -= Vector3.Dot(pole, direction) * direction;
            if (pole.sqrMagnitude < .000001f) pole = Vector3.Cross(direction, thigh.right);
            Vector3 knee = hip + direction * along + pole.normalized * bend;
            thigh.rotation = Quaternion.FromToRotation(shin.position - hip, knee - hip) * thigh.rotation;
            shin.rotation = Quaternion.FromToRotation(foot.position - shin.position, target - shin.position) * shin.rotation;
            foot.rotation = footRotation;
        }
        public void Tick(float now, float dt)
        {
            if (!Initialise()) return;
            if (!controls.enabled || !locomotion.enabled || !locomotion.enableLocomotion)
            { if (snapshotValid || Picking) ResetAction(); SuspendHandGrab(false); if (portal) portal.SetActive(false); Status = "Enable elephant controls and locomotion before fruit actions"; return; }
            dt = Mathf.Clamp(dt, 0, .1f);
            if (faceMode && controls.CameraSessionActive && now - lastFrame > trackingTimeout)
            { mouthOpen = false; rearRequested = false; pendingFruit = 0; if (Picking) CancelPickup(); Status = "Face lost: action cancelled, lowering"; }
            UpdatePortal(now);
            // Face actions set rearRequested directly, so the carrying rule is enforced here too.
            if ((Drinking || Sitting) && !snapshotValid && (rearRequested || pendingFruit != 0))
            { rearRequested = false; pendingFruit = 0; Status = BusyStatus; }
            if (CarryingWood && !snapshotValid && (rearRequested || pendingFruit != 0))
            { rearRequested = false; pendingFruit = 0; Status = DropWoodFirst; }
            // Backstop: wood acquired in the frame before the suspension took effect lowers the elephant.
            if (CarryingWood && snapshotValid && rearRequested && !Picking) { Lower(); Status = DropWoodFirst; }
            SuspendHandGrab(snapshotValid || rearRequested || Picking);
            if (!snapshotValid && rearRequested) Snapshot();
            RearBlend = Mathf.MoveTowards(RearBlend, rearRequested || Picking ? 1 : 0, dt / Mathf.Max(.1f, rearSeconds));
            bool actionTransition = RearBlend < .999f && (rearRequested || RearBlend > 0);
            locomotion.actionPoseOwned = snapshotValid && (actionTransition || Picking);
            locomotion.actionMovementPaused = snapshotValid && (RearBlend > 0 || rearRequested || Picking) || now < arrivalHoldUntil;
            if (!snapshotValid) return;
            if (actionTransition || Picking || RearBlend == 0 && !rearRequested)
                RestorePose();
            var rotation = Quaternion.AngleAxis(rearDegrees * Mathf.SmoothStep(0, 1, RearBlend), axis);
            model.localPosition = pivot + rotation * (modelPosition - pivot);
            model.localRotation = rotation * modelRotation;
            Vector3 leftTarget = modelParent.TransformPoint(leftAnchor), rightTarget = modelParent.TransformPoint(rightAnchor);
            LeftHindAnchor = leftTarget; RightHindAnchor = rightTarget;
            SolveHindLeg(leftThigh, leftShin, leftHind, leftTarget, modelParent.TransformPoint(leftKnee), modelParent.rotation * leftHindRotation);
            SolveHindLeg(rightThigh, rightShin, rightHind, rightTarget, modelParent.TransformPoint(rightKnee), modelParent.rotation * rightHindRotation);
            // Bend the front legs toward the chest while the two rear toes stay planted.
            foreach (var control in controls.controls)
                if (control.label == "Front left leg" || control.label == "Front right leg")
                    foreach (var bone in control.bones)
                        if (bone)
                        {
                            Quaternion baseRotation = bone.localRotation;
                            bone.localRotation = baseRotation * Quaternion.AngleAxis(25 * RearBlend, control.localAxis.normalized);
                        }
            if (pendingFruit != 0 && RearBlend >= .98f && !Picking)
            { int selection = pendingFruit; pendingFruit = 0; RequestPick(selection == 1); }
            // The body rises over its hind hips while IK holds both supporting soles.
            if (Picking)
            {
                phaseTime += dt; float u = Mathf.SmoothStep(0, 1, Mathf.Clamp01(phaseTime / 1.3f));
                Vector3 target = phase == 1 ? Vector3.Lerp(reachOrigin, held.position, u)
                    : phase == 2 ? Vector3.Lerp(reachOrigin, homeTip + Vector3.up * .6f, u)
                    : Vector3.Lerp(reachOrigin, homeTip, u);
                SolveTrunk(target);
                if (phaseTime >= 1.3f)
                {
                    if (phase == 1)
                    {
                        if (Vector3.Distance(tip.position, held.position) > .3f)
                        { CancelPickup(); Status = "Branch out of reach: move closer and retry"; }
                        else { held.SetParent(tip, true); phase = 2; phaseTime = 0; reachOrigin = tip.position; Status = "Fruit attached: lifting"; }
                    }
                    else if (phase == 2) { phase = 3; phaseTime = 0; reachOrigin = tip.position; Status = "Returning trunk with fruit"; }
                    else
                    { held.gameObject.SetActive(false); ReturnFruit(held); held = null; phase = 0; if (faceMode && !mouthOpen) rearRequested = false; Status = "Fruit picked. Reset replenishes both fruits."; }
                }
            }
            if (RearBlend == 0 && !rearRequested && !Picking)
            { RestorePose(); RestoreSkinSettings(); RestoreCameraFraming(); snapshotValid = false; pose.Clear(); locomotion.actionPoseOwned = locomotion.actionMovementPaused = false; }
        }
        void ReturnFruit(Transform fruit)
        {
            bool a = fruit == apple; fruit.SetParent(a ? appleParent : bananaParent, false);
            fruit.localPosition = a ? applePosition : bananaPosition; fruit.localRotation = a ? appleRotation : bananaRotation;
        }
        void CancelPickup() { if (held) { ReturnFruit(held); held.gameObject.SetActive(true); } held = null; phase = 0; phaseTime = 0; }
        void RestoreSkinSettings()
        {
            foreach (var item in skinSettings) if (item.Key) { item.Key.forceMatrixRecalculationPerRender = item.Value.force; item.Key.updateWhenOffscreen = item.Value.update; }
            skinSettings.Clear();
        }
        void RestoreCameraFraming()
        {
            if (framedCamera) framedCamera.fieldOfView = originalFieldOfView;
            framedCamera = null;
        }
        public void ResetAction()
        {
            if (!Initialise()) return;
            CancelPickup(); pendingFruit = 0; rearRequested = mouthOpen = false; RearBlend = 0;
            awaitFaceNeutral = faceMode;
            if (snapshotValid) RestorePose(); snapshotValid = false; pose.Clear();
            RestoreSkinSettings();
            RestoreCameraFraming();
            locomotion.actionPoseOwned = locomotion.actionMovementPaused = false;
            foreach (var fruit in new[] { apple, banana }) { ReturnFruit(fruit); fruit.gameObject.SetActive(true); }
            SuspendHandGrab(false);
            lastAction = lastSequence = -1; lastFrame = -1000; Status = "Reset: four feet, both fruits restored";
        }
        public void Lower()
        {
            if (Picking) { Status = "Finish picking before lowering, or Reset to cancel"; return; }
            rearRequested = mouthOpen = false;
            locomotion.actionPoseOwned = true;
            locomotion.actionMovementPaused = true;
            Status = "Lowering; picked fruit stays collected";
        }
        // The debug button resets fruit and actions; the portal only teleports, keeping picked fruit.
        public void MoveToStation(bool resetActions = true)
        {
            if (!Initialise() || !fruitBranch) return;
            if (Drinking || Sitting) { Status = BusyStatus; return; }
            if (resetActions) ResetAction();
            // The spot is a world position computed once from a standing pose; recomputing it
            // mid-walk (portal) would bake a stride pose and skew the facing.
            if (!stationValid && !ComputeStation()) { Status = "No clear head-on spot at the feeding bush"; return; }
            Vector3 delta = stationPosition - locomotion.travelRoot.position;
            locomotion.travelRoot.SetPositionAndRotation(stationPosition, stationRotation);
            if (locomotion.followCamera && locomotion.followElephant) locomotion.followCamera.transform.position += delta;
            // Without this, a raised hand would keep walking the elephant through the bush.
            locomotion.StopMotion();
            Status = StationReachMargin >= 0 ? "Facing the fruit at the feeding bush; rear, then select fruit"
                : "Facing the fruit, but it is " + (-StationReachMargin).ToString("0.00") + " beyond trunk reach";
        }
        // Wood pickup spot for Aurora's hand grab: face the log square-on at its middle, from the
        // elephant's current side, so the left-fist reach puts the trunk tip on the near top of the log.
        public void MoveToWood()
        {
            if (!Initialise()) return;
            if (!handGrab) handGrab = FindAnyObjectByType<DigiPhantHandGrab>(FindObjectsInactive.Include);
            if (!handGrab) { Status = "No hand grab in this scene"; return; }
            if (CarryingWood) { Status = "Already carrying the wood"; return; }
            if (Drinking || Sitting) { Status = BusyStatus; return; }
            DigiPhantGrabbable wood = null;
            if (handGrab.grabbables != null) foreach (var g in handGrab.grabbables) if (g && g.pickupCollider && g.isActiveAndEnabled) { wood = g; break; }
            if (!wood) { Status = "No grabbable wood registered"; return; }
            if (snapshotValid || Picking || rearRequested) ResetAction();

            var travel = locomotion.travelRoot;
            var collider = wood.pickupCollider;
            Vector3 centre = collider.bounds.center;
            Vector3 along = Vector3.ProjectOnPlane(wood.Body ? wood.Body.rotation * wood.localLongAxis : wood.transform.up, Vector3.up);
            if (along.sqrMagnitude < .0001f) along = Vector3.right;
            Vector3 side = Vector3.Cross(Vector3.up, along.normalized);
            // Approach from whichever side the elephant is on now; face the log.
            Vector3 toElephant = Vector3.ProjectOnPlane(travel.position - centre, Vector3.up);
            Vector3 face = Vector3.Dot(toElephant, side) > 0 ? -side : side;
            Vector3 legForward = leftFront.position + rightFront.position - leftHind.position - rightHind.position;
            legForward.y = 0;
            travel.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(legForward, face, Vector3.up), Vector3.up) * travel.rotation;

            // Near top edge of the log, seen from the approaching elephant, just outside the surface.
            Vector3 surface = collider.ClosestPoint(centre + Vector3.up * 1f - face * 1f);
            Vector3 goal = surface + (Vector3.up - face).normalized * .06f;
            // Same extra nod as the grab's own ground-reach correction for a low target.
            Vector3 tip = handGrab.PredictReachTip(0);
            float extra = handGrab.groundReachExtraLowerDegrees * Mathf.Clamp01((tip.y - surface.y) / .30f);
            tip = handGrab.PredictReachTip(extra);
            if (float.IsInfinity(tip.x)) { Status = "Hand grab rig is incomplete"; return; }
            Vector3 shift = goal - tip; shift.y = 0;
            Vector3 destination = travel.position + shift;
            destination.y = travel.position.y;
            Vector3 delta = destination - travel.position;
            travel.position = destination;
            if (locomotion.followCamera && locomotion.followElephant) locomotion.followCamera.transform.position += delta;
            locomotion.StopMotion();
            // Check with the real rule: trunk tip within the log's pickup margin in the reach pose.
            tip = handGrab.PredictReachTip(extra);
            float gap = Vector3.Distance(tip, collider.ClosestPoint(tip));
            Status = gap <= wood.pickupSurfaceMargin
                ? "At the wood: left fist reaches, both fists grab (tip " + gap.ToString("0.00") + " m from the log)"
                : "At the wood, but the reaching tip is " + gap.ToString("0.00") + " m from the log (needs ≤ " + wood.pickupSurfaceMargin.ToString("0.00") + ")";
            Debug.Log("WOOD_STATION gap=" + gap.ToString("0.000") + " margin=" + wood.pickupSurfaceMargin + " extraNod=" + extra.ToString("0.0") + " tip=" + tip + " log=" + centre);
        }
        // Head-on pick spot: the elephant faces the fruit across the branch, and every baked
        // vertex, on four feet and through the rear, stays outside the fruit, branch and bush
        // bounds. Trunk vertices only avoid the bush: the trunk reaches under the branch to the fruit.
        bool ComputeStation()
        {
            var travel = locomotion.travelRoot;
            Vector3 root = travel.position;
            Vector3 mid = (apple.position + banana.position) * .5f;
            Vector3 along = fruitBranch.right; along.y = 0;
            if (along.sqrMagnitude < .0001f) return false;
            Vector3 face = Vector3.Cross(along.normalized, Vector3.up);
            // Stand on the camera's side of the bush, facing away from the camera toward the fruit;
            // without a camera, approach from the elephant's current side.
            Vector3 toward = locomotion.followCamera ? locomotion.followCamera.transform.forward : mid - root;
            if (Vector3.Dot(face, toward) < 0) face = -face;
            Vector3 legForward = leftFront.position + rightFront.position - leftHind.position - rightHind.position;
            legForward.y = 0;
            var yaw = Quaternion.AngleAxis(Vector3.SignedAngle(legForward, face, Vector3.up), Vector3.up);
            RearFrame(out var worldPivot, out var worldAxis);
            Vector3 rearPivot = yaw * (worldPivot - root);
            var rear = Quaternion.AngleAxis(rearDegrees, yaw * worldAxis);
            Vector3 Upright(Vector3 offset) => rearPivot + rear * (offset - rearPivot);
            // Head and tusks swing furthest forward partway through the rise, so sample the arc.
            var arc = new[] { Quaternion.identity, Quaternion.AngleAxis(rearDegrees / 3, yaw * worldAxis), Quaternion.AngleAxis(rearDegrees * 2 / 3, yaw * worldAxis), rear };

            // Elephant surface as offsets from the travel root after turning, at each sampled rear angle.
            var offsets = new List<(Vector3 offset, bool trunk, bool midRise)>();
            var baked = new Mesh();
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                Mesh mesh; Matrix4x4 toWorld; BoneWeight[] weights = null; var trunkBones = new HashSet<int>();
                if (renderer is SkinnedMeshRenderer skin)
                {
                    skin.BakeMesh(baked, true); mesh = baked; toWorld = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
                    if (skin.sharedMesh) weights = skin.sharedMesh.boneWeights;
                    for (int b = 0; b < skin.bones.Length; b++) if (skin.bones[b] && skin.bones[b].name.StartsWith("elephant_Trunk")) trunkBones.Add(b);
                }
                else if (renderer.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh && filter.sharedMesh.isReadable)
                { mesh = filter.sharedMesh; toWorld = renderer.transform.localToWorldMatrix; }
                else continue;
                var vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    bool isTrunk = weights != null && i < weights.Length && trunkBones.Contains(weights[i].boneIndex0);
                    Vector3 offset = yaw * (toWorld.MultiplyPoint3x4(vertices[i]) - root);
                    for (int a = 0; a < arc.Length; a++) offsets.Add((rearPivot + arc[a] * (offset - rearPivot), isTrunk, a == 1 || a == 2));
                }
            }
            if (Application.isPlaying) Destroy(baked); else DestroyImmediate(baked);
            if (offsets.Count == 0) return false;

            var obstacles = new List<(Bounds box, bool trunkMayPass)>();
            void AddObstacles(Transform owner, bool mayPass)
            {
                foreach (var renderer in owner.GetComponentsInChildren<Renderer>())
                {
                    var bounds = renderer.bounds; bounds.Expand(stationClearance * 2);
                    obstacles.Add((bounds, mayPass));
                }
            }
            AddObstacles(fruitBranch, true);
            if (!feedingBush) feedingBush = GameObject.Find("01 Feeding bush")?.transform;
            if (feedingBush) AddObstacles(feedingBush, false);

            Vector3 trunkBase = Upright(yaw * (trunk[0].position - root));
            float reach = TrunkLength();
            Vector3 ground = new Vector3(mid.x, root.y, mid.z), side = Vector3.Cross(Vector3.up, face);
            // Both fruits must be in front of the reared trunk base, so the elephant never walks under them.
            float minimumBack = Mathf.Max(Vector3.Dot(trunkBase, face) - Vector3.Dot(apple.position - ground, face),
                Vector3.Dot(trunkBase, face) - Vector3.Dot(banana.position - ground, face));
            var blocked = new List<Vector2>();
            // strictRise: the whole rise stays clear of fruit and branch. Otherwise only the poses
            // before and after rising do, and the head may pass near the fruit mid-rise.
            bool Search(bool strictRise, out Vector3 best, out float bestGap, out float bestShift)
            {
                best = default; bestGap = float.PositiveInfinity; bestShift = 0;
                bool any = false;
                foreach (float shift in new[] { 0, -.25f, .25f, -.5f, .5f, -.75f, .75f, -1, 1, -1.5f, 1.5f })
                {
                    if (bestGap <= 0) break;
                    // Position = ground - face * back + side * shift; collect the "back" ranges that collide.
                    blocked.Clear();
                    Vector3 start = ground + side * shift;
                    foreach (var (offset, isTrunk, midRise) in offsets)
                        foreach (var (box, mayPass) in obstacles)
                            if (!(mayPass && (isTrunk || midRise && !strictRise)) && BlockedRange(start + offset, face, box, out float low, out float high))
                                blocked.Add(new Vector2(low, high));
                    blocked.Sort((a, b) => a.x.CompareTo(b.x));
                    float back = minimumBack;
                    foreach (var range in blocked)
                    {
                        if (range.x > back) break;
                        if (range.y >= back) back = range.y + .01f;
                    }
                    if (back > minimumBack + 12) continue;
                    Vector3 position = start - face * back;
                    float gap = Mathf.Max(Vector3.Distance(position + trunkBase, apple.position), Vector3.Distance(position + trunkBase, banana.position)) - reach;
                    // Shifts run outward from the centre: take the first spot the trunk reaches;
                    // only when none reaches, fall back to the closest one.
                    if (gap <= 0 || gap < bestGap - .02f) { bestGap = gap; best = position; bestShift = shift; any = true; }
                }
                return any;
            }
            bool strict = true;
            bool found = Search(true, out var chosen, out float bestGap, out float chosenShift);
            if ((!found || bestGap > 0) && Search(false, out var relaxed, out float relaxedGap, out float relaxedShift) && (!found || relaxedGap < bestGap))
            { found = true; strict = false; chosen = relaxed; bestGap = relaxedGap; chosenShift = relaxedShift; }
            Debug.Log("FRUIT_STATION " + (strict ? "clear through rise" : "clear before/after rise") + " shift=" + chosenShift +
                " gap=" + bestGap.ToString("0.00") + " reach=" + reach.ToString("0.00") + " reared trunk base height=" + (root.y + trunkBase.y).ToString("0.00"));
            if (!found) return false;
            stationPosition = chosen;
            stationValid = true;
            stationFacing = face;
            stationRotation = yaw * travel.rotation;
            StationReachMargin = -bestGap;
            return true;
        }
        // Range of "back" along -direction for which point - direction * back lies inside box.
        static bool BlockedRange(Vector3 point, Vector3 direction, Bounds box, out float low, out float high)
        {
            low = float.NegativeInfinity; high = float.PositiveInfinity;
            for (int k = 0; k < 3; k++)
            {
                float speed = -direction[k];
                if (Mathf.Abs(speed) < 1e-6f) { if (point[k] < box.min[k] || point[k] > box.max[k]) return false; continue; }
                float a = (box.min[k] - point[k]) / speed, b = (box.max[k] - point[k]) / speed;
                low = Mathf.Max(low, Mathf.Min(a, b)); high = Mathf.Min(high, Mathf.Max(a, b));
                if (low > high) return false;
            }
            return true;
        }
        // Walking into the ring, about two strides short of the fruit, teleports to the pick spot.
        void UpdatePortal(float now)
        {
            if (!showPortal || !Application.isPlaying || Application.isBatchMode || !fruitBranch)
            { if (portal) portal.SetActive(false); return; }
            if (!stationAttempted)
            {
                stationAttempted = true;
                if (!stationValid && !ComputeStation()) { Status = "Portal unavailable: no clear head-on spot at the feeding bush"; return; }
                portalPosition = stationPosition - stationFacing * portalDistance;
                BuildPortal();
            }
            if (!portal) return;
            Vector3 elephant = locomotion.travelRoot.position;
            Vector3 toStation = elephant - stationPosition; toStation.y = 0;
            // Any foot or the body centre stepping on the ring counts, so it works from every side;
            // the travel root alone sits off-centre in the body.
            float nearest = float.PositiveInfinity;
            Vector3 centre = (leftFront.position + rightFront.position + leftHind.position + rightHind.position) * .25f;
            foreach (var point in new[] { centre, leftFront.position, rightFront.position, leftHind.position, rightHind.position })
            { Vector3 toPortal = point - portalPosition; toPortal.y = 0; nearest = Mathf.Min(nearest, toPortal.magnitude); }
            bool busy = snapshotValid || rearRequested || Picking || Sitting;
            // Hide the ring once the elephant stands at the fruit, so it never sits under the body.
            portal.SetActive(!busy && toStation.magnitude > portalDistance * .5f);
            if (nearest > portalRadius + .5f) portalArmed = true;
            else if (portalArmed && !busy && nearest < portalRadius)
            { portalArmed = false; MoveToStation(false); arrivalHoldUntil = now + arrivalHoldSeconds; Status = "Portal: " + Status; }
        }
        void BuildPortal()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            portal = new GameObject("Fruit portal (runtime)");
            // Above the course path (.025) and start line (.0525) so the track never covers the ring;
            // entering only compares horizontal distance, so the height is purely visual.
            portal.transform.position = portalPosition + Vector3.up * .07f;
            foreach (var (scale, color, lift) in new[] { (1f, new Color(.25f, .9f, 1f), 0f), (.72f, new Color(.04f, .22f, .4f), .01f) })
            {
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = "Portal disc";
                if (Application.isPlaying) Destroy(disc.GetComponent<Collider>()); else DestroyImmediate(disc.GetComponent<Collider>());
                disc.transform.SetParent(portal.transform, false);
                disc.transform.localPosition = Vector3.up * lift;
                disc.transform.localScale = new Vector3(portalRadius * 2 * scale, .01f, portalRadius * 2 * scale);
                var renderer = disc.GetComponent<Renderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (!shader) continue;
                var material = new Material(shader) { color = color };
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                portalMaterials.Add(material);
                renderer.sharedMaterial = material;
            }
        }
        void DestroyPortal()
        {
            if (portal) { if (Application.isPlaying) Destroy(portal); else DestroyImmediate(portal); }
            foreach (var material in portalMaterials) if (material) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
            portal = null; portalMaterials.Clear(); stationAttempted = stationValid = false;
        }
        public void CalibrateFace()
        {
            if (!Initialise() || !faceMode || !controls.CameraSessionActive) { Status = "Enable Facial and click Camera on the left before face calibration"; return; }
            SuspendFaceActions();
            if (socket == null) { Status = "Face receiver unavailable: cannot send calibration"; return; }
            try
            { var bytes = Encoding.UTF8.GetBytes("{\"version\":1,\"fruitCalibrate\":true}"); socket.Send(bytes, bytes.Length, "127.0.0.1", controls.port + 1); }
            catch (SocketException e) { Status = "Face calibration send failed: " + e.Message; return; }
            Status = "Hold neutral face: mouth closed, eyes open";
        }
        public void SendFaceConfiguration()
        {
            if (!Initialise() || !controls.CameraSessionActive || socket == null) return;
            try
            {
                // Repeated every second, so a restarted tracker gets the role state back.
                var bytes = Encoding.UTF8.GetBytes("{\"version\":1,\"fruitEnabled\":" + (faceMode ? "true" : "false") +
                    ",\"faceSlot\":" + faceSlot + ",\"claimFace\":" + (claimingFace ? "true" : "false") +
                    ",\"handsEnabled\":" + (handsEnabled ? "true" : "false") + "}");
                socket.Send(bytes, bytes.Length, "127.0.0.1", controls.port + 1);
            }
            catch (SocketException e) { Status = "Facial configuration: " + e.Message; }
        }
        public void NotifyCameraSessionStarted()
        {
            if (Application.isPlaying && !Application.isBatchMode) BeginFaceReceiver();
            nextFaceConfiguration = 0; SendFaceConfiguration();
        }
        public void NotifyCameraSessionStopped() { if (Initialise()) SuspendFaceActions(); }
        void SuspendFaceActions()
        {
            CancelPickup(); pendingFruit = 0; mouthOpen = rearRequested = false; awaitFaceNeutral = true;
            if (RearBlend > 0) { locomotion.actionPoseOwned = locomotion.actionMovementPaused = true; }
        }
        void LateUpdate()
        {
            float now = Time.realtimeSinceStartup;
            if (controls && controls.CameraSessionActive && now >= nextFaceConfiguration) { SendFaceConfiguration(); nextFaceConfiguration = now + 1; }
            Poll(now); Tick(now, Time.deltaTime);
        }
        void OnDisable()
        {
            DestroyPortal();
            SuspendHandGrab(false);
            SetFaceMode(false);
            SendFaceConfiguration();
            socket?.Close(); socket = null;
            if (initialized)
            {
                SuspendFaceActions();
                if (snapshotValid) RestorePose();
                RestoreSkinSettings(); RestoreCameraFraming();
                RearBlend = 0; snapshotValid = false; pose.Clear();
                locomotion.actionPoseOwned = locomotion.actionMovementPaused = false;
            }
        }
        public void SetFaceMode(bool enabled)
        {
            if (!Initialise() || faceMode == enabled) return;
            faceMode = enabled;
            if (!enabled) SuspendFaceActions();
            else { awaitFaceNeutral = true; lastFrame = -1000; }
            nextFaceConfiguration = 0; SendFaceConfiguration();
            Status = enabled ? "Facial enabled: click Camera on the left; one person, neutral face for 2 seconds" : "Facial off; body mode and calibration preserved";
        }
        void OnGUI()
        {
            if (!Application.isPlaying) return;
            // Collapsible: a small button replaces the whole panel when hidden.
            if (panelCollapsed)
            {
                if (GUI.Button(new Rect(Screen.width - 150, 10, 140, 24), "Show fruit panel")) panelCollapsed = false;
                return;
            }
            GUILayout.BeginArea(new Rect(Screen.width - 275, 10, 265, 380), GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("FRUIT · anatomical right wink = apple\nleft wink = banana; both eyes ignored");
            if (GUILayout.Button("Hide", GUILayout.Width(44))) panelCollapsed = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(Status, new GUIStyle(GUI.skin.label) { wordWrap = true });
            if (GUILayout.Button("Debug: rear onto hind feet")) RequestRear();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Pick apple")) RequestPick(true);
            if (GUILayout.Button("Pick banana")) RequestPick(false);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Lower (keep picked fruit)")) Lower();
            if (GUILayout.Button("Reset (replenish both fruits)")) { if (Sitting) Status = BusyStatus; else ResetAction(); }
            if (GUILayout.Button("Debug: move to feeding bush")) MoveToStation();
            if (handGrab && GUILayout.Button("Debug: move to wood")) MoveToWood();
            // Sit-on-log pose review (phase 1): place in front of the log, sit; Stand up returns control.
            if (handGrab && (sit || (sit = GetComponent<ElephantSitOnLog>())))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(sit.IsBusy ? "Stand up" : "Debug: sit on log")) { if (sit.IsBusy) sit.StandUp(); else sit.DebugSitOnLog(); }
                GUILayout.EndHorizontal();
                // Busy states, plus any result (refusal, stop, stood up) for a few seconds.
                if (sit.IsBusy || Time.realtimeSinceStartup - sit.StatusTime < 10) GUILayout.Label(sit.Status, new GUIStyle(GUI.skin.label) { wordWrap = true });
            }
            if (faceMode && GUILayout.Button("Calibrate neutral face")) CalibrateFace();
            // Live scores: a wink needs the closed eye at least 0.45 and about 0.2 more closed than the other.
            if (faceMode) GUILayout.Label(faceReadout + "\nL/R = eyelid closure vs neutral. Wink: one eye ≥ 0.60, other ≤ 0.55");
            if (faceMode) GUILayout.Label((faceSlot > 0 ? "Face follows P" + faceSlot + " only." : "Without roles, Facial needs one person.") + " Body neutral: 10 s; face neutral: 2 s.");
            GUILayout.EndArea();
        }
    }
}
