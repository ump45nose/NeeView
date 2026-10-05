# 原版行为对照

基线 `c5c398d89`。已采集部分 Windows 安装包动态参考与 Mac 同夹具复演；Windows dirty 包未证明匹配固定源码，完整动态对照仍待执行。旧 Preview 的 56 项测试属于历史重写方案，不能作为本轮一致性证据。

| 能力 | 原出处 | 迁移方式/状态 |
|---|---|---|
| 半页位置与有向范围 | Book/PagePosition.cs、PageRange.cs；NeeView.UnitTest/PagePositions.cs | 原源码及原测试迁入 |
| 双页、宽页、首页/末页单独、分割 | PageFrames/PageFrameFactory.cs | 完整生成算法迁入，纯几何适配；组合测试 |
| 帧/单页步进 | PageFrames/PageFrameBox.cs:976 起；NextPage/NextOnePageCommand | 原方向和范围算法适配；真实目录/ZIP测试 |
| 按字段设置恢复 | BookSetting/BookSettingPolicyConfigExtensions.cs | 原 Mix 与 map 迁入 |
| 普通书籍排序验证 | Book/BookSourceFactory.cs:44 | 注册顺序回退文件名；不开放播放列表排序 |
| 自然排序 | Book/BookPageSort.cs、PageComparer.cs；NeeView.Runtime/Collections/NaturalSort | 原数字/归一规则迁入，Win32字符比较改CurrentCulture；语言细节待对照 |
| 当前图打开、排序后保持条目 | Book/Book.cs、BookHub/BookHub.cs | 原 Page 对象和 EntryName，不使用另一套身份数据库 |
| History/Props | Book/BookMemento.cs、Book.CreateMemento、SaveData/SaveDataProfile.cs | 保留 Path/Page/Props 和未知字段；Mac只补false-wide值；收回早期半页持久化，普通重开恢复阅读方向首半页 |
| 差分快捷键 | Command/CommandElement.cs、CommandTable.cs | Commands[name].ShortCutKey，null恢复默认、空串禁用；Control保持 |
| 滚动翻页 | BookPageMoveControl、PageFrameBox.ScrollToNextFrame、PageFrames/NScroll.cs、DragArea.SnapView | 原五模式、分段/终端吸附、计时与停顿顺序迁入；分页参数编辑及P3原全景/PagesAsOne已接通，真实设备验收单独记录 |
| 九数字分类、固定移动 | MoveToDestinationFolderCommand、MoveToFolderAsCommand | P4普通目录Once/All/AllLeftToRight与固定移动接入，Windows动态另验 |
| 两区分类和移动历史 | SidePanels/DestinationFolder、DestinationFolder/DestinationMoveService.cs | P4原两区/无限集合/容量与成功后变栈接入，恢复协议独立验收 |
| 原窗口/九面板/设置 | MainWindow.xaml、SidePanelFrameView.xaml、Options | 布局壳及核心面板转换；见layout-migration.md，Windows截图待验证 |
| RAR/7z | 原Archive/阅读链 | 原来源关系下替换 SharpCompress，普通及固实夹具接入；密码/分卷/嵌套待迁移 |
| 连续、瀑布流 | 原阅读链与Mac展示扩展 | P3纵向逐图/最短列、可见需求及原Page锚点；原帧级全景/变换/NScroll接入，见p3-browse.md及p3-panorama.md |
| 完整默认菜单 | Menu/MenuTree.cs:CreateDefault、MenuNode.cs、MenuElementType.cs | 原八组树逐项迁入；未迁移节点禁用占位，原语言资源解析文案 |
| 胶片条/导航器 | Config/FilmStripConfig.cs、PageSelect/FilmStrip、SidePanels/Navigate | 原选择/方向/首尾居中及可见需求算法，200ms防抖、三滚轮/确认、元数据详情与配置接入；原全局播放列表标记与覆盖自动隐藏接入 |
| 滑条联动与设置 | PageSelect/PageSlider/PageSlider.cs、PageSliderView.xaml.cs、Config/SliderConfig.cs | 原共享选择、方向/静态双页/同步步长及拖动预览释放确认；原显隐/位置/厚度/透明度/滚轮字段接入，外观独立；原五区自动隐藏/窗口显示命令接入 |
| 底部直接页号 | PageSelect/PageSlider/SliderTextBox.cs、PageSliderViewModel.cs | 原一起始数值转换与raw索引；Enter保持编辑、普通失焦和Escape均提交；文本作用域、切书/关闭草稿、旧书请求核对；自动/正式运行见p2-slider-input.md |
| 原页标记 | PlaylistItemCollection、Playlist/Pagemark.nvpls、TogglePlaylistItem/PrevPlaylistItem/NextPlaylistItem | 46.3属于全局播放列表，格式/编辑/导航及标记绘制迁入；完整文件监视/修复待迁，不新增Book私有标记 |
| 指定页/指定步长 | JumpPageCommand.cs、MoveSizePageCommandParameter.cs、PageFrameBox.cs:1057-1094 | 一起始对话框、两方向共享参数，原周期对齐及端点终止；无循环页 |
| 页面/打开导航历史 | BookHub/PageHistory.cs、BookHubHistory.cs、HistoryLimitedCollection.cs | 原100项环形/游标/分支；页面以路径+条目，打开顺序独立；失败异步重放不提交游标 |
| 普通书架与前后书 | Bookshelf/FolderList/FolderCollection、BookshelfFolderList、BookOperation.MoveBook | 原目录分组/普通排序/端点/随机循环；混合候选、失败重试和优先切书；每路径参数/持久随机种子已迁，高级排序命令保留占位 |
| 文件夹页导航 | Book/BookPageCollection.GetNextFolderIndex/GetPrevFolderIndex | 原文件名升降序目录分组；组内回首项/端点停止；不冒充子书/父书打开 |
| 侧栏拖拽组合 | SidePanels/CustomLayoutPanelManager、SidePanelViewModel、SidePanelDropAcceptor、LayoutDockPanel | 原组模型、leader 整组移动/成员拆组、分半组合及 V2 JSON 适配；组合Headless/早期真机恢复通过；侧栏浮动已接，旧布局导入待迁移 |
| 历史/书签 | Bookamrk/BookmarkCollection.cs、BookMemento、HistoryCollection | 原 JSON 树字段/顺序保留；访问排序、共享状态、移动/递归合并/确认/颜色/删除恢复、目录导航/搜索/书架联动接入；Mac异步编辑宿主适配，修复及高级树布局继续占位 |
| 历史列表导航/管理 | HistoryList、HistoryListViewModel、HistoryListBox、BookHistoryCollection | 原过滤后前后规则、KeepHistoryOrder/SkipSamePlace、日期/四开关、单或双击、批次移除和全部清空；结构化搜索/表达式历史、四模板、登记/保留策略及可靠无效清理已接入 |
| 原五区自动隐藏与显示锁 | MainWindowModel/Controller/ViewModel、AutoHideBehavior、MainWindow.xaml.cs | 原资格、覆盖插槽、内容余量、滑条/胶片条联动、延迟/边缘/焦点/弹出层/捕获及显示锁；Mac焦点适配，见p2-autohide.md |
| 全屏与置顶 | 原WindowConfig/窗口控制命令 | Mac实际WindowState/Topmost；全屏取消恢复上一普通/最大化状态，FullDesktop等占位 |
| 旧Profile/nvzip | SaveData | 待 P5，完整旧迁移规则待迁入 |

