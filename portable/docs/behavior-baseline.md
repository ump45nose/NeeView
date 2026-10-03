# 原版行为对照

基线 `c5c398d89`。当前只确认源码规则和自动测试，Windows 动态对照待执行。旧 Preview 的 56 项测试属于历史重写方案，不能作为本轮一致性证据。

| 能力 | 原出处 | 迁移方式/状态 |
|---|---|---|
| 半页位置与有向范围 | Book/PagePosition.cs、PageRange.cs；NeeView.UnitTest/PagePositions.cs | 原源码及原测试迁入 |
| 双页、宽页、首页/末页单独、分割 | PageFrames/PageFrameFactory.cs | 完整生成算法迁入，纯几何适配；组合测试 |
| 帧/单页步进 | PageFrames/PageFrameBox.cs:976 起；NextPage/NextOnePageCommand | 原方向和范围算法适配；真实目录/ZIP测试 |
| 按字段设置恢复 | BookSetting/BookSettingPolicyConfigExtensions.cs | 原 Mix 与 map 迁入 |
| 普通书籍排序验证 | Book/BookSourceFactory.cs:44 | 注册顺序回退文件名；不开放播放列表排序 |
| 自然排序 | Book/BookPageSort.cs、PageComparer.cs；NeeView.Runtime/Collections/NaturalSort | 原数字/归一规则迁入，Win32字符比较改CurrentCulture；语言细节待对照 |
| 当前图打开、排序后保持条目 | Book/Book.cs、BookHub/BookHub.cs | 原 Page 对象和 EntryName，不使用另一套身份数据库 |
| History/Props | Book/BookMemento.cs、SaveData/SaveDataProfile.cs | 保留 Path/Page/Props 和未知字段；Mac只补半页与false-wide值 |
| 差分快捷键 | Command/CommandElement.cs、CommandTable.cs | Commands[name].ShortCutKey，null恢复默认、空串禁用；Control保持 |
| 滚动翻页 | BookPageMoveControl、PageFrameBox.ScrollToNextFrame、PageFrames/NScroll.cs、DragArea.SnapView | 原五模式、分段/终端吸附、计时与停顿顺序迁入；分页接通，全景/完整参数编辑待后续，真鼠标待验 |
| 九数字分类、固定移动 | MoveToDestinationFolderCommand、MoveToFolderAsCommand | 元数据保留，业务待P4 |
| 两区分类和移动历史 | SidePanels/DestinationFolder、DestinationFolder/DestinationMoveService.cs | 完整目标登记；待P4，不能继承旧测试通过状态 |
| 原窗口/九面板/设置 | MainWindow.xaml、SidePanelFrameView.xaml、Options | 布局壳及核心面板转换；见layout-migration.md，Windows截图待验证 |
| RAR/7z | 原Archive/阅读链 | 原来源关系下替换 SharpCompress，普通及固实夹具接入；密码/分卷/嵌套待迁移 |
| 连续、瀑布流 | 原阅读链 | 待 P3 |
| 完整默认菜单 | Menu/MenuTree.cs:CreateDefault、MenuNode.cs、MenuElementType.cs | 原八组树逐项迁入；未迁移节点禁用占位，原语言资源解析文案 |
| 胶片条/导航器 | Config/FilmStripConfig.cs、PageSelect/FilmStrip、SidePanels/Navigate | 原选择/方向/首尾居中及可见需求算法，200ms防抖、三滚轮/确认、元数据详情与配置接入；原全局播放列表标记与覆盖自动隐藏接入 |
| 滑条联动与设置 | PageSelect/PageSlider/PageSlider.cs、PageSliderView.xaml.cs、Config/SliderConfig.cs | 原共享选择、方向/静态双页/同步步长及拖动预览释放确认；原显隐/位置/厚度/透明度/滚轮字段接入，外观独立；原五区自动隐藏/窗口显示命令接入 |
| 底部直接页号 | PageSelect/PageSlider/SliderTextBox.cs、PageSliderViewModel.cs | 原一起始数值转换与raw索引；Enter保持编辑、普通失焦和Escape均提交；文本作用域、切书/关闭草稿、旧书请求核对；自动/正式运行见p2-slider-input.md |
| 原页标记 | PlaylistItemCollection、Playlist/Pagemark.nvpls、TogglePlaylistItem/PrevPlaylistItem/NextPlaylistItem | 46.3属于全局播放列表，格式/编辑/导航及标记绘制迁入；完整文件监视/修复待迁，不新增Book私有标记 |
| 指定页/指定步长 | JumpPageCommand.cs、MoveSizePageCommandParameter.cs、PageFrameBox.cs:1057-1094 | 一起始对话框、两方向共享参数，原周期对齐及端点终止；无循环页 |
| 页面/打开导航历史 | BookHub/PageHistory.cs、BookHubHistory.cs、HistoryLimitedCollection.cs | 原100项环形/游标/分支；页面以路径+条目，打开顺序独立；失败异步重放不提交游标 |
| 普通书架与前后书 | Bookshelf/FolderList/FolderCollection、BookshelfFolderList、BookOperation.MoveBook | 原目录分组/13项排序/普通端点/随机循环；混合候选、失败重试和优先切书；每路径参数/持久随机种子待迁 |
| 文件夹页导航 | Book/BookPageCollection.GetNextFolderIndex/GetPrevFolderIndex | 原文件名升降序目录分组；组内回首项/端点停止；不冒充子书/父书打开 |
| 侧栏拖拽组合 | SidePanels/CustomLayoutPanelManager、SidePanelViewModel、SidePanelDropAcceptor、LayoutDockPanel | 原组模型、leader 整组移动/成员拆组、分半组合及 V2 JSON 适配；Headless/真机恢复通过；浮动/旧布局导入待迁移 |
| 历史/书签 | Bookamrk/BookmarkCollection.cs、BookMemento、HistoryCollection | 原 JSON 树字段/顺序保留；访问排序、共享状态与原移动/递归合并/确认/颜色/删除恢复接入；完整导航/修复/原Popup宿主待迁，见p2-bookmark-operations.md |
| 历史列表导航/管理 | HistoryList、HistoryListViewModel、HistoryListBox、BookHistoryCollection | 原过滤后前后规则、KeepHistoryOrder/SkipSamePlace、日期/四开关、单或双击、批次移除和全部清空；搜索语法/表达式历史由第十二批接入；样式/无效清理待迁 |
| 原五区自动隐藏与显示锁 | MainWindowModel/Controller/ViewModel、AutoHideBehavior、MainWindow.xaml.cs | 原资格、覆盖插槽、内容余量、滑条/胶片条联动、延迟/边缘/焦点/弹出层/捕获及显示锁；Mac焦点适配，见p2-autohide.md |
| 全屏与置顶 | 原WindowConfig/窗口控制命令 | Mac实际WindowState/Topmost；全屏取消恢复上一普通/最大化状态，FullDesktop等占位 |
| 旧Profile/nvzip | SaveData | 待 P5，完整旧迁移规则待迁入 |

