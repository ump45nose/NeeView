# P2 首批阅读与导航实施记录

日期：2026-10-02。承接用户已认可的总体布局，保留三项目及原 Book/Page/BookOperation/Command/Config/SaveData/Archive 关系。本批为可运行增量，P2 仍在实施中。

## 本批范围

完整原八组菜单树与禁用占位；普通/固实 RAR、7z；访问历史搜索/打开；原书签树字段和基础文件夹/更名/移除；可见胶片条与单页导航器；全部原命令的键位编辑；官方 AppKit 精确滚动与捏合桥接。源文件出处在 source-migration.json，契约和生命周期在 M02–M06/M08。

## 原行为与适配

菜单从原 CreateDefault 逐项转换，不按已实现命令过滤。书签迁入原 Node 字段与遍历，部分 Collection 操作适配原 JSON。FilmStrip 保留原部分配置及方向，显示采用共享加载工厂和可见窗口控件。归档后端在原 Archive 抽象下替换，独立顺序 Reader 避免关闭源索引。没有新增阅读内核、数据库、事件总线、停靠框架或第二个入口。

## 用户追加目标

1. 原项目已有但未迁入的菜单能力保留可识别占位。本批已落地默认菜单树；动态菜单配置、完整参数和能力仍按清单迁入。
2. 左右侧栏拖拽自动组合/停靠在第二批独立增量中接入：跨栏移动/排序、拖入现有组形成原分割组、成员拆组、组间比例与布局恢复、拖动期间自动隐藏锁定，未迁入面板仍可选择占位。详见 [第二批契约](p2-docking-scroll.md)。

原依据：CustomLayoutPanelManager.cs:37-53、SidePanelFrameView.xaml:74-98/134-160、SidePanelIcon.xaml:44、SidePanelViewModel.cs:19-35/212-228/257-269、LayoutDockPanel、SidePanelDropAcceptor。首批只有固定七列和宽度拖动；第二批有独立原布局模型和实际拖放测试，不能把首批证据算作拖拽通过。

## P2 尚待完成

原 NScroll 分页滚动/停顿/五模式及差分参数已在第二批接入，全景 PagesAsOne 和完整参数编辑仍待后续。第三批已接入胶片条选择确认、防抖/去重、详情、三滚轮与滑条联动，以及指定页/步长和两种导航历史，见 [第三批契约](p2-selection-navigation.md)。第四批接入普通书架、前后书和文件夹页，见 [第四批契约](p2-bookshelf-navigation.md)。P2 剩余包括页标记/直接页号输入/历史列表菜单、完整输入作用域/鼠标组合、书签完整操作及真实子书/父书定位。浮动面板/旧布局导入与完整自动隐藏细节仍在清单中。真实鼠标/触控板/IME、NAS、长期内存和原 Windows 动态对照分别验收。各项未完成能力不冒充已迁移。

## 验证

构建/自动测试原始输出见 ../acceptance/p2-validation.json；正式运行见 ../acceptance/p2-macos-runtime.md；阶段状态见 ../acceptance/stages.md。首图/翻页/完整帧 P95 仍为待测目标，未以服务耗时替代显示完成性能。
