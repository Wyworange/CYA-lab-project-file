using System.Collections.Generic;
using UnityEngine;

namespace DigiPhant.StudentWork
{
    // Gesture role claim: the person who opens their mouth becomes Face, then the person who raises
    // both hands becomes Hands; the remaining person drives the body (Upper body). Slots are the
    // tracker's position-based performer numbers, so any renumbering clears the roles.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(900)]
    public class RoleAssignment : MonoBehaviour
    {
        public enum Step { Idle, ClaimFace, ClaimHands, Assigned }

        public string faceName = "Yuwen", handsName = "Aurora", bodyName = "Chunzi";
        [Tooltip("Seconds the claiming gesture must be held by exactly one person.")]
        [Min(.1f)] public float holdSeconds = .6f;
        [Range(.1f, 1)] public float mouthOpenThreshold = .45f;
        [Tooltip("Uncalibrated hand height in Seated / upper body mode (scaled by shoulder width; wrists clearly above the head).")]
        [Min(.1f)] public float handsRaisedUpperBody = 1.2f;
        [Tooltip("Uncalibrated hand height in Full body mode (scaled by torso length).")]
        [Min(.1f)] public float handsRaisedFullBody = .6f;
        [Tooltip("A claimant may drop out of tracking this long without restarting the hold.")]
        [Min(0)] public float dropoutGrace = .25f;

        public Step Current { get; private set; }
        public string Prompt { get; private set; } = "";
        public int FaceSlot { get; private set; }
        public int HandsSlot { get; private set; }
        public int BodySlot { get; private set; }
        /// <summary>True when nobody was left for Upper body, so the Face person also drives the body.</summary>
        public bool BodyShared { get; private set; }

        DigiPhantController controller;
        DigiPhantLocomotion locomotion;
        FruitInteraction fruit;
        DigiPhantHandGrab handGrab;
        int candidate;
        float candidateSince, candidateSeen;
        int claimants;
        int savedCount = -1;
        bool savedUpperBody;
        readonly List<int> savedControls = new List<int>();
        readonly List<int> savedSources = new List<int>();
        int savedHandPerformer = -1;
        int savedDrinkPerformer = -1;
        bool handsEnabled, claimHands;

        /// <summary>Aurora's hand grab exists in the scene, so the Hands toggle can be offered.</summary>
        public bool HandsAvailable => handGrab;
        public bool HandsEnabled => handsEnabled;

        void OnEnable()
        {
            controller = GetComponent<DigiPhantController>();
            locomotion = GetComponent<DigiPhantLocomotion>();
            fruit = GetComponent<FruitInteraction>();
            handGrab = FindAnyObjectByType<DigiPhantHandGrab>(FindObjectsInactive.Include);
            if (controller) controller.TrackingReset += OnTrackingReset;
        }
        void Start() => SetHandsEnabled(handsEnabled);

        /// <summary>The Hands toggle: hand recognition and the hand grab run only while it is on.</summary>
        public void SetHandsEnabled(bool on)
        {
            handsEnabled = on;
            if (handGrab) handGrab.enabled = on;
            if (fruit) { fruit.handsEnabled = on; fruit.SendFaceConfiguration(); }
            if (on && Current == Step.Assigned && HandsSlot == 0)
                Prompt = "Hands was off when roles were assigned, so nobody owns Hands yet: click Reassign roles (" + handsName + " raises both hands).";
        }
        void LateUpdate()
        {
            // Without a Hands role, never listen for a performer that does not exist (scene default is P3).
            if (handGrab && HandsSlot == 0 && controller && handGrab.performerId > controller.performerCount)
                handGrab.performerId = controller.performerCount;
        }
        void OnDisable()
        {
            if (controller) controller.TrackingReset -= OnTrackingReset;
            ClearRoles("");
        }

        public string RoleLabel(int slot)
        {
            if (Current != Step.Assigned) return "";
            var names = new List<string>();
            if (slot == FaceSlot) names.Add("Face · " + faceName);
            if (slot == HandsSlot) names.Add("Hands · " + handsName);
            if (slot == BodySlot) names.Add("Upper body · " + (BodyShared ? SlotName(slot) + " (test)" : bodyName));
            return names.Count == 0 ? "" : " (" + string.Join(", ", names) + ")";
        }

        string SlotName(int slot) => slot == FaceSlot ? faceName : slot == HandsSlot ? handsName : bodyName;

        public void BeginClaim()
        {
            if (!controller || !controller.CameraSessionActive) { Prompt = "Click Camera first, with everyone in view."; return; }
            ClearRoles("");
            // Only the selected modes are claimed: Facial → Face, Hands → Hands; Upper body is the rest.
            bool claimFace = fruit && fruit.faceMode;
            claimHands = handsEnabled && handGrab;
            // Until roles exist nobody owns the body, so raised hands must not walk the elephant.
            controller.InvalidateCalibration();
            candidate = 0;
            if (claimFace)
            {
                fruit.SetFaceClaim(true);
                Current = Step.ClaimFace;
                Prompt = "Face (" + faceName + "): open your mouth wide and hold.";
            }
            else if (claimHands) StartHandsClaim();
            else Assign();
        }

