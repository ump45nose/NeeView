# P5 第三批：旧设置与布局静默验收

2026-10-06；分支 `feature/macos-port`，固定 Windows 参考 `c5c398d89`、build 4340。[契约](../docs/p5-legacy-compatibility.md)；[完整构建/测试输出](p5-legacy-compatibility-validation.json)；[合成来源/升级/正常保存候选](p5-legacy-compatibility-evidence.json)。

## 实际结果

| 层面 | 结果 |
|---|---|
| 全量自动回归 | 967通过，0失败，2资源用例按显式图片目录条件跳过，共969 |
| 导入与布局专项 | 最终全量中150通过；新增旧版本/布局63，原预览37、应用27、浮窗12、停靠11 |
| 正式构建 | Engine、正式Library编译、默认目录ARM64 `.app` 均通过；串行 `-m:1`、锁定依赖 |
| 签名 | strict/deep 本地ad-hoc校验通过，未执行Developer ID、公证、安装或发布 |
| 源码/依赖边界 | 仍三个生产项目；71原源码、255局部适配、26原库源码指纹通过；无Windows文件改动 |
| 真机与Windows | 本批未启动真实应用、未激活窗口、未执行Windows动态对照 |
| 用户数据 | 本批全部合成Profile，写入独立临时目录后清理；未操作用户图片、Profile或NAS |

主验证命令为 `python3 portable/scripts/validate.py --dotnet /Users/yuwk/.local/share/neeview-dotnet/dotnet --phase p5-legacy-compatibility --macos-source --macos`。构建之后仅补充源码出处登记并再次核验全部静态边界/哈希；生产代码没有变更。没有默认输出目录冲突或切换。

完整TRX、完整测试产生的非本批页面/图片与JSON留在 `/Users/yuwk/.codex/artifacts/neeview/p5-legacy-compatibility-20261006`。历史p2图片在复演后从原HEAD恢复，本批仅提交相关候选/验证材料。用户未跟踪 `.DS_Store` 保留。

## 故障与独立复核

初次122项专项中1项测试断言将39版本未转换的数值枚举当作字符串读取，修正为真实枚举反序列化；随后122项通过。补充独立布局复核后147项专项通过；之后新增损坏布局3项在最终全量中通过，最终相关专项为150。

独立复核发现“旧选中面板未知时打开首组”的真实偏差，已按原 `LayoutDockPanelContent.Restore` 修复，并验证未知/null选择及保存。未采纳省略 `SelectedItem:null`：Mac原JSON递归合并需要明确清除旧选择，否则保存会复活旧值，格式区别已记入契约。

移动约束疑点经直接核对固定原 `ViewConfig` 两个空setter排除；保留原 getter/validator 的实际组合，包括 Snap→LockUntilResized。UNC疑点经直接核对原 `UncPathTools` 排除：原实现采用小写共享根的缓存键保留首次拼写，Mac使用OrdinalIgnoreCase字典等效；新增同共享根不同大小写的Books合并测试通过，尾部大小写保持。

## 待验与能力限制

设置38–46.3按原已迁分支转换；38非零字体尺寸缺少来源Windows MessageFontSize时仍只预览。旧效果参数/缓存保留，效果层/预设升级与执行明确未接入。独立附属文件范围仍46.1–46.3，旧目录validator及完整差分、附属文件、高级设置/格式继续后续阶段。

用户两分支真实导出、真机选择器/菜单和Windows动态未从合成Headless继承为通过。P3/P4完整浮窗菜单/停靠、屏幕P95按用户要求跳过；AX长期资源和NAS原生阻塞仍保留。P5整体未完成，正式分发凭据与签名公证尚未使用。

本批经回归后自动本地提交，并推送当前分支；实际提交/推送结果在交付回复单独报告，不把构建通过称为发布通过。
