# 环境（天空与地面）

## 月球地面（2026-10-13）
- 只更换 **Ground**（外围大地面，`Ochre ground.mat`）和 **Stage**（中间 60×60 场地，`Stage.mat`）的**材质**；位置、大小、网格、Stage 的 Mesh Collider（木头物理）和名字都不变，所以所有交互不受影响。
- 贴图由 `tools/environment/make_moon_texture.py` 生成（无外部素材，可无缝平铺）：`Moon/MoonRegolith_Albedo.png`（灰色月壤、陨石坑、亮坑沿与溅射纹）、`Moon/MoonRegolith_Normal.png`（同一高度场的法线贴图）。改参数后重新运行脚本即可。
- 材质：`Moon/Moon Regolith Ground.mat`（亮度 0.82）、`Moon/Moon Regolith Stage.mat`（亮度 1.0，表演区更清楚），URP Lit，粗糙（Smoothness 0.08），每 4 米重复一次。
- `Editor/MoonGroundSetup.cs`：编译后自动应用一次（每台电脑），Console 出现 `DIGIPHANT_GROUND moon regolith`。菜单：**DigiPhant → Environment → Use Moon Ground / Use Original Ground**。
- 备份：`Backups/2026-10-13_before_moon_ground/`（主场景与两个原材质）。

## 彩虹跑道 + 透明地面（2026-10-13）
- **Open path**（黄色跑道）换成独立的彩虹材质 `Rainbow/Rainbow Track.mat`：七色条纹沿跑道方向延伸（宽度方向红→紫）。原网格没有 UV，所以复制了一份带 UV 的 `Rainbow/Open path rainbow.asset` 给它用；原网格和原 `Pale sand path.mat` 不改（木头两端的 Cut end 也用这个材质）。
- **Ground / Stage**（灰色地面）只关闭 MeshRenderer，看起来完全透明；位置、Stage 的 Mesh Collider（木头落地）不变，所有交互不受影响。注意：地面上的影子也随之消失。
- `Editor/RainbowTrackSetup.cs`：编译后自动应用一次，Console 出现 `DIGIPHANT_TRACK rainbow track`。菜单：**DigiPhant → Environment → Use Rainbow Track (hide ground) / Use Sand Track (show ground)**。
- 备份：`Backups/2026-10-13_before_rainbow_track/`。

## 霓虹发光树 + 暗天空（2026-10-13）
- 18 棵 Acacia 树和水果灌木（01 Feeding bush）换成新材质（`Shaders/NeonTree.shader`）：树干/树枝是淡蓝→淡紫→粉的虹彩条纹，树冠是深蓝底加慢慢闪烁的青/品红/橙色光点。网格、位置、层级都不变，所以坐木头的空间检查和水果站完全不受影响。原来的 Bark / foliage 材质没改（木头 Timber 和终点门柱也在用）。
- 发光：场景加了全局 Volume「Neon glow (Bloom)」（`Neon/Neon Bloom.asset`，阈值 1.6），主摄像机打开 Post Processing；同一个 Volume 关掉 URP 默认的 Tonemapping 和暗角，所以其他物体颜色不变。
- 天空：保持白天的 Above Day A。可选暗天空 `Neon/Neon Dim Sky.mat`（副本，Exposure 0.12、深蓝），菜单 **Use Dark Sky / Use Day Sky** 切换；AllSky 原资源不改。
- Shader 每个 pass 都有 DOTS 变体并共用同一个材质 cbuffer（GPU Resident Drawer 打开时也能正常显示）。
- `Editor/NeonTreesSetup.cs`，菜单 **DigiPhant → Environment → Use Neon Trees (with glow) / Use Original Trees**，Console 出现 `DIGIPHANT_TREES`。
- 备份：`Backups/2026-10-13_before_neon_trees/`。
- 水果传送门（蓝色圆环）抬高到离地 0.07 m，不再被跑道盖住；进入判定只看水平距离，不变。

## 天空盒
- AllSky **Above Day A**（6 面版本）：`Editor/SkyboxSetup.cs`；菜单 **DigiPhant → Environment → Use Above Day A Skybox / Use Original Solid Background**。
- Play 时缓慢旋转（`Runtime/SkyboxRotation.cs`，默认 0.8°/秒），旋转的是运行时复制的材质，不改资源文件。
