# P2 第十一批：原书签结构化搜索

2026-10-04。承接原书签列表导航，迁入原查询解析、递归筛选、搜索历史与输入流程。只有一套原书签集合和阅读链；不新增数据库、身份、生产项目或 Preview。

## 职责、依赖与契约

- 原 `NeeLaboratory.IO.Search` 固定为 Windows 基线 gitlink 的 `d8568a3a14688d77ab591f0dd2d296e545aa53e1`。解析、别名、过滤、值比较及 Kanaxs 归一源码内嵌 Engine；未使用的命令队列/诊断宿主不迁入。出处含原 SHA256，改造文件另记目标指纹。
- `SearchBookmarkFolderCollection` 保留原 Default/Date/Size/Book Profile 与五属性。结果是唯一 `BookmarkNode` 引用，后台使用名称、属性及历史路径快照，不遍历正在编辑的 ObservableCollection。
- `BookmarkFolderList.SearchAsync` 拥有位置、搜索表达式、请求代次和结果；空查询恢复当前目录直接子项。只有成功且仍属于当前代次的请求可以提交；导航清空查询。
- `BookmarkListViewModel` 按原 `SearchBoxModel` 的 Trim、增量、确认、历史和环境重置流程适配可等待任务；150ms合并连续输入。布局/排序/书签事务重筛原节点，不丢弃无效输入草稿，不请求正文解码。
- `IArchiveFactory.GetFileMetadataAsync` 经 `BookOperation` 接入；Backends 复用两槽/15秒的 `SourceIo` 查询指定路径。控件不调用目录、压缩库或 JSON。
- `SaveData.BookmarkSearchHistory` 使用原 `HistoryStringCollection`。追加/删除进入既有三文件事务，失败恢复同一集合；不建立新历史文件。

## 原行为及必要适配

| 行为 | 固定原出处 | 当前迁入范围 |
|---|---|---|
| 默认匹配 | SearchKeyAnalyzer / SearchStringTools | 名称模糊匹配；全半角、大小写、假名和数字归一；引号或 /exact 为原精确子串匹配，并非整个名称相等 |
| 逻辑和选项 | DefaultSearchProfile / Searcher | 空格默认 AND；/and、/or、/not、/word、/fuzzy、/re、/ire、比较及底层 /p.* /m.* 按原解析器执行；不新增自定义语法 |
| 属性 | FolderItem.GetValue / BookSearchProfile | 名称、真实来源最后写入时间/大小、非文件夹书签、真实访问历史；虚拟文件夹日期为 EntryTime，大小为 -1 |
| 日期/大小 | DateSearchProfile / SizeSearchProfile | /since、/until、/date、/size 及比较；仅用到日期/大小时后台探测，重复来源别名单次查询只读一次；缺失来源保留原默认时间/大小 |
| 查询范围 | SearchBookmarkFolderCollection.CreateFolderItemCollectionRaw | 默认递归当前书签目录；关闭递归只搜索直接子项；根本身不参与 |
| 结果排序 | BookmarkFolderCollection.Sort / TreeListNode.GetIndex | 沿原 FolderOrder，不按相关度；注册顺序按各自父级局部索引，同索引保持树枚举顺序，降序反转；Directory/File 的 ConstOrder 都为2 |
| 搜索历史 | SearchBoxModel.Search / HistoryStringCollection.Append | 有效语法确认即登记，不等待后台读取成功；增量输入不登记；重复前置、空白忽略、原上限8和零容量、单项删除 |
| 配置/保存 | BookmarkConfig / SystemConfig / BookHistoryCollection | 原递归和搜索框默认 true、原增量默认 true；History.IsKeepSearchHistory 控制已迁书签历史落盘，其他模块旧历史/未知字段保留 |
| 焦点命令 | FocusBookmarkSearchBoxCommand / SidePanelFrame | 显示原书签面板、搜索框并聚焦全选；不冒充尚未迁入的 FocusBookmarkList 书架定位 |

原搜索 Profile 没有注册 Page/元数据属性，`/tags`、`/title` 等不能在这里宣称支持。时间/大小**排序**仍需列表全量元数据契约，继续禁用占位；这与查询属性能力分别登记。

## 状态、资源与错误

查询按调用时捕获的范围筛选，取消、位置改变、书签提交/回滚、关闭及释放使旧请求失效。不能中断的系统调用由原 SourceIo 继续观察、释放槽；其结果不得更新旧窗口。关闭取消未确认筛选并等待已经开始的查询/历史提交，正常保存失败恢复输入。

语法错误、正则错误/超时及来源权限/超时不替换当前可用列表。历史保存错误单独报告，集合回滚，同词可重试；已成功筛选结果可以保留。原 Search(string) 内部吞解析错误并返回全量的路径不作为执行入口，先 Analyze 再使用原键集合筛选。

唯一解析算法的必要运行改造是用户正则每项250ms超时；正常语法/匹配不变，病态回溯报告超时，不长时间阻塞后台槽。搜索字符串缓存只属于单次请求。尚未测量万条目查询耗时或长时间原生内存，不将后台运行等同性能目标达成。

书签编辑自动按已提交表达式重筛；原节点更名后不再匹配则移出结果，新增匹配可进入。已有结果编辑保留查询，显式“定位所在书签文件夹”或树主动导航回到真实父级。`/history` 只在书籍历史路径成员变化时重新筛选，翻页的时间更新不重复全树查询。

## 界面、测试与扩展点

正式 BookmarkListView 在原导航栏下、树/列表上保留搜索、清空和历史区域，错误与任务状态独立显示。搜索框 Enter 只确认查询，Backspace/Delete/数字输入不执行正文或分类命令。历史菜单只消费模型集合；更多菜单接入搜索框、递归、增量与保存开关。保存失败恢复原字段和表现。主题与布局调整不修改解析器或集合规则。

自动回归包含13项原库格式/匹配测试（7个Fact及1个Theory的6组参数），以及17项新增书签业务/正式视图测试：原语法、递归/局部索引排序、日期/大小/别名去重、语法与权限失败、晚到/关闭、原历史去重/容量/落盘/删除/回滚、真实书籍 /history 成员变化、草稿保持、同词失败重试和正式 XAML 键路由。全量175项通过；证据见[本批验收](../acceptance/p2-bookmark-search-runtime.md)。

完整树布局/路径选择、书架 bookmark scheme/目录树互联、StartUp.LastBookmarkFolder、每目录参数、显示模板、搜索帮助全文及完整旧数据导入仍待迁。原通用 Search 库可供后续书架/历史/页面模块复用，各模块先核对真实 Profile，不扩展第二套查询语法。P2尚未封板；真机/Windows对照、NAS、P95与发布独立待验。
