# NeeView Mac

基于 `ump45nose/NeeView` 46.3 分类 fork，独立维护 Mac 源码。保留原阅读/设置算法、命令名、JSON与窗口区域，集中替换WPF/Windows依赖。本轮仅P0/P1；原Windows工程保持不变作为固定参考。

[整体架构](docs/architecture.md) · [前端边界](docs/frontend-boundaries.md) · [源码出处](docs/source-migration.json) · [命令表](docs/command-migration.md) · [布局表](docs/layout-migration.md) · [行为对照](docs/behavior-baseline.md) · [阶段验收](acceptance/stages.md)

## 当前增量

- 三个生产项目：Engine、Backends、MacOS；唯一正式入口，没有Preview、第二个阅读内核或SQLite。
- 原PagePosition/PageRange、设置Mix、排序、PageFrameFactory迁入；Book/Page/Archive/BookOperation为P1子集适配。
- 图片/目录/ZIP/CBZ、单双页、方向、宽图/首页/末页/分割规则、缩放/平移、基础目录/页面导航和信息。
- 顶部菜单/地址、原左右面板分组、中央查看器、底部滑条/状态；原颜色/图标资源，设置窗口左导航/右内容结构。
- 原UserSetting/History的已支持分支、未知字段保留、位置/设置恢复和失败可重试保存。
- RAR/7z、胶片条、书签、连续/瀑布流、fork分类、完整导入及高级内容尚未接入；清单保留，不沿用旧方案的通过状态。

## 开发与验证

需要.NET10 SDK；正式Host还需macOS workload及与其匹配的完整Xcode，验收平台macOS15+ / Apple Silicon。Mac API版本锁定27.0（当前workload 27.0.10722）；升级workload时同步核对Xcode与锁文件。

```sh
cd portable
python3 scripts/validate.py --dotnet dotnet
python3 scripts/validate.py --dotnet dotnet --macos-source --macos
dotnet run --project src/NeeView.MacOS/NeeView.MacOS.csproj -- /path/to/images-or-book.cbz
```

本机隔离SDK为 `/Users/yuwk/.local/share/neeview-dotnet/dotnet`；PATH命中旧版本时传入该路径，脚本清除不匹配DOTNET_ROOT，不改系统SDK配置。

validate串行执行Engine构建、原算法/正式XAML/真实目录ZIP/解码/资源测试。`--macos-source`是正式入口Library编译检查，**不产生可运行应用验收**；`--macos`是默认正式应用构建。结果分别记录在acceptance/p1-validation.json。默认bin/obj，失败不换输出目录。

本机已安装Xcode27.0，正式.app构建与本地ad-hoc签名校验通过；目录/中文CBZ、原分页快捷键、窗口关闭重开、完整退出恢复与系统明确打开已完成真机验证，见[运行记录](acceptance/p1-macos-runtime.md)。最终开发应用位于`src/NeeView.MacOS/bin/Debug/net10.0-macos/NeeView.MacOS.app`；RID子目录中的.app是SDK中间产物。CI只运行P1模块测试，未推送或远端执行。

## 数据与操作

数据目录 `~/Library/Application Support/NeeView.Mac`，原 `UserSetting.json`、`History.json` 为权威数据。不修改旧Windows或NeeView.Portable目录，不直接自动导入旧状态。Mac仅补充半页/false-wide值；完整旧版本迁移和路径映射待P5。

默认Left/左点击下一帧、Right/右点击上一帧；单张步进与帧步进分开。文本输入隔离阅读键，Command+O/W/Q用于打开/关闭/退出。未迁入滚动翻页不会改为普通翻页；当前滚轮可报告未迁移状态，高精度增量/修饰键用于平移/缩放。完整触控板适配待P2。

目录和页面列表位于原左栏，信息位于原右栏；边栏可调整、显隐及基础自动隐藏。主题/布局调整入口见frontend-boundaries.md。

## 发布与验收

```sh
python3 scripts/package_macos.py --dotnet dotnet
python3 scripts/package_macos.py --dotnet dotnet --sign 'Developer ID Application: …' --notary-profile NeeView
```

只发布正式ARM64.app，先要求完整Xcode。签名公证由用户环境凭据提供，脚本不创建/换号/改全局配置。依赖清单取实际发布输出并保留许可。P5才执行正式发布。

27项自动测试、正式构建与本地签名输出见阶段记录；正式运行截图与Headless截图分别留证。系统打开事件已验证；Finder双击/拖放、菜单子项/原生打开对话框、触控板/多屏/NAS、完整显示性能、Windows动态对照和用户视觉验收仍需分别执行。旧macos-ui-2026-10-02.md和benchmark记录仅保留为重写方案历史，不作为本轮迁移通过证据。
