# P5 第五批：原配置与命令差分静默验收

日期：2026-10-06。Windows 源码固定基线 `c5c398d89` / build4340；实现范围见[契约](../docs/p5-difference-settings.md)。本批没有前端变化，没有启动或激活桌面应用，没有修改用户图片、Windows Profile 或当前应用数据。

## 校验结果

`python3 portable/scripts/validate.py --dotnet /Users/yuwk/.local/share/neeview-dotnet/dotnet --phase p5-difference-settings --macos-source --macos` 完整执行通过。

- 全量 1055 通过、0 失败、2 资源用例跳过，共 1057；新增 51 项差分回归。
- Engine、正式 macOS Library 源码检查和默认输出目录 ARM64 `.app` 构建通过。
- 正式 `.app` strict/deep 本地 ad-hoc 签名通过；这不是 Developer ID 分发、公证或安装验收。
- 边界检查为三个生产项目、72 个原源码文件、263 个局部适配、26 个原库源码指纹。
- 两轮独立只读复核完成；复核没有运行测试，不替代实际测试结果。

机器结果：[validation](p5-difference-settings-validation.json)；隔离合成 Profile 的读取、差分写出与恢复：[evidence](p5-difference-settings-evidence.json)。完整 TRX、日志及其他模块的重复生成截图/JSON 归档到 `/Users/yuwk/.codex/artifacts/neeview/p5-difference-settings-20261006`，历史验收证据恢复原版本；用户未跟踪的 `.DS_Store` 保留。

## 行为与修复

迁入原 `DiffJsonConverter`，五文件事务写出候选副本，默认比较使用该候选配置，避免借用尚未提交的 `Config.Current`。已迁字段省略原默认值，未知字段及未迁 Windows 配置分支保留；四种列表模板分别比较自身默认，关闭面板的 `SelectedItem:null` 和完整布局快照保留。

命令按输入方案/方向恢复默认后再裁剪快捷键、鼠标手势；空字符串解绑仍写出。共享参数由 owner 保存，缺失或 null owner 可从早期 Mac alias 恢复，非空 owner 优先；未来 `$type` 保持原材料。九数字命令采用各自 Index1–9，显式 Index0 保留。原触摸/通知默认元数据迁入，触摸默认不随方向交换；本批不增加触摸执行能力。

旧滚动 setter 会转换现代字段，不能只删除旧 JSON：先写实际 getter 结果，再将旧字段归档至 `MacImportedLegacyParameterFields`。旧自动隐藏读取别名同样退役到兼容材料，避免设置恢复默认后重载时复活。保存失败、重试、未知字段大小写和参数 `$type` 命名/顺序均有回归。

## 验收边界

已迁配置和命令参数的差分完成，不表示原完整 schema、所有设置页面或全部命令已经迁完。两分支用户真实导出、Windows 动态、真机、旧效果/预设、附属文件及高级能力仍待分批验证。P5 整体未完成。

P3/P4 的完整浮窗菜单/停靠及屏幕 P95 维持用户要求的跳过记录。AX 长期资源与 NAS 原生 open 阻塞仍未关闭。本轮没有重启这些验收，也没有推送以外的发布动作；实际提交/推送在交付时单独报告。
