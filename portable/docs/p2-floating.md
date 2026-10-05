# P2 第二十二批：原侧栏浮动宿主

## 职责、依赖与契约

Engine.LayoutPanelManager迁入原StandAlone/Open/OpenWindow/OpenDock/Close关系；浮动成员拆成单面板，保留原栏位置。WindowPlacement/WindowStateEx保持五段物理像素字符串和枚举。当前打开集合保存到原Windows.Panels，与关闭后保留的Panel.WindowPlacement分开。未知位置字符串、Windows字段、未识别面板和AlternativePanelSource继续保存。

SidePanelPresenter只管理宿主、坐标、拖放和输入转交；FloatingPanelWindow.axaml负责结构。原九个内容控件只创建一次，不复制业务集合、不增加生产项目或停靠依赖。原NameScope继续引用正式控件，视觉父级迁移不使事件和命令失效。

## 状态与资源生命周期

浮动先保存组权重、拆组、清除该组选择并解除旧视觉父级，再创建或复用Owner为主窗的宿主。主窗Opened后才显示浮窗。关闭快照位置、终止拖拽/捕获、解除事件和内容父级、删除打开集合；位置保留，再点图标仍浮动。停靠/拖回删除打开集合、清除有效位置、选中原/目标组，唯一内容重新停靠。

主退出先取消拖拽、快照位置、冻结浮窗，再进入原四JSON保存。失败恢复同一宿主、内容、租约和操作状态；成功后才关闭和释放。退出销毁宿主不删除已保存Windows集合，重启据此恢复。

## 业务规则与错误

跨窗拖回经PointToScreen/PointToClient统一物理坐标，不对不同视觉树使用TranslatePoint。内容命中沿原分半组合，图标栏沿原顺序插入；无效落点/Escape/捕获丢失只取消。浮窗承载单面板，不增加浮动分组模型。标题单击不关闭；图标单击保持切换；右键提供浮动、停靠、关闭。

非模态浮窗不触发主窗自动隐藏的对话框锁，也不禁止主查看器手势；真实对话框仍锁定。键盘沿同一命令入口按实际窗口焦点区分文本、列表和菜单。Command+W关闭当前浮窗，Command+Q退出应用。

客户端物理像素尺寸在窗口Opened后按实际RenderScaling转为DIP，恢复中的事件不回存中间值；工作区/位置单独使用Screen.Scaling的桌面单位（macOS为1，不能替代Retina绘制缩放）。客户端尺寸按RenderScaling回存；最大化保留正常位置。最小化/Windows FullDesktop不作为启动状态，保持可访问。Windows外框和Mac客户端尺寸有平台差别；单屏Retina重开/重启尺寸复验通过，详见[缺陷修复](../acceptance/p34-fixes-runtime.md)，多屏与完整Windows浮窗动态对照仍待验。

## 测试、验收与扩展

七项专项覆盖原状态、JSON/未知字段、唯一内容/选择、菜单/标题单击、最大化、跨窗拖回/Escape、文本/列表/Enter/Command+W、自动隐藏、退出保存失败/重启、导航器释放和断开显示器恢复。全量、正式构建及签名独立留证。

Headless装载正式浮窗XAML，不启动正式应用或访问用户数据。独立复核提出的FindControl失效假设由原NameScope引用与浮动历史Enter实测排除。真实捕获、IME/触控板、多屏/Retina与Windows动态分别待验。旧V0/V1导入保留为P5目标。
