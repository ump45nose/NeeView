# P5 第二十四批：版本窗口静默验收

2026-10-06，固定 Windows 源码基线 c5c398d89。原版本布局/ICO、实际 Engine 构建版本、复制文本/链接、单附属窗口及 macOS 关于入口已接入。

- 6 项专项、最终 1442 项全量通过，0 失败，2 项需显式资源目录的测试跳过。
- 18 项真实 macOS 后台测试通过，其中4项新增链接拒绝/取消/缺许可测试；不打开网页或产品窗口。
- Engine、正式 Library、默认目录 ARM64 .app 构建及 strict/deep 本地 ad-hoc 签名通过，产品0警告。
- 实际 .app/Contents/Resources/LICENSE.md 与仓库原文 SHA256 相同：ec932513a904c9f037cb580d9fef47ea50a29390e36144889eea10f60eddb8d1。
- 正式 Headless 主菜单/附属窗口、关闭与内存剪贴板写入/读取回归通过；[版本窗口截图](p5-version-window-layout.png)已检查。

第一次全量已通过，但原生测试构建两处缺 TestContext 取消令牌，修正后完整串行复跑通过。原失败及最终日志保留在 /Users/yuwk/.codex/artifacts/neeview/；其余派生全量截图移至该目录，不重复写入仓库。

原235命令当前193执行入口、42禁用占位，数量不代表功能覆盖率。原 Windows 更新源不适用于独立Mac，更新检查/更改记录明确占位。未激活正式应用、改用户图片/Profile或实际系统剪贴板；浏览器/Finder链接、Windows动态及真实导出另验。Developer ID/公证/干净安装未执行；P5整体尚未完成，P3/P4跳过及已知问题保持。
