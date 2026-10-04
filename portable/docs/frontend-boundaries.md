# 前端独立调整边界

生产界面在唯一 `NeeView.MacOS` 项目内。XAML、主题、表现和绘制各有入口；界面变化不改阅读规则、排序、保存格式和文件操作语义。

| 调整 | 入口 | 保持的契约 |
|---|---|---|
| 主窗口区域和面板排布 | Views/MainWindow.axaml | 原命名插槽、命令 Tag、区域关系 |
| 颜色、图标路径 | Styles/NeeViewResources.axaml | 原资源 key；颜色及路径来自原 XAML |
| 间距、控件模板、外观 | Styles/NeeViewTheme.axaml | 样式类与资源引用 |
| 胶片条显示、可见需求、详情与确认 | Views/ThumbnailView.cs | Engine.PageSelector/FilmStrip；200ms防抖、revision及共享像素租约 |
| 底部页号显示、编辑、焦点与转换 | Views/SliderTextBox.axaml(.cs) | 来源身份＋原始索引回调；宿主进入唯一 BookOperation.JumpAsync，控件不修改书籍/配置 |
| 页面/书架选择、侧栏显隐 | ViewModels/ReaderWorkspaceViewModel.cs | 原 Page 与 BookOperation.Bookshelf；纯表现/书架刷新不请求图片 |
| 面板拖放、组合、分隔与预览 | Views/SidePanelPresenter.cs | Engine.LayoutPanelManager 数据与原布局 JSON；不操作阅读业务 |
| 绘制、焦点、拖动、缩放 | Views/ReaderView.cs | 原 PageFrame、像素租约、revision |
| 完整菜单数据/呈现 | Engine/Menu/default-menu.json、Views/MenuPresenter.cs | 原节点、顺序、禁用占位与稳定命令名 |
| 菜单执行、键鼠、对话框、窗口关闭 | Views/MainWindow.axaml.cs | 稳定命令名、Engine/系统契约 |
| 书签拖动、选择和对话框反馈 | Views/MainWindow.Bookmarks.cs | Engine.BookmarkCollection 与 SaveData；不编写集合/保存规则 |
| 书签登记字段与按钮布局 | Views/BookmarkRegistrationWindow.axaml | 原 BookmarkPopupEdit；取消不提交，已确认目标需重新校验 |
| 播放列表结构、行选择、焦点与输入 | Views/PlaylistView.axaml(.cs)、ViewModels/PlaylistViewModel.cs | 原 PlaylistHub 当前文件/条目及集合；不枚举目录或写文件 |
| 滑条及胶片条页标记 | Views/PageMarkersView.cs、Views/ThumbnailView.cs | 原 BookPageMarker 页索引；标记变化只重绘，不申请正文资源 |
| 五区自动隐藏、覆盖插槽、延迟/焦点/捕获 | Views/AutoHidePresenter.cs | 原Config资格/窗口状态；ChromeRefreshed不刷新正文或写JSON |
| 设置页导航/内容结构 | Views/SettingsWindow.axaml | 原 BookSettingConfig 和恢复策略 |
| 具体后端及实例装配 | MacApp.cs | 唯一可引用 Backends 的启动装配点 |

视图和表现模型不枚举目录、不解压、不调用 Magick/SharpCompress、不创建平台实现。目录/归档候选由 BookOperation.Bookshelf 和 IArchiveFactory.ListBooksAsync 契约获取；像素由 BitmapFactory 租用。侧栏 Hover/选择和临时页选择只通知表现属性，不发布阅读刷新。列表先更新来源再恢复当前Page/FolderItem，避免TwoWay回报抹掉选中项。书架浏览位置/选中项独立于正文，只有打开成功才提交切书结果；默认排序控件不承担业务排序。Engine 不反向调用控件，也不保留 Avalonia Bitmap。

页面列表按 `Book` 引用和 `PageOrderVersion` 缓存展示数组。原集合排序提交后产生新的表现列表引用，仍持有相同 `Page`；普通导航复用数组。`Pages` getter与Refresh均及时读取已提交书籍，避免打开后的显隐绑定读取旧数组。排序算法及主页面定位仍在Engine，表现数组不能单独排序或成为另一套状态。实际ListBox回归及真机升降序见[设备资源记录](../acceptance/p2-device-resources-runtime.md)。

主图是单绘制控件，不为每页创建图像控件；页面列表使用虚拟化 ListBox。主图及可见缩略图的显示 Bitmap 与像素租约归各查看器所有，先释放 Bitmap 再释放租约。切书/缩放/视口变化用 revision 拒绝旧请求；所有 UI 对象在 Dispatcher 线程修改。

