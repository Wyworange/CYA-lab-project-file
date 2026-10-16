using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using DigiPhant.StudentWork.Drinking;

namespace DigiPhant.StudentWork.Drinking.Editor
{
    public sealed class DrinkingValidationGate : MonoBehaviour, IDrinkingActionGate
    {
        public bool allow = true;
        public int acquired, released;
        public bool CanRecognize(DrinkingInteraction owner) => allow;
        public bool TryAcquire(DrinkingInteraction owner) { if (!allow) return false; acquired++; return true; }
        public void Release(DrinkingInteraction owner) { released++; }
    }
    public static class DrinkingDeliveryBuilder
    {
        const string Folder = "Assets/StudentWork/Drinking";
        const string Output = "Deliverables/Drinking_2026-10-09";
        static void Check(bool pass, string message) { if (!pass) throw new Exception(message); }
        [MenuItem("DigiPhant/Chunzi Delivery/Build and validate drinking package")]
        public static void Build()
        {
            Check(!EditorApplication.isPlaying, "Exit Play before building delivery");
            Directory.CreateDirectory(Output);
            Scene preview = EditorSceneManager.OpenPreviewScene("Assets/StudentWork/GroupPerformance.unity");
            try
            {
                var all = preview.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
                var source = all.First(t => t.name == "02 Water basin");
                var prop = UnityEngine.Object.Instantiate(source.gameObject);
                SceneManager.MoveGameObjectToScene(prop, preview);
                prop.name = "Student drinking station";
                prop.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                int index = 0;
                foreach (var filter in prop.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!filter.sharedMesh) continue;
                    string path = Folder + "/Meshes/Basin_" + index++ + ".asset";
                    if (!AssetDatabase.LoadAssetAtPath<Mesh>(path)) AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(filter.sharedMesh), path);
                    filter.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                }
                index = 0;
                foreach (var renderer in prop.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        string path = Folder + "/Materials/Basin_" + index++ + ".mat";
                        if (!AssetDatabase.LoadAssetAtPath<Material>(path))
                        {
                            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                            if (materials[i]) mat.SetColor("_BaseColor", materials[i].HasProperty("_BaseColor") ? materials[i].GetColor("_BaseColor") : materials[i].color);
                            AssetDatabase.CreateAsset(mat, path);
                        }
                        materials[i] = AssetDatabase.LoadAssetAtPath<Material>(path);
                    }
                    renderer.sharedMaterials = materials;
                }
                PrefabUtility.SaveAsPrefabAsset(prop, Folder + "/Prefabs/WaterPool.prefab");
                UnityEngine.Object.DestroyImmediate(prop);
                var controller = preview.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<DigiPhantController>(true)).First();
                var rigNotes = "Existing working rig references (GroupPerformance):\n";
                foreach (var c in controller.controls.Where(c => c.label == "Head turn" || c.label == "Trunk curl"))
                    rigNotes += c.label + ": " + string.Join(", ", c.bones.Select(t => t ? t.name : "MISSING")) + "\n";
                File.WriteAllText(Output + "/RIG_VERIFIED.txt", rigNotes);
                var camera = preview.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).First(c => c.name == "Main Camera");
                var texture = new RenderTexture(1280, 720, 24);
                var previous = RenderTexture.active;
                var target = camera.targetTexture;
                try
                {
                    camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
                    var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                    File.WriteAllBytes(Folder + "/Placement.png", image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
                }
                finally { camera.targetTexture = target; RenderTexture.active = previous; texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            Validate();
            AssetDatabase.Refresh(); AssetDatabase.SaveAssets();
            var paths = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith(Folder + "/", StringComparison.Ordinal) && !AssetDatabase.IsValidFolder(p)).ToArray();
            foreach (var p in paths)
                foreach (var dependency in AssetDatabase.GetDependencies(p, true))
                    Check(dependency.StartsWith(Folder + "/", StringComparison.Ordinal) || dependency.StartsWith("Packages/", StringComparison.Ordinal)
                        || dependency.StartsWith("Resources/", StringComparison.Ordinal) || dependency.StartsWith("Library/", StringComparison.Ordinal),
                        "External asset dependency: " + p + " -> " + dependency);
            AssetDatabase.ExportPackage(paths, Output + "/Drinking_2026-10-09.unitypackage", ExportPackageOptions.Default);
            Debug.Log("CHUNZI_DRINKING_DELIVERY_OK " + Path.GetFullPath(Output));
        }
        [MenuItem("DigiPhant/Chunzi Delivery/Validate drinking module")]
        public static void Validate()
        {
            Check(!EditorApplication.isPlaying, "Exit Play before validation");
            Directory.CreateDirectory(Output);
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = new GameObject("Drinking isolated validation"); SceneManager.MoveGameObjectToScene(go, scene);
                var rig = new GameObject("Rig").transform; rig.SetParent(go.transform, false);
                var head = new GameObject("Head").transform; head.SetParent(rig, false);
                var trunk = new GameObject("Trunk").transform; trunk.SetParent(head, false);
                var water = new GameObject("Water").transform; water.SetParent(go.transform, false); water.localPosition = Vector3.forward * 3;
                var d = go.AddComponent<DrinkingInteraction>(); d.showDebugUI = d.recognizeCamera = false;
                d.travelRoot = go.transform; d.rigRoot = rig; d.head = head; d.trunk = new[] { trunk }; d.water = water;
                var gate = go.AddComponent<DrinkingValidationGate>(); d.actionGate = gate;
                gate.allow = false; Check(!d.TryStart() && !d.IsActive, "Busy gate accepted");
                gate.allow = true;
                water.localPosition = Vector3.forward * 100; Check(!d.TryStart(), "Distant water accepted"); water.localPosition = Vector3.forward * 3;
                d.ObserveHands(true, .3f, 0, .1f); Check(d.HoldProgress == 0, "One hand triggered hold");
                for (int i = 0; i < 10; i++) d.ObserveHands(true, .3f, .3f, .1f);
                float before = d.HoldProgress;
                d.ObserveHands(false, 0, 0, .1f); Check(d.HoldProgress == before, "Short dropout earned/reset hold");
                for (int i = 0; i < 12; i++) d.ObserveHands(true, .3f, .3f, .1f);
                Check(d.IsActive && d.SequenceStarts == 1, "Two-second hold did not start once");
                Check(!d.TryStart() && d.SequenceStarts == 1, "Active sequence restarted");
                go.transform.SetPositionAndRotation(Vector3.one * 10, Quaternion.Euler(0, 90, 0));
                d.Tick(.1f, 1); Check(go.transform.position == Vector3.zero && Quaternion.Angle(go.transform.rotation, Quaternion.identity) < .001f, "Root not held");
                Check(Quaternion.Angle(head.localRotation, Quaternion.identity) > .01f, "Head did not move");
                for (int i = 0; i < 70; i++) d.Tick(.1f, 2 + i * .1f);
                Check(!d.IsActive && d.AwaitingNeutral && gate.released == 1, "Sequence did not complete/release lock");
                Check(Quaternion.Angle(head.localRotation, Quaternion.identity) < .001f && Quaternion.Angle(trunk.localRotation, Quaternion.identity) < .001f, "Rig not restored");
                for (int i = 0; i < 30; i++) d.ObserveHands(true, .3f, .3f, .1f);
                Check(d.SequenceStarts == 1, "Held gesture retriggered");
                Check(d.IsRecognizing, "Held both hands must reserve movement even while awaiting neutral");
                for (int i = 0; i < 5; i++) d.ObserveHands(true, 0, 0, .1f);
                Check(!d.AwaitingNeutral, "Neutral did not rearm");
                for (int i = 0; i < 8; i++) d.ObserveHands(true, .3f, .3f, .1f);
                for (int i = 0; i < 3; i++) d.ObserveHands(false, 0, 0, .1f);
                Check(d.HoldProgress == 0, "Long dropout did not reset");
                Check(d.TryStart(), "Manual start failed"); d.Tick(.1f, 20); d.Cancel();
                Check(!d.IsActive && gate.released == 2 && Quaternion.Angle(head.localRotation, Quaternion.identity) < .001f, "Cancel did not restore/release");
                d.actionGate = null; Check(!d.TryStart(), "Missing lock accepted outside solo preview");
                d.soloTesting = true;
                go.transform.rotation = Quaternion.Euler(0, 180, 0);
                Check(d.TryStart(), "Rotated rig did not start");
                d.Tick(.1f, 30);
                Check(Vector3.Dot(head.forward, Vector3.up) < 0, "Facing 180 degrees reversed head lowering");
                for (int i = 0; i < 40; i++) d.Tick(.1f, 30 + i * .1f);
                Check(Vector3.Dot(head.forward, Vector3.up) > .1f, "Post-drink head did not lift while facing 180 degrees");
                Check(d.IsSprayingWater && d.GetComponent<DrinkingWaterSpray>(), "Raised phase spray/component missing");
                d.Cancel();
                Check(d.TryStart(), "Repeat release test could not start");
                for (int i = 0; i < 5; i++) d.ObserveHands(true, 0, 0, .1f);
                for (int i = 0; i < 67; i++) d.Tick(.1f, 40+i*.1f);
                Check(!d.IsActive && !d.AwaitingNeutral && !d.IsSprayingWater,
                    "Release during sequence failed to rearm or spray remained active");
                int starts = d.SequenceStarts;
                for (int i = 0; i < 21; i++) d.ObserveHands(true, .3f, .3f, .1f);
                Check(d.IsActive && d.SequenceStarts == starts + 1, "Second deliberate raise did not start exactly once");
                d.Cancel();
                File.WriteAllText(Output + "/VALIDATION.txt", "PASS: isolated editor checks: busy/missing lock rejection; distance rejection; one-hand rejection; 2s hold; dropout pause/reset; single trigger; complete 6.5s sequence; root lock; rig restore; neutral rearm; manual start; cancel.\nNOT VALIDATED: live camera, three performers, shared lock integration, fruit/wood coexistence, Unity 6000.6.3f1.\n");
                Debug.Log("CHUNZI_DRINKING_MODULE_TESTS_OK");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
