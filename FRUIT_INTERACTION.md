# 大象采水果 · 使用与接入记录

## 原 DigiPhant 场景中的扩展

打开原场景 **`Assets/DigiPhant/Scenes/DigiPhant.unity`** 并按 Play。水果互动直接附加到已有 DigiPhant Controls 与 Locomotion；保留原控制器、动作映射、Savannah 场景、镜头和输入配置。使用 **DigiPhant → Fruit → Integrate Actions Into DigiPhant Scene** 可幂等地恢复场景中的水果组件与站点。新增水果 mesh/material/prefab 仍位于 `Assets/StudentWork/FruitInteraction`。

站点使用课程内已有的 Feeding bush，树枝高 4.55、水果高 4.15，位于鼻子可触及范围。后腿用解析 IK 保持支撑脚掌，抬起时临时启用 skinned renderer 矩阵重算与离屏更新；这是程序化动作，不是物理平衡模拟。

按用户选择，起身、直立保持、采摘及降低过程中固定移动与转向，完全降低后恢复原四脚行走与转向；站立期间仍可使用头/鼻手势。后腿 IK 用于原地支撑，不提供后腿站立行走步态。

## 正面采摘站点与传送门

- **正面朝向水果**：采摘站点由 `FruitInteraction.ComputeStation()` 运行时计算。大象垂直于树枝、正面面对水果，站在跟随相机一侧（灌木前方），背对相机朝向水果。
- **不重叠**：计算时烘焙大象网格，检查四脚姿态和起身弧线（0、1/3、2/3、全角度），确保身体与苹果、香蕉、树枝、支撑杆和 Feeding bush 的包围盒至少保持 **Station Clearance**（默认 0.25）的距离。鼻子顶点只需避开灌木（摘水果时鼻子要伸到树枝下方够水果）。从正中开始向两侧尝试，取第一个鼻子够得到的位置；都够不到时取最接近的一个。若起身全程不碰水果/树枝时够不到，则放宽为仅要求起身前与站直后不碰（起身途中头部可短暂经过水果附近），灌木始终避开；Console 打印 FRUIT_STATION 说明所用模式、横移和差距；若仍超出鼻长，状态栏显示 "beyond trunk reach"。
- **传送门（传送门）**：Play 时在站点后方约两步（**Portal Distance** 3.5，**Portal Radius** 1.2）生成青色圆环。任一只脚或身体中心踩进圆环（任意方向均可）会把大象直接传送到正面站点，并暂停移动 1.5 秒，防止举手继续前进穿过灌木。传送不会重置已摘水果。大象在站点或动作进行中时圆环隐藏；离开后才能再次触发。**Show Portal** 可关闭。传送门是运行时对象，不保存到场景，原场景和水果场景都可用。
- **Debug: move to feeding bush** 使用同一站点，但会先 Reset 动作和水果。站点只在 Play 开始时大象静止站立的姿态下计算一次并缓存；传送门和按钮共用，不会在走路中途重新计算（走路姿态会导致朝向和距离偏差）。修改 Station Clearance 等参数需重新 Play。

## 先用按钮测试

1. 打开原 DigiPhant 场景并 Play，点击 **Debug: move to feeding bush**，或把大象走进青色传送门，到正面采摘站点。
2. 点击 **Debug: rear onto hind feet**，观察前腿收起、后脚保持着地。起身约 1.2 秒。
3. 点击 **Pick apple** 或 **Pick banana**。起身中选择会排队，完全起身后执行；鼻子伸向水果、抓住、抬起并返回，已摘水果隐藏。
4. 点击 **Lower (keep picked fruit)** 恢复四脚姿态，已摘水果不会重新出现。
5. 点击 **Reset (replenish both fruits)** 取消动作并补充两种水果，再测试另一种。表情模式 Reset 后先闭嘴、睁眼回到中立状态，再张嘴；持续张嘴不会立即重新触发起身。离树枝太远会拒绝采摘并显示状态；先降低、靠近再起身。

## 稍后测试真人表情

