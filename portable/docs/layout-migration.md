# 原布局与面板迁移表

原布局出处为 `NeeView/MainWindow/MainWindow.xaml`、`NeeView/SidePanels/SidePanelFrameView.xaml`，主题出处为 `NeeView/Styles/Colors.xaml`、`IconGeometries.xaml`。窗口壳是区域转换，尚未宣称所有原交互已经还原。固定 Windows 截图/运行材料待取得。面板默认分组直接取自 `CustomLayoutPanelManager.cs:37-53`；原默认右栏选目标文件夹，该模块待P4，P1暂显示信息面板并保留原位置的禁用入口。

| 原区域 | Mac 入口 | 当前状态 |
|---|---|---|
| DockMenuSocket / 地址栏 | MainWindow.axaml 顶部 Menu/AddressBar | 完整原八组默认菜单、地址打开；未迁移能力禁用占位，动态菜单配置待后续 |
| SidePanelFrameView 左右栏 | SidePanelFrame 七列、左右rail | 原左3/右6入口、41 DIP栏/36 DIP图标，分隔拖动、显隐、基础自动隐藏 |
| MainViewSocket | ReaderView | 原页框绘制、缩放/平移、当前帧和邻图预取 |
| DockFilmStripSocket | 同名底部插槽 | 原位置可见胶片条、方向与选择定位、基础自动隐藏；完整预览模式待迁入 |
| DockPageSliderSocket | PageSliderView | 位置滑条与页码，导航防抖 |
| DockStatusArea / 覆盖层 | 状态文本/MessageLayer | 当前条目、模式、方向、错误/加载 |
| 设置左导航/搜索、右内容 | SettingsWindow.axaml | 当前/默认阅读设置、可搜索235命令键位编辑；其他页占位 |
| 停靠、拖动、自动隐藏详细规则 | 原SidePanel控件 | 用户追加拖拽重排/跨栏/自动组合/边缘分组目标；P2后续独立增量，进入P3前处理 |

| 原面板 | 当前区域/入口 | 当前状态 |
|---|---|---|
| FolderPanel | 左栏 | 当前目录直接子目录、上一级和双击打开；树/延迟展开待P2/P3 |
| HistoryPanel | 左rail历史 | 访问时间排序、搜索、双击打开并恢复原位置 |
| BookmarkPanel | 右rail书签 | 原树基础编辑/书籍切换/双击打开；完整服务待迁入 |
| PlaylistPanel | 右rail播放列表 | 待P5，禁用 |
| DestinationFolderPanel | 右rail目标文件夹 | 待P4，禁用；两区配置/九数字命令已登记 |
| PageListPanel | 左栏 | 虚拟化名称列表和定位；缩略图待P2/P3 |
| FileInformationPanel | 右栏 | 条目、尺寸、字节、来源和解码错误 |
| NavigatePanel | 右rail导航器 | 当前页缩略图与点击定位，图像外留白不触发 |
| ImageEffectPanel | 右rail效果 | 待P5，禁用 |

Headless 测试检查区域顺序、栏宽、图像绘制、调整列宽/显隐保持、输入作用域及设置窗口装载。P1截图见 `../acceptance/p1-layout.png`，P2另存 `../acceptance/p2-layout.png`。用户已认可总体布局；尚未将拖拽组合标为通过。正式Mac应用已启动和绘制，真机截图另存为 `../acceptance/p1-macos-runtime.png`；两类证据不能替代Windows动态对照或用户视觉验收。

## 左右侧栏拖拽组合后续验收

- 图标跨左右栏移动和重排，拖入现有面板组自动组合。
- 拖到边缘形成独立组；组间分隔比例、顺序、选择和窗口重开恢复。
- 自动隐藏面板拖动期间保持展开；取消不破坏布局。
- 未迁入面板保留可识别占位。

依据原 CustomLayoutPanelManager、SidePanelFrameView、SidePanelIcon、SidePanelViewModel、LayoutDockPanel 与 SidePanelDropAcceptor；不增加大型停靠框架。
