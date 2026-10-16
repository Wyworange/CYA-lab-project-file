using UnityEngine;

namespace DigiPhant.StudentWork.Drinking
{
    // Visual effect only. Read the existing sequence; never write rig or movement transforms.
    [DefaultExecutionOrder(1200)]
    public class DrinkingWaterSpray : MonoBehaviour
    {
        DrinkingInteraction action;
        Transform tip, previous;
        ParticleSystem spray;
        Material material;
        Texture2D droplet;
        bool emitting;
        public bool IsEmitting => spray && spray.isEmitting;

        void Start()
        {
            action = GetComponent<DrinkingInteraction>();
            if (!action || action.trunk == null || action.trunk.Length < 2) return;
            tip = action.trunk[action.trunk.Length - 1];
            previous = action.trunk[action.trunk.Length - 2];
            if (!tip || !previous) { Debug.LogWarning("Water spray: trunk tip bones missing.", this); return; }
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!shader) { Debug.LogWarning("Water spray: URP particle shader missing.", this); return; }
            var effect = new GameObject("Drinking water spray");
            effect.transform.SetParent(transform, false);
            spray = effect.AddComponent<ParticleSystem>();
            spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = spray.main;
            main.playOnAwake = false; main.loop = true; main.duration = 2;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.45f, .85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(.045f, .10f);
            main.startColor = new Color(.22f, .75f, 1f, .9f);
            main.gravityModifier = 1; main.maxParticles = 500;
            var emission = spray.emission; emission.enabled = true; emission.rateOverTime = 220;
            var shape = spray.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6; shape.radius = .035f;
            droplet = new Texture2D(32,32,TextureFormat.RGBA32,false);
            for (int y=0;y<32;y++) for(int x=0;x<32;x++)
            {
                float radius = new Vector2((x-15.5f)/15.5f,(y-15.5f)/15.5f).magnitude;
                droplet.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01((1-radius)*5)));
            }
            droplet.Apply();
            material = new Material(shader);
            material.SetFloat("_Surface",1); material.SetFloat("_Blend",0);
            material.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite",0); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetTexture("_BaseMap",droplet); material.SetTexture("_MainTex",droplet);
            material.renderQueue = 3000;
            var renderer = spray.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 2; renderer.velocityScale = .025f;
        }
        void LateUpdate()
        {
            if (!spray || !tip || !previous) return;
            Vector3 direction = (tip.position - previous.position).normalized;
            if (direction.sqrMagnitude < .1f) return;
            spray.transform.SetPositionAndRotation(tip.position + direction * .08f, Quaternion.LookRotation(direction));
            if (action.IsSprayingWater)
            {
                if (!emitting)
                {
                    spray.Play(); emitting = true;
                    Debug.Log("[Chunzi Water] START tip=" + tip.name + " at=" + spray.transform.position + " direction=" + direction, this);
                }
            }
            else if (emitting)
            {
                spray.Stop(true,ParticleSystemStopBehavior.StopEmitting); emitting = false;
                Debug.Log("[Chunzi Water] STOP particles=" + spray.particleCount, this);
            }
        }
        void OnDisable() { emitting = false; if (spray) spray.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); }
        void OnDestroy()
        {
            if (spray) Destroy(spray.gameObject);
            if (material) Destroy(material);
            if (droplet) Destroy(droplet);
        }
    }
}
