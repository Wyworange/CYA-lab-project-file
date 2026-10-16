using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DigiPhant.StudentWork.Editor
{
    public static class CameraLifecycleValidation
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Camera lifecycle validation: " + message); }
        static object Receiver(DigiPhantCameraPreview preview) => typeof(DigiPhantCameraPreview).GetField("receiver", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preview);
        static string Mappings(DigiPhantController controls) => string.Join("\n", controls.controls.Select(c => JsonUtility.ToJson(c)));
        public static void RunAll()
        {
            EditorSceneManager.OpenScene(FruitInteractionSetup.ScenePath);
            var action = UnityEngine.Object.FindAnyObjectByType<FruitInteraction>();
            var controls = action.GetComponent<DigiPhantController>();
            var preview = controls.GetComponent<DigiPhantCameraPreview>();
            if (!preview) preview = controls.gameObject.AddComponent<DigiPhantCameraPreview>();
            Check(action.Initialise(), "Rig fixture missing");
            controls.inputMode = InputMode.Camera; // Simulate a scene saved in Camera mode.
            controls.InitializeInputSession();
            typeof(DigiPhantCameraPreview).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(preview, null);
            preview.BeginPreview();
            Check(controls.inputMode == InputMode.TestSliders && !controls.CameraSessionActive && !preview.IsPreviewActive && !preview.RetryPreview(),
                "Play/saved Camera/preview enable/Retry activated a camera without the left Camera action");
            int count = controls.performerCount; bool body = controls.upperBodyOnly; string mapping = Mappings(controls);
            action.SetFaceMode(true); action.SetFaceMode(false);
            Check(!controls.CameraSessionActive && !preview.IsPreviewActive && controls.inputMode == InputMode.TestSliders && controls.performerCount == count && controls.upperBodyOnly == body && Mappings(controls) == mapping,
                "Facial toggle started a camera or changed Test sliders/body/count/mappings");
            using (var commands = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            using (var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                int oldPort = controls.port, oldFacePort = action.facePort;
                controls.port = ((IPEndPoint)commands.Client.LocalEndPoint).Port - 1;
                action.facePort = ((IPEndPoint)reservation.Client.LocalEndPoint).Port; reservation.Close(); action.BeginFaceReceiver();
                controls.SetInputMode(InputMode.Camera);
                Check(controls.CameraSessionActive && preview.IsPreviewActive, "Explicit Camera action did not start the receiver/session");
                object initialReceiver = Receiver(preview);
                controls.SetInputMode(InputMode.Camera);
                Check(ReferenceEquals(initialReceiver, Receiver(preview)), "Repeated Camera action restarted the same session");
                controls.SetUpperBodyOnly(true);
                var neutral = new PoseFrame { version = 1, performerCount = controls.performerCount, upperBodyOnly = true,
                    people = Enumerable.Range(1, controls.performerCount).Select(i => new PerformerFrame { slot = i, values = new float[6], confidence = Enumerable.Repeat(1f, 6).ToArray() }).ToArray() };
                Check(controls.AcceptPacket(JsonUtility.ToJson(neutral), 0) && controls.CalibrateAt(0), "Body neutral fixture failed");
                action.SetFaceMode(true);
                Check(controls.upperBodyOnly && action.faceMode && controls.CameraSessionActive && controls.IsCalibrated && controls.performerCount == count && Mappings(controls) == mapping && ReferenceEquals(initialReceiver, Receiver(preview)),
                    "Upper body + Facial could not coexist or Facial restarted/changed the calibrated body session");
                commands.Client.ReceiveTimeout = 1000; bool enabledCommand = false;
                while (commands.Available > 0)
                {
                    var source = new IPEndPoint(IPAddress.Any, 0); string text = Encoding.UTF8.GetString(commands.Receive(ref source));
                    if (text.Contains("\"fruitEnabled\":true")) { enabledCommand = true; Check(source.Port == action.facePort, "Enable command used the wrong sender port"); }
                }
                Check(enabledCommand, "Facial enable did not reach the existing command channel");
                action.apple.gameObject.SetActive(false);
                action.SetFaceMode(false);
                Check(!action.apple.gameObject.activeSelf && controls.CameraSessionActive && controls.IsCalibrated && ReferenceEquals(initialReceiver, Receiver(preview)),
                    "Turning Facial off replenished picked fruit, stopped camera or invalidated body calibration");
                Check(preview.RetryPreview() && !ReferenceEquals(initialReceiver, Receiver(preview)), "Active-session Retry did not reconnect preview");
                var locomotion = controls.GetComponent<DigiPhantLocomotion>();
                controls.ApplyControls(0, 1 / 60f); action.RequestRear();
                for (int i = 0; i < 90; i++) { controls.ApplyControls(i / 60f, 1 / 60f); action.Tick(i / 60f, 1 / 60f); }
                Check(action.RearBlend > .99f, "Camera-stop fixture did not reach upright stance");
                controls.SetInputMode(InputMode.TestSliders);
                Check(!controls.CameraSessionActive && !preview.IsPreviewActive && !preview.RetryPreview(), "Test sliders did not stop camera/preview or Retry bypassed the gate");
                Check(!action.apple.gameObject.activeSelf, "Camera stop replenished already picked fruit");
                Vector3 standingPosition = locomotion.travelRoot.position;
                locomotion.testForward = .4f; locomotion.testSteering = .2f;
                for (int i = 0; i < 90 && action.RearBlend > 0; i++)
                {
                    Check(locomotion.actionMovementPaused, "Camera stop released movement before lowering finished");
                    controls.ApplyControls(2 + i / 60f, 1 / 60f); action.Tick(2 + i / 60f, 1 / 60f);
                    Check(Vector3.Distance(standingPosition, locomotion.travelRoot.position) < .002f, "Camera stop allowed travel during lowering");
                }
                Check(action.RearBlend == 0 && !locomotion.actionMovementPaused, "Camera stop failed to complete lowering");
                for (int i = 0; i < 30; i++) { controls.ApplyControls(4 + i / 60f, 1 / 60f); action.Tick(4 + i / 60f, 1 / 60f); }
                Check(Vector3.Distance(standingPosition, locomotion.travelRoot.position) > .1f, "Test-slider travel did not resume after lowering");
                controls.port = oldPort; action.facePort = oldFacePort;
            }
            // Harmless subprocesses stand in for an owned bridge and an independently started one.
            if (System.IO.File.Exists("/bin/sleep"))
            {
                using (var owned = Process.Start(new ProcessStartInfo("/bin/sleep", "30") { UseShellExecute = false }))
                using (var external = Process.Start(new ProcessStartInfo("/bin/sleep", "30") { UseShellExecute = false }))
                {
                    int ownedPid = owned.Id;
                    try
                    {
                        typeof(DigiPhantCameraPreview).GetField("ownedBridge", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(preview, owned);
                        preview.StopPreview();
                        bool ownedStopped;
                        try { using (var inspection = Process.GetProcessById(ownedPid)) ownedStopped = inspection.WaitForExit(1000); }
                        catch (ArgumentException) { ownedStopped = true; }
                        Check(ownedStopped && !external.HasExited, "Preview stop killed an external process or left its owned process running");
                    }
                    finally { if (!external.HasExited) external.Kill(); external.WaitForExit(1000); }
                }
            }
            action.ResetAction();
            UnityEngine.Debug.Log("CAMERA_LIFECYCLE_OK | saved Camera enters Test without capture | Preview enable/Test Retry/Facial cannot activate camera | explicit Camera only | idempotent session | upper body+Facial | calibration/count/mapping preservation | enable command on existing bridge | Facial off preserves fruit/body session | Test stops owned preview/bridge | external process preserved");
        }
    }
}
