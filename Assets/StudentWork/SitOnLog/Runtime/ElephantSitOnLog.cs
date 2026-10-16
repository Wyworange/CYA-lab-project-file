using System.Collections.Generic;
using UnityEngine;

namespace DigiPhant.StudentWork
{
    // Sit on the log like a person on a bench: rump on the log, torso upright and leaning slightly
    // forward, hind feet planted with knees up, front legs held straight out in front.
    // Trigger: the log lies on the ground and Aurora (Hands) shows a V sign (either hand) for armSeconds.
    // The whole sequence then plays by itself: walk over on the real gait (locomotion autopilot) to a
    // staging point in front of the log, turn to face away from it, back up until the hips are over it,
    // settle, sit down, stay seated for holdSeconds, stand up and return to idle. Everyone's control is
    // frozen meanwhile. The next V sign counts only after the hand has dropped it.
    // The V sign always plays the sit: obstacles and the stage edge never refuse it (only an impossible
    // log or another running action does). The room check just picks the clearer side; if the walk-over
    // gets stuck, the elephant skips to the log and sits there.
    // "Debug: sit on log" skips the walk (teleports) for pose review; "Stand up" ends it early.
    // Runs after FruitInteraction (1000), so its pause flags, autopilot and pose are the last writers.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1050)]
    public class ElephantSitOnLog : MonoBehaviour
    {
        [Header("Pose")]
        [Range(40, 85)] public float torsoPitch = 72;
        [Tooltip("Forward lean of the upper spine (Spine2 + Spine3).")]
        [Range(0, 25)] public float spineLean = 12;
        [Tooltip("Share of the torso pitch taken back at the neck, so the head does not look at the sky.")]
        [Range(0, 1)] public float neckCompensation = .5f;
        [Tooltip("Hip joint height above the top of the log when seated (the rump skin sits below it).")]
        public float hipAboveSeat = .7f;
        [Tooltip("Hip joint offset forward of the log centre when seated.")]
        public float hipForward = .15f;
        [Tooltip("Before sitting the hips stand this far forward of the log, so the hind feet are clear of it.")]
        public float standOffset = 1.1f;
        [Tooltip("Seated hind feet: distance forward of the hips.")]
        public float footForward = 1.0f;
        [Tooltip("Front feet target: this far above the hind knee joint (skin thickness).")]
        public float kneeTop = .25f;
        public enum FrontLegPose { StraightForward, StraightToKnees, BentOnKnees }
        [Tooltip("StraightForward: front legs held straight out in front. StraightToKnees: straight, pointing at the hind knees. BentOnKnees: elbows bend so the feet rest on the knees.")]
        public FrontLegPose frontLegs = FrontLegPose.StraightForward;
        [Tooltip("StraightForward only: degrees below horizontal (0 = level, positive = lower).")]
        [Range(-30, 60)] public float armDrop = 10;
        [Header("Timing")]
        [Min(.1f)] public float sitSeconds = 1.2f;
        [Min(.1f)] public float standSeconds = .8f;
        [Header("Trigger and walk-over")]
        [Tooltip("A V sign (either hand) must be seen this long to start; filters single misread frames.")]
        [Min(.05f)] public float armSeconds = .3f;
        [Tooltip("Seconds the elephant stays seated before it stands up and returns to idle by itself.")]
        [Min(0)] public float holdSeconds = 1.2f;
        [Tooltip("Staging point this far in front of the final spot; the elephant turns there and backs up.")]
        [Min(1)] public float approachDistance = 3f;
        [Tooltip("Room check: obstacles (renderer bounds) must stay this far from the elephant's path.")]
        [Min(0)] public float clearance = .25f;
        [Tooltip("Refuse when trees, bushes or other props are on the way or around the seat. Turn off to sit anyway.")]
        public bool checkRoom = true;
        [Range(.2f, .65f)] public float walkInput = .5f;
        [Range(.2f, 1)] public float backInput = .5f;
        [Tooltip("When the walk-over makes no progress for this long, skip to the log and sit there.")]
        [Min(1)] public float stuckSeconds = 3f;

        public enum Phase { Idle, Approach, TurnAround, BackUp, Settle, SittingDown, Seated, StandingUp }
        public Phase Current { get; private set; }
        public bool IsBusy => Current != Phase.Idle;
        string status = "Sit: ready";
        public string Status { get => status; private set { if (value != status) StatusTime = Time.realtimeSinceStartup; status = value; } }
        /// <summary>Realtime of the last status change, so the panel can show results for a few seconds.</summary>
        public float StatusTime { get; private set; } = -1000;
        [Tooltip("The V sign must be gone this long before the elephant stands up or stops (one misread frame is ignored).")]
        [Min(0)] public float releaseDelay = .4f;
        [Tooltip("After the walk-over, wait this long so the gait settles to idle before sitting.")]
        [Min(0)] public float settleSeconds = .75f;

