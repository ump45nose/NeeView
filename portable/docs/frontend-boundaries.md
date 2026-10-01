# 前端边界与页面调整指南

目标：页面布局、视觉、交互配置可以独立迭代，保持阅读规则和文件操作语义稳定。

## 结构

```mermaid
flowchart TB
  Shell[MainWindow.axaml 页面结构] --> Window[MainWindow 宿主适配]
  Window --> Navigation[ReaderNavigationPanel]
  Window --> Destinations[ReaderDestinationPanel]
  Window --> Viewer[ReaderView]
  Window --> Settings[SettingsWindow]
  Navigation --> VM[ReaderWorkspaceViewModel]
  Destinations --> VM
  Window --> VM
  Input[ReaderInputRouter] --> VM
  VM --> App[Application 接口]
  Viewer --> App
  Viewer --> Geometry[ReaderLayoutCoordinator 后台几何]
  Theme[ReaderTheme.axaml / ReaderLabels] -.表现资源.-> Shell
  Theme -.样式类.-> Navigation
  Theme -.样式类.-> Destinations
  Theme -.样式类.-> Settings
```

Desktop 的编译依赖仅指向 Application；它不引用具体内容、解码、SQLite 或 macOS 适配项目。启动层负责注入实现。

## 调整入口

| 要调整的内容 | 修改位置 | 稳定边界 |
|---|---|---|
| 侧栏/工具栏/底部导航位置 | MainWindow.axaml | 保留命名插槽与按钮命令 Tag |
| 页面/目录/历史/书签模板 | ReaderNavigationPanel | NavigationData、FolderNode、BookmarkNode |
| 分类上下区和按钮外观 | ReaderDestinationPanel | DestinationData、SectionRatio、工作区动作 |
| 设置页面布局 | SettingsWindow | SettingsSelection、AppSettings/ReaderOptions |
| 主图绘制/占位/选择效果 | ReaderView、Styles/ReaderTheme.axaml | LayoutSnapshot 与像素租约 |
| 间距、缩略图大小、设置页尺寸 | Styles/ReaderTheme.axaml | 稳定 reader-* 样式类与控件类型 |
| 工具栏文案及枚举名称 | MainWindow.axaml、ReaderLabels | 不更改存储枚举或命令字符串 |
| 键鼠绑定规则 | ReaderInputRouter | ShortcutBinding 与 ExecuteAsync |
| 书签/分类/目录表现逻辑 | ReaderWorkspaceViewModel.Panels | 应用接口与 IReaderDialogs |

主页面结构已迁入 MainWindow.axaml；MainWindow.cs 负责接口装配、事件适配与生命周期。控件不持有 MainWindow 引用，面板内也不通过全局 Application.MainWindow 寻找服务。ReaderPanelControls 捕获事件异常并交给 ViewModel。其余面板的构造式 UI 可以逐个改为 XAML，保持表现模型/数据契约不变。

## 页面结构与样式入口

- 主页面保留 AddressField、StatusField、PositionField、BodyGrid、ViewerScroll、NavigationHost、DestinationsHost 命名插槽。五列顺序是当前侧栏宽度保存契约；改变该契约时同步宿主适配，其他控件排布可在 XAML 单独调整。
- 工具栏的 Tag 使用稳定命令标识，按钮的文字、顺序和外观可独立调整。Settings/Import 转交宿主交互，阅读按钮进入工作区命令服务。
- 导航条目使用 reader-page-row、reader-page-name、reader-list-label、reader-information；分类使用 reader-destination-label、reader-section-title；按钮统一 reader-action。
- 设置页使用 reader-settings、reader-settings-root、reader-settings-group、reader-policy-*、reader-option-choice 等样式类。缩略图尺寸与查看器色彩由 ReaderTheme.axaml 管理，不在事件代码设置局部值。

样式类定义外观，表现模型定义数据与动作；修改样式不需要修改命令、读取、数据库或文件操作服务。

## 生命周期契约

- ReaderSession 的 Changed 只发布快照。主窗口同步切 UI 线程，再更新查看器和导航值。
- 面板刷新使用窗口互斥、取消源和书籍代次；晚到结果不能更新关闭窗口或新书。
- 同目录翻页/尺寸探测不会重复重建分类列表或扫描目录。
- PanelsChanged 返回可等待 Task；SettingsChanged 在视图端投递 Dispatcher。
- ReaderView 的任务、Bitmap 和像素租约属于控件；DisposeAsync 必须在 Dispatcher 仍运行时等待。
- ReaderLayoutCoordinator 不依赖窗口；后台只计算纯几何，查看器按 revision/generation 接受结果并以最新锚点补偿。
- IReaderDialogs 可用假实现替换；表现逻辑不依赖文件选择器/模态窗口。

## 后续边界

目前 MainWindow 保留文件选择器、导入预览和 settings 窗口装配；进一步替换宿主时可抽交互 presenter。查看器仍适配图像资源和绘制，后台布局协调器已独立；若增加多渲染后端，再抽渲染接口。不要为当前单一实现增加跨模块全局事件总线。

这些是允许的后续独立调整，不要求修改 Core、Application 或持久化 schema。新视觉效果必须复核快捷键作用域、锚点补偿和资源释放。
