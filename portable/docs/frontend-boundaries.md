# 前端独立调整边界

P5 第十八批：原幻灯周期/输入重置/首周期EOS等待、页尾覆盖、自动滚动和启动选项进入唯一BookOperation与JSON；顶部原4 DIP计时条和设置草稿独立。关闭失败恢复播放及滚动；调度适配与剩余媒体边界见[幻灯契约](p5-slideshow.md)。


P5 第十七批：GIF/WebP与官方ImageIO APNG完整合成帧、原播放状态/三命令/底部媒体条和JSON设置接入唯一阅读工厂；取消首帧不缓存成功、原生来源/像素/显示统一计费。界面结构和草稿独立，详见[动图契约](p5-animated-images.md)。

P5 第十六批：ZIP AES/PKWARE、RAR4/5 与 7z 固实加密接入原 ArchiveKey 打开链。来源私有口令、真实抽取验证、逻辑路径缓存、取消及失败父链释放保持；加密 ZIP 只读，普通 ZIP 删除不变。见[压缩密码契约](p5-compressed-password.md)。


P5 第十五批：原ArchiveKey/进程AES缓存及纯输入弹窗接入唯一打开链；根/明确嵌套PDF通过系统后端解锁，后台无交互、失败父链释放、切书/关闭取消，见[密码契约](p5-pdf-password.md)。压缩密码仍待迁。

P5 第十三批：原PDF归档/页目录/三种尺寸及原JSON配置接入唯一阅读链；CoreGraphics/PDFKit官方绑定替换PDFium。正文直接输出像素，提取才惰性PNG，请求级流初始化/读/关闭串行。PDF密码交互由P5第十五批接入，压缩密码仍待迁，扩展名配置已接入，见[PDF契约](p5-pdf.md)。

P5 第十二批：嵌套解析/临时代理/父链生命周期全部位于Engine来源关系及Backends。ReaderView、页面列表和封面仍消费原Page与唯一BitmapFactory，不解压、不识别物理临时路径；布局/主题可独立修改，见[嵌套契约](p5-nested.md)。

P5 第十一批：SettingsSearchPresenter从现有布局控件/明确分区标识取得可搜索文案，CommandParameterEdit提供既有参数字段；Engine只匹配纯文本/目标键。结果直接展示同一草稿控件和命令行模板，没有第二字段配置表。重挂时固定继承DataContext，返回时恢复父级/顺序/继承，样式不参与匹配，见[搜索契约](p5-settings-search.md)。

P5 第十批：主题目录按钮保留在现有设置页，ThemeSettingsViewModel只转交当前目录草稿和平台契约。Engine负责原首次目录/样例规则，Backends负责Finder目录内容打开和真实错误；视图不创建文件或具体后端。关闭拒绝晚到结果，见[目录动作契约](p5-theme-folder.md)。

P5 第九批：字体结构在SettingsWindow.axaml，百分比/字体族选择草稿在FontSettingsViewModel。Engine只保留FontsConfig及原FontParameters纯尺寸计算；启动层注入AppKit度量，FontPresenter发布资源。主题和字体互不重置，视图的角色/间距/模板可独立调整。事务成功后才更新资源，失败/取消保持外观，见[字体契约](p5-fonts.md)。

P5 导入布局/五类选项在 ProfileImportWindow.axaml，编辑草稿、后台候选复制与代次在 ProfileImportViewModel；视图仅确认并关闭返回 Engine 请求。MainWindow 不解析或写 JSON，MacApp 负责唯一旧窗口关闭与正式窗口重建。Engine 负责原默认键位、映射/兼容、候选校验和五文件备份/提交/恢复；后端读取仅在启动层注入。第三批旧版本/布局转换也全部在 Engine，预览展示转换后的命令，界面没有解析旧格式。原 ImportBackup 接入同一界面，主题与报告布局可以独立调整，见[兼容契约](p5-legacy-compatibility.md)。

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

输入设置使用编辑副本，可搜索全部原命令；新增不可解析输入及冲突阻止保存。输入文本时不响应阅读/数字命令；Command+O/W/Q 仍是系统操作。旧 Control 只规范解析名称，不替换为 Command。PrevScrollPage/NextScrollPage 调用 Engine 的原 NScroll 计算，ReaderView 应用向量或进入原帧导航；完整分页滚动参数、鼠标组合/方向手势及正反单双页共享循环参数从原 Commands 差分读取。原帧全景及PagesAsOne/NScroll已由P3接入；连续/瀑布是同一查看器的Mac展示扩展。指定页对话框、共享步长和历史命令也进入同一正文入口；胶片条/滑条布局及主题不处理历史或阅读规则。正式 AppKit 桥接依据 HasPreciseScrollingDeltas，按窗口身份和查看器区域消费精确滚动/捏合；平移使用原 SnapView 防止图片移出视口，真实触控板待用户验收。