        DigiPhantController controller;
        DigiPhantLocomotion locomotion;
        DigiPhantHandGrab handGrab;
        Transform model, modelParent;
        Transform leftFemur, rightFemur, leftTibia, rightTibia, leftToe, rightToe;
        Transform leftHumerus, rightHumerus, leftRadius, rightRadius, leftPaw, rightPaw;
        Transform spine2, spine3, neck;
        readonly Dictionary<Transform, Pose> snapshot = new Dictionary<Transform, Pose>();
        readonly Dictionary<SkinnedMeshRenderer, (bool force, bool update)> skins = new Dictionary<SkinnedMeshRenderer, (bool, bool)>();
        Vector3 modelPosition, pivot, axis, seatCentre, facing;
        Quaternion modelRotation, leftToeRotation, rightToeRotation;
        Vector3 leftToeAnchor, rightToeAnchor;
        float blend, toeHeight;
        Camera framedCamera;
        float originalFieldOfView;
        // Walk-over plan.
        readonly List<Vector3> waypoints = new List<Vector3>();
        Vector3 finalPosition;
        Quaternion finalRotation;
        bool byGesture, needsRelease;
        float armHeld, bestDistance, progressTime, lastVictory = -1000, settleUntil, seatedUntil;

        bool Initialise()
        {
            if (model) return true;
            controller = GetComponent<DigiPhantController>();
            locomotion = GetComponent<DigiPhantLocomotion>();
            handGrab = FindAnyObjectByType<DigiPhantHandGrab>(FindObjectsInactive.Include);
            if (!locomotion || !locomotion.elephantAnimator || !locomotion.travelRoot) { Status = "Sit: locomotion rig missing"; return false; }
            var root = locomotion.elephantAnimator.transform;
            var all = root.GetComponentsInChildren<Transform>(true);
            Transform Find(string name) => System.Array.Find(all, t => t.name == name);
            leftFemur = Find("elephant_l_Femur_bone"); rightFemur = Find("elephant_r_Femur_bone");
            leftTibia = Find("elephant_l_Tibia_bone"); rightTibia = Find("elephant_r_Tibia_bone");
            leftToe = Find("elephant_l_Toe_bone"); rightToe = Find("elephant_r_Toe_bone");
            leftHumerus = Find("elephant_l_Humerus_bone"); rightHumerus = Find("elephant_r_Humerus_bone");
            leftRadius = Find("elephant_l_Radius_bone"); rightRadius = Find("elephant_r_Radius_bone");
            leftPaw = Find("elephant_l_Paw_bone"); rightPaw = Find("elephant_r_Paw_bone");
            spine2 = Find("elephant_Spine2_bone"); spine3 = Find("elephant_Spine3_bone"); neck = Find("elephant_Neck_bone");
            if (!leftFemur || !rightFemur || !leftTibia || !rightTibia || !leftToe || !rightToe || !leftHumerus || !rightHumerus ||
                !leftRadius || !rightRadius || !leftPaw || !rightPaw || !spine2 || !spine3 || !neck)
            { Status = "Sit: elephant bones not found"; return false; }
            model = root; modelParent = root.parent;
            if (controller) controller.TrackingReset += OnTrackingReset;
            return true;
        }

        void OnDestroy() { if (controller) controller.TrackingReset -= OnTrackingReset; }

        // Renumbering (Reassign people, group size...) means the ✌️ owner may have changed.
        void OnTrackingReset()
        {
            if (!byGesture) return;
            if (Current == Phase.SittingDown || Current == Phase.Seated) StandUp();
            else if (IsWalking) Stop("Sit stopped: people were renumbered");
        }

        bool IsWalking => Current == Phase.Approach || Current == Phase.TurnAround || Current == Phase.BackUp || Current == Phase.Settle;

        DigiPhantGrabbable FindLog()
        {
            if (handGrab && handGrab.grabbables != null)
                foreach (var g in handGrab.grabbables) if (g && g.pickupCollider && g.isActiveAndEnabled) return g;
            return null;
        }

