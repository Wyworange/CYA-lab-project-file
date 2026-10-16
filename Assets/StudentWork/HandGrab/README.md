# Aurora 手势抓木头 · 合并记录（2026-10-08）

来源：AuroraJ03/Aurora-DigiPhant-HandGrab（`Aurora/` 文件夹）。所有文件与她的 `SHA256SUMS.txt` 一致；原 `.meta` GUID 保留，无冲突。合并前备份在项目根目录 `Backups/2026-10-08_before_aurora_merge/`。

## 功能（Aurora 原设计，未改）
- 左手握拳：低头、鼻子伸出。双手握拳：抓起附近的木头并扛着（可继续行走）。任一只手明确张开：放下。
- 手丢失、不确定、低置信度都不会放下已抓的木头。

## 文件位置
- Unity：`Assets/StudentWork/HandGrab/{Runtime,Editor,Generated}`（命名空间仍为 `DigiPhant`）。
- Python：`tools/hand_tracking/hand_actions.py`（她的 `hand_state` / `hand_packets` 原样移植）、`setup_gesture_model.py`、`test_hand_actions.py`。
- 模型：`DigiPhantStarter/Tracking/gesture_recognizer.task`（与 Google 官方文件 SHA256 一致，不入 Git；缺失时运行 `setup_gesture_model.py`）。

## 合并时的改动
| 位置 | 改动 | 原因 |
|---|---|---|
| `DigiPhantHandGrab.cs` | 接收端口 `port + 3` → `port + 4`（**5059**） | 5058 是表情端口 |
| `DigiPhantHandGrab.cs` | 新增 `overlaySuspended`，为 true 时不开始伸鼻/抓取 | 规则 A |
| `DigiPhantHandGrabSetup.cs` | 材质目录改为 `Assets/StudentWork/HandGrab/Generated` | 文件夹迁移 |
| `DigiPhantController.cs`（共享） | 新增事件 `PostControlsApplied` / `PostLocomotionEvaluated` / `TrackingReset`，并显示她的面板 | 她的组件需要 |
| `DigiPhantCameraPreview.cs`（共享） | 场景有 HandGrab 时用 `fruit_bridge.py` 并传 `--hand-actions --hand-port 5059`；端口与表情端口相同时拒绝启动 | 同一摄像头、同一进程 |
| `fruit_bridge.py` | 新钩子：手势模型在摄像头打开前加载；`assign` 之后调用 `hands.process`；读帧失败先重试 3 次、再重开摄像头（`camera_recovery.py`） | `bridge.py` 保持逐字节不变（SHA 校验） |
| `FruitInteraction.cs` | 规则 A：扛着木头时不能起身/摘水果（提示 "Drop the wood first"），表情触发也拦截；摘水果期间暂停手势抓取 | 团队约定 |

未合并：她 `bridge.py` 里的 Windows 摄像头后端代码（Mac 不需要）。

## 端口
5055 身体 · 5056 命令 · 5057 预览 · 5058 表情（Yuwen）· **5059 手势（Aurora）**

## 角色
Aurora 站画面右侧 = **P3**：场景中 `DigiPhantHandGrab.performerId` 设为 **3**（她的默认值是 1）。以后由角色认领流程自动设置。

## Hands 开关
左侧面板 Facial 旁的 **Hands** 按钮（可与 Seated、Facial 同时选中）。关闭时 `DigiPhantHandGrab` 组件停用、追踪程序不运行手势识别；开启后才识别手势，并在角色认领中加入 "Hands: raise both hands" 一步。每次 Play 默认关闭。

## Debug: move to wood
右上角水果面板的 **Debug: move to wood**（与 move to feeding bush 同类）：大象垂直于木头、正对木头中段，从当前所在一侧接近；用 `DigiPhantHandGrab.PredictReachTip`（与左手握拳伸鼻完全相同的姿势计算，含低处目标的额外点头）把伸鼻时的鼻尖放到木头近侧上沿外约 6 cm。随后按真实规则（鼻尖到木头表面 ≤ `pickupSurfaceMargin` 0.2 m）复核并在面板显示距离；Console 打印 `WOOD_STATION gap=…`。扛着木头时不可用。需选中 **Hands** 才能实际抓取。

## 场景接入
**自动**：`Editor/HandGrabSceneIntegration.cs` 在脚本编译后（每台电脑一次）打开主场景，依次执行下面第 2、3 步并设 `performerId = 3`、保存；Console 出现 `DIGIPHANT_HAND_GRAB_MAIN_SCENE_INTEGRATED`。失败会打印原因；修好后用菜单 **DigiPhant → Hand Grab → Integrate into Main Scene (Inject + Timber)** 重跑。

手动步骤（等价）：
1. 打开 `Assets/DigiPhant/Scenes/DigiPhant.unity`（已备份）。
2. **DigiPhant → Hand Grab → Inject into Open Scene**（会把组件加到 DigiPhant Controls 并绑定头/鼻骨骼）。
3. **DigiPhant → Hand Grab → Make Timber Grabbable in Open Scene**（场景中已有 `Timber` 与两个 `Cut end`）。
4. 设 `performerId = 3`，保存场景。
5. **DigiPhant → Hand Grab → Validate Open Scene**。

## 测试状态
- Python：fruit_tracking 19 项、hand_tracking 15 项全部通过；假摄像头运行完整合并管线正常，读帧失败后自动恢复。
- Unity：Reviewer 确认合并后的代码编译通过（之后的修正尚待 Unity 重新编译）。
- **未解决**：在真实摄像头上，合并前的版本开启手势后几秒内出现 "Camera stopped delivering frames"。已加入"摄像头前加载模型"和"读帧重试/重开"两项修正，但**尚未用真实摄像头验证**。
- 未验证：她的 Editor 验证脚本在本项目中的运行结果、三人同时在画面中的手势识别。
- 她的验证脚本会把 `TimberGrab_Preview.unity` 写到 `Assets/DigiPhant/Scenes`、把验证输出写到 `Assets/StudentWork/HandGrabValidation`，运行前注意。

## 手势识别改为主线程（2026-10-09）
三人 + 固定分区的实测中，后台线程里的手势识别在 15:45:19 永久卡住（诊断日志此后每秒 `recog_ms=- expired=15`），Unity 显示 "no hand data"，重新认领也无法恢复。原因推测为多个 MediaPipe 模型（3 个分区姿势 + 表情）与后台线程的手势模型并发导致死锁。现在 `HandSession` 不再使用后台线程：在追踪循环里每秒约 8 次（`RATE`）同步识别，先把画面缩到 640 像素宽（`MAX_WIDTH`；手部坐标是归一化的，不影响与身体的匹配）。空画面实测约 10 ms/次。
