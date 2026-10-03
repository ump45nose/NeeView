# P2 第九批：原窗口自动隐藏与显示控制

本批从原 MainWindowController/MainWindowModel、AutoHideBehavior 与配置迁移显示链；不增加阅读内核、事件总线、WPF 模拟层或窗口框架。原 Windows 代码只作固定对照，动态截图/运行对照仍待取得。

## 职责、依赖与契约

Engine 保存原 `AutoHide`、`Window`、`MenuBar`、`Panels`、`Slider`、`FilmStrip` 设置。`AutoHidePresenter` 是 Mac 表现适配，管理同一窗口的五区、单调时钟、实际焦点、捕获和弹出层。`AutoHideVisibility` 仅移植原延迟决策，便于固定时钟测试。XAML 保留区域与内容插槽，主题仍独立。

| 契约 | 行为 |
|---|---|
| ReaderWorkspaceViewModel.CanHideMenu/LeftAutoHide/RightAutoHide/CanHideSlider/CanHideFilmStrip | 原资格计算，窗口自动隐藏模式与各区配置结合 |
| SetChromeVisibility / ChromeRefreshed | 发布最终表现，不发布正文 Refreshed、不重建组内容，不写 JSON |
| AutoHidePresenter.Refresh | 窗口状态或配置变化时刷新资格、覆盖插槽与窗口 Topmost |
| AutoHidePresenter.HandleKey / LeaveVisibleLocked / ShowHiddenPanels | 输入先解除旧锁，再执行原一次显示命令；连续触发保留原切换记忆 |
| HandlePlatformGesture | 窗口身份、实际控件命中及正文范围同时成立才处理精确滚动/缩放 |
| SaveData | 原分支合并保存及原三 JSON 事务；未知字段保留 |

## 原业务规则

- Window 普通/最大化默认不开自动隐藏模式，全屏默认开启。Mac 无 FullDesktop 对应宿主，本批保留字段/禁用命令，不模拟 Windows 全桌面。
- 菜单、左右栏和滑条各自采用“显式隐藏，或该区在全局模式中允许隐藏”。胶片条仅在滑条不能隐藏时独立隐藏；滑条隐藏时整个底组一起出现。总开关和有无页面继续限制最终可见性。
- 自动隐藏区域移到覆盖布局；资格改变可以重新分配正文，但同资格下隐藏/弹出不挤压正文、不改变阅读位置。侧栏的原选择/宽度/组引用保留，侧栏图标总开关独立。
- 侧栏内容和图标采用原 SidePanelMargin：菜单/滑条具备隐藏资格时保留上32、下20 DIP默认余量，余量不随悬停改变；原 ConflictTopMargin/BottomMargin 可从 JSON 配置，本批未增加这两个值的设置控件。
- 原 ToggleHideLeftPanel/RightPanel/Panel 改变自动隐藏资格，不等于关闭所选面板。原 ToggleVisibleSideBar 只切换图标；原面板选择命令仍控制实际选择/开闭。重复的早期 Mac 自动隐藏菜单退出，使用原命令入口。
- 当前区域或附加图标栏优先命中；窗口左右/上下边缘按原32 DIP默认判定，上边缘默认为 AllowPixel，下边缘默认 Allow。冲突 AllowPixel 使用原1.5 DIP边缘阈值。
- 默认隐藏1秒、显示0秒；重复请求只缩短截止时间，不无限延长。零隐藏延迟至少1ms，按键在所属区可重新延长隐藏。边缘离开取消未完成显示。
- 显式一次显示锁、文本/焦点锁、弹出层及控件捕获立即显示。拖动面板锁定两侧；滑条与分隔拖动锁定所属区域。弹出层按 Avalonia 的真实 PlacementTarget 归属，无全树定时扫描。
- 原 WPF 焦点作用域映射为 Mac 窗口内的当前/记忆焦点：逻辑模式在原生失活/弹出临时无焦点时保留，焦点移回正文即更新；这属于平台适配，非完整 WPF 焦点作用域模拟。
- 正常键盘、鼠标按下、滚轮及精确手势解除一次显示锁；单纯移动指针不解除。SetFullScreen/CancelFullScreen/ToggleFullScreen 保留进入前普通/最大化状态；Topmost 使用框架真实属性。

## 状态、资源与错误

区域可见性、待完成截止时间、一次显示锁及焦点记忆只属于当前窗口，退出后清空。40ms UI 背景计时器仅评估五区、当前焦点、捕获和已打开的弹出层，不枚举页面或读取来源。最小化期间冻结可见变化；恢复重新评估。正常退出先沿既有可靠保存，成功才停计时器并解除订阅；失败保留窗口和显示控制供重试。

覆盖插槽复用唯一胶片条控件；图像租约仍归原 ThumbnailView/BitmapFactory。配置或来源改变仍沿既有显示刷新，悬停只更新表现。设置“窗口/自动隐藏”和胶片条/滑条页面先编辑表单，取消不应用，保存写原字段。错误显示仍归主窗口；隐藏时通过重新显示区域查看，未新增通知通道。

设置保存沿用当前设置窗口“应用运行时值后持久化”的顺序；写入失败保留表单供重试，不提供失败提交后的运行时撤销。普通取消发生在应用前。三 JSON 的文件事务失败仍回滚文件及 SaveData 权威节点；完整设置提交/失败后取消的一致性处理属于后续设置完善项，不把本批普通取消测试扩大为该失败流程验收。

加载兼容原 AutoHideHitTestMargin、AutoHideConfrictTop/BottomMargin 拼写；原新字段优先。早期 Mac IsLeftAutoHide/IsRightAutoHide 映射到原 IsHideLeftPanel/RightPanel，保存时移除两别名，避免两个权威值；旧根 IsAddressBarEnabled 同理迁到 MenuBar。其他未知/未迁 Windows 字段仍保留。

## 测试、验收与扩展

固定时钟覆盖延迟/取消/重复/按键延长/零延迟，JSON覆盖原字段/旧别名/优先级/未知字段。正式 XAML 覆盖窗口状态、覆盖区域不改变正文、原命令含义、真实文本/弹出层/捕获、显示锁路由、设置取消/保存、边角冲突、精确手势排除覆盖层；原拖拽与阅读全量回归。

最终133项自动回归、Engine/正式入口Library/正式.app及本地ad-hoc严格签名通过，写入 [构建记录](../acceptance/p2-autohide-validation.json)。正式普通窗口、显示锁、文本焦点、全屏/取消、设置取消/保存、方向键和重启恢复见 [真机记录](../acceptance/p2-autohide-macos-runtime.md)。本批截图只用 `p2-autohide-*`，不覆盖旧阶段。不能用 Headless 代替真人触控板/鼠标/IME、Retina、多屏或 Windows 动态对照。P2 未封板。

原浮动窗口、完整输入手势、窗口位置/多屏恢复、窗口最大化/最小化命令、FullDesktop 等仍独立待迁。普通 macOS 系统窗口按钮继续可用，不把按钮存在当作原命令迁移完成。