        string Blocker()
        {
            var fruit = GetComponent<FruitInteraction>();
            var actionLock = GetComponent<ElephantActionLock>();
            if (fruit && fruit.IsBusy) return "Sit: fruit action in progress";
            if (actionLock && actionLock.DrinkActive) return "Sit: Chunzi is drinking";
            if (handGrab && handGrab.IsHolding) return "Sit: put the log down first";
            if (!locomotion.enableLocomotion) return "Sit: locomotion is off";
            return null;
        }

        // Final spot: hips standOffset in front of the log centre, facing away from the log (towards the
        // follow camera, so the seated pose is seen from the front).
        bool Plan(DigiPhantGrabbable log, out string reason)
        {
            // Prefer the side facing the follow camera; if that side is blocked, sit from the other one.
            if (PlanSide(log, false, out reason)) return true;
            if (reason != null && reason.StartsWith("Sit:")) return false;
            string first = reason;
            if (PlanSide(log, true, out reason)) return true;
            reason = first;
            return false;
        }

        bool PlanSide(DigiPhantGrabbable log, bool otherSide, out string reason)
        {
            reason = null;
            Vector3 centre = log.pickupCollider.bounds.center;
            Vector3 axis3 = log.Body ? log.Body.rotation * log.localLongAxis.normalized : log.transform.up;
            if (Mathf.Abs(Vector3.Dot(axis3, Vector3.up)) > .3f) { reason = "Sit: the log is not lying down"; return false; }
            Vector3 along = Vector3.ProjectOnPlane(axis3, Vector3.up).normalized;
            Vector3 face = Vector3.Cross(Vector3.up, along);
            var camera = locomotion.followCamera;
            if (camera && Vector3.Dot(face, camera.transform.forward) > 0) face = -face;
            if (otherSide) face = -face;
            float top = log.pickupCollider.ClosestPoint(centre + Vector3.up * 3).y;
            if (top > 1) { reason = "Sit: the log is too high to sit on"; return false; }

            var travel = locomotion.travelRoot;
            Vector3 legForward = leftPaw.position + rightPaw.position - leftToe.position - rightToe.position; legForward.y = 0;
            finalRotation = Quaternion.AngleAxis(Vector3.SignedAngle(legForward, face, Vector3.up), Vector3.up) * travel.rotation;
            // Hip offset from the travel root, expressed in the final orientation.
            Vector3 hipLocal = travel.InverseTransformPoint((leftFemur.position + rightFemur.position) * .5f);
            Vector3 hipOffset = finalRotation * Vector3.Scale(hipLocal, travel.lossyScale); hipOffset.y = 0;
            Vector3 hipTarget = centre + face * standOffset; hipTarget.y = travel.position.y;
            finalPosition = hipTarget - hipOffset;
            seatCentre = new Vector3(centre.x, top, centre.z);
            facing = face;

            // Walk to a staging point in front, turn there, back up onto the log. If the straight way to the
            // staging point crosses the log, go round its nearer end first.
            // BackUp travels along the final travel forward, so stage along it as well.
            Vector3 finalForward = finalRotation * Vector3.forward; finalForward.y = 0; finalForward.Normalize();
            Vector3 staging = finalPosition + finalForward * approachDistance;
            waypoints.Clear();
            // Half length along the log axis (the AABB corners projected on it).
            var box = log.pickupCollider.bounds;
            float halfLength = 0;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = box.center + Vector3.Scale(box.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                halfLength = Mathf.Max(halfLength, Mathf.Abs(Vector3.Dot(corner - centre, along)));
            }
            Vector3 start = travel.position;
            if (Vector3.Dot(start - centre, face) < 0 && SegmentNearPoint(start, staging, centre, halfLength + 1.5f))
            {
                // Behind the log: walk out past its nearer end on the log line, then round to the front side.
                float end = Vector3.Dot(start - centre, along) >= 0 ? 1 : -1;
                Vector3 pastEnd = centre + along * end * (halfLength + 3f); pastEnd.y = start.y;
                Vector3 frontSide = centre + along * end * (halfLength + 2.5f) + face * 2.5f; frontSide.y = start.y;
                waypoints.Add(pastEnd);
                waypoints.Add(frontSide);
            }
            waypoints.Add(staging);
            return RoomCheck(log, start, out reason);
        }

        static bool SegmentNearPoint(Vector3 a, Vector3 b, Vector3 p, float radius)
        {
            a.y = b.y = p.y = 0;
            Vector3 ab = b - a; float t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(a + ab * t, p) < radius;
        }