[235条命令迁移表](command-migration.md) 与源码 manifest 一致，每条单独标记状态，不用命令数量计算功能覆盖率。[源码迁入表](source-migration.json) 区分完整算法与P1子集。

P2第十一批原书签查询对照：固定依赖gitlink的原解析器/匹配测试迁入，保留Default/Date/Size/Book Profiles、名称匹配、递归范围、父级局部索引注册排序与有效语法确认即登记历史。日期/大小探测采用现有后台来源能力，原访问历史成员单独更新；未将简单Contains当作原搜索。见[p2-bookmark-search.md](p2-bookmark-search.md)和独立静默验收记录，Windows动态样本待验。

P2第十二批历史查询对照：原BookHistory.GetValue使用书名/LastAccessTime/真实文件大小/书签成员/恒真历史标志；原HistoryList逐项SearcherFilter与当前直接父目录过滤的先后保留。有效确认先登记BookHistorySearchHistory，坏语法/来源失败保持旧结果，未将书签树规则或路径Contains替代历史搜索。见[p2-history-search.md](p2-history-search.md)。

P2第十三批原历史限制对照：BookHistoryCollection.CreateMemento/Restore/Limit和SettingPageHistory保留文件/载入限制、运行集合无限、先数量后严格TakeWhile、保序不刷新日期与默认无限。设置候选三文件成功后应用；极大期限防溢出、临时目录边界检查为明确适配。见[p2-history-retention.md](p2-history-retention.md)，Windows动态对照待验。

