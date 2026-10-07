# P5 阻塞与兼容收尾：开发校验

日期：2026-10-07。分支 `feature/macos-port`。本批沿原三项目、JSON、归档与文件事务实现；契约见 [p5-compatibility.md](../docs/p5-compatibility.md)。没有激活正式 NeeView 窗口，没有修改用户图片或 Profile。

## 本轮执行结果

| 层面 | 本轮结果 | 证据与范围 |
|---|---|---|
| Engine 与原正式视图全量回归 | 1845 通过、0 失败；12 实图用例未执行，总计 1857 | [完整记录](p5-compatibility-validation.json)；原算法、JSON、文件/来源、资源与 XAML Headless |
| macOS 后端全量回归 | 48/48 通过，0 跳过 | 官方绑定测试 `.app` 后台执行，包含 10 项文件 worker 回归 |
| 分卷、发布检查、密码与关闭专项 | 46/46 通过 | 本批专项；包含既有密码回归，不能把 46 全部记为新增测试 |
| 正式 ARM64 Debug 构建及本地签名 | 通过，产品构建 0 警告/0 错误 | 默认 bin/obj，串行构建；strict/deep ad-hoc 校验 |
| 正式 Debug 无窗口入口 | 真实移动、SHA256、完成日志及释放、正常退出通过 | [合成夹具记录](p5-compatibility-formal-worker.json)；直接启动同一正式 exe 的 `--neeview-file-worker` |
| 打包脚本回归 | 15/15 通过 | 原文许可、实际依赖归属、Mach-O 清单、替换失败恢复及启动崩溃不得报告通过；只用隔离临时材料 |
| Release 开发包 | 修复后的完整打包及真实运行通过 | 18个Mach-O签名/ARM64校验、ZIP重定位，以及原包/解包后的正式exe真实移动/日志释放；见 [包记录](p5-compatibility-package.json) |

命令：`python3 portable/scripts/validate.py --dotnet /Users/yuwk/.local/share/neeview-dotnet/dotnet --phase p5-compatibility --macos --macos-native`。脚本测试为 `python3 -m unittest discover -s portable/scripts/tests -p 'test_package_macos.py' -v`。

12 个实图用例需显式选择挂载资源样本，本轮全量未启用；未执行不是通过。原生测试构建仍有 SDK `apphost PublishFolderType` 元数据警告，0 错误，实际 `.app` 已运行。损坏 PDF 回归产生 CoreGraphics 诊断，不影响其预期错误结果。

开发修复提交为 `4a1fc27f4`，开发包装载/打包修复提交为 `3dcd089ed0e398c1aae5ecfe7c613aa2456dba27`。最终包对应后者，25个实际NuGet依赖、3个SDK运行时包均附原文许可，metadata_only为空；ZIP SHA256为 `7fed8f20985c1271b363014bc366871e2517a9f91086c59223a16a966965c9d7`。包位于 `portable/artifacts/NeeView.zip`，app位于同目录 `NeeView.app`；没有写入用户Applications或正式发布。发布有既有SDK RID覆盖警告，未更换默认输出目录。

全工作树和跟踪脏状态仍为true：既有验收截图/JSON、未跟踪材料与记录文档保留。打包后单独核对 `portable/src` 相对该提交无修改，不能把全仓库脏标记解释为产品源码未固定，也不能将它伪改为false。Developer ID、公证、Gatekeeper和交互安装未执行。

## 修复回归与兼容结果

- 文件 worker 的初次取消/无 UI 入口测试曾失败：Console.In 同步包装阻止取消监听，macOS 主线程预设上下文导致异步同步等待死锁。已改用标准输入独立 StreamReader、独立监听任务，并在无 UI 分支清除同步上下文；最终专项、原生全量及正式入口通过。
- 最初开发包静态签名/ZIP校验通过，但实际worker在dyld装载时退出，原因是ad-hoc无Team ID却启用hardened library validation。现开发包使用普通ad-hoc，正式Developer ID仍启用hardened runtime，未增加禁用library validation权限。隔离重签名实测通过；打包脚本已在稳定成品替换前增加真实移动、日志释放及解包重定位后的运行验证。不能继承此前仅静态签名的包为运行通过。
- 真实提交后丢失响应回归确认 Completed 恢复日志保留，再由原后端核验恢复；忽略取消、排队取消、无进展、协议超限、无结果、正常长操作进展及晚取消成功分别覆盖。故障模拟不是 SMB 断线。
- 普通/固实 RAR、RAR2/RAR5、7z 与 ZIP 分卷使用固定上游小夹具逐条比对，首/中/尾卷缺失明确报错；来源和 SHA256 在 [夹具清单](../tests/NeeView.Engine.Tests/Fixtures/Multipart/sources.json)。加密二进制 7z 分卷读取通过。
- SharpCompress 0.50.3 明确不支持 RAR 多卷解密；加密 RAR 分卷及嵌套分卷返回能力提示，不循环询问或缓存失败口令。没有添加外部解压 CLI 或修改依赖源码。
- 发布检查只接受当前 fork 的唯一稳定 Mac ARM64 资产，原网络开关关闭时不请求。没有自动安装器，原 Windows 检查器也不包含自动安装。

## 仍待独立验收

AX 长期资源增长仍失败；目前没有可用官方修复，临时最小框架补丁待用户选择。真实独立 SMB 的断线/重连、正常退出、部分写入完整性及启动恢复尚未复验。只读原生调用仍可能占用两个有界后台槽，废纸篓/ZIP 改写等入口未纳入本批 worker；不能据此标记所有 NAS 阻塞已解决。

固定 Windows 动态对照、真实两分支导出、设备与正式分发另验。触控板、完整浮窗菜单/停靠及屏幕 P95 按用户要求跳过，不重开；多屏无环境。Developer ID、公证和干净系统安装未执行。P3/P4 及 P5 整体验收不能由这批开发回归封板。