测试直接装载这些正式 XAML、主题和控件源码。Headless 截图用于检查布局与真实图像绘制，不能证明 Mac 手势、Retina、Finder 或 Windows 动态一致性。

只读运行诊断由启动层显式启用，ReaderView仅输出现有几何/数值，不读文件、重排页面或触发绘制。默认无日志I/O；诊断开销和进程RSS由独立设备记录说明。触控板按用户要求跳过，未验，不从Headless手势测试继承为真机通过。

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

P3第三批：Engine.BrowseLayout保存不可变几何检查点，ReaderBrowsePresenter只安排后台计算并在UI线程发布快照及恢复Page锚点。快照与渲染分别持有几何和像素，旧计算不能修改正在绘制的段；视图仍不枚举/排序/写配置。外观和控件布局入口保持，详见[p3-performance.md](p3-performance.md)。

P3第四/五批已接入[渐进目录索引](p3-index.md)和[原帧全景](p3-panorama.md)，早期批次的待迁说明按该契约更新；导航高级项继续迁移，静默验收不等同设备封板。

P3收尾：页面目录组树、名称/分组/书名及搜索区域在MainWindow.axaml/MainWindow.PageNavigation，设置表单在SettingsWindow.Navigation；纯布局/主题可独立调整。NavigationSearchViewModel只有输入/历史/取消表现，PageSearchProfile/BookOperation负责正文过滤，SearchBookshelfCollection/BookshelfFolderList负责书架枚举/匹配/监视。目录树只按SourceVersion后台建立，节点仍引用原Page，表现不另存来源数组或按文件名重扫。系统图标通过Engine小型PNG契约进入独立SystemFileIcon控件，不向视图暴露AppKit对象。关闭等待已经确认的保存并取消晚到结果，见[p3-page-search.md](p3-page-search.md)和[p3-quickaccess.md](p3-quickaccess.md)。

P4第一批：DestinationFolderPanelView.axaml只定义两区、分隔和控件；DestinationFolderPanelViewModel通知独立于原数据/业务。管理窗口使用克隆草稿，保存进入Engine配置事务；数字/固定移动/撤销菜单通过MainWindow.DestinationFolders可等待宿主入口。操作对象在业务调用时捕获，文本数字作用域不触发分类；窗口关闭等待宿主和面板任务。枚举/文件协议/容量/成功后索引推进仍归Engine及后端，主题可独立调整。

P4第二批菜单与参数表单继续使用原Index/MultiPagePolicy；固定复制和数字模式只由宿主转交不同Engine入口。原CurrentPages/去重/阅读方向/实际成功项处理属于Engine，视图不推断左右图或扩大瀑布选区。详见[p4-multipage.md](p4-multipage.md)。

P4第三批：删除确认只通过可等待回调返回用户选择，捕获/复核目标、系统成功后的索引/搜索/缓存失效在Engine；SettingsWindow.Files只编辑原System字段草稿，提交沿唯一配置事务。确认路径区域可滚动，主题/结构可以独立调整，不改变单主页范围或废纸篓语义，见[p4-delete.md](p4-delete.md)。

P4第四批：名称输入、扩展名/编号及失败重试对话框由MainWindow.DestinationFolders装配，主题/按钮/路径区域可独立调整。Engine捕获并复核原Book/代次、关闭来源、实体授权与原JSON/列表路径联动，Backends执行同目录无覆盖改名；视图不判断文件身份或替换路径。退出取消未授权准备和输入，等待已经授权实体，见[p4-rename.md](p4-rename.md)。

P4第五批：主窗口只按原CopyFile/CopyBook/Paste命令转交并等待任务，菜单展开/重新激活仅重查剪贴板能力，不申请正文图片。CommandParameterEdit/Window编辑MultiPagePolicy独立草稿，SettingsWindow.Files只编辑TextCopyPolicy；原ApplyOptions保存与失败回滚保持。具体NSPasteboard实现仅在MacApp装配，Engine负责选页、来源复核及加载，控件不访问剪贴板或操作文件。Cut保留禁用，见[p4-clipboard.md](p4-clipboard.md)。

P4第六批：文件设置XAML新增原ArchiveCopyPolicy四选项，SettingsWindow.Files只读写草稿并沿原配置事务提交。CopyFile/CopyToFolderAs来源能力及实体化仍由Engine/Backends处理；唯一启动装配持有进程级临时实体后端，视图不读取归档或管理磁盘文件。主窗口退出只请求取消尚未提交的准备，既有文件动作等待边界保持，见[p4-realization.md](p4-realization.md)。

