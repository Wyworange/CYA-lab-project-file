# 角色认领（方式二：做动作认领）

三人同一摄像头：**Yuwen = Face（起身、摘水果）**，**Aurora = Hands（抓木头）**，**Chunzi = Upper body（走路、转向、头、耳朵、尾巴、喝水）**。

## 使用
1. 主场景 `Assets/DigiPhant/Scenes/DigiPhant.unity`，Play，选人数（测试 2、正式 3），**Seated / upper body**，点 **Camera**，等每个 Performer 显示 visible。
2. 在 **Full body / Seated / upper body** 旁选中要用的模式：**Facial**（Face 角色）和/或 **Hands**（Hands 角色，开启后才识别手势、才启用 Aurora 的抓取），三者可同时选中。
   然后在 **ROLES** 区点 **Assign roles (…)**：按钮会显示本次要认领的步骤；只认领已选中的角色，Upper body 始终是剩下的人。
3. 提示 "Face: open your mouth"：**只有 Yuwen** 张大嘴保持约 0.6 秒。
4. 提示 "Hands: raise both hands"：**只有 Aurora** 双手举过头顶保持约 0.6 秒。（Face 认领完成后，表情立即开始跟随 Yuwen，2 秒中立表情校准可与此步同时进行。）
5. 剩下的人自动成为 Upper body。**只有 2 人测试时**，Face 的人（没有 Face 时为 Hands 的人）同时控制上身（显示 "(test)"）；ROLES 区显示 "Walking & steering: P? · 名字"。
   认领完成后自动开始 10 秒中立姿势倒计时。
6. 所有人保持中立姿势点 **Set neutral pose (10 seconds)**；Yuwen 保持中立表情约 2 秒完成表情校准。
7. 每个 Performer 行后显示其角色。需要重来点 **Reassign roles**；**Clear roles** 恢复原映射。

同时有两个人做认领动作会被视为不明确，计时重新开始。

## 固定分区（Fixed zones，默认开启）
多人同框时原来的多人姿势模型很少同时看到所有人（三人测试日志：129 秒内同时看到 3 人 0 秒），编号来回互换。开启 **Fixed zones** 后，画面按人数**等宽竖条**切分（预览不镜像，P1 = 预览最左），每条只识别一个人，编号 = 所在竖条，不会互换。预览上画出白色分区线，底部显示 `P1`（绿=已识别，红 `empty`=未识别），顶部显示 `Fixed zones: k/N detected`。
- 每人必须待在自己的竖条里；手臂超出分界线的部分不会被识别。
- 只有身体中心（双肩中点，看不到时用鼻子）落在本条内的人才算本条的人：探身过来的邻座或骑在分界线上的人不会占用别人的编号。
- 分区识别出错时临时回到原多人识别，2 秒后自动重试（追踪程序输出里会写明）。
- 三人座位：左 Chunzi（P1）· 中 Yuwen（P2）· 右 Aurora（P3）。
- 切换开关会重新编号并清除角色与校准。
- 实现：`tools/fruit_tracking/zones.py`（每条一个 num_poses=1 的 PoseLandmarker，坐标映射回全画面），`fruit_bridge.py` 钩子替换识别、编号与预览绘制；Unity 在每 0.5 秒的配置里发送 `zones`。三条识别约 18 ms/帧（空画面）。

## 认领后
- 表情只读 Face 那个人的脸（其他人的脸被忽略）；不再要求画面只有一个人。
- Aurora 的 `DigiPhantHandGrab.performerId` 自动设为 Hands 的编号。
- 所有身体控制与走路/转向输入改为 Upper body 的编号；Clear roles 时恢复。

## 何时会清除角色
编号可能改变时：**Reassign people**、改人数、切换 Full body / Seated、停止 Camera、预览的 **Retry**、关闭 Facial（认领中）。清除角色时也会清除身体校准，需重新 Set neutral pose。编号按位置跟踪，交叉走位会导致编号互换。

## 实现
- `Runtime/RoleAssignment.cs`：认领流程、映射、面板（由 `DigiPhantController` 在 Play 时自动添加，无需改场景）。
- `DigiPhantController.TryReadRaw`：未校准的举手高度（认领发生在校准前）。
- `FruitInteraction`：`SetFaceClaim` / `SetFaceSlot`，每秒随表情配置发送给追踪程序。
- `tools/fruit_tracking/fruit_actions.py`：`match_faces` 按"脸的鼻尖最接近哪个人的鼻子（0.6 倍肩宽内）"把脸归属到编号；认领时报告每人张嘴值 `slotJaw`；指定 `faceSlot` 后只用那张脸。追踪钩子移到编号分配之后。

## 可调参数（RoleAssignment 组件）
`holdSeconds` 0.6 · `mouthOpenThreshold` 0.45 · `handsRaisedUpperBody` 1.2（Seated，按肩宽换算）· `handsRaisedFullBody` 0.6（Full body，按躯干长度）· `dropoutGrace` 0.25 秒（认领中短暂丢失追踪不重新计时）· 三人名字。

认领开始时会清除身体校准，避免举手认领时大象被带着走。表情在识别编号短暂丢失（≤0.3 秒）时按上一帧鼻子位置继续跟随同一张脸。

## 测试状态
Python 24 + 15 项通过（另含：编号短暂丢失时仍跟随同一张脸、faceSlot 命令严格校验）；Unity 运行时与编辑器程序集用 Unity 自带编译器编译 0 错误（含：认领按编号报告张嘴、多人时只用 Face 那张脸、远离任何人头部的脸不归属）。Unity 端与真人摄像头尚未验证；三人同框时脸较小，眨眼可能需要重新测试。

## 统一动作锁与 Chunzi 喝水（2026-10-09 合并）
来源：ciciii111/Chunzi-DigiPhant-Drinking `Chunzi/Latest_2026-10-09`（SHA256 全部一致，无 GUID / 类名冲突）。导入到 `Assets/StudentWork/Drinking/`，文件未改动。合并前备份：`Backups/2026-10-09_before_chunzi_merge/`。
- `Runtime/ElephantActionLock.cs`：团队唯一动作锁，实现她的 `IDrinkingActionGate`；由 `DigiPhantController` 在 Play 时自动添加，并在运行时接好 `DrinkingInteraction`（她的 Assign existing rig、`actionGate`、`soloTesting = false`），水池引用场景已有的 **02 Water basin**（与她指定位置 (9.557, 0.01, -21) 完全相同，因此不放她的 WaterPool.prefab，避免重叠）。**不修改场景文件。**
- 互斥：摘水果进行中、扛着木头或左手握拳伸鼻时不能喝水；喝水时不能起身/摘水果（提示 "Chunzi is drinking…"，张嘴触发也会被拦），Aurora 的抓取暂停。
- 喝水期间：冻结前进/转向与身体手势（`actionPoseOwned` + `actionMovementPaused`），由她的模块固定根节点与骨骼。
- 按她的要求：Chunzi 双手都高于休息高度 0.10（正在累计 2 秒喝水或等待放手）时，当帧停止前进/转向——在控制器之前（执行顺序 -100）直接读取，避免晚一帧。只举左手照常前进。
- 角色认领后 `DrinkingInteraction.performer` = Upper body 编号（默认 P1 = Chunzi），清除角色时恢复。
- 她的面板在右下角："Debug: drink"、"Cancel drinking"、"Solo preview"（三人联调时保持关闭）。
- 注意：角色认领的 Hands 步骤（双手举过头顶）进行时，Chunzi 不要举双手，否则会被认成 Hands。