        // There are almost no colliders in the course, so check renderer bounds along the path and at the seat.
        // An obstacle's footprint in its own (usually yaw-only) frame, so rotated canopies are not
        // inflated to their world AABB; heights use the world bounds.
        bool ignoreRoom;

        // Plan without the room check; pick the side whose seat is nearer the stage centre.
        bool PlanIgnoringRoom(DigiPhantGrabbable log, out string reason)
        {
            ignoreRoom = true;
            try
            {
                if (!PlanSide(log, false, out reason)) return false;
                Vector3 centre = locomotion.StageCentre;
                float near = Flat(finalPosition - centre);
                if (!PlanSide(log, true, out reason)) return PlanSide(log, false, out reason);
                if (near <= Flat(finalPosition - centre)) PlanSide(log, false, out reason);
                return true;
            }
            finally { ignoreRoom = false; }
        }

        static float Flat(Vector3 v) { v.y = 0; return v.magnitude; }

        struct Obstacle { public Matrix4x4 toLocal; public Bounds local; public Vector3 scale; public float bottom; public string name; }

        bool RoomCheck(DigiPhantGrabbable log, Vector3 start, out string reason)
        {
            reason = null;
            if (ignoreRoom) return true;
            var travel = locomotion.travelRoot;
            float ground = travel.position.y;
            // Standing elephant: head top about 3.3 m; seated head about 4.8 m.
            float standingTop = ground + 3.3f, seatedTop = ground + 4.8f;
            var obstacles = new List<Obstacle>();
            if (checkRoom)
                foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                {
                    var t = renderer.transform;
                    // The elephant itself and the log (with its cut ends) are not obstacles.
                    if (t.IsChildOf(travel) || t.IsChildOf(log.transform)) continue;
                    if (renderer is ParticleSystemRenderer || !renderer.enabled) continue;
                    var world = renderer.bounds;
                    // Ground, stage grid and flat route markers; very large objects are terrain.
                    if (world.max.y < ground + .3f || world.size.x > 20 || world.size.z > 20) continue;
                    var filter = renderer.GetComponent<MeshFilter>();
                    var obstacle = filter && filter.sharedMesh
                        ? new Obstacle { toLocal = t.worldToLocalMatrix, local = filter.sharedMesh.bounds, scale = t.lossyScale }
                        : new Obstacle { toLocal = Matrix4x4.identity, local = world, scale = Vector3.one };
                    obstacle.bottom = world.min.y;
                    obstacle.name = renderer.name;
                    // Whatever the elephant already stands next to is not a reason to refuse.
                    if (Touches(obstacle, start, 1.5f, standingTop)) continue;
                    obstacles.Add(obstacle);
                }
            Vector3 centre = locomotion.StageCentre;
            float limit = locomotion.stageRadius > 0 ? locomotion.stageRadius - 1 : float.PositiveInfinity;
            var points = new List<Vector3> { start };
            points.AddRange(waypoints);
            points.Add(finalPosition);
            for (int i = 0; i + 1 < points.Count; i++)
            {
                float length = Vector3.Distance(points[i], points[i + 1]);
                for (float d = 0; d <= length; d += .3f)
                {
                    Vector3 sample = Vector3.Lerp(points[i], points[i + 1], length < 1e-4f ? 0 : d / length);
                    Vector3 flat = sample - centre; flat.y = 0;
                    if (flat.magnitude > limit) { reason = "No room to sit on the log: too close to the stage edge"; return false; }
                    string hit = Blocked(obstacles, sample, 1.5f, standingTop);
                    if (hit != null) { reason = "No room to sit on the log: " + hit + " is in the way"; return false; }
                }
            }
            // Turning on the spot sweeps the whole body length.
            foreach (var turn in waypoints)
            {
                string hit = Blocked(obstacles, turn, 2.5f, standingTop);
                if (hit != null) { reason = "No room to turn: " + hit + " is too close"; return false; }
            }
            // Seated: the raised head, about 0.8 m in front of the hips, 1 m around, up to 4.8 m.
            string near = Blocked(obstacles, seatCentre + facing * (hipForward + .8f), 1f, seatedTop);
            if (near != null) { reason = "No room to sit on the log: " + near + " is too close"; return false; }
            return true;
        }

        string Blocked(List<Obstacle> obstacles, Vector3 at, float radius, float top)
        {
            foreach (var obstacle in obstacles) if (Touches(obstacle, at, radius, top)) return obstacle.name;
            return null;
        }

