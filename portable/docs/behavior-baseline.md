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
| 胶片条/导航器 | Config/FilmStripConfig.cs、PageSelect/FilmStrip、SidePanels/Navigate | 原选择/方向/首尾居中及可见需求算法，200ms防抖、三滚轮/确认、元数据详情与配置接入；标记/全局自动隐藏待迁入 |
| 滑条联动 | PageSelect/PageSlider/PageSlider.cs、PageSliderView.xaml.cs | 原共享选择、方向/静态双页/同步步长，拖动预览释放确认；直接页号文本框/页标记待迁入 |
| 指定页/指定步长 | JumpPageCommand.cs、MoveSizePageCommandParameter.cs、PageFrameBox.cs:1057-1094 | 一起始对话框、两方向共享参数，原周期对齐及端点终止；无循环页 |
| 页面/打开导航历史 | BookHub/PageHistory.cs、BookHubHistory.cs、HistoryLimitedCollection.cs | 原100项环形/游标/分支；页面以路径+条目，打开顺序独立；失败异步重放不提交游标 |
| 侧栏拖拽组合 | SidePanels/CustomLayoutPanelManager、SidePanelViewModel、SidePanelDropAcceptor、LayoutDockPanel | 原组模型、leader 整组移动/成员拆组、分半组合及 V2 JSON 适配；Headless/真机恢复通过；浮动/旧布局导入待迁移 |
| 历史/书签 | Bookamrk/BookmarkCollection.cs、BookMemento、HistoryCollection | 原 JSON 树字段/顺序保留；访问排序、基础编辑和共享阅读状态接入，完整服务待迁入 |
| 旧Profile/nvzip | SaveData | 待 P5，完整旧迁移规则待迁入 |

[235条命令迁移表](command-migration.md) 与源码 manifest 一致，每条单独标记状态，不用命令数量计算功能覆盖率。[源码迁入表](source-migration.json) 区分完整算法与P1子集。
