# P5 第三十批：原本地帮助静默验收

2026-10-07，固定 Windows 源码基线 c5c398d89。原 HelpMainMenu、HelpSearchOption 接通本地 HTML 生成与唯一平台打开，原八组菜单备注和八张搜索表保持；未迁能力继续明确标注。

- 8 项新增专项通过，覆盖原模板/别名/元数据边界、动态文本转义、固定文件名、UTF8/原子写入、取消/重试、正式命令及关闭资源清理。
- 1604 项全量通过，0 失败，2 项浏览资源测试跳过，共 1606 项。指定实图效果/导出样本只读启用。
- 25 项实际无窗口 macOS 后端通过。Engine、正式 Library、默认目录 ARM64 .app 及 strict/deep 本地 ad-hoc 签名通过；产品无编译警告，原生测试宿主保留 SDK PublishFolderType 警告。
- 独立只读复核未发现确定性功能缺陷；随后最小修正临时清理异常覆盖原始错误，8 项帮助专项重跑通过。

[构建/测试证据](p5-help-manuals-validation.json)和[命令登记](p5-help-manuals-commands.json)分别保存。普通宿主 213 入口，装配 ImportBackup 后 214 入口/21 占位；235 实例完整保留，数量不等于功能覆盖率。

其他回归派生材料移至 `/Users/yuwk/.codex/artifacts/neeview/p5-help-manuals-derived/`。未启动或激活正式应用、未打开真实浏览器、未修改用户 Profile/图片。系统浏览器打开、Windows 动态帮助另验；高级效果、视频、脚本及完整设置继续迁移，P5 整体尚未完成。