        public void ClearRoles(string reason)
        {
            // The old mapping's neutral pose was saved for other people: calibrate again.
            if (Current == Step.Assigned && RestoreMapping() && controller) controller.InvalidateCalibration();
            if (Current != Step.Idle && fruit) { fruit.SetFaceClaim(false); fruit.SetFaceSlot(0); }
            Current = Step.Idle;
            FaceSlot = HandsSlot = BodySlot = 0;
            BodyShared = false;
            candidate = 0;
            Prompt = reason;
        }

        void OnTrackingReset()
        {
            // Renumbering (Reassign people, group size, body mode, camera restart) breaks slot identity.
            if (Current != Step.Idle) ClearRoles("People were renumbered: assign roles again.");
        }

        void Update()
        {
            if (Current != Step.ClaimFace && Current != Step.ClaimHands) return;
            if (!controller || !controller.CameraSessionActive) { ClearRoles("Camera stopped: roles cleared."); return; }
            if (Current == Step.ClaimFace && (!fruit || !fruit.faceMode)) { ClearRoles("Facial was turned off: assign roles again."); return; }
            if (Current == Step.ClaimHands && !handsEnabled) { ClearRoles("Hands was turned off: assign roles again."); return; }
            float now = Time.realtimeSinceStartup;
            float handsRaised = controller.upperBodyOnly ? handsRaisedUpperBody : handsRaisedFullBody;
            int found = 0, count = 0;
            for (int slot = 1; slot <= controller.performerCount; slot++)
            {
                if (slot == FaceSlot || !controller.IsTracked(slot, now)) continue;
                bool claiming;
                if (Current == Step.ClaimFace)
                    claiming = fruit.TryGetClaimJaw(slot, now, out float jaw) && jaw >= mouthOpenThreshold;
                else
                    claiming = controller.TryReadRaw(slot, Movement.LeftHandHeight, now, out float left) && left >= handsRaised &&
                               controller.TryReadRaw(slot, Movement.RightHandHeight, now, out float right) && right >= handsRaised;
                if (claiming) { found = slot; count++; }
            }
            claimants = count;
            // Exactly one claimant, held steadily; two at once is ambiguous and restarts the hold.
            // Brief tracking dropouts (no claimant at all) keep the hold running.
            if (count == 0 && candidate > 0 && now - candidateSeen <= dropoutGrace) return;
            if (count != 1 || found != candidate) { candidate = count == 1 ? found : 0; candidateSince = candidateSeen = now; return; }
            candidateSeen = now;
            if (now - candidateSince < holdSeconds) return;
            if (Current == Step.ClaimFace)
            {
                FaceSlot = found;
                fruit.SetFaceClaim(false);
                // Start following this face now, so its 2 s neutral calibration overlaps the Hands step.
                fruit.SetFaceSlot(FaceSlot);
                candidate = 0;
                if (claimHands && controller.performerCount >= 2) { StartHandsClaim(); return; }
            }
            else HandsSlot = found;
            Assign();
        }

        void StartHandsClaim()
        {
            Current = Step.ClaimHands;
            candidate = 0;
            Prompt = (FaceSlot > 0 ? "Face = P" + FaceSlot + ". " : "") + "Hands (" + handsName + "): raise both hands above your head and hold.";
        }

        void Assign()
        {
            BodySlot = 0;
            for (int slot = 1; slot <= controller.performerCount && BodySlot == 0; slot++)
                if (slot != FaceSlot && slot != HandsSlot) BodySlot = slot;
            // Two-person testing: nobody is left, so the Face person (else Hands) also drives the body.
            BodyShared = BodySlot == 0;
            if (BodyShared) BodySlot = FaceSlot > 0 ? FaceSlot : HandsSlot > 0 ? HandsSlot : 1;
            SaveMapping();
            foreach (var c in controller.controls) c.performer = BodySlot;
            foreach (var source in Sources()) source.performer = BodySlot;
            if (handGrab && HandsSlot > 0) { savedHandPerformer = handGrab.performerId; handGrab.performerId = HandsSlot; }
            // Chunzi's drinking reads the Upper body person's hands.
            var drinking = GetComponent<Drinking.DrinkingInteraction>();
            if (drinking) { savedDrinkPerformer = drinking.performer; drinking.performer = BodySlot; }
            if (fruit) fruit.SetFaceSlot(FaceSlot);
            Current = Step.Assigned;
            // The body mapping changed, so the neutral pose must be taken again: start it right away.
            controller.BeginCalibrationCountdown(Time.realtimeSinceStartup);
            Prompt = "Roles set. Everyone hold a relaxed neutral pose for 10 s (automatic); " + faceName + " keeps a neutral face.";
        }

