using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigiPhant.StudentWork.Editor
{
    public static class FruitInteractionSetup
    {
        public const string Folder = "Assets/StudentWork/FruitInteraction";
        public const string ScenePath = "Assets/DigiPhant/Scenes/DigiPhant.unity";
        static Material Material(string name, Color color)
        {
            string path = Folder + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .12f);
            AssetDatabase.CreateAsset(material, path); return material;
        }
        static Mesh AppleMesh()
        {
            const int sides = 10, rings = 8;
            var vertices = new Vector3[(rings + 1) * sides];
            var triangles = new int[rings * sides * 6]; int index = 0;
            for (int r = 0; r <= rings; r++)
            {
                float latitude = Mathf.PI * r / rings;
                for (int s = 0; s < sides; s++)
                {
                    float longitude = Mathf.PI * 2 * s / sides;
                    float radius = Mathf.Sin(latitude) * .3f * (1 + .08f * Mathf.Cos(5 * longitude));
                    vertices[r * sides + s] = new Vector3(radius * Mathf.Cos(longitude), Mathf.Cos(latitude) * .27f, radius * Mathf.Sin(longitude));
                }
            }
            for (int r = 0; r < rings; r++) for (int s = 0; s < sides; s++)
            {
                int a = r * sides + s, b = r * sides + (s + 1) % sides, c = a + sides, d = b + sides;
                triangles[index++] = a; triangles[index++] = b; triangles[index++] = c;
                triangles[index++] = b; triangles[index++] = d; triangles[index++] = c;
            }
            var mesh = new Mesh { name = "Low-poly apple", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        static Mesh BananaMesh()
        {
            const int rings = 9, sides = 6;
            var vertices = new Vector3[rings * sides + 2]; var triangles = new int[(rings - 1) * sides * 6 + sides * 6]; int index = 0;
            for (int r = 0; r < rings; r++)
            {
                float u = (float)r / (rings - 1), angle = Mathf.Lerp(-65, 65, u) * Mathf.Deg2Rad;
                var center = new Vector3(Mathf.Sin(angle) * .48f, Mathf.Cos(angle) * .48f - .35f, 0);
                var radial = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0);
                float thickness = .02f + .105f * Mathf.Sin(u * Mathf.PI);
                for (int s = 0; s < sides; s++)
                {
                    float spin = s * Mathf.PI * 2 / sides;
                    vertices[r * sides + s] = center + thickness * (Mathf.Cos(spin) * radial + Mathf.Sin(spin) * Vector3.forward);
                }
            }
            for (int r = 0; r < rings - 1; r++) for (int s = 0; s < sides; s++)
            {
                int a = r * sides + s, b = r * sides + (s + 1) % sides, c = a + sides, d = b + sides;
                triangles[index++] = a; triangles[index++] = b; triangles[index++] = c;
                triangles[index++] = b; triangles[index++] = d; triangles[index++] = c;
            }
            int first = rings * sides, last = first + 1;
            for (int s = 0; s < sides; s++) { vertices[first] += vertices[s] / sides; vertices[last] += vertices[(rings - 1) * sides + s] / sides; }
            for (int s = 0; s < sides; s++)
            {
                triangles[index++] = first; triangles[index++] = (s + 1) % sides; triangles[index++] = s;
                triangles[index++] = last; triangles[index++] = (rings - 1) * sides + s; triangles[index++] = (rings - 1) * sides + (s + 1) % sides;
            }
            var mesh = new Mesh { name = "Low-poly curved banana", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        static GameObject Fruit(string name, Mesh mesh, Material material, Material bark)
        {
            var fruit = new GameObject(name); var filter = fruit.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
            fruit.AddComponent<MeshRenderer>().sharedMaterial = material;
            var stem = GameObject.CreatePrimitive(PrimitiveType.Cube); stem.name = "Stem"; UnityEngine.Object.DestroyImmediate(stem.GetComponent<Collider>());
            stem.transform.SetParent(fruit.transform, false); stem.transform.localPosition = new Vector3(0, .31f, 0);
            if (name == "Banana") { stem.transform.localPosition = new Vector3(-.46f, -.18f, 0); stem.transform.localRotation = Quaternion.Euler(0, 0, -30); }
            stem.transform.localScale = new Vector3(.055f, .16f, .055f); stem.GetComponent<Renderer>().sharedMaterial = bark;
            var prefab = PrefabUtility.SaveAsPrefabAsset(fruit, Folder + "/Prefabs/" + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(fruit); return prefab;
        }
        public static void RefreshFruitMeshes()
        {
            var apple = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Meshes/Apple.asset");
            var banana = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Meshes/Banana.asset");
            if (!apple || !banana) throw new IOException("Create fruit assets before refreshing meshes.");
            var a = AppleMesh(); var b = BananaMesh(); EditorUtility.CopySerialized(a, apple); EditorUtility.CopySerialized(b, banana);
            UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b);
            EditorUtility.SetDirty(apple); EditorUtility.SetDirty(banana);
            var prefab = PrefabUtility.LoadPrefabContents(Folder + "/Prefabs/Banana.prefab");
            var stem = prefab.transform.Find("Stem"); stem.localPosition = new Vector3(-.46f, -.18f, 0); stem.localRotation = Quaternion.Euler(0, 0, -30);
            PrefabUtility.SaveAsPrefabAsset(prefab, Folder + "/Prefabs/Banana.prefab"); PrefabUtility.UnloadPrefabContents(prefab);
            if (File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.OpenScene(ScenePath);
                var action = UnityEngine.Object.FindAnyObjectByType<FruitInteraction>();
                action.fruitBranch.Find("Fruit-bearing branch above existing bush").localPosition = new Vector3(-1.5f, 4.55f, 0);
                action.fruitBranch.Find("Fruit-bearing branch above existing bush").localScale = new Vector3(4.5f, .13f, .13f);
                action.fruitBranch.Find("Branch support in existing bush").localPosition = new Vector3(.7f, 2.5f, 0);
                action.fruitBranch.Find("Branch support in existing bush").localScale = new Vector3(.14f, 4.1f, .14f);
                action.apple.localPosition = new Vector3(-3.1f, 4.15f, -.25f); action.banana.localPosition = new Vector3(-2.6f, 4.15f, .35f);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets(); Debug.Log("FRUIT_MESHES_REFRESHED");
        }
        [MenuItem("DigiPhant/Fruit/Integrate Actions Into DigiPhant Scene")]
        public static void IntegrateIntoOriginalScene()
        {
            foreach (var name in new[] { "Scenes", "Materials", "Meshes", "Prefabs" }) Directory.CreateDirectory(Folder + "/" + name);
            AssetDatabase.Refresh();
            var appleMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Meshes/Apple.asset");
            var bananaMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Meshes/Banana.asset");
            if (!appleMesh) { appleMesh = AppleMesh(); AssetDatabase.CreateAsset(appleMesh, Folder + "/Meshes/Apple.asset"); }
            if (!bananaMesh) { bananaMesh = BananaMesh(); AssetDatabase.CreateAsset(bananaMesh, Folder + "/Meshes/Banana.asset"); }
            var bark = Material("Fruit branch bark", new Color(.26f, .13f, .055f));
            var applePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Prefabs/Apple.prefab");
            var bananaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Prefabs/Banana.prefab");
            if (!applePrefab) applePrefab = Fruit("Apple", appleMesh, Material("Apple red", new Color(.82f, .055f, .035f)), bark);
            if (!bananaPrefab) bananaPrefab = Fruit("Banana", bananaMesh, Material("Banana yellow", new Color(.96f, .74f, .055f)), bark);
            var scene = EditorSceneManager.OpenScene("Assets/DigiPhant/Scenes/DigiPhant.unity", OpenSceneMode.Single);
            var course = scene.GetRootGameObjects().Single(g => g.name == "Savannah Course [layout 4]");
            var bush = course.transform.Find("Landmarks (4, decorative)/01 Feeding bush");
            if (!bush) throw new InvalidOperationException("Selected existing feeding bush missing.");
            var root = GameObject.Find("Student fruit station · existing feeding bush");
            if (!root)
            {
                root = new GameObject("Student fruit station · existing feeding bush");
                root.transform.position = bush.position;
                root.transform.rotation = course.transform.rotation;
            }
            var branch = root.transform.Find("Fruit-bearing branch above existing bush");
            if (!branch)
            {
                var item = GameObject.CreatePrimitive(PrimitiveType.Cube); item.name = "Fruit-bearing branch above existing bush";
                UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>()); item.transform.SetParent(root.transform, false);
                item.transform.localPosition = new Vector3(-1.5f, 4.55f, 0); item.transform.localScale = new Vector3(4.5f, .13f, .13f);
                item.GetComponent<Renderer>().sharedMaterial = bark; branch = item.transform;
            }
            if (!root.transform.Find("Branch support in existing bush"))
            {
                var support = GameObject.CreatePrimitive(PrimitiveType.Cube); support.name = "Branch support in existing bush";
                UnityEngine.Object.DestroyImmediate(support.GetComponent<Collider>()); support.transform.SetParent(root.transform, false);
                support.transform.localPosition = new Vector3(.7f, 2.5f, 0); support.transform.localScale = new Vector3(.14f, 4.1f, .14f); support.GetComponent<Renderer>().sharedMaterial = bark;
            }
            var apple = root.transform.Find("Apple")?.gameObject;
            if (!apple) { apple = (GameObject)PrefabUtility.InstantiatePrefab(applePrefab, scene); apple.transform.SetParent(root.transform, false); }
            apple.transform.localPosition = new Vector3(-3.1f, 4.15f, -.25f);
            var banana = root.transform.Find("Banana")?.gameObject;
            if (!banana) { banana = (GameObject)PrefabUtility.InstantiatePrefab(bananaPrefab, scene); banana.transform.SetParent(root.transform, false); }
            banana.transform.localPosition = new Vector3(-2.6f, 4.15f, .35f);
            var controls = UnityEngine.Object.FindAnyObjectByType<DigiPhantController>();
            if (!controls) throw new InvalidOperationException("DigiPhant controls missing from the original scene.");
            if (!controls.GetComponent<DigiPhantLocomotion>()) throw new InvalidOperationException("Existing locomotion is required before fruit interaction.");
            var interaction = controls.GetComponent<FruitInteraction>();
            if (!interaction) interaction = controls.gameObject.AddComponent<FruitInteraction>();
            interaction.apple = apple.transform; interaction.banana = banana.transform; interaction.fruitBranch = root.transform;
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Unable to save fruit integration to the original scene.");
            AssetDatabase.SaveAssets();
            Debug.Log("FRUIT_ACTIONS_INTEGRATED " + ScenePath + " | original control settings preserved");
        }

        public static void IntegrateAndValidate()
        {
            IntegrateIntoOriginalScene();
            DigiPhant.Editor.DigiPhantLocomotionValidation.Run();
            FruitInteractionValidation.RunAll();
        }
    }
}
