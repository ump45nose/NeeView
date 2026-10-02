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
| 滚动翻页 | Command/Commands/NextScrollPageCommand.cs | 待P2，禁用；没有接到普通翻页 |
| 九数字分类、固定移动 | MoveToDestinationFolderCommand、MoveToFolderAsCommand | 元数据保留，业务待P4 |
| 两区分类和移动历史 | SidePanels/DestinationFolder、DestinationFolder/DestinationMoveService.cs | 完整目标登记；待P4，不能继承旧测试通过状态 |
| 原窗口/九面板/设置 | MainWindow.xaml、SidePanelFrameView.xaml、Options | 布局壳及核心面板转换；见layout-migration.md，Windows截图待验证 |
| RAR/7z、连续、瀑布流 | 原Archive/阅读链 | P2/P3，保留测试材料，未接入当前产品 |
| 书签/旧Profile/nvzip | Bookamrk、SaveData | P2/P5；完整旧迁移规则待迁入 |

[235条命令迁移表](command-migration.md) 与源码 manifest 一致，每条单独标记状态，不用命令数量计算功能覆盖率。[源码迁入表](source-migration.json) 区分完整算法与P1子集。
