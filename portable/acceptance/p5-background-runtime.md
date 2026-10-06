# P5 第二十二批静默验收

2026-10-06，起始4e36f336a，固定Windows基线c5c398d89。

- 8个原背景/像素保持入口接入，235原实例保持；190入口/45占位不代表功能覆盖率。
- 34新增/专项通过；1425全量通过，0失败，2资源跳过（总1427）；14实际macOS原生后台测试通过。
- Engine、正式Library、默认ARM64 .app及strict/deep ad-hoc签名通过，产品0警告；原生测试宿主保留已知SDK apphost PublishFolderType警告。
- 正式XAML/Skia像素、透明首像素/显示预乘、四图像刷、棋盘、nearest矩阵、晚到释放、原JSON/未知字段/阈值极值、设置取消/保存重试/搜索已回归，背景不重解码正文。设置截图已检查。
- 初轮帧引用和计数断言失败：帧可因视口绘制重建，侧栏200ms缩略任务独立；修正为原页范围/对象及正文解码、显示创建/通知的实际边界。自动取色实质差异已修复并补半透明样本。原日志保留本机，不计初轮通过。
- 合成隔离夹具，无用户文件改动、无产品窗口激活。修复上批意外覆盖的p2-autohide历史截图，恢复第二十批文件；历史证据不使用本批生成结果代替。
- Retina/Windows动态/长期资源、真实导出、P5剩余功能和Developer ID分发另验，P5未完成。

[完整验证](p5-background-validation.json)、[证据](p5-background-evidence.json)、[表单](p5-background-form.png)、[契约](../docs/p5-background.md)。