P2第十五批对照：原BookSourceFactory/ArchiveEntryCollection的三模式、WherePageAll、shortcut及坏子书规则接入；BookAddress/RequestLoadParent以真实相对条目返回，MoveToChildBook用当前主页。原ArchivePageUtility指定目标/regex/首图/Take(depth)与包内整前缀范围保留，非图像页框480×640。卡片采用原ArchivePageControl上3/下1结构和等比封面，Windows动态像素对照待验。详见[p2-book-hierarchy.md](p2-book-hierarchy.md)。

P2第二十一批对照：原HistoryListBox四模板、PanelListItemProfile/PanelThumbnailItemSize及FolderListConfig默认Content保持；History.LastAccessTime不替换为文件时间。原相对封面bookPath基准、单图RequestedEntryName及有限自然首图选择共用；真正网格虚拟化替换原WPF VirtualizingWrapPanel，Windows动态待验。

P2第二十二批：原LayoutPanelManager/WindowManager/WindowPlacement关系迁入；关闭保留位置、停靠清除位置、浮动独立成员、打开集合与JSON恢复经过固定样本和正式Headless验证。Windows动态/真实多屏捕获仍待验，见[p2-floating.md](p2-floating.md)。

- P2第二十三批：原MouseSequence/MouseSequenceBuilder、CommandTable.CreateDefaultMemento与Commands.MouseGesture差分迁入；释放/C终端/轮滚取消/配对与回滚自动对照通过，Windows动态和真机捕获待验。

- P2第二十四批：原PageChangeType/Duration、PageFrameContainerLayout方向及静态1间距，Scroll/Fade、取消、Hover/连续轮滚优先通过自动对照；全景/幻灯片专有策略为后续阶段，Windows动态待验。

- P2收尾：TogglePageMode/TogglePageModeReverseCommand.Execute 的 +1/-1 与 TogglePageModeCommandParameter.IsLoop 默认 true 迁入；两方向共享原差分参数，非循环首末停止、循环与重载绑定自动验证。RemoveUnlinkedHistory 转交既有可靠清理流程；源文件、参数出处与运行清单分别留证。

## P2设备对照修复

目录/CBZ单双页、左右方向、宽图/分割、首末单页与已采集Windows包动态样本一致。数字提示统一到输入层D0–D9；半页MacPagePart退出持久化，真机切书/重启恢复首半页。跨栏水平/垂直组合和比例与样本一致；Mac拆组/整组移动/组合重启已验但相应Windows样本不全。安装包dirty与固定源码对应仍未知，菜单/浮动/自动隐藏本轮Mac动态、真实手势、无损Retina及长期native未验，详见[运行记录](../acceptance/p2-device-input-runtime.md)。

P3第二批：原FolderTreeNodeBase/Delay/DirectoryNode/Model的普通父子、展开占位、自然排序、确认及祖先链同步子集适配；同步I/O改为可取消后台提交。PageListBox.xaml.cs:374–394的普通按下定位/释放焦点及修饰键隔离保留，方向键只选择；四模板接入当前原Page，共用来源和缩略预算。QuickAccess/监视及页面组树/搜索/智能名称由第六/七批接入；详见[p3-navigation.md](p3-navigation.md)，未执行Windows动态对照。

P3第三批：原BookSourceFactory三种收集模式、WherePageAll目录展平、shortcut及失败子书保留规则不变；只把元数据/Page创建循环后台化并补取消。Mac浏览几何检查点更新对照完整计算及实际矩形相交，原Page锚点/页内比例、顺序变更及来源所有权回归；不是原帧级全景或Windows动态对照的通过证明，见[p3-performance.md](p3-performance.md)。

