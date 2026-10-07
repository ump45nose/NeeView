# AX 最小原生补丁：隔离验证与停止结论

2026-10-07，用户已授权执行最小源码补丁验证。产品基线为 `c8f979d23`，本批未更改产品源码、依赖版本、NuGet 缓存或发布包。

**结论：不接入产品。补丁解决了静态查询中的返回引用泄漏，但动态控件仍不能回收。进一步处理涉及 Avalonia 原生节点生命周期，触发本轮约定的停止条件。MAC-AX-001 保持未解决。**

后续交付决定：用户要求已验证无法合理处理的项目保留记录、退出目标。AX诊断结果不变，现不再作为开发/交付阻塞；本轮不继续节点生命周期改造。当前目标见[开发进度与交付边界](../docs/delivery-status.md)。

## 固定来源与修改边界

- Avalonia `12.1.3`，提交 `8eeda4f6f546165b3f72e63c9f42247abb306905`，与实际 NuGet 包的 repository 元数据一致。
- MicroCom `0.11.6`，提交 `76785efcafd91b5902fd19dd11145f6dd655b7b4`。生成器 `CSharpGen.InterfaceGen.cs` 的 `BackMarshalReturn` 使用 `GetNativePointer(result, true)`；返回接口已 AddRef，由调用方释放。
- [候选补丁](ax-minimal-return-ownership.patch)仅改 `automation.mm`、`AvnView.mm`、`AvnWindow.mm`，24 行新增、21 行替换。采用 `ComPtr(raw, true)` 接管返回引用，out 参数采用空 `ComPtr.getPPV()`。覆盖数组、子 peer、node、父/根 peer、窗口、滚动条、焦点及 hit-test 返回值。
- 保持 `AvnAutomationNode`、`initWithPeer/dealloc` 和托管包装生命周期原样；没有照搬已关闭的 PR #21950，也没有修改 ABI 或禁用无障碍。
- 补丁是验收材料，不进入 solution、原生资产替换或打包链。归档采用零上下文 diff，避免把上游空白行带入材料的空白错误；仅对固定提交以 `git apply --unidiff-zero` 应用，已验证正向及反向检查。原许可为 MIT，原文见 [Avalonia licence](../licenses/upstream/avalonia/licence.md)。

## 构建、ABI 与加载

环境：Apple Silicon、macOS 27.0.1、Xcode 27.0 (27A266a)、.NET SDK 10.0.401。仅在官方源码临时 checkout 串行构建，使用官方默认 `artifacts/native/Release`：

```sh
XCODE_XCCONFIG_FILE=<native.xcconfig> dotnet build native/Avalonia.Native/Avalonia.Native.macOS.proj -c Release -m:1
```

`native.xcconfig` 仅指定 `MACOSX_DEPLOYMENT_TARGET = 15.0` 和 `ARCHS = arm64`。最初原样构建失败：Xcode 27 不接受上游 10.13 目标；改用项目既定 15.0 目标后，未修改基线及补丁版均 0 警告、0 错误。不改变构建输出目录。

两版均为 ARM64 Mach-O，导出符号集合与官方 NuGet ARM64 slice 一致，含 `CreateAvaloniaNative`；依赖为系统库，ad-hoc 签名校验通过。官方产物名称为 `libAvalonia.Native.OSX.dylib`，NuGet 名称为 `libAvaloniaNative.dylib`。本轮通过官方 `AvaloniaNativeLibraryPath` 直接装载隔离产物，没有替换 NuGet 或 NeeView 包内库。导出、install name、依赖和哈希在 [匿名证据](ax-minimal-patch-evaluation.json) 中；这不等于正式签名公证。

## 静态查询对照

隔离 Avalonia 原生窗口包含文本、输入框、按钮、下拉框、滑条和滚动容器。窗口 `ShowActivated=false`，`ShowInDock=false`；独立工具只读 AX 属性，不设置属性、不触发动作或键鼠。没有启动正式 NeeView、读取用户 Profile 或图片。

预热查询 10 次后采集起点，随后查询 100 次，再追加 1000 次。每个阶段用 gcdump 触发完整 GC。以下为同一类型的存活计数，未出现的数组类型计 0：

| 版本 / 类型 | 起点 | +100 次 | 再 +1000 次 |
|---|---:|---:|---:|
| 未修改：AvnAutomationPeerArray | 821 | 7321 | 72321 |
| 未修改：MicroComShadow | 1026 | 7526 | 72526 |
| 未修改：AvnAutomationPeer | 174 | 174 | 174 |
| 补丁：AvnAutomationPeerArray | 0 | 0 | 0 |
| 补丁：MicroComShadow | 205 | 205 | 205 |
| 补丁：AvnAutomationPeer | 174 | 174 | 174 |