        bool Touches(Obstacle obstacle, Vector3 at, float radius, float top)
        {
            if (obstacle.bottom > top) return false;
            Vector3 local = obstacle.toLocal.MultiplyPoint3x4(new Vector3(at.x, obstacle.local.center.y, at.z));
            if (obstacle.toLocal == Matrix4x4.identity) local = new Vector3(at.x, obstacle.local.center.y, at.z);
            float reach = radius + clearance;
            float rx = reach / Mathf.Max(.001f, Mathf.Abs(obstacle.scale.x)), rz = reach / Mathf.Max(.001f, Mathf.Abs(obstacle.scale.z));
            return Mathf.Abs(local.x - obstacle.local.center.x) <= obstacle.local.extents.x + rx &&
                   Mathf.Abs(local.z - obstacle.local.center.z) <= obstacle.local.extents.z + rz;
        }

        /// <summary>Pose review: stand in front of the log facing away from it, then sit down (no walk).</summary>
        public void DebugSitOnLog()
        {
            if (!Initialise() || IsBusy) return;
            var log = FindLog();
            if (!log) { Status = "Sit: no grabbable log in the scene"; return; }
            string blocker = Blocker();
            if (blocker != null) { Status = blocker; return; }
            if (!Plan(log, out string reason) && reason != null && reason.StartsWith("Sit:")) { Status = reason; return; }
            Teleport(finalPosition, finalRotation);
            byGesture = false;
            StartSettle();
        }

        void TryStartByGesture()
        {
            var log = FindLog();
            string blocker = log ? Blocker() : "Sit: no grabbable log in the scene";
            if (blocker == null && log.Body && !log.Body.isKinematic && (log.Body.linearVelocity.sqrMagnitude > .0025f || log.Body.angularVelocity.sqrMagnitude > .01f))
                blocker = "Sit: wait until the log stops moving";
            if (blocker == null && !Plan(log, out string reason))
            {
                // Only an impossible log refuses; obstacles and the edge do not.
                if (reason != null && reason.StartsWith("Sit:")) blocker = reason;
                else if (!PlanIgnoringRoom(log, out string impossible)) blocker = impossible ?? reason;
                else Debug.Log("SIT_IGNORING " + reason + " (sitting anyway)");
            }
            if (blocker != null)
            {
                Status = blocker; needsRelease = true;
                Debug.Log("SIT_REFUSED " + blocker + " (lower the V sign and show it again to retry)");
                return;
            }
            Debug.Log("SIT_START walking to the log");
            byGesture = true;
            Current = Phase.Approach;
            ResetProgress();
            Status = "Walking to the log";
        }

        // Stop on the spot and let the gait blend to idle, so the sit starts (and ends) on a standing pose.
        void StartSettle()
        {
            locomotion.autopilotActive = false;
            locomotion.autopilotForward = locomotion.autopilotTurn = 0;
            locomotion.actionPoseOwned = locomotion.actionMovementPaused = true;
            settleUntil = Time.time + settleSeconds;
            Current = Phase.Settle;
            Status = "Settling before sitting down";
        }

        void Teleport(Vector3 position, Quaternion rotation)
        {
            var travel = locomotion.travelRoot;
            Vector3 delta = position - travel.position; delta.y = 0;
            travel.SetPositionAndRotation(travel.position + delta, rotation);
            var camera = locomotion.followCamera;
            if (camera && locomotion.followElephant) camera.transform.position += delta;
            locomotion.StopMotion();
        }

        void Begin()
        {
            snapshot.Clear();
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) snapshot[t] = new Pose(t.localPosition, t.localRotation);
            modelPosition = model.localPosition; modelRotation = model.localRotation;
            skins.Clear();
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            { skins[skin] = (skin.forceMatrixRecalculationPerRender, skin.updateWhenOffscreen); skin.forceMatrixRecalculationPerRender = true; skin.updateWhenOffscreen = true; }
            // Hip pivot and axis, signed so a positive angle raises the front (as in FruitInteraction).
            Vector3 worldPivot = (leftFemur.position + rightFemur.position) * .5f;
            Vector3 worldAxis = (rightFemur.position - leftFemur.position).normalized;
            Vector3 front = (leftPaw.position + rightPaw.position) * .5f;
            if ((Quaternion.AngleAxis(10, worldAxis) * (front - worldPivot)).y < (front - worldPivot).y) worldAxis = -worldAxis;
            pivot = modelParent.InverseTransformPoint(worldPivot);
            axis = modelParent.InverseTransformDirection(worldAxis).normalized;
            leftToeAnchor = modelParent.InverseTransformPoint(leftToe.position);
            rightToeAnchor = modelParent.InverseTransformPoint(rightToe.position);
            leftToeRotation = Quaternion.Inverse(modelParent.rotation) * leftToe.rotation;
            rightToeRotation = Quaternion.Inverse(modelParent.rotation) * rightToe.rotation;
            toeHeight = Mathf.Min(leftToe.position.y, rightToe.position.y);
            framedCamera = locomotion.followCamera;
            if (framedCamera) { originalFieldOfView = framedCamera.fieldOfView; framedCamera.fieldOfView = Mathf.Max(originalFieldOfView, 50); }
            blend = 0;
            Current = Phase.SittingDown;
            Status = "Sitting down on the log";
        }