P3第四至七批：原SourcePages/Searcher/BookPageSort正文关系、全源公共前缀与BookTableOfContents目录代表页迁入；临时FileName构树不受正文反序/搜索影响。普通书架沿原FileItem五属性及递归搜索，四类原搜索历史共用总保存开关；未知字段保留。普通直接目录128项渐进扩展保持原Page/Part和真实页尾保护；QuickAccess共享原节点/JSON且拖放不移动文件。全景使用原PageFrameFactory/容器/FrameSpace/PagesAsOne/NScroll，不把Mac瀑布当作原帧规则。原出处/自动对照与本批Windows动态状态分别登记，见[p3-page-search.md](p3-page-search.md)、[P3收尾](p3-completion-checklist.md)。

P4第一批：原DestinationFolder/Collection、DestinationMoveService、DestinationFolderPanelViewModel及MoveToFolderAsCommand的两区、目录变化刷新、无限集合、数字Index/模式、Once主图、固定移动与成功后变栈迁入。原System.IsFileWriteAccessEnabled=false保留。Windows Shell替换为有界文件协议及可恢复覆盖/中断记录；真实落点更新原SourcePages及阅读位置，JSON仍唯一权威。All/AllLeftToRight、删除/剪贴板/书籍重命名明确待后续；未采集P4Windows动态，不声称全部分类一致性通过。P3剩余设备/Windows/长期性能与P4集中验收。

P4第二批：Book.CurrentPages/CurrentPage及原CollectPages迁入，Once/All/AllLeftToRight判断顺序保留；普通目录多页移动逐项成功入栈，部分失败/晚取消仍协调已成功项。CopyToFolderAs固定复制、Index及多页参数接入，不随面板模式、不要求源写权限开关；归档实体化复制仍未迁入。静默夹具与Windows/真机集中验收分别记录，见[p4-multipage.md](p4-multipage.md)。

P4第三批：原DeleteFile无MultiPagePolicy，仅CurrentPage；IsFileWriteAccessEnabled=false和IsRemoveConfirmed=true默认保持，取消不执行且删除不入分类历史。原预移除+失败reload改为系统真实成功后提交，普通目录当前图片走AppKit废纸篓，无永久回退。归档内不可逆删除/链接/列表所选页等原能力本批未迁，明确占位。空搜索保留全源锚点但不登记隐藏页历史；原空书历史不登记规则保持。自动测试与AppKit/Windows动态设备验收分别记录，见[p4-delete.md](p4-delete.md)。

P4第四批：原RenameBookCommand/BookControl.RenameBook/FileIO.RenameAsync/RestoreBook的实体范围、写权限、编号名称、扩展名确认与失败重试迁入；单图打开仍改所在书籍目录。原BookMementoTools.RenameRecursive、QuickAccess/FolderConfig/Playlist明确路径联动保持未知字段与节点身份，不猜测替换未知字符串。Windows Shell改为同目录无覆盖后端，记录支持部分保存/启动恢复；原重新加载首半页和较新请求优先保留。45项专项覆盖真实临时文件、两处导航锁关闭取消、损坏记录与其他列表失败恢复；Mac/Windows动态另验，见[p4-rename.md](p4-rename.md)。

P4第五批：原CopyFile按CollectPages/Once/All/AllLeftToRight选序与分割去重；CopyBook复制实体书籍目录/根归档，二者不写源文件或移动历史、不要求源修改开关。原TextCopyPolicy数值/默认None、QueryPath优先和Paste=加载保持，单来源进入唯一OpenCore。Mac标准fileURL与私有有限JSON替换原FileDrop；多项输入不静默删项。CutFile/CutBook按用户决定禁用，归档实体化/临时多文件播放列表/位图与FileContents另列待迁；静默测试不证明真实NSPasteboard/Finder互操作，见[p4-clipboard.md](p4-clipboard.md)。

P4第六批：原ArchivePolicy.None/SendArchiveFile/SendArchivePath/SendExtractFile数值及默认、LimitedRealization和RealizeArchiveEntry保序/Distinct接入；None仍输出QueryPath，固定复制虚拟策略改为提取。原ClipboardUtility的OriginalPath实际再次调用同一提取策略，故保留输出实体路径，未按选项名称重解释。文件名仅取叶名称，逻辑Page/QueryPath保持原所属来源；成功剪贴板资源跨切书/关窗保留，真Finder另验，见[p4-realization.md](p4-realization.md)。

