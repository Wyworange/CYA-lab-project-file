using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DigiPhant.StudentWork.Editor
{
    public static class FruitInteractionValidation
    {
        [Serializable] sealed class ControlMappingSnapshot { public BoneControl[] controls; }
        static string MappingFingerprint(BoneControl[] controls) => JsonUtility.ToJson(new ControlMappingSnapshot { controls = controls });
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Fruit validation: " + message); }
        static string Packet(int sequence, int id, string action, bool tracked = true, bool mouth = true, float left = 0, float right = 0)
            => JsonUtility.ToJson(new FaceActionFrame { version = 1, kind = "fruitActions", sequence = sequence, actionId = id, action = action, tracked = tracked, calibrated = true, mouthOpen = mouth, jawOpen = mouth ? .8f : .1f, eyeBlinkLeft = left, eyeBlinkRight = right });
        static Dictionary<SkinnedMeshRenderer, Dictionary<int, Vector3>> SoleSamples(Animator animator)
        {
            var result = new Dictionary<SkinnedMeshRenderer, Dictionary<int, Vector3>>();
            foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var baked = new Mesh(); skin.BakeMesh(baked); var vertices = baked.vertices; var weights = skin.sharedMesh.boneWeights;
                var samples = new Dictionary<int, Vector3>();
                foreach (string side in new[] { "l", "r" })
                {
                    var indices = new HashSet<int>(Enumerable.Range(0, skin.bones.Length).Where(i => skin.bones[i].name == "elephant_" + side + "_Tibia_bone" || skin.bones[i].name == "elephant_" + side + "_Toe_bone"));
                    var candidates = new Dictionary<int, Vector3>();
                    for (int i = 0; i < weights.Length; i++)
                    {
                        var w = weights[i]; float sum = (indices.Contains(w.boneIndex0) ? w.weight0 : 0) + (indices.Contains(w.boneIndex1) ? w.weight1 : 0) + (indices.Contains(w.boneIndex2) ? w.weight2 : 0) + (indices.Contains(w.boneIndex3) ? w.weight3 : 0);
                        if (sum > .85f) candidates[i] = skin.transform.TransformPoint(vertices[i]);
                    }
                    if (candidates.Count == 0) continue;
                    float bottom = candidates.Values.Min(v => v.y);
                    foreach (var item in candidates.Where(v => v.Value.y <= bottom + .06f)) samples[item.Key] = item.Value;
                }
                UnityEngine.Object.DestroyImmediate(baked); if (samples.Count > 0) result[skin] = samples;
            }
            Require(result.Values.Sum(s => s.Count) >= 8, "Could not identify actual hind-sole skin vertices"); return result;
        }
        static void CheckSoles(Dictionary<SkinnedMeshRenderer, Dictionary<int, Vector3>> samples)
        {
            foreach (var skin in samples)
            {
                var mesh = new Mesh(); skin.Key.BakeMesh(mesh); var vertices = mesh.vertices;
                foreach (var vertex in skin.Value) Require(Vector3.Distance(skin.Key.transform.TransformPoint(vertices[vertex.Key]), vertex.Value) < .025f, "Rendered hind sole lifted or slipped");
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
        public static void RunAll()
        {
            var scene = EditorSceneManager.OpenScene(FruitInteractionSetup.ScenePath);
            var action = UnityEngine.Object.FindAnyObjectByType<FruitInteraction>();
            var controls = action.GetComponent<DigiPhantController>(); var locomotion = action.GetComponent<DigiPhantLocomotion>();
            Require(action.Initialise(), "Rig references missing");
            float configuredCameraFieldOfView = locomotion.followCamera.fieldOfView;
            int performerCount = controls.performerCount;
            bool upperBodyOnly = controls.upperBodyOnly;
            InputMode inputMode = controls.inputMode;
            string controlMapping = MappingFingerprint(controls.controls);
            Require(controlMapping.Contains("\"performer\":") && controlMapping.Contains("\"bones\":") && controlMapping.Contains("\"degrees\":"),
                "Control mapping fingerprint did not serialize bone configuration");
            string forwardMapping = JsonUtility.ToJson(locomotion.forward);
            string steeringMapping = JsonUtility.ToJson(locomotion.steering);
            var preview = controls.GetComponent<DigiPhantCameraPreview>();
            bool removePreviewAfterTest = preview == null;
            if (!preview) preview = controls.gameObject.AddComponent<DigiPhantCameraPreview>();
            bool originalShowPreview = preview.showPreview, originalAutoStart = preview.autoStartBridge;
            bool originalControlsPreview = controls.showCameraPreview;
            preview.autoStartBridge = false; preview.showPreview = false; controls.showCameraPreview = false;
            var neutral = new PoseFrame { version = 1, performerCount = performerCount, upperBodyOnly = upperBodyOnly,
                people = Enumerable.Range(1, performerCount).Select(i => new PerformerFrame { slot = i, values = new float[6], confidence = Enumerable.Repeat(1f, 6).ToArray() }).ToArray() };
            Require(controls.AcceptPacket(JsonUtility.ToJson(neutral), 0) && controls.CalibrateAt(0), "Body calibration fixture failed");
            foreach (var mode in new[] { InputMode.TestSliders, InputMode.Camera })
            {
                controls.inputMode = mode;
                foreach (bool enabled in new[] { true, false })
                {
                    action.SetFaceMode(enabled);
                    Require(controls.performerCount == performerCount && controls.upperBodyOnly == upperBodyOnly && controls.inputMode == mode && controls.IsCalibrated &&
                        MappingFingerprint(controls.controls) == controlMapping && JsonUtility.ToJson(locomotion.forward) == forwardMapping && JsonUtility.ToJson(locomotion.steering) == steeringMapping,
                        "Face toggle changed mode, body calibration, group or mappings");
                }
            }
            controls.inputMode = inputMode;
            Require(!controls.showCameraPreview && !preview.showPreview && !preview.autoStartBridge,
                "Face-mode exit failed to restore preview settings");
            controls.showCameraPreview = originalControlsPreview;
            preview.showPreview = originalShowPreview; preview.autoStartBridge = originalAutoStart;
            if (removePreviewAfterTest) UnityEngine.Object.DestroyImmediate(preview);
            controls.inputMode = InputMode.TestSliders; controls.CaptureRestPose(); locomotion.testForward = 0;
            foreach (string name in new[] { "Apple", "Banana" })
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(FruitInteractionSetup.Folder + "/Meshes/" + name + ".asset");
                float volume = 0; var v = mesh.vertices; var triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3) volume += Vector3.Dot(v[triangles[i]], Vector3.Cross(v[triangles[i + 1]], v[triangles[i + 2]])) / 6;
                Require(volume > .001f, name + " mesh is inward-facing or open");
            }
            controls.ApplyControls(0, 1 / 60f);
            action.MoveToStation(); controls.ApplyControls(0, 1 / 60f);
            Require(action.StationReachMargin >= 0, "Head-on station leaves fruit beyond trunk reach by " + (-action.StationReachMargin) + ": " + action.Status);
            var soleSamples = SoleSamples(locomotion.elephantAnimator);
            Debug.Log("FRUIT_RIG_DIMENSIONS base=" + action.TrunkTip.position + " apple=" + action.apple.position + " banana=" + action.banana.position);
            Vector3 origin = locomotion.travelRoot.position;
            float originalFieldOfView = locomotion.followCamera.fieldOfView;
            void Step(int frames, float start = 0)
            {
                for (int i = 0; i < frames; i++) { float now = start + i / 60f; controls.ApplyControls(now, 1 / 60f); action.Tick(now, 1 / 60f); }
            }
            action.RequestRear(); locomotion.testForward = 1;
            Require(locomotion.followCamera.fieldOfView >= 50, "Rear camera did not temporarily widen framing");
            Require(action.RequestPick(true), "Wink during rise did not queue");
            Step(90);
            Require(action.RearBlend > .99f && action.Picking, "Queued pickup failed once upright: " + action.Status + " | tip=" + action.TrunkTip.position + " fruit=" + action.apple.position);
            var toes = locomotion.elephantAnimator.GetComponentsInChildren<Transform>().Where(t => t.name == "elephant_l_Toe_bone" || t.name == "elephant_r_Toe_bone").OrderBy(t => t.name).ToArray();
            Require(Vector3.Distance(toes[0].position, action.LeftHindAnchor) < .002f && Vector3.Distance(toes[1].position, action.RightHindAnchor) < .002f, "Hindfeet slipped during rear");
            Require(Vector3.Distance(origin, locomotion.travelRoot.position) < .002f, "Locomotion moved while action owned rig");
            CheckSoles(soleSamples);
            float originalFruitHeight = action.apple.position.y;
            Step(80, 1.5f); Require(action.apple.parent == action.TrunkTip, "Trunk did not attach apple after reaching it");
            Step(45, 2.84f); Require(action.apple.position.y > originalFruitHeight + .15f, "Attached apple did not lift");
            Step(135, 3.6f);
            Require(!action.Picking && !action.apple.gameObject.activeSelf, "Apple reach/attach/lift/return failed: " + action.Status);
            Require(action.banana.gameObject.activeSelf, "Apple selection also picked banana");
            var headControl = controls.controls.Single(c => c.label == "Head turn");
            var trunkControl = controls.controls.Single(c => c.label == "Trunk curl");
            headControl.smoothing = trunkControl.smoothing = 0;
            headControl.testValue = trunkControl.testValue = 0;
            Step(1, 5.8f);
            Quaternion neutralHead = headControl.bones[0].localRotation;
            Quaternion neutralTrunk = trunkControl.bones[0].localRotation;
            Vector3 standingPosition = locomotion.travelRoot.position;
            Quaternion standingRotation = locomotion.travelRoot.rotation;
            locomotion.testForward = .4f; locomotion.testSteering = .2f;
            headControl.testValue = trunkControl.testValue = 1;
            Step(30, 5.82f);
            Require(action.RearBlend > .99f && !action.Picking && !locomotion.actionPoseOwned && locomotion.actionMovementPaused, "Standing hold did not separate body gestures from movement lock");
            Require(Vector3.Distance(standingPosition, locomotion.travelRoot.position) < .002f && Quaternion.Angle(standingRotation, locomotion.travelRoot.rotation) < .01f, "Standing hold allowed walking or steering");
            Require(Quaternion.Angle(neutralHead, headControl.bones[0].localRotation) > 2 &&
                Quaternion.Angle(neutralTrunk, trunkControl.bones[0].localRotation) > 2, "Body gestures stayed paused during standing hold");
            Require(Vector3.Distance(action.LeftHindAnchor, action.GetComponent<DigiPhantLocomotion>().elephantAnimator.transform
                .GetComponentsInChildren<Transform>().Single(t => t.name == "elephant_l_Toe_bone").position) < .002f &&
                Vector3.Distance(action.RightHindAnchor, action.GetComponent<DigiPhantLocomotion>().elephantAnimator.transform
                .GetComponentsInChildren<Transform>().Single(t => t.name == "elephant_r_Toe_bone").position) < .002f,
                "Hind feet lost their anchored standing pose while walking");
            headControl.testValue = trunkControl.testValue = 0;
            action.Lower(); Step(90, 6.4f);
            Require(action.RearBlend == 0 && !locomotion.actionPoseOwned && !locomotion.actionMovementPaused && !action.apple.gameObject.activeSelf, "Lower respawned collected fruit or retained movement lock");
            Require(Vector3.Distance(standingPosition, locomotion.travelRoot.position) > .05f && Quaternion.Angle(standingRotation, locomotion.travelRoot.rotation) > .1f, "Walking/steering did not resume after fully lowering");
            Require(Mathf.Abs(locomotion.followCamera.fieldOfView - originalFieldOfView) < .001f, "Lower failed to restore original camera framing");
            locomotion.testForward = locomotion.testSteering = 0; action.ResetAction(); action.MoveToStation();
            Require(action.apple.gameObject.activeSelf && action.banana.gameObject.activeSelf, "Reset failed to replenish fruit");
            action.RequestRear(); Step(90, 8); Require(action.RequestPick(false), "Banana unreachable: " + action.Status); Step(260, 10);
            Require(!action.banana.gameObject.activeSelf && action.apple.gameObject.activeSelf, "Banana pickup/selection failed: " + action.Status);
            action.ResetAction(); locomotion.travelRoot.Rotate(Vector3.up, 73, Space.World); action.RequestRear(); Step(90, 16);
            Require(Vector3.Distance(toes[0].position, action.LeftHindAnchor) < .002f && Vector3.Distance(toes[1].position, action.RightHindAnchor) < .002f, "Rotated travel root broke rear foot anchoring");
            action.ResetAction(); action.MoveToStation();
            using (var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            { controls.port = ((IPEndPoint)reservation.Client.LocalEndPoint).Port; reservation.Close(); }
            controls.SetInputMode(InputMode.Camera);
            Require(controls.CameraSessionActive, "Explicit Camera session fixture failed to start");
            action.faceMode = true;
            Require(!action.AcceptPacket("{\"version\":1}", 20), "Incomplete packet accepted");
            Require(!action.AcceptPacket(Packet(1, 1, "apple", right: float.NaN), 20), "Nonfinite face input accepted");
            Require(action.AcceptPacket(Packet(1, 1, "", mouth: false), 20), "Valid neutral frame rejected");
            Require(!action.AcceptPacket(Packet(1, 2, "rearUp"), 20.01f), "Duplicate sequence accepted");
            Require(action.AcceptPacket(Packet(2, 2, "rearUp"), 20.02f), "Rear event rejected");
            Require(action.AcceptPacket(Packet(3, 3, "apple", left: .9f, right: .9f), 20.03f), "Bilateral frame rejected instead of safely ignored");
            action.faceMode = false; Step(90, 20.04f); Require(!action.Picking, "Bilateral blink picked a fruit");
            Require(action.RequestPick(true), "Loss test pickup failed to start"); action.faceMode = true;
            Require(action.AcceptPacket(Packet(4, 4, "apple", tracked: false), 21.6f), "Loss frame rejected");
            Require(!action.Picking && action.apple.gameObject.activeSelf, "Fresh untracked packet failed to cancel pickup");
            action.ResetAction(); action.AcceptPacket(Packet(5, 5, "rearUp"), 21.61f); action.Tick(21.61f, .1f);
            Require(action.RearBlend == 0, "Held-open mouth retriggered immediately after Reset");
            action.AcceptPacket(Packet(6, 5, "", mouth: false), 21.62f);
            action.faceMode = false; action.ResetAction(); action.RequestRear(); Step(90, 22); action.faceMode = true; action.Tick(100, .1f);
            Require(!action.Picking && action.RearBlend < 1, "Timeout did not lower safely");
            action.faceMode = false; action.ResetAction(); action.RequestRear(); Step(90, 24); locomotion.ResetPosition();
            Require(action.RearBlend == 0 && !locomotion.actionPoseOwned, "Return to starting position retained action snapshot");
            using (var commands = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            using (var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                int oldPort = controls.port;
                controls.port = ((IPEndPoint)commands.Client.LocalEndPoint).Port - 1;
                action.facePort = ((IPEndPoint)reservation.Client.LocalEndPoint).Port; reservation.Close();
                action.faceMode = true; action.BeginFaceReceiver(); action.CalibrateFace(); commands.Client.ReceiveTimeout = 1000;
                var source = new IPEndPoint(IPAddress.Any, 0); var bytes = commands.Receive(ref source);
                Require(source.Port == action.facePort && System.Text.Encoding.UTF8.GetString(bytes).Contains("fruitCalibrate"), "Calibration did not use the allowed bound face source port");
                controls.port = oldPort;
            }
            controls.SetInputMode(InputMode.TestSliders);
            Debug.Log("FRUIT_VALIDATION_OK | mode/body/group/mapping preservation | movement locked throughout rear/hold/pick/lower | standing head/trunk gestures | walking/steering resumes on four feet | temporary camera restore | grounded hindfeet | queued wink | both fruit reach/attach/lift/return | no bilateral pickup | malformed/duplicate/nonfinite UDP | loss cancel | lower without respawn | reset/return");
        }
    }
}
