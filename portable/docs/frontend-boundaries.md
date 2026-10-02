# 前端独立调整边界

生产界面在唯一 `NeeView.MacOS` 项目内。XAML、主题、表现和绘制各有入口；界面变化不改阅读规则、排序、保存格式和文件操作语义。

| 调整 | 入口 | 保持的契约 |
|---|---|---|
| 主窗口区域和面板排布 | Views/MainWindow.axaml | 原命名插槽、命令 Tag、区域关系 |
| 颜色、图标路径 | Styles/NeeViewResources.axaml | 原资源 key；颜色及路径来自原 XAML |
| 间距、控件模板、外观 | Styles/NeeViewTheme.axaml | 样式类与资源引用 |
| 页面/目录选择、侧栏显隐 | ViewModels/ReaderWorkspaceViewModel.cs | 原 Page 与 BookOperation；纯表现刷新不请求图片 |
| 绘制、焦点、拖动、缩放 | Views/ReaderView.cs | 原 PageFrame、像素租约、revision |
| 完整菜单数据/呈现 | Engine/Menu/default-menu.json、Views/MenuPresenter.cs | 原节点、顺序、禁用占位与稳定命令名 |
| 菜单执行、键鼠、对话框、窗口关闭 | Views/MainWindow.axaml.cs | 稳定命令名、Engine/系统契约 |
| 设置页导航/内容结构 | Views/SettingsWindow.axaml | 原 BookSettingConfig 和恢复策略 |
| 具体后端及实例装配 | MacApp.cs | 唯一可引用 Backends 的启动装配点 |

视图和表现模型不枚举目录、不解压、不调用 Magick/SharpCompress、不创建平台实现。目录信息由 BookOperation 契约获取；像素由 BitmapFactory 租用。侧栏 Hover/选择只通知表现属性，不发布阅读刷新。Engine 不反向调用控件，也不保留 Avalonia Bitmap。

主图是单绘制控件，不为每页创建图像控件；页面列表使用虚拟化 ListBox。主图及可见缩略图的显示 Bitmap 与像素租约归各查看器所有，先释放 Bitmap 再释放租约。切书/缩放/视口变化用 revision 拒绝旧请求；所有 UI 对象在 Dispatcher 线程修改。

主布局参照原 MainWindow.xaml、SidePanels/SidePanelFrameView.xaml、菜单和 Dock 插槽。41 DIP 侧栏、36 DIP 按钮及转换资源保留。胶片条、历史、书签和导航器已接入首批业务；停靠拖拽、重排/自动组合及完整自动隐藏尚未迁入，详见 layout-migration.md。样式现代化应另做增量，先保留区域和操作流程。

输入设置使用编辑副本，可搜索全部原命令；原复杂绑定保留，新增不可解析输入及冲突阻止保存。输入文本时不响应阅读/数字命令；Command+O/W/Q 仍是系统操作。旧 Control 只规范解析名称，不替换为 Command。未迁入命令不通过相近动作代替：滚动翻页当前禁用，原 NScroll 到边界再翻页属于 P2 后续。正式 AppKit 桥接依据 HasPreciseScrollingDeltas，按窗口身份和查看器区域消费精确滚动/捏合；真实触控板待用户验收。

测试直接装载这些正式 XAML、主题和控件源码。Headless 截图用于检查布局与真实图像绘制，不能证明 Mac 手势、Retina、Finder 或 Windows 动态一致性。
