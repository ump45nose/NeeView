# P5 第十五批：PDF 密码静默验收

2026-10-06；起点 `901603d6f`，Windows源码基线 `c5c398d89`。仅隔离合成夹具，未激活正式窗口、Finder/Windows，也未修改用户图片、Profile 或 NAS。

- 原口令状态、精确逻辑路径、进程AES缓存、错误后重试、取消及迟到文本拒绝通过。根/ZIP内PDF、失败父链释放及后台无弹窗共用唯一来源。
- 正式拥有弹窗的遮罩、非空、保留空白、Enter/Escape、切书及关闭用例通过。新增关闭用例发现代次失效后IsLoading未清除，关闭方清除后59相关专项通过。
- 最终 **1298通过、0失败、2资源跳过，总计1300**；本批新增8项。
- **10项macOS原生后台测试通过**；实际加密PDF的错误→正确、索引/目录/像素/PNG、缓存、嵌套/迟到取消及JSON不含口令通过。未创建原生窗口。
- Engine、正式Library/default ARM64 `.app`、strict/deep本地ad-hoc签名通过。产品构建0警告；官方测试引导仍有既有generic apphost/bundle警告。

本轮失败及修复原样归档：第一次全量关闭状态断言失败；第二次全量已通过，但手动原生专项相对XML路径在测试bundle根写入报告，导致随后签名失败。移走本轮生成目录、默认目录串行重建并以绝对报告路径复验后通过，未更换构建输出目录。已通过的Engine全量不重复执行。[步骤记录](p5-pdf-password-validation.json)保留失败/恢复步骤；TRX/XML和重复材料位于 `/Users/yuwk/.codex/artifacts/neeview/p5-pdf-password-20261006`。

[正式弹窗Headless截图](p5-pdf-password-dialog.png)由主线程视觉检查，文字/输入/按钮未裁切。[契约](../docs/p5-pdf-password.md)、[证据](p5-pdf-password-evidence.json)。缓存成功是实际后端解锁/索引成功，不是Book事务或显示完成；后台自动递归不弹窗，未解锁子PDF保留为候选，明确打开才能输入。

压缩密码、效果/媒体/脚本、完整原设置、真实两分支导出及Developer ID/公证/安装继续待迁/待验。P3/P4用户跳过项与AX/NAS已知问题保持。P5整体未完成。
