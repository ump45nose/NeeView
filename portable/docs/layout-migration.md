# 原布局与面板迁移表

原布局出处为 `NeeView/MainWindow/MainWindow.xaml`、`NeeView/SidePanels/SidePanelFrameView.xaml`，主题出处为 `NeeView/Styles/Colors.xaml`、`IconGeometries.xaml`。窗口壳是区域转换，尚未宣称所有原交互已经还原。固定 Windows 截图/运行材料待取得。面板默认分组直接取自 `CustomLayoutPanelManager.cs:37-53`；原默认右栏选目标文件夹，该模块待P4，P1暂显示信息面板并保留原位置的禁用入口。

| 原区域 | Mac 入口 | 当前状态 |
|---|---|---|
| DockMenuSocket / 地址栏 | MainWindow.axaml 顶部 Menu/AddressBar | 完整原八组默认菜单、地址打开；未迁移能力禁用占位，动态菜单配置待后续 |
| SidePanelFrameView 左右栏 | SidePanelFrame 七列、左右rail | 原左3/右6入口、41 DIP栏/36 DIP图标，分隔拖动、显隐、基础自动隐藏 |
| MainViewSocket | ReaderView | 原页框绘制、缩放/平移、当前帧和邻图预取 |
| DockFilmStripSocket | 同名底部插槽 | 原位置可见胶片条、独立选择/确认、三滚轮、详情和首尾居中；播放列表标记/完整自动隐藏待迁入 |
| DockPageSliderSocket | PageSliderView | 位置滑条与页码，导航防抖 |
| DockStatusArea / 覆盖层 | 状态文本/MessageLayer | 当前条目、模式、方向、错误/加载 |
| 设置左导航/搜索、右内容 | SettingsWindow.axaml | 当前/默认阅读设置、可搜索235命令键位编辑；其他页占位 |
| 停靠、拖动、自动隐藏详细规则 | LayoutPanelManager / SidePanelPresenter | 跨栏重排、分割组合/拆组、比例/选择恢复、拖动锁定已接入；浮动窗口及完整细节待后续 |

| 原面板 | 当前区域/入口 | 当前状态 |
|---|---|---|
| FolderPanel | 左栏 | 原普通书架目录/归档混合列表、默认排序、独立浏览/同步/刷新及Enter/双击打开；树/封面/每目录参数待迁 |
| HistoryPanel | 左rail历史 | 访问时间排序、搜索、双击打开并恢复原位置 |
| BookmarkPanel | 右rail书签 | 原树基础编辑/书籍切换/双击打开；完整服务待迁入 |
| PlaylistPanel | 默认右rail播放列表 | 待P5，可选择占位 |
| DestinationFolderPanel | 默认右rail目标文件夹 | 待P4，可选择占位；两区配置/九数字命令已登记 |
| PageListPanel | 左栏 | 虚拟化名称列表和定位；缩略图待P2/P3 |
| FileInformationPanel | 右栏 | 条目、尺寸、字节、来源和解码错误 |
| NavigatePanel | 右rail导航器 | 当前页缩略图与点击定位，图像外留白不触发 |
| ImageEffectPanel | 默认右rail效果 | 待P5，可选择占位 |

Headless 测试检查区域顺序、栏宽、图像绘制、调整列宽/显隐保持、输入作用域及设置窗口装载。P1 截图见 `../acceptance/p1-layout.png`，P2 首批见 `../acceptance/p2-layout.png`；第二批另存 `p2-docking-layout.png` 和 `p2-docking-reading-layout.png`，不覆盖历史截图。组合、跨栏和取消经过实际 Headless 指针测试，正式 Mac 已验证组合/拆组、跨栏、比例及重启恢复，见 `../acceptance/p2-docking-macos-runtime.md`。用户已认可总体布局；新增交互仍待用户验收，不能替代 Windows 动态对照。

## 左右侧栏拖拽组合当前规则与边界

- 图标跨栏移动和重排；原 leader 图标携带整组，非 leader 标题拖到图标栏拆出单面板。
- 拖入内容形成横向/纵向分割组；沿用原分半算法，多项组保留方向，同组重排，跨组仅均分目标项权重。原模型并非标签页，不另增中心合并分支。
- 组内分隔比例、组顺序、选择及窗口重开恢复；保存原 PanelLayoutV2/SelectedItem/GridLength。
- 拖动期间两栏自动隐藏锁定；Escape、捕获丢失和无效落点取消，不改变布局。
- 未迁入面板可组合、选择并显示明确占位。
- 浮动窗口、旧 V0/V1 布局导入及完整自动隐藏细节未迁入；未知旧字段只保留，不宣称其行为已支持。

依据原 CustomLayoutPanelManager、SidePanelFrameView、SidePanelIcon、SidePanelViewModel、LayoutDockPanel 与 SidePanelDropAcceptor；不增加大型停靠框架。
