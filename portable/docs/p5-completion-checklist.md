# P5 完成清单

P5 已开始。P3/P4 的两项真机检查按用户要求 [跳过并记录](../acceptance/p34-skipped-validation.md)；AX 和 NAS 已知问题保留，不因进入 P5 自动关闭。

| 交付 | 状态 | 边界 |
|---|---|---|
| 原 Profile/.nvzip 读取、路径映射及只读预览 | 本批完成，37 项专项/877 全量通过，2 项资源测试跳过 | 原 JSON/命令，当前状态与来源只读；真机及用户实际导出待验 |
| 选择项、备份、实际应用与失败恢复 | 第二批完成，65专项/905全量通过，2资源用例跳过 | 原五文件事务、完整备份、唯一窗口重建；未选文件保持 |
| 原旧版本升级、旧布局与完整差分兼容 | 部分完成 | 44–46.3历史/书签明确分支、46.1–46.3设置已接入；完整旧设置/布局仍待迁入 |
| 用户两分支真实导出数据 | 待验 | 本批仅真实格式的合成样本 |
| 完整设置界面、高级格式/效果 | 待分批迁移 | 保留原控制关系与禁用能力清单 |
| ARM64 独立分发、许可清单、签名公证与安装 | 待交付 | 开发 ad-hoc 签名不等于 Developer ID 分发 |

第一批契约：[p5-profile-preview.md](p5-profile-preview.md)。构建、自动回归、界面 Headless、设备/Windows、提交/推送与发布分别报告。

实际结果：[静默验收](../acceptance/p5-profile-preview-runtime.md)。正式默认目录 ARM64 应用构建及 strict/deep 本地 ad-hoc 签名通过，不等于 Developer ID 分发通过。

第二批契约：[p5-profile-apply.md](p5-profile-apply.md)；[静默验收](../acceptance/p5-profile-apply-runtime.md)。原 ImportBackup 接入，原235实例为168个入口/67个占位，数量不代表完整覆盖率。P5整体尚未完成。
