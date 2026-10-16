using UnityEngine;

namespace DigiPhant.StudentWork
{
    // Slowly turns the skybox in Play so the clouds drift. Rotates a runtime copy of the material,
    // so the AllSky asset itself never changes; the original is put back when Play stops.
    // Added automatically to any scene whose skybox has a _Rotation property.
    public class SkyboxRotation : MonoBehaviour
    {
        [Tooltip("Degrees per second around the vertical axis. 0.8 = one full turn in 7.5 minutes.")]
        public float degreesPerSecond = .8f;

        static readonly int RotationId = Shader.PropertyToID("_Rotation");
        Material original, rotating;
        float start;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddToScene()
        {
            var sky = RenderSettings.skybox;
            if (!sky || !sky.HasProperty(RotationId) || FindAnyObjectByType<SkyboxRotation>()) return;
            new GameObject("Skybox Rotation (runtime)").AddComponent<SkyboxRotation>();
        }

        void OnEnable()
        {
            original = RenderSettings.skybox;
            if (!original || !original.HasProperty(RotationId)) { enabled = false; return; }
            rotating = new Material(original) { name = original.name + " (rotating)" };
            start = original.GetFloat(RotationId);
            RenderSettings.skybox = rotating;
        }

        void Update()
        {
            rotating.SetFloat(RotationId, Mathf.Repeat(start + Time.time * degreesPerSecond, 360));
        }

        void OnDisable()
        {
            if (original) RenderSettings.skybox = original;
            if (rotating) Destroy(rotating);
            rotating = null;
        }
    }
}