主布局参照原 MainWindow.xaml、SidePanels/SidePanelFrameView.xaml、菜单和 Dock 插槽。41 DIP 侧栏、36 DIP 按钮及转换资源保留。SidePanelPresenter 复用九个唯一内容控件，按 Engine 的组顺序/方向排布；拖放预览、指针捕获、分隔条、单面板浮窗与原图标资源归表现端。布局改变只触发 PanelsRefreshed，不请求主图。侧栏浮动/关闭/重开/停靠已接入，高级窗口/输入细节仍待后续，详见 layout-migration.md。样式现代化应另做增量，保留区域和操作流程。

输入设置使用编辑副本，可搜索全部原命令；新增不可解析输入及冲突阻止保存。输入文本时不响应阅读/数字命令；Command+O/W/Q 仍是系统操作。旧 Control 只规范解析名称，不替换为 Command。PrevScrollPage/NextScrollPage 调用 Engine 的原 NScroll 计算，ReaderView 应用向量或进入原帧导航；完整分页滚动参数、鼠标组合/方向手势及正反单双页共享循环参数从原 Commands 差分读取。全景作用在 P3。指定页对话框、共享步长和历史命令也进入同一正文入口；胶片条/滑条布局及主题不处理历史或阅读规则。正式 AppKit 桥接依据 HasPreciseScrollingDeltas，按窗口身份和查看器区域消费精确滚动/捏合；平移使用原 SnapView 防止图片移出视口，真实触控板待用户验收。

测试直接装载这些正式 XAML、主题和控件源码。Headless 截图用于检查布局与真实图像绘制，不能证明 Mac 手势、Retina、Finder 或 Windows 动态一致性。

只读运行诊断由启动层显式启用，ReaderView仅输出现有几何/数值，不读文件、重排页面或触发绘制。默认无日志I/O；诊断开销和进程RSS由独立设备记录说明。触控板本轮用户要求跳过，未验，不从Headless手势测试继承为真机通过。

历史列表由 Engine.HistoryList/SaveData 管理过滤、导航和编辑，HistoryRow/MainWindow.History 管理分组显示、焦点、多选及菜单。四种显示模板和可靠无效清理已接通；主题/布局调整不触发阅读解码或修改访问顺序。原 RemoveUnlinkedHistory 转交既有异步清理，不在菜单中实现存在检测。

第七批滑条页号结构、输入反馈与主题分开；ReaderWorkspaceViewModel提供位置/显隐/尺寸/透明度，SettingsWindow只改表现字段时不调用ApplySettingAsync。原SliderConfig字段写回现有JSON，未知/未迁字段继续合并保留。Fluent轨道留白/滑块尺寸使用主题资源覆盖，15–50 DIP内不裁切，不维护第二套Slider模板；见[p2-slider-input.md](p2-slider-input.md)。第八批接入全局播放列表页标记，五区自动隐藏由第九批窗口控制迁入。

第八批列表行引用与业务条目身份分开，集合刷新恢复选中容器焦点；普通方向键只选行，Enter/Delete在列表隧道消费。前后登记命令更新Hub当前项时回显新高亮，当前项仍在选中批次中则保留多选。关闭先禁止新动作并等待已授权编辑；尚在采集路径/文字的对话框返回后禁止续写。主题、标记绘制及面板排序不重建正文，详见[p2-playlist.md](p2-playlist.md)。

第九批自动隐藏区使用最终shown状态，实际悬停/焦点/捕获由独立AutoHidePresenter采集，不与延迟后的显示状态混用。覆盖区域复用原控件与组引用，内容上下余量保留原资格语义，正文区域不受它改变。原系统手势桥接仍负责设备信息，主窗口用实际控件命中排除覆盖面板；见[p2-autohide.md](p2-autohide.md)。

后续检查遵循[静默优先验证流程](validation-workflow.md)：常规构建/Headless后台执行，真实焦点/键鼠与系统窗口验收单独安排，不改变产品焦点行为来制造测试通过。

第十一批的搜索输入/清空/历史下拉/错误布局在 BookmarkListView.axaml(.cs)，输入草稿和任务表现由独立 BookmarkListViewModel 管理。原 Profile/匹配/递归在 Engine.SearchBookmarkFolderCollection 和 BookmarkFolderList，历史事务在 SaveData；控件不读取目录或用户JSON。稳定命令 FocusBookmarkSearchBox 只显示并聚焦本面板，搜索框 Enter/数字/Delete 与阅读键路由隔离。详见[p2-bookmark-search.md](p2-bookmark-search.md)。

第十二批历史搜索输入、清空、历史菜单和错误区域在MainWindow.axaml/MainWindow.History，草稿、确认、500ms合并及取消由独立HistorySearchViewModel管理。窗口表现只转交已提交列表，不在UI执行匹配或I/O；前后历史导航使用同一Engine.HistoryList结果。切换增量取消未确认输入，保存失败回滚菜单选项。详见[p2-history-search.md](p2-history-search.md)。