        public void StandUp()
        {
            if (Current == Phase.SittingDown || Current == Phase.Seated) { Current = Phase.StandingUp; Status = "Standing up"; }
            else if (IsWalking) Stop("Sit stopped");
        }

        /// <summary>Immediate return to the standing snapshot (resets, disable).</summary>
        public void Cancel()
        {
            if (!IsBusy) return;
            if (IsWalking) { Stop("Sit cancelled"); return; }
            blend = 0; ApplyPose(0); Finish("Sit cancelled");
        }

        // Walk-over ends where it is; control returns.
        void Stop(string status)
        {
            Current = Phase.Idle;
            ReleaseControl();
            Status = status;
            needsRelease = byGesture;
        }

        void ReleaseControl()
        {
            if (locomotion)
            {
                locomotion.autopilotActive = false;
                locomotion.autopilotForward = locomotion.autopilotTurn = 0;
                locomotion.actionPoseOwned = locomotion.actionMovementPaused = false;
                locomotion.StopMotion();
            }
            if (handGrab) handGrab.overlaySuspended = false;
        }

        void Finish(string status)
        {
            RestorePose();
            foreach (var item in skins) if (item.Key) { item.Key.forceMatrixRecalculationPerRender = item.Value.force; item.Key.updateWhenOffscreen = item.Value.update; }
            skins.Clear();
            if (framedCamera) framedCamera.fieldOfView = originalFieldOfView;
            framedCamera = null;
            snapshot.Clear();
            ReleaseControl();
            Current = Phase.Idle;
            Status = status;
            needsRelease = byGesture;
        }

        void ResetProgress() { bestDistance = float.PositiveInfinity; progressTime = Time.time; }

        void LateUpdate()
        {
            if (!Initialise()) return;
            float now = Time.realtimeSinceStartup;
            bool victory = handGrab && handGrab.AnyVictory(now);
            if (victory) lastVictory = now;
            // Released only after releaseDelay without the V sign: one misread frame does not end the sit.
            bool released = !victory && now - lastVictory > releaseDelay;
            if (!IsBusy)
            {
                // Arming: both hands V held for armSeconds; after a refusal or a finish, release first.
                // A brief recognition gap (under releaseDelay) does not restart the count.
                if (released) { needsRelease = false; armHeld = 0; return; }
                if (needsRelease) { if (victory) Status = "Lower the V sign, then show it again to retry"; return; }
                if (!victory) return;
                armHeld += Time.deltaTime;
                Status = "V sign " + armHeld.ToString("0.0") + "/" + armSeconds.ToString("0.0") + " s: sit on the log";
                if (armHeld >= armSeconds) { armHeld = 0; TryStartByGesture(); }
                return;
            }
            if (!locomotion || !locomotion.enabled || !locomotion.enableLocomotion) { Cancel(); return; }
            // Everyone's control is frozen; FruitInteraction (1000) has already written its flags this frame.
            locomotion.actionPoseOwned = locomotion.actionMovementPaused = true;
            if (handGrab) handGrab.overlaySuspended = true;
            if (IsWalking)
            {
                if (Current == Phase.Settle)
                {
                    locomotion.autopilotActive = false;
                    if (Time.time >= settleUntil) { Teleport(finalPosition, finalRotation); Begin(); }
                    return;
                }
                Drive();
                return;
            }
            locomotion.autopilotActive = false;
            // The whole sequence plays out by itself: seated for holdSeconds, then stand up.
            if (Current == Phase.Seated && Time.time >= seatedUntil) StandUp();
            float dt = Mathf.Min(Time.deltaTime, .1f);
            if (Current == Phase.SittingDown) { blend = Mathf.MoveTowards(blend, 1, dt / sitSeconds); if (blend >= 1) { Current = Phase.Seated; seatedUntil = Time.time + holdSeconds; Status = "Seated on the log"; } }
            else if (Current == Phase.StandingUp) { blend = Mathf.MoveTowards(blend, 0, dt / standSeconds); }
            ApplyPose(Mathf.SmoothStep(0, 1, blend));
            if (Current == Phase.StandingUp && blend <= 0) Finish("Stood up; control returned");
        }

