# P5 第二十八批：原图像效果、预设与几何静默验收

2026-10-07，固定 Windows 源码基线 c5c398d89。本批沿原 EffectUnit/Layer/Cache/Profile、PageCustomSize/PageViewSizeCalculator 和六区域效果侧栏迁移，不增加生产项目、阅读内核或状态体系。

- 最终全量 **1585 项通过、0 失败、2 项浏览资源用例跳过**，共 1587 项；本轮显式选用用户指定挂载目录的一张图片，实图效果和原字节/View 导出均参与全量并通过。
- **25 项无窗口 macOS 后端测试通过，0 失败、0 跳过**。Engine、正式 Library 和默认目录 ARM64 `.app` 编译通过，strict/deep 本地 ad-hoc 签名校验通过。
- 正式应用编译无警告/错误。原生测试宿主仍有 SDK apphost 的 PublishFolderType 警告；不将该警告改成产品构建例外。
- 修复导入预览反射 JsonObject 索引器、默认效果集合差分保存、绘制提交失败租约，以及裁剪运算顺序引起的一像素导出尾差；相关回归进入最终全量。

实际执行四类原自有 shader：Level、Hsv、ColorSelect、Colorize。真实 Skia 像素覆盖透明/半透明、逆层顺序、Colorize表及点变化；原14类参数、多态/未知字段、默认差分、原缓存/六分支预设、草稿隔离和保存失败回滚通过。九种自定义比例、裁剪、DPI及原整个页框网格接入同一布局与查看器。

View 导出共享同一效果/几何绘制；CopyImage 保留原像素。显示及离屏租约、提交失败、scene-graph 重绘/关闭、瀑布重排和切书后的归还测试通过。当前页优先和原字节缓存保持，工作预算超限/坏参数/未迁算法明确失败，不能输出原图冒充效果结果。

用户挂载图片只读，输出及 Profile 在隔离临时目录，结束清理。实际自定义600×900、四边裁剪后导出480×630，启用Hsv后像素改变；源字节和修改时间保持，关闭后工厂计费资源归零。匿名尺寸/哈希见[p5-image-effects-resource.json](p5-image-effects-resource.json)。合成红图变绿的[侧栏截图](p5-image-effects-panel.png)已人工检查；顶部/左右栏/底部原区域及六区面板滚动保持，参数和枚举的完整本地化后续补齐。

普通宿主207执行入口，装配ImportBackup后208入口/27占位；235实例完整登记，数量不代表功能覆盖率。非本批派生截图/日志保留在 `/Users/yuwk/.codex/artifacts/neeview/p5-image-effects-derived/`。

**P5整体未完成。** 另外十类WPF/Expression效果仍只迁参数，启用时明确待迁，View导出拒绝；resize、放大镜、视频、脚本、完整原设置及剩余命令继续推进。Windows动态效果、Retina/长期原生性能、系统选择器、Developer ID、公证和干净安装未执行。本轮没有激活正式应用、抢占焦点、修改用户Profile或原图；没有继承以前设备验收为本批通过。
