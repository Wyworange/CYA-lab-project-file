using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DigiPhant.StudentWork.Editor
{
    public static class FruitInteractionVisualQA
    {
        public static void Capture()
        {
            EditorSceneManager.OpenScene(FruitInteractionSetup.ScenePath);
            var action = UnityEngine.Object.FindAnyObjectByType<FruitInteraction>();
            var controls = action.GetComponent<DigiPhantController>(); var locomotion = action.GetComponent<DigiPhantLocomotion>();
            controls.inputMode = InputMode.TestSliders; controls.CaptureRestPose(); action.MoveToStation();
            var original = Camera.main;
            float originalFieldOfView = original.fieldOfView;
            var camera = new GameObject("Temporary fruit visual QA camera").AddComponent<Camera>();
            camera.backgroundColor = new Color(.57f, .73f, .81f); camera.clearFlags = CameraClearFlags.Skybox;
            camera.orthographic = true; camera.orthographicSize = 4.3f; camera.nearClipPlane = .1f; camera.farClipPlane = 200;
            Vector3 target = locomotion.travelRoot.position + Vector3.up * 2.2f + Vector3.right;
            camera.transform.position = target + new Vector3(-2, 2.5f, -15); camera.transform.LookAt(target);
            string folder = "/private/tmp/yuwen-fruit-visual-qa-20261007"; Directory.CreateDirectory(folder);
            void Step(int frames)
            { for (int i = 0; i < frames; i++) { controls.ApplyControls(i / 60f, 1 / 60f); action.Tick(i / 60f, 1 / 60f); } }
            void Image(Camera from, string name)
            {
                var render = new RenderTexture(1440, 900, 24, RenderTextureFormat.ARGB32); render.Create();
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = render };
                RenderPipeline.SubmitRenderRequest(from, request);
                var previous = RenderTexture.active; RenderTexture.active = render;
                var texture = new Texture2D(1440, 900, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0); texture.Apply();
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), texture.EncodeToPNG());
                RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); render.Release(); UnityEngine.Object.DestroyImmediate(render);
            }
            Step(1); Image(camera, "00_warmup"); Image(camera, "01_idle_station");
            action.RequestRear(); Step(90);
            foreach (var bone in locomotion.elephantAnimator.GetComponentsInChildren<Transform>())
                if (bone.name == "elephant_l_Toe_bone" || bone.name == "elephant_l_Tibia_bone" || bone.name == "elephant_l_Femur_bone")
                    Debug.Log("FRUIT_REAR_BONE " + bone.name + " pos=" + bone.position + " target=" + action.LeftHindAnchor + " rot=" + bone.rotation.eulerAngles);
            Image(camera, "02_rear_side"); Image(original, "03_rear_original_camera");
            Debug.Log("FRUIT_CAMERA_FRAMING active=" + original.fieldOfView + " original=" + originalFieldOfView);
            if (!action.RequestPick(true)) throw new InvalidOperationException(action.Status);
            Step(85); Image(camera, "04_apple_attached"); Step(45); Image(camera, "05_apple_lifted"); Step(130);
            action.ResetAction(); action.RequestRear(); Step(90);
            if (!action.RequestPick(false)) throw new InvalidOperationException(action.Status);
            Step(85); Image(camera, "06_banana_attached"); Step(45); Image(camera, "07_banana_lifted");
            action.ResetAction(); Step(1); Image(original, "08_four_feet_camera_restored");
            if (Mathf.Abs(original.fieldOfView - originalFieldOfView) > .001f) throw new InvalidOperationException("Original camera framing was not restored.");
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
            Debug.Log("FRUIT_VISUAL_QA_CAPTURED " + folder);
        }
    }
}
