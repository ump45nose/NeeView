# NeeView Mac

基于 `ump45nose/NeeView` 46.3 分类 fork，独立维护 Mac 源码。保留原阅读/设置算法、命令名、JSON与窗口区域，集中替换WPF/Windows依赖。**P2–P4 开发范围已完成，P3/P4集中设备、Windows动态及长期性能验收未封板。** 原Windows工程保持不变作为固定参考。

[整体架构](docs/architecture.md) · [前端边界](docs/frontend-boundaries.md) · [源码出处](docs/source-migration.json) · [命令表](docs/command-migration.md) · [布局表](docs/layout-migration.md) · [行为对照](docs/behavior-baseline.md) · [阶段验收](acceptance/stages.md)

## 当前增量

- 三个生产项目：Engine、Backends、MacOS；唯一正式入口，没有Preview、第二个阅读内核或SQLite。
- 原PagePosition/PageRange、设置Mix、排序、PageFrameFactory迁入；Book/Page/Archive/BookOperation按原功能链适配，不代表高级媒体/控制已全部迁完。
- 图片/目录/ZIP/CBZ、单双页、方向、宽图/首页/末页/分割规则、缩放/平移、基础目录/页面导航和信息。
- 顶部菜单/地址、原左右面板分组、中央查看器、底部滑条/状态；原颜色/图标资源，设置窗口左导航/右内容结构。
- 原UserSetting/History/Bookmark/Foldres/QuicAccess JSON及全局.nvpls、未知字段保留、位置/设置恢复和失败可重试保存。
- P2首批：完整八组菜单及禁用占位、普通/固实RAR与7z、历史/书签树、可见胶片条/导航器、全部原命令键位编辑与AppKit手势桥接。
- P2第二/三批：原侧栏拖拽组合/拆组、布局比例恢复、原NScroll滚动翻页、共享页选择/滑条联动、胶片条详情/三滚轮、指定页/步长与两种导航历史。
- P2第四批：原普通书架目录/归档混合列表、默认排序及目录分组、独立浏览/同步/刷新、前后书阅读恢复与文件夹页分组导航。
- P2第五批：原书签移动/递归合并、同名确认、颜色、登记编辑、批次删除/恢复；原JSON和节点引用保持。
- 后续P2增量：结构化历史/书签查询、登记/保留/可靠清理策略、每目录参数/随机种子、真实Folder/Archive页及父子书/递归、播放列表/标记、直接页号、四列表模板/可见封面、原查看器变换/参数、页尾/锁定及书架书签互联。
- P2收尾：五区自动隐藏/单面板浮动、原A/B/C与复杂鼠标组合/方向手势、滚动/分页Scroll/Fade动画、Hover/连续轮滚、资源测量与重复显示缓冲优化。原235命令中138个入口接入、97个占位；入口数量不表示完整功能覆盖。
- P3完成连续/瀑布、后台检查点布局、普通目录渐进索引、原帧全景、目录树/QuickAccess及监视、页面目录/名称和页面/书架搜索；共用原页面、缓存和JSON，见[收尾清单](docs/p3-completion-checklist.md)。
- P4完成fork双区分类面板、九数字/页组、覆盖/撤销重做、当前页及整书文件操作、目录/归档实体复制、普通实体/播放列表/ZIP删除、显式多选和图片/HTML/URL粘贴拖放、Mac链接；见[收尾清单](docs/p4-completion-checklist.md)。Cut按用户决定禁用；内部归档目录提取保留原版TODO提示。主视图浮动、动态菜单配置、高级树布局等原能力继续逐项占位，完整导入/高级媒体/发布在P5。

## 开发与验证

P4收尾全量803项、文件操作专项205项和设置文案复核通过，正式ARM64构建及本地严格ad-hoc签名通过；指定资源三个子目录218页的只读瀑布/缩略采样及关闭资源归零通过，见[运行记录](acceptance/p4-completion-runtime.md)。当前原235命令保留，167个入口/68个占位；入口数量不代表功能覆盖率，正式系统交互和设备验收独立记录。

需要.NET10 SDK；正式Host还需macOS workload及与其匹配的完整Xcode，验收平台macOS15+ / Apple Silicon。Mac API版本锁定27.0（当前workload 27.0.10722）；升级workload时同步核对Xcode与锁文件。

```sh
cd portable
python3 scripts/validate.py --dotnet dotnet
python3 scripts/validate.py --dotnet dotnet --phase p4-completion --macos-source --macos
dotnet run --project src/NeeView.MacOS/NeeView.MacOS.csproj -- /path/to/images-or-book.cbz
```

