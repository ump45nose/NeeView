# NeeView Mac

基于 `ump45nose/NeeView` 46.3 分类 fork，独立维护 Mac 源码。保留原阅读/设置算法、命令名、JSON与窗口区域，集中替换WPF/Windows依赖。当前完成P2第四批可运行增量，P2尚未整体完成；原Windows工程保持不变作为固定参考。

[整体架构](docs/architecture.md) · [前端边界](docs/frontend-boundaries.md) · [源码出处](docs/source-migration.json) · [命令表](docs/command-migration.md) · [布局表](docs/layout-migration.md) · [行为对照](docs/behavior-baseline.md) · [阶段验收](acceptance/stages.md)

## 当前增量

- 三个生产项目：Engine、Backends、MacOS；唯一正式入口，没有Preview、第二个阅读内核或SQLite。
- 原PagePosition/PageRange、设置Mix、排序、PageFrameFactory迁入；Book/Page/Archive/BookOperation为P1子集适配。
- 图片/目录/ZIP/CBZ、单双页、方向、宽图/首页/末页/分割规则、缩放/平移、基础目录/页面导航和信息。
- 顶部菜单/地址、原左右面板分组、中央查看器、底部滑条/状态；原颜色/图标资源，设置窗口左导航/右内容结构。
- 原UserSetting/History/Bookmark的已支持分支、未知字段保留、位置/设置恢复和失败可重试保存。
- P2首批：完整八组菜单及禁用占位、普通/固实RAR与7z、历史/书签树、可见胶片条/导航器、全部原命令键位编辑与AppKit手势桥接。
- P2第二/三批：原侧栏拖拽组合/拆组、布局比例恢复、原NScroll滚动翻页、共享页选择/滑条联动、胶片条详情/三滚轮、指定页/步长与两种导航历史。
- P2第四批：原普通书架目录/归档混合列表、默认排序及目录分组、独立浏览/同步/刷新、前后书阅读恢复与文件夹页分组导航。
- 每目录参数、树/封面、真实Folder页/父书定位、完整书签/输入/标记等仍待迁；连续/瀑布流、fork分类、完整导入及高级内容待后续阶段。

## 开发与验证

需要.NET10 SDK；正式Host还需macOS workload及与其匹配的完整Xcode，验收平台macOS15+ / Apple Silicon。Mac API版本锁定27.0（当前workload 27.0.10722）；升级workload时同步核对Xcode与锁文件。

```sh
cd portable
python3 scripts/validate.py --dotnet dotnet
python3 scripts/validate.py --dotnet dotnet --phase p2-bookshelf --macos-source --macos
dotnet run --project src/NeeView.MacOS/NeeView.MacOS.csproj -- /path/to/images-or-book.cbz
```

本机隔离SDK为 `/Users/yuwk/.local/share/neeview-dotnet/dotnet`；PATH命中旧版本时传入该路径，脚本清除不匹配DOTNET_ROOT，不改系统SDK配置。

validate串行执行Engine构建、原算法/正式XAML/真实来源/解码/资源测试。`--macos-source`是正式入口Library编译检查，**不产生可运行应用验收**；`--macos`是默认正式应用构建。结果按--phase分别保存，本批为acceptance/p2-bookshelf-validation.json，保留前批证据。默认bin/obj，失败不换输出目录。

本机已安装Xcode27.0，正式.app构建与本地ad-hoc签名校验通过；目录/中文CBZ、原分页快捷键、窗口关闭重开、完整退出恢复与系统明确打开已完成真机验证，见[运行记录](acceptance/p1-macos-runtime.md)。最终开发应用位于`src/NeeView.MacOS/bin/Debug/net10.0-macos/NeeView.MacOS.app`；RID子目录中的.app是SDK中间产物。CI只运行P1模块测试，未推送或远端执行。

## 数据与操作

数据目录 `~/Library/Application Support/NeeView.Mac`，原 `UserSetting.json`、`History.json`、`Bookmark.json` 为权威数据。不修改旧Windows或NeeView.Portable目录，不直接自动导入旧状态。Mac仅补充半页/false-wide值；完整旧版本迁移和路径映射待P5。

默认Left/左点击下一帧、Right/右点击上一帧；单张步进与帧步进分开。查看器Up/Down前后书；列表/树/排序控件方向键隔离，书架Enter才打开。文本输入隔离阅读键，Command+O/W/Q用于打开/关闭/退出。普通滚轮进入原NScroll先滚动再边界翻页，高精度增量/修饰键用于平移/缩放。原生桥接已接入，真实触控板操作待用户验收。

书架和页面列表默认位于原左栏，信息默认位于原右栏；边栏可跨栏重排、拖拽组合/拆组、调整比例、显隐及基础自动隐藏。主题/布局调整入口见frontend-boundaries.md。

## 发布与验收

```sh
python3 scripts/package_macos.py --dotnet dotnet
python3 scripts/package_macos.py --dotnet dotnet --sign 'Developer ID Application: …' --notary-profile NeeView
```

只发布正式ARM64.app，先要求完整Xcode。签名公证由用户环境凭据提供，脚本不创建/换号/改全局配置。依赖清单取实际发布输出并保留许可。P5才执行正式发布。

78项自动测试、正式构建与本地签名输出见本批阶段记录；正式运行截图与Headless截图分别留证。组合/比例、页选择/历史、书架/排序保存、前后书及章节跳页已进行正式应用合成输入验收；Finder双击/拖放、全部菜单/原生对话框、真人触控板/IME/多屏/NAS、完整显示性能、Windows动态对照和用户新增交互验收仍需分别执行（总体布局已获用户认可）。旧macos-ui-2026-10-02.md和benchmark记录仅保留为重写方案历史，不作为本轮迁移通过证据。

P2范围与未完成项见[首批及后续索引](docs/p2-reading-navigation.md)，第四批契约见[书架导航设计](docs/p2-bookshelf-navigation.md)，正式运行证据见[本批验收记录](acceptance/p2-bookshelf-macos-runtime.md)。菜单占位及侧栏组合已接入；完整细节仍按迁移清单推进。