P4第七批：原BookHubTools/ContentDropReceiver多项保存为临时.nvpls；原PlaylistArchive按接收顺序解析、失败跳过及连续Id，代理真实SystemPath/类型/实体化与显示名分开。WherePageAll保留按真实父路径过滤规则；仅Entry类别按登记序，临时书运行历史保留、写出过滤、FirstLoader跳过恢复，不改全局Hub。自动原源码对照见[p4-multi-paste.md](p4-multi-paste.md)，Windows动态仍待集中验收。

P4第八批：原DeleteBook处理根目录/根文件，用户.nvpls仅移走列表；GetNextItem下一优先/末项退前/选中回退、IsOpenNextBookWhenRemove默认true保持。原BookControl忽略ConfirmFileIO.DeleteAsync的false返回，本批明确修正为真实成功才导航，取消保持旧书、系统失败恢复memento/搜索/锁定；历史和书签不删除。AppKit废纸篓替换Windows系统实现，临时/Profile/卷根/逻辑条目/链接范围有明确限制，设备与Windows动态仍待验，见[p4-delete-book.md](p4-delete-book.md)。

P4第九批：原BookControl整书固定复制/移动与1-based目标Index接入。移动先由FileIO.CloseBook释放来源，未发现该入口预捕获下一书，不套用DeleteBook的邻项流程；真实成功经FileIO.BookMementoRenameRecursive更新原明确地址。Mac复制保持阅读，移动成功保持卸载、不进分类栈，失败恢复原书是明确改进；同名目录明确整目录替换/取消，不声称等于Windows Shell的合并/冲突选择。逻辑目录提取/链接继续待迁，见[p4-book-transfer.md](p4-book-transfer.md)。

P4第十批普通目录页复制对照：ArchiveEntry.RealizeAsync的IsFileSystem直接返回SystemPath，目录实际递归复制由Shell承担；Mac复用已验证目录协议。Archive.CanRealize排除归档内部目录，SendExtractFile原明确TODO返回null；其他策略仍可传根归档/逻辑路径。Mac保留范围与LimitedRealization，增加未提取能力提示。普通目录与列表别名按真实名称复制、混合组保序去重、分类双栈保持、确认取消与当前目录重载见[p4-directory-copy.md](p4-directory-copy.md)。Windows目录合并/冲突动态仍待，Mac明确整体替换/取消。

P4第十一批：BookControl.CopyBookAsync以Book.Path创建条目；CopyBookToFolderAs经DestinationFolder.CopyAsync路径转条目后LimitedRealization，不能误解为直接原始路径复制。Mac的CreateBookEntry保留当前书籍归属，RequestedArchive不把显式图片当作书；包内目录按原四策略，内部提取TODO保持。用户.nvpls整书复制其文件，不复制列表引用目标；移动仍仅根实体，见[p4-logical-book-copy.md](p4-logical-book-copy.md)。

## P4开发收尾

固定出处：PageFileIO/BookPageActionControl的File、PlaylistEntry、ArchiveEntry分组与不可逆确认；ZipArchive条目/目录删除及ZipArchiveConfig独立写权限；PlaylistArchive只删登记；ContentDropReceiver按来源优先及一般失败后回退到下一数据。Mac在实际成功后移除页面，修正原先先移除后操作导致的失败丢页。ZIP流式重建保留旧Page物理ID，避免删除后读错幸存条目。

符号链接操作对象为目录项自身，指纹包含原LinkTarget文字；目录树不跟随链接。Finder别名由Foundation打开其目标，不能据此删除目标；不模拟Windows .lnk/COM FileContents。普通标准图片/URL回退接入，只有未实体化文件承诺而无标准数据时明确提示。目录树任意选中对象的完整文件管理为后续扩展，当前P4目标为原当前书/当前页文件动作。设备和固定Windows动态未从静态源码或Headless继承为通过；当前证据见[p4-completion-runtime.md](../acceptance/p4-completion-runtime.md)。