本机隔离SDK为 `/Users/yuwk/.local/share/neeview-dotnet/dotnet`；PATH命中旧版本时传入该路径，脚本清除不匹配DOTNET_ROOT，不改系统SDK配置。

validate串行执行Engine构建、原算法/正式XAML/真实来源/解码/资源测试。`--macos-source`是正式入口Library编译检查，**不产生可运行应用验收**；`--macos`是默认正式应用构建。结果按阶段写入 `acceptance/<phase>-validation.json`，保留前批证据。默认bin/obj，失败不换输出目录。按[静默流程](docs/validation-workflow.md)默认不启动/激活正式应用、不发系统键鼠、不读写用户Application Support；前台真机另行安排。

本机已安装Xcode27.0，正式.app构建与本地ad-hoc签名校验通过；目录/中文CBZ、原分页快捷键、窗口关闭重开、完整退出恢复与系统明确打开已有真机记录，见[运行记录](acceptance/p1-macos-runtime.md)。最终开发应用位于`src/NeeView.MacOS/bin/Debug/net10.0-macos/NeeView.MacOS.app`；RID子目录中的.app是SDK中间产物。本轮本地验证不代表远端CI结果。

## 数据与操作

数据目录 `~/Library/Application Support/NeeView.Mac`，原 `UserSetting.json`、`History.json`、`Bookmark.json`、`Foldres.json`、`QuicAccess.json`及全局`.nvpls`为对应模块权威数据。不修改旧Windows或NeeView.Portable目录，不直接自动导入旧状态。Mac补充原Props不能无歧义编码的false-wide值；半页恢复沿原条目语义，不再使用早期MacPagePart扩展。完整旧版本迁移和路径映射待P5。

默认A方案Left下一帧、Right上一帧；单张步进与帧步进分开，键位按A/B/C、阅读方向及自定义差分计算。查看器Up/Down前后书；列表/树/排序控件方向键隔离，书架Enter才打开。文本输入隔离阅读键，Command+O/W/Q用于打开/关闭/退出。普通滚轮按绑定进入原NScroll，启用连续轮滚时按原优先规则平移；AppKit精确滚动/捏合独立处理，右键方向手势释放执行。真实鼠标/触控板操作待用户验收。

书架和页面列表默认位于原左栏，信息默认位于原右栏；边栏可跨栏重排、拖拽组合/拆组、调整比例、五区自动隐藏、浮动/关闭/重开/停靠。主题/布局调整入口见frontend-boundaries.md。

## 发布与验收

```sh
python3 scripts/package_macos.py --dotnet dotnet
python3 scripts/package_macos.py --dotnet dotnet --sign 'Developer ID Application: …' --notary-profile NeeView
```

只发布正式ARM64.app，先要求完整Xcode。签名公证由用户环境凭据提供，脚本不创建/换号/改全局配置。依赖清单取实际发布输出并保留许可。P5才执行正式发布。

各批正式运行截图与Headless截图分别留证；已有组合/比例、页选择/历史、书架/排序保存、前后书及部分Windows复演记录保留，见[设备输入](acceptance/p2-device-input-runtime.md)与[设备资源](acceptance/p2-device-resources-runtime.md)。P3/P4尚需集中验证真实剪贴板/Finder/废纸篓、分类和覆盖恢复、完整焦点/弹出层、无损Retina、真跨卷/NAS、固定Windows动态与长期native。触控板按用户要求跳过，多屏无环境；只读Headless抽样不能外推屏幕P95或长期稳定性。旧重写方案证据只作历史。

当前范围与验收边界见[P4收尾清单](docs/p4-completion-checklist.md)，各阶段契约与证据见[整体架构](docs/architecture.md)和[阶段验收](acceptance/stages.md)。P4开发完成不等于原全部功能已迁移，也不等于整体验收封板。已验证增量按仓库规范提交并推送；Developer ID、公证与发布尚未执行。

2026-10-05 最新[剩余设备验收](acceptance/p34-final-runtime.md)补齐隔离 ZIP 真删除/重启及固定 Windows 整书/删除子集；真实 NAS 恢复和 popup 方向键隔离失败，新增两项[已知问题](docs/known-issues.md)。长期资源及 Retina 浮窗缺陷仍未修，完整停靠/自动隐藏和有效屏幕 P95 未通过；原 Profile 与十个共享挂载已核对恢复/保留。P3/P4 保持未封板。
