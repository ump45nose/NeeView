# P5 第三十二批：整视口与空间效果静默验收

2026-10-07，固定 Windows 基线 c5c398d89。六空间效果与官方原 Blur 核补齐，十四类原效果均有实际后端；效果作用域修正为原整阅读区域，View 导出共享，不改源 Copy。

- 89 项效果专项全部通过，包括八组挂载只读实图的裁剪/尺寸 View 导出；原始哈希和修改时间保持，关闭后工厂资源归零。
- 1662 项全量通过，0失败，2项浏览资源用例跳过，共1664项。原生 macOS 后端25项通过；Engine、正式 Library、默认 ARM64 .app 及 strict/deep ad-hoc 签名通过。
- 双页接缝、页底色、透明间隙、Blur 外扩/累计中心、半页/90°旋转、1×/2×几何、原点/opacity、CPU/GPU Auto 两策略、scene-graph 重绘/关闭及失败记录释放均有像素/资源专项。
- 官方 WPF 核的半径1负权保留；原 ShaderEffect Auto 的 CPU nearest/GPU linear 差别由固定官方源码核验。两策略软件表面测试不代表真实 GPU 已验。
- 14 项打包脚本回归通过，纯源码算法依赖登记和许可 SHA 校验纳入分发脚本。正式 .app 的 Licenses/dotnet-wpf.LICENSE.txt 已确认。

[完整构建与测试证据](p5-spatial-effects-validation.json)、[命令登记](p5-spatial-effects-commands.json)和[契约](../docs/p5-spatial-effects.md)分别保存。源/目标显式 BGRA 工作面合计128MiB；驱动、shader、picture及进程总内存尚未据此验收。

派生图像/中间日志留在 /Users/yuwk/.codex/artifacts/neeview/p5-spatial-effects-derived/，用户图片只读、不进入仓库；没有启动或激活正式应用，没有修改用户 Profile。Windows动态、实际GPU边缘、长期性能另验；resize、真正Loupe、视频/脚本及完整设置继续迁移，P5整体未完成。

Release 开发包刷新通过：源码算法许可原文/固定提交已在实际 dependencies.json 核验，所有Mach-O重新签名、ZIP随机解包重定位与strict/deep签名通过；[打包证据](p5-spatial-effects-package.json)。ad-hoc开发包，不等于Developer ID/公证/干净安装通过。