        IEnumerable<MovementSource> Sources()
        {
            if (!locomotion) yield break;
            foreach (var driver in new[] { locomotion.forward, locomotion.steering })
                if (driver?.sources != null)
                    foreach (var source in driver.sources) if (source != null) yield return source;
        }
        void SaveMapping()
        {
            savedCount = controller.performerCount; savedUpperBody = controller.upperBodyOnly;
            savedControls.Clear(); savedSources.Clear();
            foreach (var c in controller.controls) savedControls.Add(c.performer);
            foreach (var source in Sources()) savedSources.Add(source.performer);
        }
        bool RestoreMapping()
        {
            // Only undo our own mapping: if the group size or body mode changed, the controller
            // already applied its defaults and those must stay.
            bool ours = controller && savedControls.Count == controller.controls.Length &&
                        controller.performerCount == savedCount && controller.upperBodyOnly == savedUpperBody;
            if (ours) foreach (var c in controller.controls) ours &= c.performer == BodySlot;
            if (ours)
            {
                for (int i = 0; i < savedControls.Count; i++) controller.controls[i].performer = savedControls[i];
                int k = 0;
                foreach (var source in Sources()) if (k < savedSources.Count) source.performer = savedSources[k++];
            }
            if (handGrab && savedHandPerformer > 0 && handGrab.performerId == HandsSlot) handGrab.performerId = savedHandPerformer;
            var drinking = GetComponent<Drinking.DrinkingInteraction>();
            if (drinking && savedDrinkPerformer > 0 && drinking.performer == BodySlot) drinking.performer = Mathf.Min(savedDrinkPerformer, controller ? controller.performerCount : 4);
            savedDrinkPerformer = -1;
            savedHandPerformer = -1;
            savedControls.Clear(); savedSources.Clear();
            return ours;
        }

        public void DrawControls(float width)
        {
            bool face = fruit && fruit.faceMode, hands = handsEnabled && handGrab;
            if (hands)
            {
                bool receiving = Time.realtimeSinceStartup - handGrab.LastHandPacketTime < 1;
                GUILayout.Label("Hands reads P" + handGrab.performerId + (HandsSlot > 0 ? " (" + handsName + ")" : " (not claimed)") +
                    (receiving ? " · hand data OK" : " · no hand data for this person"), new GUIStyle(GUI.skin.label) { wordWrap = true }, GUILayout.Width(width));
            }
            GUILayout.Label("ROLES · Facial " + (face ? "on" : "off") + " · Hands " + (hands ? "on" : "off") + " · Upper body = remaining person");
            if (Current == Step.Idle)
            {
                string steps = face && hands ? "open mouth → raise hands" : face ? "open mouth" : hands ? "raise hands" : "body only";
                if (GUILayout.Button("Assign roles (" + steps + ")")) BeginClaim();
            }
            else if (Current == Step.Assigned)
            {
                string walker = BodyShared ? SlotName(BodySlot) : bodyName;
                GUILayout.Label("Walking & steering: P" + BodySlot + " · " + walker + " (left hand up = walk, tilt shoulders = steer)",
                    new GUIStyle(GUI.skin.label) { wordWrap = true }, GUILayout.Width(width));
                if (!controller.IsCalibrated && !controller.CalibrationPending)
                    GUILayout.Label("Not calibrated yet: the elephant cannot walk. Click Set neutral pose.", new GUIStyle(GUI.skin.label) { wordWrap = true }, GUILayout.Width(width));
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Reassign roles")) BeginClaim();
                if (GUILayout.Button("Clear roles")) ClearRoles("Roles cleared.");
                GUILayout.EndHorizontal();
            }
            else
            {
                float held = candidate > 0 ? Mathf.Clamp01((Time.realtimeSinceStartup - candidateSince) / holdSeconds) : 0;
                GUILayout.Label(claimants > 1 ? claimants + " people are doing it — only one at a time." :
                    candidate > 0 ? "Detecting P" + candidate + " · " + Mathf.RoundToInt(held * 100) + "%" : "Waiting for one person…");
                if (Current == Step.ClaimFace && fruit && !string.IsNullOrEmpty(fruit.Status) && fruit.Status.StartsWith("Facial unavailable"))
                    GUILayout.Label(fruit.Status, new GUIStyle(GUI.skin.label) { wordWrap = true }, GUILayout.Width(width));
                if (GUILayout.Button("Cancel")) ClearRoles("Role assignment cancelled.");
            }
            if (!string.IsNullOrEmpty(Prompt))
                GUILayout.Label(Prompt, new GUIStyle(GUI.skin.label) { wordWrap = true }, GUILayout.Width(width));
        }
    }
}
