# P2 阅读与导航实施索引

更新：2026-10-04。P2开发范围完成，整体验收未封板。保留三项目及原 Book/Page/BookOperation/Command/Config/SaveData/Archive 关系。当前范围与验收边界见[收尾清单](p2-completion-checklist.md)；下面保留首批出处，并索引后续实际实现。

## 首批范围与出处

完整原八组菜单树与禁用占位；普通/固实 RAR、7z；访问历史搜索/打开；原书签树字段和基础文件夹/更名/移除；可见胶片条与单页导航器；全部原命令的键位编辑；官方 AppKit 精确滚动与捏合桥接。源文件出处在 source-migration.json，契约和生命周期在 M02–M06/M08。

## 原行为与适配

菜单从原 CreateDefault 逐项转换，不按已实现命令过滤。书签迁入原 Node 字段与遍历，部分 Collection 操作适配原 JSON。FilmStrip 保留原部分配置及方向，显示采用共享加载工厂和可见窗口控件。归档后端在原 Archive 抽象下替换，独立顺序 Reader 避免关闭源索引。没有新增阅读内核、数据库、事件总线、停靠框架或第二个入口。

## 用户追加目标

1. 原项目已有但未迁入的菜单能力保留可识别占位。本批已落地默认菜单树；动态菜单配置、完整参数和能力仍按清单迁入。
2. 左右侧栏拖拽自动组合/停靠在第二批独立增量中接入：跨栏移动/排序、拖入现有组形成原分割组、成员拆组、组间比例与布局恢复、拖动期间自动隐藏锁定，未迁入面板仍可选择占位。详见 [第二批契约](p2-docking-scroll.md)。

原依据：CustomLayoutPanelManager.cs:37-53、SidePanelFrameView.xaml:74-98/134-160、SidePanelIcon.xaml:44、SidePanelViewModel.cs:19-35/212-228/257-269、LayoutDockPanel、SidePanelDropAcceptor。首批只有固定七列和宽度拖动；第二批有独立原布局模型和实际拖放测试，不能把首批证据算作拖拽通过。

## 后续实施契约

| 批次 | 迁移功能与设计 |
|---|---|
| 2–3 | [侧栏组合/原NScroll](p2-docking-scroll.md)、[共享页选择/胶片条/滑条/指定页及两种导航历史](p2-selection-navigation.md) |
| 4–6 | [书架/前后书/文件夹页](p2-bookshelf-navigation.md)、[书签集合/登记/恢复](p2-bookmark-operations.md)、[历史导航/管理](p2-history-list.md) |
| 7–9 | [直接页号/滑条](p2-slider-input.md)、[全局播放列表/页标记](p2-playlist.md)、[五区自动隐藏/显示锁/全屏](p2-autohide.md) |
| 10–13 | [书签目录/排序](p2-bookmark-navigation.md)、[书签搜索](p2-bookmark-search.md)、[历史搜索](p2-history-search.md)、[原数量/期限限制](p2-history-retention.md) |
| 14–17 | [每目录参数/种子](p2-folder-parameters.md)、[真实Folder/Archive页/父子书/递归](p2-book-hierarchy.md)、[A/B/C/鼠标组合](p2-mouse-input.md)、[变换/BaseScale/分页参数](p2-view-transform.md) |
| 18–21 | [页尾/锁定/Unload](p2-book-controls.md)、[书架书签互联](p2-bookshelf-bookmarks.md)、[历史登记/可靠清理](p2-history-policy.md)、[四列表模板/可见封面](p2-list-templates.md) |
| 22–25 | [单面板浮动宿主](p2-floating.md)、[原方向手势](p2-direction-gestures.md)、[Scroll/Fade/Hover/连续轮滚](p2-animation.md)、[资源测量/优化和原命令参数收尾](p2-resources.md) |

各批文档记录当时的改造与证据；早期“待迁”不作为当前状态，当前状态以整体架构、收尾清单、命令/布局表及模块契约为准。没有第二阅读内核、数据库、事件总线或Preview入口。P3连续/瀑布流/完整目录树/逐页缩略完善，P4分类，P5完整旧数据导入/高级内容/发布独立推进。旧V0/V1布局导入、高级树布局、主视图浮动等原能力继续明确占位。

## 验证

最终串行构建、355项自动回归、Headless软件渲染与本地签名原始输出见[p2-completion-validation.json](../acceptance/p2-completion-validation.json)，静默收尾及设备边界见[收尾记录](../acceptance/p2-completion-runtime.md)。首批及早期正式运行证据保留，不继承为新增交互已验。固定软件完整帧已测，真实鼠标/触控板/IME/Retina/多屏、Finder/NAS、长期native、屏幕性能、Windows动态对照及用户新增交互验收仍待集中进行。默认静默，不抢用户焦点。
