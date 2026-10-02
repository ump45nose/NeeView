# P2 第四批：原普通书架与文件夹页导航

日期：2026-10-02。承接前三批，在原 FolderPanel 插槽迁入普通书架、前后书和页面目录组导航。P2 尚未整体完成；书签完整操作在下一独立增量。

## 职责与依赖

Engine 的 BookshelfFolderList 管理位置、条目、选中项、排序及加载状态；FolderCollection 保留原排序规则，BookPageCollection 保留原文件夹页算法。Backends 在 IArchiveFactory.ListBooksAsync 下枚举目录/归档候选元数据。表现模型订阅书架快照，XAML 负责显示及输入反馈，不直接枚举文件、不打开所有候选书籍。

## 契约与生命周期

- SetPlaceAsync 成功后提交新位置/列表；失败保留旧列表，提供错误。请求使用 revision 和关联取消令牌，取消、取代及关闭后的结果不能提交。
- SyncAsync 取当前书籍的普通书架父目录并选中来源；同书翻页不重复扫描。UpAsync/EnterAsync 只浏览书架，不改变正文；双击或 Enter 才打开。
- ChangeOrder/Reorder 在已有元数据上重排，保持路径选择；RefreshAsync 刷新同目录并保持选择。默认排序保存在原 Bookshelf.DefaultFolderOrder，目录分组保存在 FolderSortOrder。
- MoveBookAsync 使用书架所选项寻找前后书，加载成功才提交目标选择；失败保留当前书和选择，可修复后重试。默认加载期间忽略切书；原 IsPrioritizeBookMove 开启后允许新请求取代旧加载。
- MoveFolderPageAsync 使用当前范围最小索引及原目录组算法，生成目标时仍按原正向入口处理；不打开其他书籍。
- BookOperation 拥有书架及其取消生命周期，关闭时释放。表现订阅归窗口所有；书架状态更新只刷新面板，不触发正文解码。

## 原业务规则及出处

| 行为 | 固定 Windows 基线出处 | 本批适配 |
|---|---|---|
| 13 项排序、目录置前/置后/混排 | SidePanels/Bookshelf/FolderList/FolderOrder.cs、FolderCollection.cs | 保留自然名称、类型、时间、大小及组内随机；时间/大小同值仍自然名称正序 |
| 书架来源与同步 | BookshelfFolderList、Book/BookSource.cs、Archiver/ArchiveEntryCollection.cs:GetParentPlace | 普通来源同步父目录，选择当前书籍；不误改为当前图片目录 |
| 普通书架候选 | FolderItemFactory、Archive 关系 | 目录及支持归档；图片不是独立书籍项，坏归档保留候选，到真正打开时才报错 |
| 前后书 | BookOperation/BookOperation.cs、PrevBookCommand、NextBookCommand | 普通排序端点停止，仅随机顺序循环；保留阅读位置恢复，不混入页尾自动切书参数 |
| 文件夹页 | Book/BookPageCollection.cs:GetNextFolderIndex/GetPrevFolderIndex | 仅文件名升/降序；组内后退先回本组首项，组首才回前组，端点不循环 |

ResetNextBookPageMode 属于页尾自动切书，不能改变普通 PrevBook/NextBook。MoveToParentBook 需要父书中的真实子条目/Folder 页面，本批继续禁用占位；书架“上一级”只是浏览操作。原源码、指纹及必要改造见 source-migration.json。

## 状态保存与前端边界

Bookshelf 原 JSON 分支合并保存，未知字段保留；IsPrioritizeBookMove 保持原默认 false。排序控件当前编辑**普通书架全局默认**，没有每路径 FolderParameter/FolderConfig.json。随机种子仅在当前书架生命周期中保持；刷新和设置重排不意外洗牌，未持久化种子。设置保存失败显示错误，“刷新”可重试保存。

列表/树、排序 ComboBox 的导航键与查看器 Up/Down 前后书隔离。先替换 ItemsSource 再恢复选择，避免 TwoWay 清空；当前书页变化不清空独立浏览选择。布局、颜色和模板仍由 XAML/主题调整，业务规则留在 Engine。

## 错误、测试与验收

枚举失败、取消及晚到结果保留旧状态；打开失败保留正文和书架选择；正常端点无操作。损坏包作为候选保留，不在扫描中尝试解压。NAS 沿用有界来源后台任务，真实断线待验。

本批 BookshelfNavigationTests 覆盖混合候选、原排序/同值、随机稳定与循环、前后书恢复/端点/失败重试、取消/晚到/关闭、优先切书、两种文件名方向分组、JSON 未知字段和正式 ListBox 方向键/Enter。真机发现旧边界提示遮蔽后续页面，已在成功导航提交时清除，并补边界/打开失败后的阅读回归。累计 **78 项通过**。最终构建/签名原始输出见 ../acceptance/p2-bookshelf-validation.json；正式运行单列 ../acceptance/p2-bookshelf-macos-runtime.md。Headless 截图独立保存，不覆盖前三批证据。

## 扩展点与未完成能力

每目录参数/持久随机种子、巡回、搜索/排除、监视、书架历史、目录树/封面、递归与真实 Folder 页均待迁。书签合并/移动/恢复在下一节点。Windows 动态对照、真人触控板/IME、NAS、长期进程/native 内存、完整显示 P95、Developer ID/公证/安装/发布未完成，78 项测试不代表 P2 封板。
