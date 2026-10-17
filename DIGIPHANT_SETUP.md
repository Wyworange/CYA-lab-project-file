# DigiPhant Setup Guide

This guide supplements the root [README](README.md). It describes how to restore the shared assets,
prepare tracking, and run the adapted performance scene. The historical integration notes at the end
record earlier development checks; they are not evidence of a new test run or the current Git state.

## 1. Project and requirements

Open the repository root, containing `Assets/`, `Packages/`, and `ProjectSettings/`.
The performance scene is `Assets/DigiPhant/Scenes/DigiPhant.unity`.

| Component | Required or recorded version |
| --- | --- |
| Unity | 6000.6.3f1 |
| Render pipeline | URP 17.6.0; default asset `Assets/Settings/PC_RPAsset.asset` |
| Python | 3.14; original local integration used 3.14.7 |
| MediaPipe | 0.10.35 |
| OpenCV package | `opencv-contrib-python==4.14.0.94`; reported `cv2` version 4.14.0 |
| Tested platform | Apple Silicon macOS; the original integration recorded macOS 26.5.1 |
| Starter | [kommanderpi/studentstarter](https://github.com/kommanderpi/studentstarter) |
| Pinned starter commit | `4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f` |

Windows paths exist but are untested. Do not copy a virtual environment from another computer.
Keep Python tracking code and downloaded models outside Unity's `Assets/` directory.

## 2. Restore shared assets and install tracking

Run the following from a fresh checkout's root. Skip steps already completed on an existing setup.
Do not replace the adapted `Assets/DigiPhant` files with starter copies.

```sh
git clone https://github.com/kommanderpi/studentstarter.git DigiPhantStarter
git -C DigiPhantStarter checkout 4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f
python3 tools/restore_shared_assets.py
python3.14 -m venv DigiPhantStarter/Tracking/.venv
DigiPhantStarter/Tracking/.venv/bin/python -m pip install -r DigiPhantStarter/Tracking/requirements.txt
DigiPhantStarter/Tracking/.venv/bin/python -m pip check
DigiPhantStarter/Tracking/.venv/bin/python tools/fruit_tracking/setup_face_model.py
DigiPhantStarter/Tracking/.venv/bin/python tools/hand_tracking/setup_gesture_model.py
```

The restore tool validates the pinned starter commit and checksums, restores the shared Elephant assets,
and refuses to overwrite an incomplete or different existing Elephant directory. The pose model comes
from the pinned starter; the face and gesture setup scripts download and verify their models.
The starter's tracked source and dependency pins stay unchanged. Our extensions run through
`tools/fruit_tracking/fruit_bridge.py` rather than modifying `DigiPhantStarter/Tracking/bridge.py`.

For Windows PowerShell environment creation and installation, see README §3.4. Use
`DigiPhantStarter/Tracking/.venv/Scripts/python.exe` for subsequent Python commands on Windows.

On Apple Silicon macOS, an alternative is to install an isolated interpreter with `uv` before creating
the same virtual environment:

```sh
UV_PYTHON_INSTALL_DIR="$PWD/DigiPhantStarter/Tracking/.python" uv python install 3.14.7
DigiPhantStarter/Tracking/.python/cpython-3.14.7-macos-aarch64-none/bin/python3.14 -m venv DigiPhantStarter/Tracking/.venv
```

That interpreter path is specific to the recorded Apple Silicon setup. On other platforms, use the
actual installed interpreter path and keep the `.venv` location shown above.

## 3. Open Unity and check controls

1. In Unity Hub, select **Add project from disk** and open the repository with Unity 6000.6.3f1.
2. Open `Assets/DigiPhant/Scenes/DigiPhant.unity`, wait for compilation, and inspect the Console.
3. Press **Play**. **Test sliders** checks the legs, head, trunk, ears, tail, Travel, Turn, and
   **Return to starting position**, without opening the webcam.
4. Use README §4 for the fruit, drinking, and sitting debug controls.
5. If AllSky is unavailable, choose **DigiPhant → Environment → Use Original Solid Background**.
   The purchased skybox source assets are excluded from this repository.

The course scenery has no colliders, apart from the stage floor supporting the log. The movement
boundary is not road collision detection and does not prevent walking through scenery.

## 4. Camera, roles, and calibration

For the final three-person performance, follow the illustrated README §5 in order:

1. Seat all three performers side by side with heads, shoulders, and raised hands visible.
2. Select **3 people**, **Seated / upper body**, **Facial**, **Hands**, and **Fixed zones**.
3. Click **Camera**. Unity Editor launches one tracking bridge using the project's `.venv`.
   Camera index 0 is the default. A built player requires the bridge to be started manually.
4. Use **Assign roles (open mouth → raise hands)**. P2 opens the mouth to claim Face;
   P3 then raises both hands to claim Hands. P1 receives Upper body.
5. Hold a relaxed neutral pose with hands at a comfortable middle height for **10 seconds**;
   P2 also keeps a neutral face for about **2 seconds**. Use **Set neutral pose (10 seconds)**
   if calibration does not start automatically.

| Final role | Position in the unmirrored preview | Main controls |
| --- | --- | --- |
| P1 / Chunzi | Left strip | Walking, turning, body parts, and drinking |
| P2 / Yuwen | Middle strip | Mouth-controlled rearing; winks select fruit |
| P3 / Aurora | Right strip | Fists grab/carry the log; a V sign triggers sitting |

Keep performers inside their strips. Reassign roles and recalibrate after starting a new Play session
or changing group size, body mode, or Fixed zones. For detailed gestures, action locks, and the exception
allowing P1 to walk while P3 carries the log, use README §6. Earlier body-only mappings in the appendix
are historical and must not replace this final role layout.

**Stopping and recovery:** stopping Play closes the bridge launched by Unity. A separately launched
bridge may be reused, but is not closed by stopping Play; press Q in its preview to quit it.
Use **Retry** to reconnect or restart Unity's bridge, **Reassign people** to recover identities, then
recalibrate. The original standalone preview also supports R for identity reassignment.
Tracking loss returns affected poses toward rest and stops locomotion when required inputs disappear.

If there is no camera image, allow Unity or Python in macOS **System Settings → Privacy & Security →
Camera**, close other camera applications, and retry. This starter-only diagnostic tests another camera
index without the project's face/hand extensions:

```sh
DigiPhantStarter/Tracking/.venv/bin/python DigiPhantStarter/Tracking/bridge.py --people 1 --upper-body-only --camera 1
```

All communication is local UDP: 5055 body data, 5056 Unity commands, 5057 JPEG preview,
5058 face actions, and 5059 hand gestures. Run only one bridge and one Unity receiver per port set.

## 5. Fruit integration and recording

Fruit interaction uses the main performance scene, not a separate interaction scene. The editor menu
**DigiPhant → Fruit → Integrate Actions Into DigiPhant Scene** can reapply the fruit components and stations
idempotently. Sources are under `Assets/StudentWork/FruitInteraction`; tracking and model setup are under
`tools/fruit_tracking`. See README §6 for current operation. The former `FRUIT_INTERACTION.md` document
has been removed; this guide does not depend on it.

Movement and turning are locked while rising, standing, picking, and lowering; walking resumes once the
elephant is fully lowered. Standing head/trunk controls remain available. The camera retains its original
FOV 38 and temporarily widens to at least 50 for the action, restoring it on lowering, reset, or disable.
**Facial** is independent of Seated and body calibration: turning it off releases the face model and stops
face actions while body tracking continues. The camera frames are shared rather than opening a second camera.

The recording component attaches at runtime. After calibration and a camera control check, select
**Record performance**, keep Game view visible, then select **Stop recording and save**.
The local output is `Recordings/<session>/performance.mp4`, with a timestamped session directory.
Export is at most 1280×720, 10 fps, without microphone audio. Frames and timing data remain locally for
export recovery. Play the MP4 to check that it contains both the elephant and preview before uploading it.

Current demo/video folder, as linked in the README:
<https://drive.google.com/drive/folders/1XvavueuYdmjC1N-thfG7S0AxbH4DiyA7>.
This replaces the different Drive folder recorded in the original local notes. The application does not
upload automatically. Keep recordings and intermediate frames out of Git.

## 6. Verification and repository scope

These commands perform tracking unit tests without opening the webcam:

```sh
DigiPhantStarter/Tracking/.venv/bin/python -m unittest discover -s DigiPhantStarter/Tracking -p 'test_*.py'
DigiPhantStarter/Tracking/.venv/bin/python -m unittest discover -s tools/fruit_tracking -p 'test_*.py'
DigiPhantStarter/Tracking/.venv/bin/python -m unittest discover -s tools/hand_tracking -p 'test_*.py'
```

Then check the Unity Console, test sliders/debug controls, calibration, real-camera gestures, tracking
loss/recovery, and a short recorded MP4. README §8 reports the later successful three-person camera test;
the older checks below describe earlier development stages. Restoring/translating this document does not
constitute rerunning any of those tests or independently reproducing the project on another computer.

Keep the repository lightweight: exclude `Library/`, `Temp/`, `Logs/`, build outputs, `.venv`, recordings,
intermediate frames, backups, and local notes. Restore the heavy Elephant model/textures and pose model
from the pinned starter. Do not redistribute AllSky source assets without the required permission.
Retain adapted scripts, small resources and their `.meta` files, `Packages/`, and `ProjectSettings/`.

## Appendix: historical local integration record

The following preserves the original setup document's development findings. Absolute temporary paths,
old Git status, and early mappings are historical snapshots, not requirements for a new checkout.

- **Local project layout:** the original root was `/Users/yuwenwang/Vscode/Unity/Yuwen_TFD_Playground`.
  An earlier `Yuwen_TDF_Playground` spelling had been retired. The starter lived in `DigiPhantStarter`;
  `Tracking/face_landmarker.task` was then an untracked local addition. Unity imports were under
  `Assets/Elephant` and `Assets/DigiPhant`. `.plastic` was retained locally and ignored.
- **Python environment:** the original independent interpreter used the Apple Silicon path shown in §2.
  Dependency metadata initially reported 19 distributions and no conflicts. Offline `ensurepip` installed
  pip 26.2.1, increasing the count to 20; an actual `pip check` reported no broken requirements.
  MediaPipe/OpenCV versions and starter dependency pins were unchanged.
- **Earlier body-only controls:** P1 controlled front legs, travel from left-hand height, and turning from
  lean/shoulder tilt; P2 controlled hind legs and tail; P3 controlled head, trunk, and ears. A single person
  controlled every part. Seated hand lifts could affect legs and travel/trunk together. Initial identities
  were assigned left-to-right. These precede the final face/hand roles in §4.
- **Savannah import:** `DigiPhantStarter/SavannahCourse` was imported into `Assets/SavannahCourse`, preserving
  upstream `.meta` files and GUIDs, and injected into the saved main scene as one layout4 course root.
  A local `DigiPhant_BeforeSavannah.unity` backup matched the pre-injection scene byte-for-byte.
  Hash comparisons found only the intended existing-scene change; other assets/settings were retained.
  The scene's Elephant Travel radius override was set to 30 while the code default remained 15.
  Regenerated/new components may therefore need their radius checked.
- **Initial checks:** 11 Python tests passed, including localhost UDP and synthetic MP4 encoding;
  UDP testing ran outside the sandbox. Isolated Unity course package/generator/injection and base
  controller/locomotion checks exited 0. Logs were under
  `/private/tmp/yuwen-savannah-unity-validation-20261007/` (`injection.log`, `bundle.log`, `digiphant.log`).
  A main-Editor scene-copy check initially refused a dirty scene after geometry preview; the scene was
  subsequently saved. That refusal was not counted as a successful full in-Editor run. Temporary helpers
  were removed. Scene/Game views were inspected, but the camera and human recording tests were deferred
  at that stage. The summary was `/private/tmp/yuwen-savannah-integration-20261007/final-integration-summary.txt`.
- **Fresh reconstruction snapshot:** staged tree `42037c2f5c0760adc528ce5e59b9c4aeec2dc484` was reconstructed at
  `/private/tmp/yuwen-digiphant-fresh-reconstruction-20261007`. The tool restored 96 shared Elephant files;
  all 31 active-scene GUID references resolved. A wider scan still found 17 existing missing references:
  seven PC_Renderer probe-volume debug/blending references, six vendor skybox references, and four
  DefaultVolumeProfile custom/test scripts (`OutlineVolumeComponent`, `TestAnimationCurveVolumeComponent`,
  `OasisFogVolumeComponent`, `TestVolume`). Because URP global defaults reference that volume profile,
  these cannot all be dismissed as unused; optional effects could be missing or warn. No new blocking or
  control fault was identified, but complete asset integrity was not claimed. Details were in
  `/private/tmp/yuwen-savannah-integration-20261007/reconstruction-guids.json`. This snapshot staged 158
  files, none above 1 MB, before later documentation changes. Python was not reinstalled and Unity was
  not run in that reconstruction copy, so it was not a full second-machine reproduction.
- **Fruit checks:** eight face tests and the original 11 tests passed; face model SHA256 and `pip check`
  passed. Isolated fruit, base, and Savannah checks exited 0 under
  `/private/tmp/yuwen-current-verification-jabchnn0/`. Early FOV 38 screenshots cropped the head/tusks.
  Narrow fixes locked standing locomotion, temporarily widened FOV, and cleaned up owned previews.
  `fruit-standing-final.log` and `base-standing-final.log` passed with exit 0; static renders under
  `visual-standing-final/` showed the standing elephant in frame with feet grounded, then restored FOV 38
  after lowering. `visual-standing-final.log` exited 0. These renders used no human camera input.
- **Unified Camera lifecycle:** the old auto-start-on-Play and **Start face camera** flows were replaced.
  Fifteen face tests and 11 starter tests passed; model SHA256 passed. Simulated checks confirmed shared
  body/face frames and continued body packets when the face model was missing. Face loading/release was
  dynamic; at that stage Facial displayed a single-person limitation in multiplayer while body input
  remained available. Logs under `/private/tmp/yuwen-camera-lifecycle-fwmqg7zr/` reported
  `CAMERA_LIFECYCLE_OK`, `FRUIT_VALIDATION_OK`, `DIGIPHANT_VALIDATION_OK`, and
  `DIGIPHANT_LOCOMOTION_VALIDATION_OK`, all exit 0. Stopping Camera after 90 programmatic standing frames
  caused less than 0.002 units of travel during lowering and restored walking afterward. No real camera
  was opened. Intermittent `cgWindowNotFound` / `noWindowsAvailable` UI errors prevented the final main-Editor
  Play confirmation in that round.
- **Historical Git state:** early notes recorded staged files, no configured author, and no commit/remote/push;
  a later camera round explicitly made no Git submission. These describe those rounds only, not today's
  published repository. Temporary validation directories above are not durable deliverables.