第十三批HistorySettingsViewModel持有原候选值及自定义值草稿，SettingsWindow只负责历史设置区域和导航。历史更多菜单共用原设置窗口，实际限制/临时排除/提交回滚在Engine；视图不扫描历史来源、运行集合不裁剪。保存禁用重入和关闭，不因历史外观或策略变更重建正文，见[p2-history-retention.md](p2-history-retention.md)。

第十四批书架排序控件调用Engine.ChangeFolderOrderAsync，编辑的是原当前目录参数；枚举、种子、JSON归一及事务不进入界面。原排序菜单勾选与执行共用CommandTable.BookOrderCommands。布局及主题不变，失败后原选择/排序恢复，见[p2-folder-parameters.md](p2-folder-parameters.md)。

第十五批ArchivePageRenderer独立转换原封面/叠页/文件信息区，主题使用ArchivePage资源。ReaderView只命中封面并发出实际Page动作，Engine核对所属Book并加载；封面选择/目录递归/父级定位均在Engine，控件不访问文件。空封面/未知文件逐页处理，其他可见需求继续。详见[p2-book-hierarchy.md](p2-book-hierarchy.md)。

第十六批MouseGestureSource只处理框架动作到原输入标识的转换，设置校验与执行共用；A/B/C和反转规则在Engine.DefaultInputScheme。SettingsWindow编辑副本经BookOperation.ApplyOptionsAsync提交，失败恢复原设置引用；外观与输入不重建阅读帧。布局/配色仍可单独调整，见[p2-mouse-input.md](p2-mouse-input.md)。

第十七批ReaderTransformPresenter独立管理原变换图和唯一矩阵，ReaderView管理资源/输入；CommandParameterEdit管理克隆草稿，CommandParameterWindow只呈现字段。设置草稿最后进入原ApplyOptionsAsync，布局/主题不实现参数算法，详见[p2-view-transform.md](p2-view-transform.md)。

第十八批MainWindow.BookControls只呈现原页尾三按钮，BookOperation在回报后核对身份/代次/位置；关闭释放回调，设置页不承担页尾规则。

第十九批MainWindow.BookshelfBookmarks只负责焦点/树选择/面板转交；虚拟位置和每目录排序在原BookshelfFolderList/BookmarkFolderList，元数据通过Engine后端契约。两个列表选择独立，主题不参与地址解析或保存。

第二十批接入原历史登记策略与可靠清理，继续使用唯一BookMementoControl/SaveData及四JSON事务；表现仅编辑草稿与转交命令，见[契约](p2-history-policy.md)。

第二十一批列表结构在PanelListItemView.axaml，切换/虚拟化在PanelListPresentation/VirtualizingThumbnailPanel，显示租约在ListCoverImage；原Profile、封面选择及唯一缓存归Engine。列表主题不改变原条目身份/排序/打开或阅读访问，见[p2-list-templates.md](p2-list-templates.md)。

第二十二批浮窗结构在FloatingPanelWindow.axaml，SidePanelPresenter负责唯一内容父级、同一输入路由及屏幕坐标；Engine保存原浮动/位置JSON，非模态宿主不锁住主查看器，见[p2-floating.md](p2-floating.md)。

方向手势由Engine原序列判定，ReaderView仅捕获/提示，MainWindow转交原命令；方向编辑独立于键位文本，主题不改变命令语义。

ReaderMotionPresenter只插值表现点与透明度；退出帧不复制页面/像素且受原工厂租约预算，XAML可独立调整动画编辑布局。

P2 资源收尾：ReaderView 只在同一真实图片/解码规格/来源版本时复用显示缓冲，Folder/Archive 封面仍走原选择请求。新来源/新 Page 不复用旧书显示资源。缓存是否回收仍由 Engine.BitmapFactory 决定；颜色、模板或动画布局调整不修改缓存/版本规则。

P3：ReaderBrowsePresenter是唯一ReaderView的连续/瀑布表现辅助；Engine.BrowseLayout只处理几何和可见索引，BookOperation提交模式/缩放/位置，主题使用Gallery.*。主窗口区域保持，顶部展示选择仅转交Engine；不在视图排序、扫描目录或保存配置。原分页变换仍独立，参见[p3-browse.md](p3-browse.md)。

P3第二批：FolderTreeView.axaml/.cs只绑定原DirectoryNode并转交确认/焦点；MainWindow.DirectoryTree管理书架内部Top/Left分隔和原配置提交，枚举在Engine节点及既有来源。页面四模板复用共享表现，ListCoverImage显式PageSource/LoadPageAsync直接租用当前来源。原Page身份、排序/导航和JSON仍在Engine；样式/布局调整不扫描来源或重建正文，见[p3-navigation.md](p3-navigation.md)。
