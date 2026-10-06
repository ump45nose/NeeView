# P5 第十四批：PDF 扩展名静默验收

2026-10-06；起点 `517ccfeec`，Windows源码基线 `c5c398d89`。使用隔离合成夹具；未启动正式界面、Finder或Windows，未修改用户图片、Profile或NAS。

- 原扩展名集合/引号解析、默认/空集合/未知字段/差分保存、实际目录书架及嵌套/历史识别通过。
- 压缩/播放列表优先；PDF配置为图片后缀后，其内部虚拟PNG仍保持原Page身份。三收集模式的显式页/Exists/条目拒绝用例通过，不再误开嵌套PDF。
- 原源码构造/Restore未规范ValidateItem的缺陷明确记录并修复；大小写、缺点号、引号内分号、重复和批量操作覆盖。差分序列化修复字符串converter被当成对象的问题。
- 79项PDF/差分专项通过。最终 **1290通过、0失败、2资源跳过，总计1292**，本批新增10项。
- **7项macOS原生后台测试通过**，包括真实PDF的自定义扩展名根/ZIP内打开与像素；未创建窗口。
- Engine、正式Library源码检查、默认ARM64 `.app`、strict/deep本地ad-hoc签名通过。仅官方测试引导有一个generic apphost/bundle警告，产品构建0警告。

[正式设置Headless截图](p5-pdf-filetypes-settings.png)已由主线程视觉检查；新增表单没有裁切，左右导航和原草稿/取消结构保持。[验证](p5-pdf-filetypes-validation.json)、[证据](p5-pdf-filetypes-evidence.json)。重复截图/JSON和TRX/native XML归档于 `/Users/yuwk/.codex/artifacts/neeview/p5-pdf-filetypes-20261006/final-full`。

密码交互、效果/媒体/脚本、完整原设置、真实两分支导出及Developer ID/公证/安装仍待迁/待验；不继承本轮测试为真机、Windows对照或长期内存验收。P3/P4跳过项与AX/NAS已知问题保持。P5整体未完成。
