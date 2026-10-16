using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigiPhant.Editor
{
    // Environment (Yuwen, 2026-10-09): show the AllSky "Above Day A" sky in the main scene. The camera
    // used a solid dark-blue background, so the skybox never showed. Ambient light stays as it was
    // (flat colour), so object lighting does not change. Runs once per machine; menus redo/undo it.
    [InitializeOnLoad]
    static class SkyboxSetup
    {
        const string ScenePath = "Assets/DigiPhant/Scenes/DigiPhant.unity";
        const string SkyboxPath = "Assets/Allsky/Above/Above Day A/Above Day A 6sided.mat";
        const string DoneKey = "DigiPhant.Skybox.AboveDayA.v1";
        static readonly Color SolidBackground = new Color(.055f, .085f, .11f);

        static SkyboxSetup()
        {
            if (Application.isBatchMode || EditorPrefs.GetBool(DoneKey, false)) return;
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorPrefs.GetBool(DoneKey, false)) Apply(true);
            };
        }

        [MenuItem("DigiPhant/Environment/Use Above Day A Skybox")]
        static void ApplyMenu() => Apply(true);

        [MenuItem("DigiPhant/Environment/Use Original Solid Background")]
        static void RevertMenu() => Apply(false);

        static void Apply(bool sky)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Skybox setup: stop Play first."); return; }
            EditorPrefs.SetBool(DoneKey, true);
            var material = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
            if (sky && !material) { Debug.LogError("Skybox setup: missing " + SkyboxPath + ". Import AllSky 'Above Day A' first, then run DigiPhant/Environment/Use Above Day A Skybox."); return; }
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { EditorPrefs.DeleteKey(DoneKey); return; }
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            var locomotion = Object.FindAnyObjectByType<DigiPhantLocomotion>();
            var camera = locomotion && locomotion.followCamera ? locomotion.followCamera : Camera.main;
            if (!camera) { Debug.LogError("Skybox setup: no camera found in " + ScenePath); return; }
            Undo.RecordObject(camera, "Skybox");
            camera.clearFlags = sky ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            if (!sky) camera.backgroundColor = SolidBackground;
            // Sky only as background and reflections; keep the flat ambient colour.
            RenderSettings.skybox = sky ? material : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            DynamicGI.UpdateEnvironment();
            EditorUtility.SetDirty(camera);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("DIGIPHANT_SKYBOX " + (sky ? "Above Day A" : "solid background") + " applied to " + ScenePath);
        }
    }
}
