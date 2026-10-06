# P5 第十九批：开发分发静默验收

2026-10-06，在默认 Release/bin/obj 串行发布唯一 ARM64 正式项目；未启动或激活产品、未写用户 Applications 或 Profile。

- 12项脚本回归通过、0失败。第一次新增许可测试发现 macOS /var→/private/var 的解析路径差异，已修正相对路径计算并重跑通过。
- 实际 Release 发布通过，产品0警告；完整 Mach-O 含arm64、逐文件和app strict/deep ad-hoc签名通过。
- 21个实际NuGet依赖，3个SDK运行时包，18个Mach-O。实际为CoreCLR，不以输出目录MonoBundle名称推断Mono运行时。
- 所有实际依赖含原文许可，metadata_only为空；固定上游源码提交和7份原文SHA通过。运行时/原生包第三方材料及原项目许可进入成品。
- ZIP完整性、随机目录解包、重定位签名和原生指纹一致性通过；大小 69177352 字节。
- 独立核验发现替换中断可能被stage清理，已将旧材料备份移至stage外，加入回滚失败保留测试；资源同名误归属也加入回归并限定实际MonoBundle。

详细机器记录：[validation](p5-distribution-validation.json)。打包时存在本节点未提交跟踪修改，记录tracked_source_dirty=true；提交后再次生成可追溯提交的开发成品，用户未跟踪.DS_Store保持。不把脏源码包作为已发布版本。

Developer ID、Apple公证、Gatekeeper及干净系统交互安装未执行，默认ad-hoc仅开发分发。进程中断时多个工件不保证原子替换，旧材料保留用于恢复。完整设置/效果/视频/脚本、原备份命令及真实两分支导出继续迁移/验收，P5未完成。
