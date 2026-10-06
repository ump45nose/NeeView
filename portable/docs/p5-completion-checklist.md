# P5 完成清单

P5 已开始。P3/P4 的两项真机检查按用户要求 [跳过并记录](../acceptance/p34-skipped-validation.md)；AX 和 NAS 已知问题保留，不因进入 P5 自动关闭。

| 交付 | 状态 | 边界 |
|---|---|---|
| 原 Profile/.nvzip 读取、路径映射及只读预览 | 本批完成，37 项专项/877 全量通过，2 项资源测试跳过 | 原 JSON/命令，当前状态与来源只读；真机及用户实际导出待验 |
| 选择项、备份、实际应用与失败恢复 | 第二批完成，65专项/905全量通过，2资源用例跳过 | 原五文件事务、完整备份、唯一窗口重建；未选文件保持 |
| 原旧版本升级、旧布局与完整差分兼容 | 第五批完成已迁配置/命令差分；整体仍部分完成 | 设置38–46.3有字体门槛；历史/书签/快速访问树44–46.3；独立目录46.0–46.3，旧字典独立转换。已迁配置/参数的原默认差分完成，未迁原分支继续保留；旧效果及附属文件尚未完成 |
| 用户两分支真实导出数据 | 待验 | 本批仅真实格式的合成样本 |
| 完整设置界面、高级格式/效果 | 待分批迁移 | 保留原控制关系与禁用能力清单 |
| ARM64 独立分发、许可清单、签名公证与安装 | 待交付 | 开发 ad-hoc 签名不等于 Developer ID 分发 |

第一批契约：[p5-profile-preview.md](p5-profile-preview.md)。构建、自动回归、界面 Headless、设备/Windows、提交/推送与发布分别报告。

实际结果：[静默验收](../acceptance/p5-profile-preview-runtime.md)。正式默认目录 ARM64 应用构建及 strict/deep 本地 ad-hoc 签名通过，不等于 Developer ID 分发通过。

第二批契约：[p5-profile-apply.md](p5-profile-apply.md)；[静默验收](../acceptance/p5-profile-apply-runtime.md)。原 ImportBackup 接入，原235实例为168个入口/67个占位，数量不代表完整覆盖率。P5整体尚未完成。

第三批契约：[p5-legacy-compatibility.md](p5-legacy-compatibility.md)；[静默验收](../acceptance/p5-legacy-compatibility-runtime.md)。967全量通过、2资源用例跳过，其中150导入/布局专项（63新增）通过；正式默认ARM64应用和本地签名通过。源版本/未接入效果有明确报告，未知/关闭选择不打开首组；未激活桌面应用。

第四批契约：[p5-folder-compatibility.md](p5-folder-compatibility.md)；[静默验收](../acceptance/p5-folder-compatibility-runtime.md)。1004全量通过、2资源跳过，其中164导入专项含37新增（加入布局回归共187相关用例）。
独立目录4065/4209升级和旧字典转换分别保留，快速访问内嵌未来版本不能被覆盖；缩略目标与旧滚动参数编辑回归通过。正式ARM64构建及本地签名通过；完整差分写出、旧效果、附属文件和高级功能继续待迁，P5整体未完成。

第五批契约：[p5-difference-settings.md](p5-difference-settings.md)；[静默验收](../acceptance/p5-difference-settings-runtime.md)。默认恢复、owner/alias、九数字Index、四模板、旧字段退役和失败重试有隔离回归；原布局完整快照保持。P5整体仍未完成，未继承合成测试为真实Windows/导出验收。