先手动选择 **1 person**，再选 **Full body / Seated / upper body**。**Facial** 是身体模式旁独立的开关，可与 Seated 同时勾选。勾选 Facial 后，点击左侧 **Camera** 统一启动同一个桥接、身体追踪与预览；Play、Test sliders 和单独切换 Facial 都不启动摄像头。旧 Start face camera 流程已被替代。

脸和头肩保持入镜，眼睛睁开、嘴闭合，表情自动中立校准约 2 秒且至少 10 帧，需要重做时点击 **Calibrate neutral face**。身体移动仍使用原 **Set neutral pose (10 seconds)**，两种校准分开。Facial 不改变人数、身体模式、身体映射或身体校准；表情仅支持单人，多人时显示提示并暂停表情，身体追踪仍可用。

关闭 Facial 会取消表情动作、降低并释放 FaceLandmarker，身体 Camera 会话与预览继续。切回 **Test sliders** 或停止 Play 则关闭 Unity 自己启动的桥接和预览；独立启动的进程不由 Unity 关闭。再次 Camera 需重新确认身体校准状态；重新 Facial 后需中立表情校准。

- 张嘴：大象起身；起身后保持张嘴再选择水果。
- **本人解剖学右眼**单眼眨眼：苹果；**本人左眼**单眼眨眼：香蕉。不是画面左右；预览不镜像。
- 两眼同时闭合：忽略，不选择水果；起身过程中的有效选择排队到站稳后。
- 眨眼判定（改用眼睑测量）：用 face mesh 眼睑点计算每只眼的张开度，相对中立表情比较（张嘴不会影响）。闭眼的一侧张开度 < 40%、另一侧 ≥ 45%，在 0.6 秒内累计 ≥ 0.06 秒（至少 2 帧）即触发；眨眼中途两眼短暂同时闭合不会取消。两眼同时闭合的普通眨眼忽略；下次眨眼前两眼需回到 > 60% 张开。按录制的真人数据回放，8 次眨眼全部识别、方向正确、无误触发。水果面板 L/R 显示相对中立的闭合度。
- 闭嘴：没有采摘时降低；采摘已开始时先完成采摘再降低。
- 脸丢失或超时：取消采摘、清空待执行选择并降低，不继续无人控制动作。恢复取景后看状态，必要时重新校准。

关闭左侧 **Facial** 退出表情动作模式，再用按钮检查；停止 Play 关闭 Unity 启动的进程。真人表情尚未验证，初次测试先站点调试、再表情校准，最后分别测试两眼、双眼、丢失恢复。

## 实际数据路径与恢复

Camera 启动一份摄像头捕获：同一 webcam frame → wrapper 复用固定 starter 的 PoseLandmarker 与 JPEG 预览；Facial 开启时再延迟加载 FaceLandmarker blendshapes → 本机 UDP → Unity 身体控制和水果动作组件。关闭 Facial 释放脸部模型，不关闭身体追踪。表情开关命令 `fruitEnabled` 从 5058 发到 5056，未开表情时也有 enabled/error 心跳；缺失/损坏模型或表情推理失败只报表情错误，身体数据继续。关闭再开启 Facial 可重试。原端口保留：5055 身体数据/Unity 配置来源，5056 Python 命令接收，5057 JPEG 预览；新增 **5058** 接收表情动作，也从此端口向 5056 发送表情校准 reset。每组端口只运行一套桥接。

追踪扩展位于 `tools/fruit_tracking`：`fruit_bridge.py`、`fruit_actions.py`、`test_fruit_actions.py`、`setup_face_model.py`。wrapper 在内存中挂接固定 bridge，SHA256 防护会在 starter 源码改变时停止并要求复查，不修改 starter 源文件。升级 starter 时不可直接绕过校验。

重建学生仓库后，先按 DIGIPHANT_SETUP 恢复共享 Elephant 和 Python 环境，再运行：

```sh
python3 tools/fruit_tracking/setup_face_model.py
DigiPhantStarter/Tracking/.venv/bin/python -m unittest discover -s tools/fruit_tracking -p 'test_*.py'
```