        // Autopilot: steer the real gait (inputs are read by the next frame's Evaluate).
        void Drive()
        {
            var travel = locomotion.travelRoot;
            Vector3 position = travel.position;
            Vector3 forward = travel.forward; forward.y = 0;
            float distance;
            float drive = 0, steer = 0;
            if (Current == Phase.Approach)
            {
                Vector3 to = waypoints[0] - position; to.y = 0;
                distance = to.magnitude;
                float yaw = Vector3.SignedAngle(forward, to, Vector3.up);
                steer = Mathf.Clamp(yaw / 30f, -1, 1);
                drive = Mathf.Abs(yaw) < 25 ? Mathf.Clamp(distance / 2.5f, .3f, walkInput) : 0;
                bool arrived = distance < .35f;
                // Turning on the spot is progress too.
                distance += Mathf.Abs(yaw) / 30f;
                if (arrived)
                {
                    waypoints.RemoveAt(0);
                    ResetProgress();
                    if (waypoints.Count == 0) { Current = Phase.TurnAround; Status = "Turning around"; }
                    return;
                }
            }
            else if (Current == Phase.TurnAround)
            {
                Vector3 target = finalRotation * Vector3.forward; target.y = 0;
                float yaw = Vector3.SignedAngle(forward, target, Vector3.up);
                distance = Mathf.Abs(yaw) / 30f;
                steer = Mathf.Clamp(yaw / 25f, -1, 1);
                if (Mathf.Abs(yaw) < 2.5f) { Current = Phase.BackUp; ResetProgress(); Status = "Backing up onto the log"; return; }
            }
            else
            {
                Vector3 target = finalRotation * Vector3.forward; target.y = 0; target.Normalize();
                float remaining = Vector3.Dot(position - finalPosition, target);
                // Cross-track: angle the heading so reversing removes the sideways error.
                float sideways = Vector3.Dot(position - finalPosition, Vector3.Cross(Vector3.up, target));
                Vector3 heading = Quaternion.AngleAxis(Mathf.Clamp(sideways * 20f, -15, 15), Vector3.up) * target;
                distance = Mathf.Max(0, remaining) + Mathf.Abs(sideways);
                steer = Mathf.Clamp(Vector3.SignedAngle(forward, heading, Vector3.up) / 25f, -.5f, .5f);
                drive = -Mathf.Clamp(remaining / 1.2f, .25f, backInput);
                if (remaining <= .05f) { StartSettle(); return; }
            }
            locomotion.autopilotActive = true;
            locomotion.autopilotForward = drive;
            locomotion.autopilotTurn = steer;
            if (distance < bestDistance - .1f) { bestDistance = distance; progressTime = Time.time; }
            else if (Time.time - progressTime > stuckSeconds)
            {
                // Blocked on the way: skip to the seat and sit anyway (Settle ends with a teleport there).
                Debug.Log("SIT_SKIP walk-over stuck in " + Current + "; moving to the log and sitting");
                StartSettle();
            }
        }

        void RestorePose()
        {
            foreach (var pair in snapshot) if (pair.Key) pair.Key.SetLocalPositionAndRotation(pair.Value.position, pair.Value.rotation);
            if (model) model.SetLocalPositionAndRotation(modelPosition, modelRotation);
        }

        void ApplyPose(float s)
        {
            RestorePose();
            if (s <= 0) return;
            Vector3 worldAxis = modelParent.TransformDirection(axis);
            // 1. Torso up about the hips.
            var pitch = Quaternion.AngleAxis(torsoPitch * s, axis);
            model.localPosition = pivot + pitch * (modelPosition - pivot);
            model.localRotation = pitch * modelRotation;
            // 2. Hips back and down onto the log.
            Vector3 hip = (leftFemur.position + rightFemur.position) * .5f;
            Vector3 seatedHip = seatCentre + Vector3.up * hipAboveSeat + facing * hipForward;
            model.localPosition += modelParent.InverseTransformVector((seatedHip - hip) * s);
            // 3. Upper spine leans forward; the neck takes back part of the pitch.
            Lean(spine2, -spineLean * .5f * s, worldAxis);
            Lean(spine3, -spineLean * .5f * s, worldAxis);
            Lean(neck, -torsoPitch * neckCompensation * s, worldAxis);
            // 4. Hind feet: from the standing anchors to planted feet in front of the seated hips, knees up.
            Vector3 hipNow = (leftFemur.position + rightFemur.position) * .5f;
            SolveHind(leftFemur, leftTibia, leftToe, leftToeAnchor, leftToeRotation, hipNow, s);
            SolveHind(rightFemur, rightTibia, rightToe, rightToeAnchor, rightToeRotation, hipNow, s);
            // 5. Front feet rest on the hind knees.
            SolveFront(leftHumerus, leftRadius, leftPaw, leftTibia, s);
            SolveFront(rightHumerus, rightRadius, rightPaw, rightTibia, s);
        }

