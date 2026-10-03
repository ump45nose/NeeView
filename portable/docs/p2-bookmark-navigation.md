# P2 第十批：原书签列表目录导航与排序

2026-10-04。迁入原 BookmarkFolderList 的进入、返回、固定根及当前书定位流程，以及 BookmarkFolderCollection/FolderCollection 的无磁盘探测排序。本批是原书签列表子集；原结构化搜索由[第十一批](p2-bookmark-search.md)接入，书架互联和显示模板仍待迁。

## 职责、依赖与契约

- Engine.BookmarkFolderList 接收 SaveData 的唯一 BookmarkCollection，位置、条目和选择都是原 BookmarkNode 引用，不另建身份、树、数据库或阅读会话。
- BookmarkListViewModel 只负责显示与 UI 线程通知；BookmarkListView 的 XAML、模板和主题可独立调整。控件不访问目录、归档、解码库或 JSON 文件。
- MainWindow 接入既有打开和保存入口，统一协调可选编辑树与列表的节点选择、批次删除和登记编辑。原侧栏组合与阅读内核不改。
- SetPlace 只接受当前树中的文件夹；MoveToParent 只在书签范围返回，并选中刚离开的原节点。固定根采用原集合根引用，显示路径只作面包屑，不作为身份。
- Sync 优先当前位置的同路径书签，再沿原树顺序查找其他别名；找不到只刷新当前目录。这里是手动“同步当前书签”，不是尚未迁完的书架 bookmark scheme 互联。
- Reveal 用于编辑完成后进入实际保留节点的父级并选中；可选树点击文件夹则显示其内容，点击书籍则定位其父级。

## 业务规则与原出处

| 行为 | 原出处 | 当前适配 |
|---|---|---|
| 书签根与父级边界 | BookmarkFolderList.RootPath/CanMoveToParent/GetFixedHome；FolderList.MoveToParent | 同一原节点树内进入/返回，根不能返回文件系统 |
| 当前书同步 | BookmarkFolderList.SyncBookAsync；BookmarkCollectionService.FindBookmark(QueryPath, FolderCollection) | 当前目录优先，同路径多个别名不跳到其他目录 |
| 默认排序 | BookmarkConfig.BookmarkFolderOrder；FolderParameter.GetDefaultFolderOrder | 读取/保存原 Bookmark 分支及枚举数值 |
| 注册顺序 | BookmarkFolderCollection.Sort/GetIndex | 使用节点索引而非 EntryTime 日期；普通 Directory/File 的 ConstOrder 均为 2，升序保持树顺序，降序整体反转，不应用全局目录分组 |
| 名称/路径/类型/随机 | FolderCollection.Sort/ComparerFileName/ComparerFullPath/ComparerFileType | 自然名称、原真实 Path/虚拟 bookmark:路径；其他模式先依原 Bookshelf.FolderSortOrder 分组，当前位置随机种子稳定 |
| 单/双击与 Enter | FolderListBox.ClickToLoadBook/FolderListItem_MouseDoubleClick/OpenBook_Executed | 单击按同一行释放且无修饰键打开；双击和 Enter 打开主选中项，允许主项属于多选批次，空白不打开旧选择 |
| 新建目录 | FolderList.NewFolder | 列表在当前目录新建同级节点；已有编辑树保留选中目录/真实父级的编辑上下文 |

时间/大小排序需要真实来源元数据，当前保留禁用选项。若旧配置选择这些模式，显示明确能力提示，当前列表临时采用名称排序，保存保留原字段，不能静默改写为已支持模式。原自然排序已替换 Windows 比较器的边界继续适用，文化排序仍待 Windows 动态样本对照。

## 状态、资源与错误

- 目录被移动仍保留同一节点位置并更新面包屑；删除当前位置退至最近仍存活的已知祖先，最终回根。删除后不会虚构新节点。
- SaveData.BookmarksChanged 只在书签事务提交或原地回滚完成后回报。翻页/进度保存不触发列表排序，避免每秒重复扫描书签树。
- 排序前派生父级索引，路径键按条目预先计算，比较器不反复全树查父级。该索引只属于当前位置显示，不产生持久化身份体系；性能 P95 尚未测量。
- 排序和树/数量显示写入失败恢复配置与表现，列表批次和原节点可继续重试。其他设置窗口的既有失败后取消问题仍是独立待办。
- Enter/Backspace 属于书签列表；方向键只修改选择，Delete 交由宿主的统一批次删除及确认。中文输入及真实设备仍需真机验证。
- 可选树顶部固定，提示换行不能在释放前移动命中行；保留旧树拖动、取消和保存失败回归。树布局目前是顶部编辑区，原左右/顶部可切换与尺寸拖动尚未完整迁入。
- 面板动作串行，窗口关闭先拒绝新输入并等待已开始的打开/设置任务，再进入原状态保存与释放。最终保存失败恢复面板输入；Dispose 解除数据订阅，晚到回报不更新已释放窗口。

## 验证与剩余能力

自动测试覆盖进入/逐级返回、同路径别名同步、注册顺序与三种分组、名称/类型/路径/随机、移动/删除位置恢复、未知 JSON 与未支持排序、进度保存不重排、失败回滚，以及正式列表键盘、树联动、同级新建、缺失来源、排序失败重试、关闭等待和旧树拖动。

证据独立保存为 `p2-bookmark-navigation-*`；不覆盖历史阶段截图。构建、自动回归、Headless 图像与真机分别记录，见[验收记录](../acceptance/p2-bookmark-navigation-runtime.md)。本批默认静默，未启动正式应用、未发送真实键鼠，未修改用户 Application Support 数据。

原结构化搜索/搜索历史/递归范围已由第十一批迁入。仍待迁：四显示模板及完整属性、完整树布局/路径选择、无效清理/链接修复/监视、StartUp.LastBookmarkFolder、按目录 FolderParameter 持久化、书架 bookmark scheme 与目录树互联、IsSyncBookshelfEnabled 的完整加载请求语义。FocusBookmarkList 原意是在书架显示书签位置，不冒充聚焦本面板；FocusBookmarkSearchBox 已由第十一批接通。完整旧 Profile/路径映射在 P5。

前台单/双击、弹出菜单、树拖动/焦点和真实触控板/IME等按[静默优先流程](validation-workflow.md)另行安排；Windows 动态对照、NAS、长期 native 内存、完整帧/P95和发布均未由本批自动测试证明。P2不封板。
