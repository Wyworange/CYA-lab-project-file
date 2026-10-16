using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigiPhant.Editor
{
    // Environment (Yuwen, 2026-10-13): lunar-surface look for the land. Only the materials of "Ground"
    // and "Stage" change; transforms, meshes, the Stage MeshCollider (timber physics) and names stay,
    // so no interaction is affected. Textures come from tools/environment/make_moon_texture.py.
    // Backup of the scene and original materials: Backups/2026-10-13_before_moon_ground/.
    // Runs once per machine after compile; the menus redo or undo it.
    [InitializeOnLoad]
    static class MoonGroundSetup
    {
        const string ScenePath = "Assets/DigiPhant/Scenes/DigiPhant.unity";
        const string Folder = "Assets/StudentWork/Environment/Moon";
        const string AlbedoPath = Folder + "/MoonRegolith_Albedo.png";
        const string NormalPath = Folder + "/MoonRegolith_Normal.png";
        const string OriginalGround = "Assets/SavannahCourse/Generated/v4/Ochre ground.mat";
        const string OriginalStage = "Assets/DigiPhant/Materials/Stage.mat";
        const string DoneKey = "DigiPhant.MoonGround.v1";
        // Metres covered by one repeat of the texture.
        const float TileMetres = 4;

        static MoonGroundSetup()
        {
            if (Application.isBatchMode || EditorPrefs.GetBool(DoneKey, false)) return;
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorPrefs.GetBool(DoneKey, false)) Apply(true);
            };
        }

        [MenuItem("DigiPhant/Environment/Use Moon Ground")]
        static void MoonMenu() => Apply(true);

        [MenuItem("DigiPhant/Environment/Use Original Ground")]
        static void OriginalMenu() => Apply(false);

        static void Apply(bool moon)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Moon ground: stop Play first."); return; }
            EditorPrefs.SetBool(DoneKey, true);
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { EditorPrefs.DeleteKey(DoneKey); return; }
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            var ground = Find(scene, "Ground");
            var stage = Find(scene, "Stage");
            if (!ground || !stage) { Debug.LogError("Moon ground: 'Ground' or 'Stage' not found in " + ScenePath); return; }
            Material groundMaterial, stageMaterial;
            if (moon)
            {
                if (!PrepareTextures(out var albedo, out var normal)) return;
                groundMaterial = MoonMaterial("Moon Regolith Ground", albedo, normal, ground, .82f);
                stageMaterial = MoonMaterial("Moon Regolith Stage", albedo, normal, stage, 1f);
            }
            else
            {
                groundMaterial = AssetDatabase.LoadAssetAtPath<Material>(OriginalGround);
                stageMaterial = AssetDatabase.LoadAssetAtPath<Material>(OriginalStage);
                if (!groundMaterial || !stageMaterial) { Debug.LogError("Moon ground: original materials missing (see Backups/2026-10-13_before_moon_ground)."); return; }
            }
            Undo.RecordObjects(new Object[] { ground, stage }, "Ground material");
            ground.sharedMaterial = groundMaterial;
            stage.sharedMaterial = stageMaterial;
            EditorUtility.SetDirty(ground); EditorUtility.SetDirty(stage);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("DIGIPHANT_GROUND " + (moon ? "moon regolith" : "original savanna") + " applied to " + ScenePath);
        }

        static MeshRenderer Find(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.name == name) return renderer;
            return null;
        }

        static bool PrepareTextures(out Texture2D albedo, out Texture2D normal)
        {
            albedo = normal = null;
            var albedoImporter = AssetImporter.GetAtPath(AlbedoPath) as TextureImporter;
            var normalImporter = AssetImporter.GetAtPath(NormalPath) as TextureImporter;
            if (!albedoImporter || !normalImporter)
            { Debug.LogError("Moon ground: textures missing. Run tools/environment/make_moon_texture.py."); return false; }
            bool changed = false;
            if (albedoImporter.wrapMode != TextureWrapMode.Repeat || !albedoImporter.sRGBTexture)
            { albedoImporter.wrapMode = TextureWrapMode.Repeat; albedoImporter.sRGBTexture = true; changed = true; albedoImporter.SaveAndReimport(); }
            if (normalImporter.textureType != TextureImporterType.NormalMap || normalImporter.wrapMode != TextureWrapMode.Repeat)
            { normalImporter.textureType = TextureImporterType.NormalMap; normalImporter.wrapMode = TextureWrapMode.Repeat; changed = true; normalImporter.SaveAndReimport(); }
            if (changed) AssetDatabase.Refresh();
            albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
            normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
            return albedo && normal;
        }

        static Material MoonMaterial(string name, Texture2D albedo, Texture2D normal, Renderer target, float brightness)
        {
            string path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", new Color(brightness, brightness, brightness, 1));
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_Smoothness", .08f);
            material.SetFloat("_Metallic", 0);
            // Tile by the object's real size, so both surfaces show the same crater scale.
            var size = target.bounds.size;
            material.SetTextureScale("_BaseMap", new Vector2(Mathf.Max(1, size.x / TileMetres), Mathf.Max(1, size.z / TileMetres)));
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }
    }
}
