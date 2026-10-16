using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DigiPhant.Editor
{
    // Environment (Yuwen, 2026-10-13): glowing neon look for the 18 acacia trees and the feeding bush, plus Bloom
    // and a dimmed copy of the day sky so the trees stand out.
    // Looks only: the renderers under those objects get new materials (Shaders/NeonTree.shader). Meshes,
    // transforms and hierarchy stay, so the sit-on-log room check and the fruit station see the same bounds.
    // The original Bark / foliage materials are shared with the Timber and the finish posts, so they are not edited.
    // Backup: Backups/2026-10-13_before_neon_trees/. Runs once per machine; the menus redo or undo it.
    [InitializeOnLoad]
    static class NeonTreesSetup
    {
        const string ScenePath = "Assets/DigiPhant/Scenes/DigiPhant.unity";
        const string Folder = "Assets/StudentWork/Environment/Neon";
        const string ShaderName = "DigiPhant/Neon Tree";
        const string Generated = "Assets/SavannahCourse/Generated/v4/";
        const string VolumeName = "Neon glow (Bloom)";
        const string DaySky = "Assets/Allsky/Above/Above Day A/Above Day A 6sided.mat";
        const string DoneKey = "DigiPhant.NeonTrees.v3";  // v3: day sky back; dark sky only from its menu
        static readonly string[] Owners = { "Acacia trees (18)", "01 Feeding bush" };

        static NeonTreesSetup()
        {
            if (Application.isBatchMode || EditorPrefs.GetBool(DoneKey, false)) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorPrefs.GetBool(DoneKey, false)) return;
                if (SceneManager.GetActiveScene().path == ScenePath && GameObject.Find(VolumeName)
                    && !(RenderSettings.skybox && RenderSettings.skybox.name == "Neon Dim Sky")) { EditorPrefs.SetBool(DoneKey, true); return; }
                Apply(true);
            };
        }

        [MenuItem("DigiPhant/Environment/Use Neon Trees (with glow)")]
        static void NeonMenu() => Apply(true);

        [MenuItem("DigiPhant/Environment/Use Original Trees")]
        static void OriginalMenu() => Apply(false);

        static void Apply(bool neon)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Neon trees: stop Play first."); return; }
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            var bark = AssetDatabase.LoadAssetAtPath<Material>(Generated + "Bark.mat");
            var olive = AssetDatabase.LoadAssetAtPath<Material>(Generated + "Olive foliage.mat");
            var sage = AssetDatabase.LoadAssetAtPath<Material>(Generated + "Sage foliage.mat");
            if (!bark || !olive || !sage) { Debug.LogError("Neon trees: original tree materials missing in " + Generated); return; }
            var shader = Shader.Find(ShaderName);
            if (neon && !shader) { Debug.LogError("Neon trees: shader '" + ShaderName + "' not found."); return; }

            // Original <-> neon pairs; the undo menu swaps them back the other way.
            var pairs = new (Material original, Material neon)[]
            {
                (bark, neon ? NeonMaterial("Neon Bark", shader, 0, new Color(.05f, .10f, .38f), .3f) : Load("Neon Bark")),
                (olive, neon ? NeonMaterial("Neon Foliage Deep", shader, 1, new Color(.04f, .08f, .32f), .28f) : Load("Neon Foliage Deep")),
                (sage, neon ? NeonMaterial("Neon Foliage Light", shader, 1, new Color(.07f, .17f, .48f), .36f) : Load("Neon Foliage Light")),
            };
            var swap = new Dictionary<Material, Material>();
            foreach (var (original, neonMaterial) in pairs)
                if (original && neonMaterial) swap[neon ? original : neonMaterial] = neon ? neonMaterial : original;

            int changed = 0;
            foreach (var renderer in OwnedRenderers(scene))
            {
                var materials = renderer.sharedMaterials;
                bool any = false;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] && swap.TryGetValue(materials[i], out var replacement)) { materials[i] = replacement; any = true; }
                if (!any) continue;
                Undo.RecordObject(renderer, "Neon trees");
                renderer.sharedMaterials = materials;
                EditorUtility.SetDirty(renderer);
                changed++;
            }
            SetGlow(scene, neon);
            SetSky(false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorPrefs.SetBool(DoneKey, true);
            Debug.Log("DIGIPHANT_TREES " + (neon ? "neon trees + bloom, day sky" : "original trees, no bloom, day sky") + ": " + changed + " renderers in " + ScenePath);
        }

        static IEnumerable<MeshRenderer> OwnedRenderers(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (System.Array.IndexOf(Owners, t.name) >= 0)
                        foreach (var renderer in t.GetComponentsInChildren<MeshRenderer>(true))
                            yield return renderer;
        }

        static Material Load(string name) => AssetDatabase.LoadAssetAtPath<Material>(Folder + "/" + name + ".mat");

        static Material NeonMaterial(string name, Shader shader, int mode, Color baseColour, float sparkles)
        {
            Directory.CreateDirectory(Folder);
            var material = Load(name);
            if (!material)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, Folder + "/" + name + ".mat");
            }
            material.shader = shader;
            material.SetFloat("_Mode", mode);
            material.SetColor("_BaseColor", baseColour);
            material.SetFloat("_SparkleAmount", sparkles);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        // Global Bloom volume plus post-processing on the main camera. The URP asset's own volume profile
        // (SampleSceneProfile) adds Neutral tonemapping and a vignette, so this volume switches both off.
        static void SetGlow(Scene scene, bool on)
        {
            GameObject volumeObject = null;
            foreach (var root in scene.GetRootGameObjects()) if (root.name == VolumeName) volumeObject = root;
            if (on && !volumeObject)
            {
                volumeObject = new GameObject(VolumeName);
                SceneManager.MoveGameObjectToScene(volumeObject, scene);
                Undo.RegisterCreatedObjectUndo(volumeObject, "Neon glow");
                var volume = volumeObject.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = BloomProfile();
            }
            else if (!on && volumeObject) Undo.DestroyObjectImmediate(volumeObject);

            Camera camera = null;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<Camera>(true))
                    if (c.CompareTag("MainCamera")) camera = c;
            if (!camera) { Debug.LogWarning("Neon trees: no MainCamera found; Bloom will not show."); return; }
            if (!camera.TryGetComponent(out UniversalAdditionalCameraData data))
                data = Undo.AddComponent<UniversalAdditionalCameraData>(camera.gameObject);
            Undo.RecordObject(data, "Neon glow");
            data.renderPostProcessing = on;
            EditorUtility.SetDirty(data);
        }

        [MenuItem("DigiPhant/Environment/Use Dark Sky")]
        static void DarkSkyMenu() => SkyMenu(true);

        [MenuItem("DigiPhant/Environment/Use Day Sky")]
        static void DaySkyMenu() => SkyMenu(false);

        static void SkyMenu(bool dark)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Sky: stop Play first."); return; }
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) { Debug.LogWarning("Sky: open " + ScenePath + " first."); return; }
            SetSky(dark);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("DIGIPHANT_SKY " + (dark ? "dark" : "day"));
        }

        static VolumeProfile BloomProfile()
        {
            string path = Folder + "/Neon Bloom.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (!profile)
            {
                Directory.CreateDirectory(Folder);
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            // Only bright HDR values bloom: sparkles, rims and glowing bark, not the sunlit scene (sun intensity 2).
            var bloom = Component<Bloom>(profile);
            bloom.threshold.Override(1.6f);
            bloom.intensity.Override(1.4f);
            bloom.scatter.Override(.65f);
            Component<Tonemapping>(profile).mode.Override(TonemappingMode.None);
            Component<Vignette>(profile).intensity.Override(0f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static T Component<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T component)) return component;
            component = profile.Add<T>(true);
            component.name = typeof(T).Name;
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        // Dark, night-blue copy of the day sky; the AllSky asset itself is not edited. Ambient light is a
        // flat colour in this scene, so object lighting does not change. SkyboxRotation still turns it in Play.
        static void SetSky(bool dim)
        {
            var day = AssetDatabase.LoadAssetAtPath<Material>(DaySky);
            if (!day) { Debug.LogWarning("Neon trees: day sky material missing; sky left as is."); return; }
            string path = Folder + "/Neon Dim Sky.mat";
            if (dim)
            {
                var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!sky)
                {
                    sky = new Material(day) { name = "Neon Dim Sky" };
                    AssetDatabase.CreateAsset(sky, path);
                }
                sky.SetFloat("_Exposure", .12f);
                sky.SetColor("_Tint", new Color(.28f, .31f, .50f, .5f));
                EditorUtility.SetDirty(sky);
                AssetDatabase.SaveAssets();
                RenderSettings.skybox = sky;
            }
            else if (RenderSettings.skybox && RenderSettings.skybox.name == "Neon Dim Sky") RenderSettings.skybox = day;
        }
    }
}