        static void Lean(Transform bone, float degrees, Vector3 worldAxis)
        {
            if (bone) bone.rotation = Quaternion.AngleAxis(degrees, worldAxis) * bone.rotation;
        }

        void SolveHind(Transform thigh, Transform shin, Transform toe, Vector3 anchor, Quaternion toeRotation, Vector3 hipMid, float s)
        {
            Vector3 standing = modelParent.TransformPoint(anchor);
            Vector3 lateral = Vector3.ProjectOnPlane(thigh.position - hipMid, Vector3.up);
            lateral -= Vector3.Dot(lateral, facing) * facing;
            Vector3 seated = new Vector3(hipMid.x, toeHeight, hipMid.z) + lateral + facing * footForward;
            Vector3 target = Vector3.Lerp(standing, seated, s);
            Vector3 pole = thigh.position + facing + Vector3.up * .6f;
            SolveTwoBone(thigh, shin, toe, target, pole);
            toe.rotation = modelParent.rotation * toeRotation;
        }

        void SolveFront(Transform upper, Transform lower, Transform paw, Transform knee, float s)
        {
            Vector3 knees = knee.position + Vector3.up * kneeTop + facing * .05f;
            if (frontLegs == FrontLegPose.StraightForward)
            {
                // Held straight out in front: aim the standing-shape leg along the facing, tilted down by armDrop.
                Vector3 side = Vector3.Cross(Vector3.up, facing);
                Vector3 reach = Quaternion.AngleAxis(armDrop, side) * facing;
                knees = upper.position + reach * Vector3.Distance(upper.position, paw.position);
            }
            Vector3 target = Vector3.Lerp(paw.position, knees, s);
            if (frontLegs != FrontLegPose.BentOnKnees)
            {
                // Keep the elbow and wrist as they stand (RestorePose); swing the whole leg from the shoulder.
                upper.rotation = Quaternion.FromToRotation(paw.position - upper.position, target - upper.position) * upper.rotation;
                return;
            }
            // Elbows bend back and slightly out, like forearms resting on knees.
            Vector3 outward = Vector3.ProjectOnPlane(upper.position - (leftHumerus.position + rightHumerus.position) * .5f, Vector3.up).normalized;
            Vector3 pole = upper.position - facing * .6f + outward * .3f;
            SolveTwoBone(upper, lower, paw, target, pole);
        }

        // Two-bone IK (same construction as FruitInteraction.SolveHindLeg): reach clamped to the chain.
        static void SolveTwoBone(Transform upperBone, Transform lowerBone, Transform end, Vector3 target, Vector3 poleTarget)
        {
            Vector3 root = upperBone.position;
            float upper = Vector3.Distance(root, lowerBone.position), lower = Vector3.Distance(lowerBone.position, end.position);
            Vector3 direction = target - root; float distance = direction.magnitude;
            if (distance < .0001f || upper < .0001f || lower < .0001f) return;
            direction /= distance;
            distance = Mathf.Clamp(distance, Mathf.Abs(upper - lower) + .00001f, upper + lower - .00001f);
            float along = (upper * upper + distance * distance - lower * lower) / (2 * distance);
            float bend = Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
            Vector3 pole = poleTarget - root; pole -= Vector3.Dot(pole, direction) * direction;
            if (pole.sqrMagnitude < .000001f) pole = Vector3.Cross(direction, upperBone.right);
            Vector3 joint = root + direction * along + pole.normalized * bend;
            upperBone.rotation = Quaternion.FromToRotation(lowerBone.position - root, joint - root) * upperBone.rotation;
            lowerBone.rotation = Quaternion.FromToRotation(end.position - lowerBone.position, target - lowerBone.position) * lowerBone.rotation;
        }

        void OnDisable() => Cancel();
    }
}