正式计入的未修改复跑及补丁版查询均无 AX 错误，遍历节点数与查询量记录在 JSON。首次未修改版的 1000 次查询曾返回非零退出，旧诊断脚本未保存原始失败结果，不能确定原因；该段不计通过。脚本补齐错误回执后，串行 `baseline-repeat` 完成同一阶段及正常退出。不能把这次成功解释为旧查询故障根因已消失。

静态数组增长已被最小补丁消除，但静态结果不代表完整长期资源通过。

## 动态控件释放与对照

随后整批替换控件子树 100 次，每次只读查询一次；最后清空容器，等待两秒、完整 GC，再采集 gcdump 和再次完整 GC。WeakReference 只追踪旧根，不强持有控件；共 101 个旧根，包括最初与最后清空的子树。

| 版本 / 观测方式 | GC 后旧根存活 | AvnAutomationPeer | MicroComShadow | TextBlock |
|---|---:|---:|---:|---:|
| 未修改 / 每批只读 AX | 101 / 101 | 16574 | 105627 | 2424 |
| 补丁 / 每批只读 AX | 101 / 101 | 16574 | 16605 | 2424 |
| 补丁 / 不发显式外部 AX 查询 | 101 / 101 | 371 | 402 | 2424 |
| Headless / 相同替换及清空逻辑 | 0 / 101 | 0 | 0 | 0 |

原生两种查询条件下，旧子树均保留，ScrollViewer 为 303 个；Headless 对照旧根、ScrollViewer 和 TextBlock 全部回收。没有显式外部查询仍可能产生 macOS 自动 AX 通知，因此不能把第三行称为“完全没有 AX”。

补丁版静态 peer 为 174，动态测试后为 16574，原先未释放的返回引用并非唯一问题。源码存在生命周期环的线索：`AvnAccessibilityElement._peer` 持有托管回调，托管 `AvnAutomationPeer.Node` 持有原生节点，节点 `__strong _owner` 指向元素；MicroCom 为 native callback 保留 GCHandle，托管 finalizer 才调用 `Node.Dispose()`。本轮结果与该环一致，但没有逐条取得原生 retain 的完整根图，不能声称已排除所有其他保留路径。

仅将 owner 改弱引用仍涉及节点有效性、直接 `delete _node`、托管 proxy 的释放顺序和重建，不能作为未经验证的一行修复。本轮到此停止，不继续扩展框架生命周期补丁。

## 证据、复现与未验范围

原始 `.gcdump`、报表、源码、查询工具、运行脚本、构建日志、ABI 和哈希回执保存在本机 `/Users/yuwk/.codex/artifacts/neeview/ax-patch-20261007`。复现入口为该目录 `run_probe.py`，参数分别为 `baseline-repeat`、`patched`、`patched-noax`、`headless-noax`，输出目录必须未存在。先按 `probe/Probe.csproj` 构建；原生版本使用固定源码和候选 patch，Headless 只作控件释放对照。初版 harness 与加入 Headless 分支后的版本均保留来源指纹；前者使用旧 `Watermark` 别名，后者使用 `PlaceholderText`，控件替换/弱引用/GC 流程相同。

诊断工具以 `DOTNET_ROOT=<NeeView SDK>`、`DOTNET_ROLL_FORWARD=Major` 运行既有 `dotnet-gcdump collect -p <pid> -o <file>` 和 `report <file>`。gcdump 图字节数、`GC.GetTotalMemory` 与 RSS 分别处理，不能混成一个内存指标；这里没有图像解码或缓存负载。

本轮完成的是隔离原生构建、装载、静态保留和动态释放诊断，全部诊断进程已结束。没有继续 30–60 分钟浏览、焦点/弹出层或正式应用验收；产品未接入补丁，也未重新构建产品或继承此前全量测试为本补丁证据。正式包保持上一批状态。

后续只有在可维护的节点生命周期修复明确、固定依赖通过同一动态释放门槛后，才进入真实 NeeView 同负载长期验证和焦点/弹出层回归。官方修复是优先路线；本轮不增加长期框架分支或第二构建链。NAS、Windows 动态范围、正式分发及用户已跳过的设备项保持各自状态，不因本轮静态结果关闭。
