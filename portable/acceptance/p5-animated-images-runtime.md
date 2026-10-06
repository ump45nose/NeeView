# P5 第十七批：原动图静默验收

2026-10-06，起点 `e4e3501e9`，固定Windows源码基线 `c5c398d89`。只读原源码对照，全部运行夹具为隔离合成材料，不激活用户正式窗口。

原GIF/WebP帧合成、官方ImageIO APNG、播放/暂停/独立秒数参数/定位/循环、底部媒体条与原Image/Archive.Media JSON进入唯一阅读链。动图与视频循环字段分别保存，表单为独立草稿。分页及原全景播放；连续/瀑布和缩略图使用首帧。

最终 **1340通过、0失败、2资源跳过，总计1342**；其中22项动画专项通过。**14项官方macOS原生后台测试通过**，包括APNG合成、Background/Previous/偏移/预乘/时长/随机读/关闭。Engine、正式Library、默认ARM64 `.app` 和strict/deep本地ad-hoc签名通过，产品构建0警告，不等于Developer ID分发。

目录和ZIP经原Book/Page/BitmapFactory与正式ReaderView验证真实红/绿像素、自动推进、暂停和seek、模式切换、静态页及晚到关闭归零。原默认差分/重启/未知字段、原前后独立Delta、设置取消/保存失败回滚/重试、首帧取消后重提和不能中断的原生帧关闭分别回归。主线程已检查[正文](p5-animated-images-reader.png)和[设置](p5-animated-images-settings.png)截图；Headless截图不代表真实屏幕播放帧率。

校验修复记录：一处测试Vector类型冲突、旧命令总数断言已修复；列表模板最终封面断言在全量失败、单项重跑通过，改为等待当前有效视口的真实封面并回报错误/10秒超时，最终全量通过。初次失败和最终TRX/XML均归档在 `/Users/yuwk/.codex/artifacts/neeview/p5-animated-images-20261006`，不由重跑抹除失败记录。

[契约](../docs/p5-animated-images.md)、[构建步骤](p5-animated-images-validation.json)、[证据](p5-animated-images-evidence.json)。当前171入口/64占位，数量不等于功能覆盖率。原媒体结束驱动幻灯/自动换书、视频、效果/预设、脚本、完整设置、真实导出、长期原生内存及正式分发继续迁移/验收。P3/P4用户跳过项与AX/NAS已知问题保持，P5整体未完成。
