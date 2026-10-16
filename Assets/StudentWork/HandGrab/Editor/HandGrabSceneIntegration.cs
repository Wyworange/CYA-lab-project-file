using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigiPhant.Editor
{
    // Integration step (Yuwen, 2026-10-08): put Aurora's hand grab into the main scene and make the
    // course Timber grabbable, so the team does not have to.
    //
    // Aurora's "Inject into Open Scene" re-solves a wrapped trunk grip around a 20 cm test cube and
    // refuses on this scene ("no safe 150-220 degree wrapped grip"). Her own scene already holds the
    // grip she authored for the Timber on this same shared elephant rig, so the component is built
    // here like her Inject does, but with those authored bone-local grip values, and without the test
    // cube. Her Timber setup is then run unchanged.
    // Runs once per machine after the scripts compile; the menu item reruns it.
    [InitializeOnLoad]
    static class HandGrabSceneIntegration
    {
        const string ScenePath = "Assets/DigiPhant/Scenes/DigiPhant.unity";
        const string RootName = "DigiPhant Hand Grab Test";
        const string DoneKey = "DigiPhant.HandGrab.MainSceneIntegrated.v2";

        // From Aurora's authored scene (AuroraJ03/Aurora-DigiPhant-HandGrab, ReferenceOnly DigiPhant.unity).
        static readonly Quaternion[] AuthoredGrip =
        {
            new Quaternion(-0.0000058810565f, -0.000001563281f, 0.034935012f, 0.99938965f),
            new Quaternion(-0.0000060858188f, 0.00000006315224f, -0.19850148f, 0.98010063f),
            new Quaternion(-0.000006068564f, -0.00000056986295f, -0.109985635f, 0.9939332f),
            new Quaternion(-0.000003994433f, 0.000013207951f, 0.2252006f, 0.97431237f),
            new Quaternion(-0.000007861551f, -0.000007604228f, 0.40231055f, 0.9155033f),
            new Quaternion(-0.000005116997f, -0.000012805177f, 0.35564703f, 0.9346203f),
            new Quaternion(-0.0000032306857f, -0.000011638654f, 0.29942232f, 0.9541207f),
        };
        const int AuthoredGripRoot = 3;
        static readonly Vector3 AuthoredGripAnchor = new Vector3(-0.14912124f, -0.30533823f, -0.0000036112754f);

        static HandGrabSceneIntegration()
        {
            if (Application.isBatchMode || EditorPrefs.GetBool(DoneKey, false)) return;
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorPrefs.GetBool(DoneKey, false)) Integrate();
            };
        }

        [MenuItem("DigiPhant/Hand Grab/Integrate into Main Scene (Inject + Timber)")]
        static void Integrate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { Debug.LogWarning("Hand grab integration: stop Play first, then run DigiPhant/Hand Grab/Integrate into Main Scene."); return; }
            // Attempt once automatically; failures are reported and the menu can retry.
            EditorPrefs.SetBool(DoneKey, true);
            try
            {
                if (!OpenCleanMainScene()) { EditorPrefs.DeleteKey(DoneKey); return; }
                var scene = SceneManager.GetActiveScene();
                var hand = BuildHandGrab(scene);
                EditorSceneManager.SaveScene(scene);
                // Aurora's tool, unchanged: Timber gets collider, Rigidbody, cut ends, grabbable registration.
                DigiPhantTimberSetup.MakeTimberGrabbableInOpenScene();
                // Aurora stands on the right (P3); the role claim overrides this at runtime.
                hand.performerId = 3;
                EditorUtility.SetDirty(hand);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("DIGIPHANT_HAND_GRAB_MAIN_SCENE_INTEGRATED " + ScenePath + " (hand grab with Aurora's authored grip, Timber grabbable: " +
                          hand.grabbables.Length + ", performerId 3)");
            }
            catch (Exception e)
            {
                Debug.LogError("Hand grab integration failed: " + e.Message +
                               "\nFix the cause, then run DigiPhant/Hand Grab/Integrate into Main Scene (Inject + Timber).");
            }
        }

        static bool OpenCleanMainScene()
        {
            var active = SceneManager.GetActiveScene();
            if (active.path == ScenePath && active.isDirty)
            {
                // An earlier failed setup may have left half-built objects in memory.
                if (!EditorUtility.DisplayDialog("Hand grab integration",
                        "The main scene has unsaved changes (possibly a half-finished hand-grab setup).\n\nReload it from disk before integrating? Unsaved changes in it will be discarded.",
                        "Reload and integrate", "Cancel")) return false;
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            else if (active.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            return true;
        }

        static DigiPhantHandGrab BuildHandGrab(Scene scene)
        {
            var controller = UnityEngine.Object.FindObjectsByType<DigiPhantController>(FindObjectsInactive.Include).SingleOrDefault();
            if (!controller) throw new InvalidOperationException("The main scene has no single DigiPhant Controls.");
            string backup = AssetDatabase.GenerateUniqueAssetPath(System.IO.Path.ChangeExtension(scene.path, null) + "_BeforeHandGrab.unity");
            if (!AssetDatabase.CopyAsset(scene.path, backup)) throw new System.IO.IOException("Could not create scene backup.");
            foreach (var old in UnityEngine.Object.FindObjectsByType<DigiPhantHandGrab>(FindObjectsInactive.Include))
                UnityEngine.Object.DestroyImmediate(old.gameObject);

            var root = new GameObject(RootName);
            var hand = root.AddComponent<DigiPhantHandGrab>();
            hand.controller = controller;
            var anchor = new GameObject("Hand Grab Grip Anchor");
            anchor.transform.SetParent(root.transform, false);
            hand.gripAnchor = anchor.transform;
            // Same bone lookup as Aurora's Inject: the controller's mapped head and trunk bones.
            hand.head = controller.controls.FirstOrDefault(c => c.label == "Head turn")?.bones?.FirstOrDefault();
            var trunkControl = controller.controls.FirstOrDefault(c => c.label == "Trunk curl");
            hand.trunk = trunkControl?.bones?.Where(x => x).ToArray() ?? Array.Empty<Transform>();
            hand.trunkTip = hand.trunk.LastOrDefault()?.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(x => x.name == "elephant_Trunk7-nub_bone") ?? hand.trunk.LastOrDefault();
            if (!hand.head || hand.trunk.Length != AuthoredGrip.Length || !hand.trunkTip)
                throw new InvalidOperationException("Could not find the elephant head and 7 trunk bones.");
            hand.trunkReachAxis = trunkControl.localAxis;
            hand.headReachReference = hand.head.localRotation;
            hand.trunkReachReference = hand.trunk.Select(x => x.localRotation).ToArray();
            var locomotion = controller.GetComponent<DigiPhantLocomotion>();
            hand.reachReferenceRoot = locomotion && locomotion.travelRoot ? locomotion.travelRoot : controller.transform;
            hand.headReachWorldRelative = Quaternion.Inverse(hand.reachReferenceRoot.rotation) * hand.head.rotation;
            // Aurora's authored overlay and grip values.
            hand.pickupVolume = new Vector3(.4f, .4f, .4f);
            hand.handTimeout = 2f;
            hand.headLowerAxis = Vector3.forward;
            hand.headLowerDirection = 1;
            hand.trunkReachDirection = -1;
            hand.trunkCarryDegrees = 12;
            hand.trunkCarryDirection = -1;
            hand.gripCurlDegrees = 47.09691f;
            hand.gripCurlDirection = -1;
            hand.gripJointCount = 4;
            hand.gripRadius = .33980766f;
            hand.trunkGripReference = (Quaternion[])AuthoredGrip.Clone();
            hand.gripAnchorRoot = hand.trunk[AuthoredGripRoot];
            hand.gripAnchorLocalPosition = AuthoredGripAnchor;
            hand.grabbables = Array.Empty<DigiPhantGrabbable>();
            EditorUtility.SetDirty(hand);
            EditorSceneManager.MarkSceneDirty(scene);
            return hand;
        }
    }
}
