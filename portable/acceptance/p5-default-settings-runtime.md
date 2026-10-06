# P5 第二十三批静默验收

2026-10-06，起始43c5b9b4b，固定Windows基线c5c398d89。

- 原默认十二字段复制、递归重收集与文件权限两入口接入，235实例保持；192入口/43占位不等于功能覆盖率。
- 11新增/专项、1436全量通过，0失败，2资源跳过（总1438）。Engine、正式Library、默认ARM64.app及strict/deep ad-hoc签名通过，产品0警告。
- 原设置事件订阅的历史请求经完整链路复核，实际变化解除移除抑制，相同值保持；保存失败恢复完整计数/登记/抑制。递归来源失败保留旧书，新打开优先和菜单On/Off语义分别回归。
- 初轮全量仅旧命令计数快照189→191未更新导致1失败，修正后完整复跑通过；初轮日志保留本机，不计为通过。
- 本批无后端改动，未重复原14原生测试；不继承为新设备/Windows验收。正式Headless无产品激活或用户数据改动，派生截图仅留本机。
- 真实导出、设备/Windows、P5剩余功能与正式分发继续待迁/待验，P5未完成。

[契约](../docs/p5-default-settings.md)、[完整校验](p5-default-settings-validation.json)、[证据](p5-default-settings-evidence.json)、[命令登记](p5-default-settings-commands.json)。
