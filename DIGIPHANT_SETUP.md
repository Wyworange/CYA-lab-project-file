# DigiPhant 本机接入记录

## 当前环境与来源

项目根目录是 `/Users/yuwenwang/Vscode/Unity/Yuwen_TFD_Playground`。原项目已恢复到 TFD 名称；此前的 `Yuwen_TDF_Playground` 路径已不存在。

| 项目 | 实际版本或位置 |
| --- | --- |
| 系统 | macOS 26.5.1，Apple Silicon arm64 |
| Unity | 6000.6.3f1 |
| 渲染管线 | URP 17.6.0；默认 `Assets/Settings/PC_RPAsset.asset` |
| Python | 3.14.7；独立解释器 `DigiPhantStarter/Tracking/.python/cpython-3.14.7-macos-aarch64-none/bin/python3.14` |
| 虚拟环境 | `DigiPhantStarter/Tracking/.venv` |
| MediaPipe | 0.10.35 |
| OpenCV | 安装包 `opencv-contrib-python` 4.14.0.94，`cv2.__version__` 为 4.14.0 |
| Starter | [kommanderpi/studentstarter](https://github.com/kommanderpi/studentstarter.git) |
| 固定提交 | `4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f` |

Starter 在项目根目录的 `DigiPhantStarter`，已跟踪源码保持未修改；`Tracking/face_landmarker.task` 是本机新增的未跟踪模型。原依赖 pins 没有修改；此前虚拟环境安装了 19 个包，metadata 检查为 0 冲突；本轮已通过离线 ensurepip 在项目虚拟环境内安装 pip 26.2.1，现为 20 个 distributions，实际执行 `pip check` 返回 No broken requirements found。MediaPipe 和 OpenCV 版本未变。追踪代码与模型留在 Assets 外；Unity 导入位于 `Assets/Elephant` 和 `Assets/DigiPhant`。

## 使用与校准

在 Hub 用 **Add project from disk** 添加当前 TFD 目录。打开 `Assets/DigiPhant/Scenes/DigiPhant.unity`，等编译完成后按 Play。Play 与 Test sliders 不启动摄像头；左侧 **Camera** 是统一启动入口，Editor 使用 `.venv/bin/python` 启动一份桥接，默认摄像头索引 **0**。构建后的 player 仍需手动启动桥接。Facial 为身体模式旁独立开关，可与 Seated 同选，不自行启动桥接。

先在 **Test sliders** 检查骨骼控制、Travel、Turn 和 Return to starting position。首次真人测试建议 **1 person → Seated / upper body → Camera**，保持头、肩、手可见。看见 P1 标签后，将双手放在舒适的中间高度，点击 **Set neutral pose (10 seconds)**，在倒计时结束前保持不动。失败时调整取景后重新校准。改变人数、身体模式或身份分配后也要重新校准。

| 三人默认分工 | 输入 |
| --- | --- |
| P1 前腿 | 左右抬脚；坐姿模式用左右抬手替代 |
| P1 移动与转向 | 左手高度控制前进/后退；lean（坐姿为肩部倾斜）控制转向 |
| P2 后腿与尾巴 | 左右抬脚/坐姿抬手；lean 控制尾巴 |
| P3 头、鼻子、耳朵 | lean 控制头；右手高度控制鼻子；双手间距控制耳朵 |

单人模式由 P1 控制所有部位；坐姿抬手会同时影响腿和 travel/trunk。预览不镜像，初次身份按画面从左到右分配。三人测试时选择 **3 people**，让全部三人入镜再校准。

**停止与恢复：**停止 Play 会关闭 Unity 自己启动的桥接。独立启动的桥接可被复用，但停止 Play 不会关闭它；在其预览窗口按 Q 退出，再点击 Camera。预览的 Retry 用于重连或重启 Unity 自己的桥接。人物身份错误时用 Reassign people，再重新校准；独立预览中 R 可重分配身份。追踪丢失时受影响的姿态回到休息状态，locomotion 在所需输入缺失时停止；恢复后检查身份和校准状态。这些行为仍须真人验证。

无摄像头画面时，检查 macOS **System Settings → Privacy & Security → Camera**、关闭其他占用摄像头的应用，并可手动测试其他索引：

```sh
cd /Users/yuwenwang/Vscode/Unity/Yuwen_TFD_Playground
DigiPhantStarter/Tracking/.venv/bin/python DigiPhantStarter/Tracking/bridge.py --people 1 --upper-body-only --camera 1
```

本机数据路径为 Python/MediaPipe → localhost UDP → Unity controls/locomotion；5055 接收追踪并发送配置，5056 为桥接端口，5057 接收 JPEG 预览。每组端口只运行一个桥接与一个 Unity 接收器。

## 录制

录制组件在运行时自动附加。先校准并确认 Camera 动作有效，再点击 **Record performance**。保持 Game view 可见，点击 **Stop recording and save** 后检查项目根目录 `Recordings/<session>/performance.mp4`（session 为日期时间目录）。导出最大 1280×720、10 fps，不录麦克风。检查 MP4 能播放且确实包含大象与预览后，再自行上传[用户指定的 Drive 文件夹](https://drive.google.com/drive/folders/1pMZViKVTm0vaFiKc1a0N4PM1z5oxSGb6?usp=drive_link)；程序不自动上传。视频只留在本机 Recordings 与 Drive，不进入 Git。帧与时间数据保留用于导出恢复。合成帧 MP4 编码测试通过，真人 Game view 录像尚未验证。

## Savannah 场景与本次修改

课程包从根目录外部 checkout `DigiPhantStarter/SavannahCourse` 导入到 `Assets/SavannahCourse`，保留全部上游 `.meta` 与 GUID。按供应工具向当前已保存的 `Assets/DigiPhant/Scenes/DigiPhant.unity` 注入课程；场景有一个 layout4 课程根节点。

原场景的逐字节备份是 **`Assets/DigiPhant/Scenes/DigiPhant_BeforeSavannah.unity`**，已比对确认与注入前原文件完全一致。现有 Assets、Packages、ProjectSettings 的文件 hash 对比，除预期的当前场景变更外，原有文件未变；新增课程及备份单独保留。自定义控制、摄像机、模型、追踪脚本和项目设置均保留。

按用户批准，将当前场景 `Elephant Travel` 上的 locomotion 半径覆盖值设为 **30**，原源代码默认 **15** 不改。因此新建或重新生成的控制组件仍可能默认 15；测试其他场景前检查组件的半径。课程本身是没有碰撞体的静态景观，30 单位圆形移动边界不是道路碰撞或沿道路约束，不能保证大象不会穿过景物。

## 验证结果与剩余检查

- Python 3.14.7 下 MediaPipe/OpenCV 导入成功；11 项测试全部通过，包括本机 UDP 与合成 MP4 编码测试。本机 UDP 测试在沙箱外执行；19 个包的 metadata 依赖检查为 0 冲突。
- 隔离 Unity 副本中，课程导入包检查、完整生成器测试、场景副本/注入检查，以及 DigiPhant 模板和 locomotion 验证通过，退出码 0。日志在 `/private/tmp/yuwen-savannah-unity-validation-20261007/`：`injection.log`、`bundle.log`、`digiphant.log`。
- 主 Editor 的课程包及确定性几何检查通过。现场生成器的 scene-copy 阶段曾因几何预览令当前场景标记 dirty 而拒绝执行；随后已通过 Editor 保存当前场景，不把这次拒绝记录为现场全套检查成功。临时验证辅助脚本已移除。
- Editor 的 Scene 与 Game view 已目视确认大象和课程，最后处于非 Play 状态。真实摄像头推理、身份分配、10 秒校准、动作响应、追踪丢失/恢复、真人 Game view 录制、MP4 播放和 Drive 上传都未验证；用户明确选择稍后测试摄像头。
- 本次记录汇总：`/private/tmp/yuwen-savannah-integration-20261007/final-integration-summary.txt`。临时目录不是持久交付文件，本文件保留实际结论。

真人测试顺序：打开主场景 → Play → Test sliders 检查腿/头/鼻子/耳朵、Travel、Turn 和回起点 → 选 1 person / Seated / upper body / Camera → 看见 P1 → 中立姿势校准 10 秒 → 小幅抬左右手及肩倾斜 → 测试移动到课程远端和边界 → 短暂离开镜头确认停止并恢复身份 → 录制短视频、停止并播放 MP4 → 上传指定 Drive。暂不需要三人才能完成首轮测试。

## Git 与重建

本机独立学生 Git 已初始化，文件已暂存，但没有 commit、remote 或 push；Git 作者身份未配置，不虚构身份提交。原 `.plastic` 已保留并忽略。学生仓库保留适配后的 DigiPhant、项目配置及恢复工具，排除 starter、Elephant 重资产、Python 环境、Unity 缓存与录像。

以下从新的学生项目 checkout 根目录运行；现有本机环境已装好，不要重复克隆或重建：

```sh
git clone https://github.com/kommanderpi/studentstarter.git DigiPhantStarter
git -C DigiPhantStarter checkout 4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f
python3 tools/restore_shared_assets.py
```

恢复工具校验固定提交与 checksum，只恢复 Elephant，不覆盖学生已适配的 DigiPhant；已有 Elephant 若不完整或不同会停止，避免覆盖。

新的机器需重新安装兼容 Python 3.14.7。可安装该版本后用自己的解释器创建 `.venv`；也可在有 `uv` 的机器上将独立 Python 保持在本项目布局：

```sh
UV_PYTHON_INSTALL_DIR="$PWD/DigiPhantStarter/Tracking/.python" uv python install 3.14.7
DigiPhantStarter/Tracking/.python/cpython-3.14.7-macos-aarch64-none/bin/python3.14 -m venv DigiPhantStarter/Tracking/.venv
DigiPhantStarter/Tracking/.venv/bin/python -m pip install -r DigiPhantStarter/Tracking/requirements.txt
DigiPhantStarter/Tracking/.venv/bin/python -m pip check
DigiPhantStarter/Tracking/.venv/bin/python -m unittest discover -s DigiPhantStarter/Tracking -p 'test_*.py'
```

独立解释器路径适用于本次 Apple Silicon macOS；其他平台按实际安装路径创建相同 `.venv`。不要复制旧虚拟环境。再用 Unity 6000.6.3f1 导入新 checkout，并完成真人测试。

本次对暂存 tree `42037c2f5c0760adc528ce5e59b9c4aeec2dc484` 做本地重建，目录 `/private/tmp/yuwen-digiphant-fresh-reconstruction-20261007`：恢复工具验证并恢复 96 个共享 Elephant 文件，当前活动场景的全部 31 个 GUID 引用可解析。广泛扫描仍发现 17 个原有引用缺失：7 个 PC_Renderer probe-volume debug/blending 引用、6 个 vendor skybox 引用（主场景使用内置 skybox），以及 DefaultVolumeProfile 的 4 个自定义/测试脚本引用（OutlineVolumeComponent、TestAnimationCurveVolumeComponent、OasisFogVolumeComponent、TestVolume）。DefaultVolumeProfile 被 URP 全局默认设置引用，不能称这些全部不生效；原有可选效果可能缺失或产生警告。本次没有改变这些引用，未发现新增阻塞或控制故障，但不声称所有资源完整。详情在 `/private/tmp/yuwen-savannah-integration-20261007/reconstruction-guids.json`。暂存 158 个文件，没有大于 1 MB 的文件；这是文档更新前的检查快照，后续文档暂存会改变 tree。没有在重建副本重新安装 Python 或运行 Unity，因此尚未完成另一台机器上的完整重建验证。

## 新增水果互动

水果互动已集成在原场景 `Assets/DigiPhant/Scenes/DigiPhant.unity`，未使用独立互动场景。可从 **DigiPhant → Fruit → Integrate Actions Into DigiPhant Scene** 幂等恢复水果组件和站点。新增模型、树枝与动作组件位于 `Assets/StudentWork/FruitInteraction`，追踪 wrapper/测试/模型恢复脚本位于 `tools/fruit_tracking`；复用原 controller、locomotion 和动作映射。

按用户选择，起身、直立、采摘与降低期间固定移动/转向，完全降低后恢复四脚行走；站立头/鼻手势保留。当前界面用左侧 Camera 统一启动身体追踪与预览，独立 Facial 可与 Seated 同选，不改人数、身体模式或身体校准。关闭 Facial 释放脸部模型、停止表情动作，身体追踪继续；Test sliders/停止 Play 关闭 Unity 启动的桥接。原端口 5055/5056/5057、表情 5058 保留。身体 10 秒与表情约 2 秒校准独立。原相机 FOV 38 保留，动作时临时至少 FOV 50，降低/Reset/禁用时恢复。详情见 [FRUIT_INTERACTION.md](FRUIT_INTERACTION.md)。

## 历史复核记录（旧启动流程）

实际重新运行表情 Python 8 项与原 11 项测试，全部通过；face model SHA256 已验证，pip check 通过。此前隔离 Unity 的水果 RunAll、基础控制器/locomotion、Savannah 检查退出码 0，日志在 `/private/tmp/yuwen-current-verification-jabchnn0/{fruit-current,base-current,savannah-current}.log`。修正前原场景相机 FOV 38 的静态截图显示起身时头与象牙裁切，不能套用之前独立场景 FOV 50 的取景结论；后续临时 FOV 修正见下文。

独立审查指出的直立移动脚掌滑动/后腿手势覆盖、取景裁切及 OnDisable 清理缺口已按用户选择做窄范围修正：站立全过程固定移动转向，保留原镜头配置并暂时扩大 FOV，按所有权清理预览。`fruit-standing-final.log` 的 RunAll 与最终四脚状态基础回归 `base-standing-final.log` 均 PASS、退出码 0，覆盖 Camera/Test 模式、原配置保持、降低后恢复行走、头/鼻控制、相机恢复、预览生命周期清理及水果动作。最终静态画面已由主 agent 与 Builder 核验：站立时头、象牙、鼻子与脚完整入镜、脚掌贴地，降低后恢复 FOV 38 的正常步态取景。PNG 在 `/private/tmp/yuwen-current-verification-jabchnn0/visual-standing-final/`，渲染日志 `visual-standing-final.log` 退出码 0。此检查无真人摄像头输入；真人表情、录像和上传仍待现场验证。Git 身份未提供，未创建 commit/remote/push。

## 统一 Camera 与独立 Facial

旧 Play 自动启动与 Start face camera 流程已替代；上面旧验证日志仅记录当时结果。当前 Python 表情测试 15 项及 starter 11 项重新通过，模型 SHA256 验证通过；模拟测试确认一份摄像头帧共享身体/脸部，缺失脸模型时身体 packet 继续。Facial 动态开启才加载模型，关闭释放；多人时显示单人限制，身体仍可用。Unity 最终隔离验证通过，日志在 `/private/tmp/yuwen-camera-lifecycle-fwmqg7zr/`：`camera-lifecycle-final.log` 为 CAMERA_LIFECYCLE_OK，`fruit-regression.log` 为 FRUIT_VALIDATION_OK，`base-regression.log` 为 DIGIPHANT_VALIDATION_OK 与 DIGIPHANT_LOCOMOTION_VALIDATION_OK，三项均退出码 0。Camera 生命周期验证还包含真实程序化站立 90 帧后停止 Camera，整个降低过程移动小于 0.002 单位，完全降低后恢复行走。该验证没有打开真实摄像头。主 Editor 新界面目视受 Computer Use 的 cgWindowNotFound / noWindowsAvailable 间歇错误限制，未成功 Play 确认新界面；真人摄像头未测试。用户要求不 Git 提交，本轮没有 commit/remote/push。
