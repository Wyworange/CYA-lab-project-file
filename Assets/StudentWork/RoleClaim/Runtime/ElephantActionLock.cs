using UnityEngine;
using DigiPhant.StudentWork.Drinking;

namespace DigiPhant.StudentWork
{
    // The team's single action lock: drinking (Chunzi), fruit (Yuwen) and wood (Aurora) never run
    // together. Fruit and wood keep their own state; drinking asks through IDrinkingActionGate.
    // Runs before the controller (-100) so the pause flags it sets are used in the same frame.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public class ElephantActionLock : MonoBehaviour, IDrinkingActionGate
    {
        [Tooltip("Existing course water; the drinking module checks its distance to the elephant.")]
        public string waterName = "02 Water basin";

        public MonoBehaviour Owner { get; private set; }
        public DrinkingInteraction Drinking => drinking;
        /// <summary>Drinking owns the elephant: fruit and wood must not start.</summary>
        public bool DrinkActive => drinking && drinking.IsActive;

        DigiPhantController controller;
        DigiPhantLocomotion locomotion;
        FruitInteraction fruit;
        DigiPhantHandGrab handGrab;
        DrinkingInteraction drinking;
        bool pausedByLock;

        void Awake()
        {
            controller = GetComponent<DigiPhantController>();
            locomotion = GetComponent<DigiPhantLocomotion>();
            fruit = GetComponent<FruitInteraction>();
            handGrab = FindAnyObjectByType<DigiPhantHandGrab>(FindObjectsInactive.Include);
        }

        void Start()
        {
            // Wire Chunzi's module at runtime (no scene edit): her rig lookup, the existing basin, this lock.
            drinking = GetComponent<DrinkingInteraction>();
            if (!drinking) drinking = gameObject.AddComponent<DrinkingInteraction>();
            if (!drinking.travelRoot || !drinking.head || drinking.trunk == null || drinking.trunk.Length == 0) drinking.AssignExistingRig();
            if (!drinking.water)
            {
                var basin = GameObject.Find(waterName) ?? GameObject.Find("WaterPool");
                if (basin) drinking.water = basin.transform;
            }
            drinking.actionGate = this;
            drinking.soloTesting = false;
        }

        bool FruitBusy => fruit && fruit.IsBusy;
        // FruitInteraction resets the pause flags every frame only while it is running.
        bool FruitRewritesFlags => fruit && fruit.isActiveAndEnabled && fruit.Initialise();
        // Holding wood, or the left-fist reach that precedes a grab.
        bool WoodBusy => handGrab && handGrab.isActiveAndEnabled && (handGrab.IsHolding || handGrab.LeftGesture == HandGesture.Fist);

        ElephantSitOnLog sit;
        bool SitBusy => (sit || (sit = GetComponent<ElephantSitOnLog>())) && sit.IsBusy;

        public bool CanRecognize(DrinkingInteraction owner) => !FruitBusy && !WoodBusy && !SitBusy && (!Owner || Owner == owner);

        // Chunzi's drinking panel (bottom-right, 265x220) is hidden through its own showDebugUI flag;
        // these buttons sit just above it, or alone in the corner when it is hidden.
        void OnGUI()
        {
            if (!drinking) return;
            if (drinking.showDebugUI)
            {
                if (GUI.Button(new Rect(Screen.width - 60, Mathf.Max(0, Screen.height - 256), 50, 24), "Hide")) drinking.showDebugUI = false;
            }
            else if (GUI.Button(new Rect(Screen.width - 170, Screen.height - 34, 160, 24), "Show drinking panel")) drinking.showDebugUI = true;
        }

        void OnDisable()
        {
            if (pausedByLock && locomotion) locomotion.actionPoseOwned = locomotion.actionMovementPaused = false;
            pausedByLock = false;
            Owner = null;
        }

        public bool TryAcquire(DrinkingInteraction owner)
        {
            if (!CanRecognize(owner)) return false;
            Owner = owner;
            Pause();
            return true;
        }

        public void Release(DrinkingInteraction owner)
        {
            if (Owner == owner) Owner = null;
        }

        void Pause()
        {
            // With locomotion off, Evaluate treats actionPoseOwned as a stale fruit action and resets it.
            if (locomotion && locomotion.enableLocomotion) { locomotion.StopMotion(); locomotion.actionPoseOwned = locomotion.actionMovementPaused = true; }
            if (handGrab) handGrab.overlaySuspended = true;
            pausedByLock = true;
        }

        void LateUpdate()
        {
            if (Owner && (!drinking || !drinking.IsActive)) Owner = null;
            // FruitInteraction rewrites these flags every frame; without it, undo our own pause here.
            if (pausedByLock && !FruitRewritesFlags)
            {
                if (locomotion) locomotion.actionPoseOwned = locomotion.actionMovementPaused = false;
                if (handGrab) handGrab.overlaySuspended = false;
            }
            pausedByLock = false;
            if (DrinkActive) { Pause(); return; }
            // Chunzi's drink gesture is both hands up for 2 s; raising the left hand alone walks. While both
            // are up, stop travel this frame (read directly: her detector runs later in the frame).
            if (SitBusy || !drinking || !locomotion || !controller || controller.inputMode != InputMode.Camera || !controller.IsCalibrated) return;
            float now = Time.realtimeSinceStartup;
            bool bothUp = controller.TryReadMovement(drinking.performer, Movement.LeftHandHeight, now, out float left) &&
                          controller.TryReadMovement(drinking.performer, Movement.RightHandHeight, now, out float right) &&
                          left > DrinkingInteraction.RaiseMargin && right > DrinkingInteraction.RaiseMargin;
            // Freeze body gestures too: the same raised hands drive the trunk curl and (seated) the legs,
            // and the drink snapshot must start from the neutral pose, not a curled trunk.
            if ((bothUp || drinking.IsRecognizing) && locomotion.enableLocomotion)
            { locomotion.StopMotion(); locomotion.actionMovementPaused = locomotion.actionPoseOwned = true; pausedByLock = true; }
        }
    }
}