下载官方 FaceLandmarker 到 `DigiPhantStarter/Tracking/face_landmarker.task`，SHA256 为 `64184e229b263107bc2b804c6625db1341ff2bb731874b0bcc2fe6544e0bc9ff`。文件已有且不同或下载校验失败时工具停止，不覆盖已有模型。模型与环境仍排除于学生 Git；重建需联网下载。

## 历史验证与限制（旧启动流程）

本次重新执行 8 项表情 Python 测试与原 11 项测试，全部通过；face model checksum 与 wrapper 使用的原始源码字节指纹已验证，不修改固定 starter 源码。Unity 映射验证改为序列化包裹数组的对象，确保比较实际骨骼控制字段。项目虚拟环境通过离线 ensurepip 安装 pip 26.2.1，现有 20 个 distributions；MediaPipe 0.10.35 与 opencv-contrib-python 4.14.0.94 不变，`pip check` 返回 **No broken requirements found**。

此前隔离 Unity 的水果 RunAll、原控制器与 locomotion、Savannah 检查均退出码 0，日志位于 `/private/tmp/yuwen-current-verification-jabchnn0/`：`fruit-current.log`、`base-current.log`、`savannah-current.log`。按用户选择修正后的 `fruit-standing-final.log` 与 `base-standing-final.log` 也 PASS、退出码 0，覆盖整个站立/采摘/降低期间移动转向固定、站立头/鼻控制、完全降低后正常行走转向、Camera 与 Test 两种输入、身体校准/人数/模式/骨骼与移动映射保留、预览所有权及重复清理、两种水果鼻尖接触/抬起/Reset。

主场景序列化相机仍为 FOV 38。水果站立时临时使用至少 FOV 50，降低、Reset 或禁用组件时恢复原相机快照；没有改原场景镜头配置。修正后的静态图由主 agent 与 Builder 核验：站立时头、象牙、鼻子和脚完整入镜，脚掌贴地；完全降低后恢复原 FOV 38 的正常步态取景。PNG 位于 `/private/tmp/yuwen-current-verification-jabchnn0/visual-standing-final/`（03 为站立，08 为恢复正常步态），`visual-standing-final.log` 渲染退出码 0。站立临时 FOV 为 50；这是无真人摄像头的静态渲染。

真人摄像头、多人追踪及实际 Game view 录制仍未验证。新界面在主 Editor 的现场目视检查未完成（Computer Use 间歇返回 cgWindowNotFound / noWindowsAvailable，未成功在主场景 Play 确认新界面）。首次表情测试请选单人并确认取景。动作仍是程序化姿势，不是物理平衡。

录制沿用原 **Record performance / Stop recording and save**，MP4 在 `Recordings/<session>/performance.mp4`；确认可播放后上传[指定 Drive 文件夹](https://drive.google.com/drive/folders/1pMZViKVTm0vaFiKc1a0N4PM1z5oxSGb6?usp=drive_link)。视频不入 Git。无 Git 作者身份，未创建 commit 或 remote/push。

## Camera / Facial 新流程复核

以上 Camera 统一启动与独立 Facial 流程替代早前 Play 自动启动、Start face camera 和表情组件拥有预览的流程；前文旧日志保留为历史验证，不证明新流程。新增 Python 表情测试当前共 15 项（原 8 + 新 7）通过，原 starter 11 项测试重新通过，模型 SHA256 验证通过。新测试含实际 patched main 的模拟单共享摄像头、脸模型缺失仍输出原身体 packet；没有打开真实摄像头。Unity 最终隔离验证通过，日志在 `/private/tmp/yuwen-camera-lifecycle-fwmqg7zr/`：`camera-lifecycle-final.log` 为 CAMERA_LIFECYCLE_OK，`fruit-regression.log` 为 FRUIT_VALIDATION_OK，`base-regression.log` 为 DIGIPHANT_VALIDATION_OK 与 DIGIPHANT_LOCOMOTION_VALIDATION_OK，三项均退出码 0。Camera 生命周期验证还包含真实程序化站立 90 帧后停止 Camera，整个降低过程移动小于 0.002 单位，完全降低后恢复行走。该验证没有打开真实摄像头。