[235条命令迁移表](command-migration.md) 与源码 manifest 一致，每条单独标记状态，不用命令数量计算功能覆盖率。[源码迁入表](source-migration.json) 区分完整算法与P1子集。

P2第十一批原书签查询对照：固定依赖gitlink的原解析器/匹配测试迁入，保留Default/Date/Size/Book Profiles、名称匹配、递归范围、父级局部索引注册排序与有效语法确认即登记历史。日期/大小探测采用现有后台来源能力，原访问历史成员单独更新；未将简单Contains当作原搜索。见[p2-bookmark-search.md](p2-bookmark-search.md)和独立静默验收记录，Windows动态样本待验。

P2第十二批历史查询对照：原BookHistory.GetValue使用书名/LastAccessTime/真实文件大小/书签成员/恒真历史标志；原HistoryList逐项SearcherFilter与当前直接父目录过滤的先后保留。有效确认先登记BookHistorySearchHistory，坏语法/来源失败保持旧结果，未将书签树规则或路径Contains替代历史搜索。见[p2-history-search.md](p2-history-search.md)。

P2第十三批原历史限制对照：BookHistoryCollection.CreateMemento/Restore/Limit和SettingPageHistory保留文件/载入限制、运行集合无限、先数量后严格TakeWhile、保序不刷新日期与默认无限。设置候选三文件成功后应用；极大期限防溢出、临时目录边界检查为明确适配。见[p2-history-retention.md](p2-history-retention.md)，Windows动态对照待验。

P2第十五批对照：原BookSourceFactory/ArchiveEntryCollection的三模式、WherePageAll、shortcut及坏子书规则接入；BookAddress/RequestLoadParent以真实相对条目返回，MoveToChildBook用当前主页。原ArchivePageUtility指定目标/regex/首图/Take(depth)与包内整前缀范围保留，非图像页框480×640。卡片采用原ArchivePageControl上3/下1结构和等比封面，Windows动态像素对照待验。详见[p2-book-hierarchy.md](p2-book-hierarchy.md)。

P2第二十一批对照：原HistoryListBox四模板、PanelListItemProfile/PanelThumbnailItemSize及FolderListConfig默认Content保持；History.LastAccessTime不替换为文件时间。原相对封面bookPath基准、单图RequestedEntryName及有限自然首图选择共用；真正网格虚拟化替换原WPF VirtualizingWrapPanel，Windows动态待验。
