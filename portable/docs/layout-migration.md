# 原布局与面板迁移表

原布局出处为 `NeeView/MainWindow/MainWindow.xaml`、`NeeView/SidePanels/SidePanelFrameView.xaml`，主题出处为 `NeeView/Styles/Colors.xaml`、`IconGeometries.xaml`。窗口壳是区域转换，尚未宣称所有原交互已经还原。固定 Windows 截图/运行材料待取得。面板默认分组直接取自 `CustomLayoutPanelManager.cs:37-53`；原默认右栏选目标文件夹，该模块待P4，P1暂显示信息面板并保留原位置的禁用入口。

| 原区域 | Mac 入口 | 当前状态 |
|---|---|---|
| DockMenuSocket / 地址栏 | MainWindow.axaml 顶部 Menu/AddressBar | 核心菜单、地址打开；完整原菜单配置待P2/P5 |
| SidePanelFrameView 左右栏 | SidePanelFrame 七列、左右rail | 原左3/右6入口、41 DIP栏/36 DIP图标，分隔拖动、显隐、基础自动隐藏 |
| MainViewSocket | ReaderView | 原页框绘制、缩放/平移、当前帧和邻图预取 |
| DockFilmStripSocket | 同名底部插槽 | 原位置保留；胶片条待P2，高度0 |
| DockPageSliderSocket | PageSliderView | 位置滑条与页码，导航防抖 |
| DockStatusArea / 覆盖层 | 状态文本/MessageLayer | 当前条目、模式、方向、错误/加载 |
| 设置左导航/搜索、右内容 | SettingsWindow.axaml | P1当前/默认阅读设置；其他页入口禁用 |
| 停靠、拖动、自动隐藏详细规则 | 原SidePanel控件 | 完整交互待P2/P5；基础显隐不等同完整停靠 |

| 原面板 | 当前区域/入口 | 当前状态 |
|---|---|---|
| FolderPanel | 左栏 | 当前目录直接子目录、上一级和双击打开；树/延迟展开待P2/P3 |
| HistoryPanel | 左rail历史 | 待P2，禁用；历史JSON已写入 |
| BookmarkPanel | 右rail书签 | 待P2，禁用 |
| PlaylistPanel | 右rail播放列表 | 待P5，禁用 |
| DestinationFolderPanel | 右rail目标文件夹 | 待P4，禁用；两区配置/九数字命令已登记 |
| PageListPanel | 左栏 | 虚拟化名称列表和定位；缩略图待P2/P3 |
| FileInformationPanel | 右栏 | 条目、尺寸、字节、来源和解码错误 |
| NavigatePanel | 右rail导航器 | 待P2，禁用 |
| ImageEffectPanel | 右rail效果 | 待P5，禁用 |

Headless 测试检查区域顺序、栏宽、图像绘制、调整列宽/显隐保持、输入作用域及设置窗口装载。截图见 `../acceptance/p1-layout.png`。截图是正式XAML和Skia渲染的证据，未在正式Mac应用运行，不能替代原版动态对照或用户视觉验收。