P4第七批：主窗口、Finder/启动参数与拖入只转交OpenFilesAsync，临时.nvpls创建/解析/过滤在Engine及Backends。Entry排序菜单按当前来源资格启用，原Page与阅读设置沿同一模型；界面不改变全局PlaylistHub，不保存临时列表或解释显示别名，见[p4-multi-paste.md](p4-multi-paste.md)。

P4第八批：DeleteBook通过既有可等待文件任务转交；ConfirmDeleteBookAsync仅呈现整书范围并回报选择，设置页只编辑原Bookshelf.IsOpenNextBookWhenRemove。真实范围/双次元数据复核、来源释放/失败恢复、邻项及原JSON清启动目标归Engine，AppKit废纸篓归后端；布局和主题可独立调整，见[p4-delete-book.md](p4-delete-book.md)。

P4第九批：原整书目标菜单与Index沿既有宿主文件任务；ConfirmBookOverwriteAsync只呈现目录全部替换或单文件范围。目标、快照、指纹、来源释放/失败恢复、原JSON联动归Engine/后端；菜单与确认主题独立，不在表现层操作文件，见[p4-book-transfer.md](p4-book-transfer.md)。

P4第十批目录复制沿既有CopyFile/CopyToFolderAs入口，MainWindow只呈现独立整树覆盖文案与确认/取消，关闭解除进程服务回调。目录类型/保护/指纹、当前目录索引刷新及阅读恢复均在Engine/既有后端；主题/布局不承担复制规则。原内部目录提取未完成项显示能力提示，详见[p4-directory-copy.md](p4-directory-copy.md)。

P4第十一批逻辑书复制没有新增视图业务：既有菜单依赖CanCopyBook/CanCopyBookToFolder，Book.Path条目和归档策略由唯一来源及Engine处理，覆盖/取消复用原整书表单。布局/主题不感知实体化或内容类型，详见[p4-logical-book-copy.md](p4-logical-book-copy.md)。

P4收尾：ContentDropSnapshot只借用拖放数据并复制有限字节，Bitmap所有权仍归发送者；下载/编码探测/临时材料由后端接收器处理。页面列表Delete只转交原Page显式选区，类型分组、ZIP权限/强制确认及真实成功项协调归Engine/来源。SettingsWindow.Files只编辑原ZIP权限草稿；样式、布局和对话框文案独立，见[p4-completion.md](p4-completion.md)。

设备修复：页面列表Delete在窗口Tunnel的焦点作用域先分派，避免全局DeleteFile抢先处理；控件不实现删除规则。Retina吸附在最终显示矩阵完成，绘制/命中/诊断共用；原PageFrame和逻辑Pan不变。Finder引用URL只在后端解析，界面没有Foundation依赖。无障碍查询的存活回调数组增长另记为设备失败，不因主题、控件或输入简化掩盖，见[设备验收](../acceptance/p34-device-runtime.md)。

P5 第八批：主题结构在SettingsWindow.axaml，选择/目录/扫描草稿在ThemeSettingsViewModel；Engine读取原JSON和颜色引用/继承/回退，ThemePresenter只发布应用颜色/Fluent变体。启动与导入重建绑定同一Config，设置保存成功才刷新主题；取消或失败保留已显示资源。主/浮/设置/弹出层共用资源；不打开书籍、重排页面或请求像素，详见[p5-theme.md](p5-theme.md)。

P5第二十批：原备份/保存/重载命令进入唯一Profile；仅UserSetting原地恢复，来源DirtyBook变化才重收集，前端布局/主题/字体独立恢复。动态预算、旧备份保持、晚到选择器/新打开边界见[契约](p5-profile-commands.md)。

P5第二十一批：原随机跳页/来源资格排序、地址栏/滑条菜单与快捷键语义、窗口状态与独立设置目录进入唯一命令链。正文与Chrome通知分离；关闭取消/等待系统目录动作。见[契约](p5-original-commands.md)。

P5第二十二批：原六种画布背景、五自定义刷、透明页底色/HSV棋盘及双轴nearest进入唯一Config/BitmapFactory；背景变化仅重绘。源首像素RGB与预乘显示分离，显示租约/UI发布和独立草稿沿原边界，见[契约](p5-background.md)。

P5第二十三批：默认设置复制/递归重收集与全局写权限全部在原BookOperation/SaveData；菜单只传入原上下文并查询资格，不修改阅读或文件规则。失败恢复完整历史资格，新打开优先；见[契约](p5-default-settings.md)。

P5第二十四批：原版本窗口布局/图标、实际Mac构建版本、复制/许可/项目链接及macOS关于菜单接入；窗口与阅读独立，Windows更新检查保留Mac待接入区域。见[契约](p5-version-window.md)。

P5第二十五批：原外部应用命令/集合与独立设置草稿接入；Engine捕获页组及策略，唯一平台字面提交，随机材料进程留存/2GiB共用预算；布局与启动解耦，见[外部应用契约](p5-external-applications.md)。
