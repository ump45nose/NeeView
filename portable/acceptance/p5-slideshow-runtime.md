# P5 第十八批：原幻灯播放静默验收

2026-10-06，起点 `1ae112e37`，固定Windows源码基线 `c5c398d89`。原定时/输入/首周期EOS等待、页尾/过渡覆盖、自动滚动、顶部4 DIP进度及启动选项进入唯一BookOperation/JSON；表单结构、中文草稿和原业务分开。所有夹具隔离合成，不激活正式窗口、不操作用户图片/Profile/Windows/NAS。

最终 **1358通过、0失败、2资源跳过，总计1360**；18项本节点专项，含原AnimationTests共29相关专项通过。**14项官方macOS原生后台测试通过**。Engine、正式Library、默认ARM64 `.app` 和strict/deep本地ad-hoc签名通过，产品构建0警告，不等于Developer ID分发或真机屏幕帧率验收。

目录/ZIP实际定时导航、暂停、进度显隐、自动滚动、原页尾动作、EOS两循环模式/暂停/旧播放器、计时补偿/晚到命令与单个在途、默认差分/旧别名退役/未知字段及设置失败回滚通过。退出保存失败后恢复同一播放及自动滚动，重试关闭后工厂归零。主线程检查[正文](p5-slideshow-reader.png)、[播放中的计时条](p5-slideshow-progress.png)和[中文设置](p5-slideshow-settings.png)；实际计时条像素是原Control.Accent，未改正文布局。

首次全量因旧右击菜单测试仍把ToggleSlideShow视为禁用占位失败；已更新已迁能力并保留ExportBackup禁用覆盖，第二次完整校验全部通过。初次失败、最终TRX/XML及外围截图归档于 `/Users/yuwk/.codex/artifacts/neeview/p5-slideshow-20261006`，不覆盖历史失败记录。薄计时条迁入原SimpleProgressBar比例绘制，已修复初次Avalonia.Rect类型引用编译错误。

独立核验提出命令名和进度疑点后，核对原CreateCommandName确为NextPage，原MainView每次Played按补偿Interval从1到0，两者保持；canExecute=false时原MoveNext也递增周期计数，迁移保持。EOS回调改为下一UI回报执行（≤40ms调度间隔），损坏时长容错有明确[契约](../docs/p5-slideshow.md)，不将这些适配表述为逐指令运行等价。

[构建记录](p5-slideshow-validation.json)与[证据](p5-slideshow-evidence.json)。当前172入口/63占位不等于功能覆盖率。视频媒体书限制、效果/预设、脚本、完整设置、真实两分支导出、设备/长期内存及正式分发继续推进；P3/P4用户跳过项和AX/NAS问题保留，P5整体未完成。
