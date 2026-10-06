# P5 第十九批：ARM64 开发分发

## 职责与依赖

只装配唯一 NeeView.MacOS 正式项目的 SDK Release 成品；仍使用默认 bin/obj、锁定依赖和串行构建。查询本次 AppBundleDir，不把 RID 中间目录或旧 .app 作为正式输出。Info.plist 补充已经接入的 PDF 打开登记，最低 macOS 15 保持。

## 契约与来源

`python3 portable/scripts/package_macos.py --dotnet <SDK路径>` 生成 portable/artifacts/NeeView.app、NeeView.zip 和 NeeView.report.json。默认为 ad-hoc 开发签名。正式分发只接受明确的 Developer ID Application 身份；公证只使用用户提供的 keychain profile，脚本不读取或生成密钥。

依赖来自本次 osx-arm64 project.assets.json 的 runtime/native 与实际 MonoBundle 文件交叉匹配；包的其他平台实现若实际存在也列入，未分发的构建/native 包不列入。该 SDK 把 NuGet 文件平铺到 MonoBundle，basename 冲突明确失败，Resources 同名材料不参与归属。SDK 运行时来自 ResolvedFrameworkReference 的真实 RuntimePackPath，不推断运行时实现。完整 Mach-O 清单独立覆盖主程序、动态库和 framework；无扩展名、fat64 也识别，符号链接不重复计费。

成品记录版本、当前提交、固定基线、全工作树与跟踪文件脏状态，以及签名前/后原生 SHA256。原项目 LICENSE.md 原字节保留。包内许可保留相对目录，包含 THIRD-PARTY/NOTICE；缺少原文的包使用相同 NuGet 版本及 nuspec repository commit 的固定上游材料。licenses/upstream.json 保存原地址、提交和原文哈希，发布过程不联网，版本/提交/原文不匹配即失败。

## 生命周期与错误

构建、装配、逐 Mach-O/框架签名、最终 app 签名、ARM64 及 strict/deep 验证、ZIP 检查和随机目录重定位全部成功后才替换稳定成品。中途失败保持旧成品。成品替换的异常可回滚；旧材料临时备份在 stage 外，回滚失败或进程被杀时不会被 stage 自动删除，明确保留 `.previous-package-*` 供恢复。多个工件替换不宣称跨文件原子性；发布不并发运行。

公证成功后 staple、spctl 检查，再重新打 ZIP 和复核。报告分别列签名、公证、Gatekeeper、ZIP 重定位和交互安装。默认开发包不声明 Developer ID、公证或干净系统安装通过。不修改用户 Applications，不启动或激活产品窗口。

## 测试与扩展

12项隔离脚本回归覆盖许可原文/提交/哈希、路径越界、重名许可、资产归属、Mach-O、失败替换及回滚失败保留。真实 SDK 发布、签名及重定位证据见[验收](../acceptance/p5-distribution-runtime.md)。后续在用户提供凭据后复用同一脚本完成 Developer ID 和公证；干净系统安装仍须独立验收。
