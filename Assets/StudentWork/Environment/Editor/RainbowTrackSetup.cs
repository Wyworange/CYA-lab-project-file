using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigiPhant.Editor
{
    // Environment (Yuwen, 2026-10-13): rainbow track and invisible grey ground. Looks only:
    //  - "Open path" gets its own material and a copy of its mesh with UVs (the generated mesh has none).
    //    The shared "Pale sand path" material is left alone because the Timber cut ends use it too.
    //  - "Ground" and "Stage" stop rendering; their transforms and the Stage MeshCollider (timber physics) stay.
    // Backup: Backups/2026-10-13_before_rainbow_track/. Runs once per machine; the menus redo or undo it.
    [InitializeOnLoad]
    static class RainbowTrackSetup
    {
        const string ScenePath = "Assets/DigiPhant/Scenes/DigiPhant.unity";
        const string Folder = "Assets/StudentWork/Environment/Rainbow";
        const string TexturePath = Folder + "/RainbowTrack.png";
        const string MaterialPath = Folder + "/Rainbow Track.mat";
        const string MeshPath = Folder + "/Open path rainbow.asset";
        const string OriginalMesh = "Assets/SavannahCourse/Generated/v4/Open path.asset";
        const string OriginalMaterial = "Assets/SavannahCourse/Generated/v4/Pale sand path.mat";
        const string DoneKey = "DigiPhant.RainbowTrack.v1";

        static RainbowTrackSetup()
        {
            if (Application.isBatchMode || EditorPrefs.GetBool(DoneKey, false)) return;
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorPrefs.GetBool(DoneKey, false)) Apply(true);
            };
        }

        [MenuItem("DigiPhant/Environment/Use Rainbow Track (hide ground)")]
        static void RainbowMenu() => Apply(true);

        [MenuItem("DigiPhant/Environment/Use Sand Track (show ground)")]
        static void OriginalMenu() => Apply(false);

        static void Apply(bool rainbow)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Rainbow track: stop Play first."); return; }
            EditorPrefs.SetBool(DoneKey, true);
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { EditorPrefs.DeleteKey(DoneKey); return; }
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            var path = Find(scene, "Open path");
            var ground = Find(scene, "Ground");
            var stage = Find(scene, "Stage");
            if (!path || !ground || !stage) { Debug.LogError("Rainbow track: 'Open path', 'Ground' or 'Stage' not found in " + ScenePath); return; }
            var filter = path.GetComponent<MeshFilter>();
            Mesh mesh; Material material;
            if (rainbow)
            {
                mesh = RainbowMesh(AssetDatabase.LoadAssetAtPath<Mesh>(OriginalMesh) ?? filter.sharedMesh);
                material = RainbowMaterial();
            }
            else
            {
                mesh = AssetDatabase.LoadAssetAtPath<Mesh>(OriginalMesh);
                material = AssetDatabase.LoadAssetAtPath<Material>(OriginalMaterial);
            }
            if (!mesh || !material) { Debug.LogError("Rainbow track: assets missing (see Backups/2026-10-13_before_rainbow_track)."); return; }
            Undo.RecordObjects(new Object[] { filter, path, ground, stage }, "Rainbow track");
            filter.sharedMesh = mesh;
            path.sharedMaterial = material;
            ground.enabled = stage.enabled = !rainbow;
            foreach (var o in new Object[] { filter, path, ground, stage }) EditorUtility.SetDirty(o);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("DIGIPHANT_TRACK " + (rainbow ? "rainbow track, ground hidden (Stage collider kept)" : "sand track, ground shown") + " in " + ScenePath);
        }

        static MeshRenderer Find(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.name == name) return renderer;
            return null;
        }

        // Copy of the road mesh with UVs: u = distance along the route (0..1), v = across the width (0..1).
        // The road is built as vertex pairs (left, right) per route point.
        static Mesh RainbowMesh(Mesh source)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (mesh) return mesh;
            Directory.CreateDirectory(Folder);
            mesh = Object.Instantiate(source);
            mesh.name = "Open path rainbow";
            var vertices = mesh.vertices;
            int points = vertices.Length / 2;
            var along = new float[points];
            for (int i = 1; i < points; i++)
                along[i] = along[i - 1] + Vector3.Distance((vertices[2 * i] + vertices[2 * i + 1]) * .5f, (vertices[2 * i - 2] + vertices[2 * i - 1]) * .5f);
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < points; i++)
            {
                float u = along[points - 1] > 0 ? along[i] / along[points - 1] : 0;
                uv[2 * i] = new Vector2(u, 0);
                uv[2 * i + 1] = new Vector2(u, 1);
            }
            mesh.uv = uv;
            AssetDatabase.CreateAsset(mesh, MeshPath);
            return mesh;
        }

        static Material RainbowMaterial()
        {
            Directory.CreateDirectory(Folder);
            if (!File.Exists(TexturePath))
            {
                // Seven-colour bands across the track width (red to violet), soft edges.
                const int height = 256;
                var texture = new Texture2D(4, height, TextureFormat.RGBA32, false);
                for (int y = 0; y < height; y++)
                {
                    var colour = Color.HSVToRGB(y / (float)(height - 1) * .78f, .72f, 1f);
                    for (int x = 0; x < 4; x++) texture.SetPixel(x, y, colour);
                }
                File.WriteAllBytes(TexturePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(TexturePath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Rainbow Track" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", .25f);
            material.SetFloat("_Metallic", 0);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }
    }
}
