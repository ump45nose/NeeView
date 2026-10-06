# P5 第二十一批静默验收

2026-10-06，起始8b286e3ab，Windows基线c5c398d89。

- 7个原窗口/导航入口迁入，235原实例保留；182入口/53占位不代表功能覆盖率。
- 11新增/23相关专项、1391全量通过，0失败、2资源跳过（总1393）；14实际macOS原生后台测试通过。
- Engine、正式Library、默认ARM64 .app与strict/deep ad-hoc签名通过；产品构建0警告。原生测试宿主保留已知apphost PublishFolderType SDK警告。
- 正式XAML Headless截图已检查，显示区域、滑条显隐及正文不刷新有回归；合成夹具，无用户文件改动、无前台应用激活。
- 首轮两个xUnit取消令牌检查失败，已修正后专项/全量通过；原日志保留本机。
- 原生窗口状态/Finder、Windows动态、长期资源和正式分发另验，P5未完成。

[完整验证](p5-original-commands-validation.json)、[证据](p5-original-commands-evidence.json)、[布局](p5-original-commands-layout.png)、[契约](../docs/p5-original-commands.md)。
