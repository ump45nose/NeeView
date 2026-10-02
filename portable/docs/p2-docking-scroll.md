# P2 第二批：原侧栏组合与滚动翻页

日期：2026-10-02。承接首批 `3fdd706f8`，原 Windows 固定基线 `c5c398d89`。这是 P2 的可运行增量，P2 尚未全部完成。

## 职责与依赖

迁移用户要求的左右侧栏拖拽组合及原滚动翻页。保留三个生产项目、原阅读内核和 JSON 格式，不引入停靠框架、数据库或第二入口。

Engine 的 `LayoutPanelManager` 保存原面板组数据和移动规则；`NScroll`、`ScrollResult`、`DragArea` 与 `PageFrameScrollControl` 计算原滚动向量、边界及停顿。MacOS 的 `SidePanelPresenter` 负责内容排布和输入表现，`ReaderView` 应用 Engine 计算结果并调用既有 `BookOperation.MoveAsync`。

原出处及固定 SHA256 见 [source-migration.json](source-migration.json)。原 LayoutPanel/Collection/DockPanelContent/Manager 是移除 WPF 控件的子集适配；NScroll、ScrollResult 和 DragArea 数值算法直接迁入。原 `PageFrame.Direction` 表示书籍阅读方向，帧移动方向由 `BookOperation.MoveDirection` 分开保留，视口重算不能丢失反向半页位置。

## 契约与业务规则

### 侧栏

- 默认保留原左三/右六面板。图标栏移动 leader 时携带整组；其他成员拖标题到图标栏时拆出独立组。
- 拖到内容只移动单个成员；已有多项组沿当前方向分半，单项组按离中心较远的轴分半。同组重排，跨组只均分目标项权重，保留其他成员比例。
- 原组是横向/纵向分割布局，未另建标签页或中心合并逻辑。插入预览不修改实际模型，有效释放时一次提交。
- 使用原 `Config.Panels.Layout.Docks.Left/Right.PanelLayoutV2`、`SelectedItem` 和 `Panels[key].GridLength`。例如 `Vertical:FileInformationPanel,PageListPanel`，比例采用星号权重。
- 九个内容控件只创建一次，跨栏移动复用并显式绑定表现模型。`PanelsRefreshed` 只改变表现，不发布阅读刷新；实际面板所属栏用于菜单勾选和自动隐藏。
- 未迁入面板保留可拖放、可选择的阶段占位；其菜单业务命令继续受真实能力控制。

### 滚动

- 五种原模式：NType、ZType、Diagonal、Horizontal、Vertical。分段步幅、终端 `SnapZero`、阅读方向和换行顺序不重写。
- 保留原参数默认值：NType、Scroll=1、EndMargin=10、LineBreakStopTime=0、LineBreakStopMode=Line、PagesAsOne=false。参数从原 `Commands[name].Parameter` 差分读取，支持枚举字符串/数字与旧字段映射；保存不删除未知参数。
- 按原 RepeatLimiter 顺序先判断间隔、再重置时间、再计算换行/终止。停顿时返回无动作；非终止只平移，终止才进入原帧范围导航。
- 原 NType/ZType 使用传入 `EndMargin`；其他模式按原算法自己的参数处理，不统一改变默认容差。
- 切书或帧范围变化按阅读/移动方向进入起点或尾部，普通刷新与手工缩放保留当前平移。精确滚动和拖动使用原 `DragArea.SnapView` 约束边界，适合窗口的小图居中。

## 状态与资源生命周期

Layout 属于窗口表现模型，关闭前保存原 JSON。指针属于一次拖动：超过阈值时锁定两栏自动隐藏，结束、Escape、捕获丢失或关闭时释放。布局重建解除旧父容器，复用内容并重新放入目标；隐藏/脱离视觉树的缩略图取消资源需求。

滚动命令使用查看器串行入口，导航仍由 BookOperation 协调。显示需求继续使用既有 revision、像素租约和取消链路；新书不能被旧解码结果覆盖。滚动平移不重新枚举来源或创建新阅读状态体系。

## 错误与边界

无效落点、同面板目标、Escape 和捕获丢失保留布局。布局字符串解析错误明确失败；重复/未知面板键不生成重复控件，缺失原面板补回默认栏。未支持的 Windows 窗口字段通过扩展数据保留。

未迁入：浮动窗口、原 V0/V1 布局导入、完整自动隐藏细节、全景 PagesAsOne 行为、完整滚动参数编辑、复杂鼠标组合及完整输入作用域。这些不能因 JSON 字段保留而算作已支持。

## 测试与验收

[独立构建/测试记录](../acceptance/p2-docking-validation.json)：49 项自动测试通过；Engine 构建、正式源码 Library 检查、正式 Mac `.app` 构建和本地签名严格校验分别通过。

测试覆盖五模式几何、RTL、终端容差、注入时钟的换行停顿；组移动/拆组/组合/唯一性/方向规则、原 JSON 选择与比例和未知字段；实际 Headless 拖放、跨栏、Escape/自动隐藏恢复，纯布局不发布阅读刷新；查看器先滚动后翻页、反向进入尾部、小图直接翻页/边界约束与反向半页视口重算。

[真机记录](../acceptance/p2-docking-macos-runtime.md) 单独验证组合/拆组/跨栏、比例及选择重启恢复、未迁入面板占位和阅读恢复。Headless 图、正式 Mac 图、Windows 对照和用户验收分开判断。真实滚轮/触控板、IME、NAS、长期内存和完整显示 P95 未在本批完成。

## 后续扩展

继续迁移 P2 的完整胶片条、常用导航、书签操作和输入能力；按原 Layout 和阅读控制链补充浮动/旧布局兼容与参数编辑。外观调整仍在 XAML/主题/Presenter 中完成，不改变上述阅读和保存规则。
