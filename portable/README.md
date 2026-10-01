# NeeView macOS

基于 `ump45nose/NeeView` 46.3 分类 fork 的独立跨平台工程。Windows 工程保持为行为参照。架构见 [整体架构](docs/architecture.md)，前端调整见 [独立边界](docs/frontend-boundaries.md)，模块契约见 [M01](docs/modules/M01.md) 至 [M10](docs/modules/M10.md)，实际证据见 [阶段验收](acceptance/stages.md)。

## 开发

需要 .NET 10 SDK。正式 AppKit Host 还需要 macOS workload 和与 workload 匹配的完整 Xcode。

```sh
cd portable
python3 scripts/validate.py --dotnet dotnet
python3 scripts/validate.py --dotnet dotnet --benchmarks
dotnet run --project src/NeeView.Preview -- /path/to/images-or-book.cbz
dotnet run --project src/NeeView.Preview -- --smoke /path/to/images-or-book.cbz
dotnet workload install macos
dotnet build src/NeeView.MacOS -m:1
```

当前任务安装的隔离 SDK 路径为 `/Users/yuwk/.local/share/neeview-dotnet/dotnet`。若 PATH 优先命中旧版本，可用该绝对路径并清除不匹配的 `DOTNET_ROOT`，不修改系统 SDK 设置。

Preview 用于共享界面、真实解码和保存恢复验证。正式 Host 使用 Foundation/AppKit 文件能力；本机没有完整 Xcode，所以正式应用构建和签名安装验收仍需补齐环境。

## 操作

- 打开图片即浏览所在目录；支持目录及 ZIP/CBZ、RAR/CBR、7z。
- 默认 Left 下一 frame、Right 上一 frame；单页/双页、方向、连续、瀑布流可以切换。
- 瀑布流中点击图片明确选择分类对象，双击进入分页；导航列表可定位。
- 手动目标目录前九项对应数字 1–9；目标列表双击分类。复制开关只影响数字/面板分类，原 `MoveToFolderAs` 固定移动。
- 文本框和对话框隔离快捷键；常用系统操作使用 Command。
- 独立设置页编辑当前书籍、默认阅读、恢复策略、键位鼠标、侧栏和历史容量；冲突会报告。
- 导入接受旧 `.nvzip` 或 Profile 路径，以 `Windows前缀 => macOS前缀` 配置映射；先预览再应用，源只读。
- Command+W 关闭窗口，点击 Dock 可重开；Command+Q 保存并退出。

用户状态：`~/Library/Application Support/NeeView.Portable`；缓存：`~/Library/Caches/NeeView.Portable`。覆盖备份在用户数据的 `file-backups`；导入前备份在 `backups`。数据库迁移脚本位于 `src/NeeView.Persistence/Sql`。

## 打包

```sh
python3 scripts/package_macos.py --preview
python3 scripts/package_macos.py
python3 scripts/package_macos.py --sign 'Developer ID Application: …' --notary-profile NeeView
```

默认构建输出使用 SDK 目录；打包成品位于 `artifacts`。Preview 包带 `Preview` 名称，仅用于本机验证。正式签名路径需要有效证书及已配置的 notarytool keychain profile；脚本不会创建凭据或自动换号。依赖清单由实际 publish .deps.json 文件生成，并保留仓库许可与可取得的 NuGet 许可材料。

## 验收边界

首版功能是逐模块实施的工程增量。触控板、Retina、NAS、中断恢复、性能 P95、Windows 行为对照、正式签名/公证/干净安装需各自执行验收，不能用编译或 Headless 测试替代。未支持的 PDF、媒体、密码/分卷/嵌套和旧脚本会明确提示或进入导入报告。

## 前端调整

主页面排布与工具栏位于 ReaderShell.axaml，宿主装配独立 ReaderNavigationPanel、ReaderDestinationPanel、ReaderView 和 SettingsWindow。命令与面板业务进入 ReaderWorkspaceViewModel，目录/书签数据由应用接口加载，控件不引用数据库或解压实现。颜色、间距、缩略图和设置页尺寸集中在 ReaderTheme.axaml，显示文本使用 ReaderLabels；其余面板可逐个替换为 XAML 模板，不影响阅读规则和文件操作。后台布局协调器只计算纯几何，与窗口和绘制独立。

## 可复现证据

validate.py 串行构建、测试、生成 12 张混合横竖 4K JPEG/CBZ、真实目录与归档解码、退出恢复；生成 acceptance/latest-validation.json。--benchmarks 额外生成四个服务链路报告，96 次采样，每次空像素缓存，OS 文件缓存未清空；不包含绘制帧。结束后强制 GC 数据仅为诊断。

共享 net10.0 工程与 Preview 的 RuntimeIdentifiers 固定为 osx-arm64、osx-x64、linux-x64，发布不会改写开发锁文件。CI 已配置 macOS/Linux 模块及解码冒烟，当前未推送，远端尚未执行。自动脚本使用自己的临时状态目录，不导入或移动用户图片。
