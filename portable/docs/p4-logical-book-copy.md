# P4 第十一批：原逻辑书籍复制

固定出处 `c5c398d89`：`BookControl.cs:138–161` 创建Book.Path对应条目后复制到剪贴板；`BookControl.cs:215–235` 调用DestinationFolder.TryCopyAsync；`DestinationFolder.cs:76–111` 将路径转回ArchiveEntry，再经RealizeArchiveEntry及LimitedRealization输出实体；`ArchiveEntry.cs:581–611` 为原四策略。不能仅根据BookControl传字符串就把目标目录复制理解为原始文件路径操作，也不能用CurrentPage代替书籍条目。

## 职责与依赖

Archive.CreateBookEntry提供当前Book.Path的原条目关系，默认是普通根目录、根归档或用户播放列表文件；RequestedArchive委托其原来源，ArchiveDirectory保留底层归档与目录名称。该替换点不打开新来源、不枚举、不持有新流。

BookOperation.CopyFilesAsync(book:true)沿既有ArchiveEntryUtility解析书籍条目，QueryPath保持Book.Path。TransferBookToFolderAsync仅在逻辑书场景先经LimitedRealization，再复用同一IBookTransferBackend规划/覆盖确认/传输。普通根实体继续既有传输链。移动仍要求真实根实体，不允许因复制策略而移动归档容器。

Mac菜单只依赖既有CanCopyBook/CanCopyBookToFolder及异步命令。没有新视图、业务集合、状态存储、传输服务或生产项目。

## 契约与业务规则

| 书籍条目 | CopyBook剪贴板 | CopyBookToFolderAs |
|---|---|---|
| 普通目录/根归档/用户.nvpls | 四策略均使用真实根地址 | 沿第九批整书协议复制；不复制列表引用目标 |
| 包内目录，None | 仅原QueryPath | 不产生实体 |
| 包内目录，SendArchiveFile | 根归档地址及原QueryPath | 复制根归档一次，真实名称保持 |
| 包内目录，SendArchivePath | 虚拟书籍地址及原QueryPath | LimitedRealization降为提取，按原TODO跳过并提示 |
| 包内目录，SendExtractFile | 仅原QueryPath，未提取提示 | 按原TODO跳过并提示 |

整书复制不受CopyFile多页参数、当前图像、页内分割、搜索空结果或分类面板移动模式影响。固定复制不要求源写权限；不加入或清空分类Undo/Redo，不改历史/书签/列表路径，保持Book/Page/位置/锁定。粘贴私有QueryPath重新打开原逻辑书，不能因系统文件输出根归档而改为整包。

## 状态与资源生命周期

书籍来源所有权仍属于Book。CreateBookEntry只表达条目，不改变来源索引或引入第二身份。归档实体化批次沿原请求所有权：剪贴板提交成功后转交进程服务；失败/取消释放。固定复制在finally释放批次，后端文件/journal清理继续第九批协议。

准备、确认和提交前核对原Book/打开代次；切书或关闭取消未授权准备。已经提交的实体操作等待真实结果，不能影响新书。根归档实体的来源/目标指纹、实际路径保护、整书覆盖确认与取消保持；逻辑书仅复制，不释放当前阅读来源或建立移动路径marker。

## 错误与明确限制

来源不存在、链接、来源/目标变化、Profile/临时根/卷根保护、权限、后端传输及清理失败沿既有错误通道回报。内部目录递归提取是原版未完成能力，不补建新内核；None主动不输出也不解释为错误。

本批支持现有可打开的包内目录书。嵌套归档仍不能打开，属于后续内容能力；Windows快捷方式、Mac链接、真实NSPasteboard/Finder互操作及Windows Shell冲突动态继续单独迁移/验收。

## 测试与扩展点

LogicalBookCopyTests用真实临时目录/ZIP/原格式.nvpls/Profile及模拟剪贴板覆盖四策略、固定复制LimitedRealization、无效CopyFile参数隔离、根来源、显式图片定位、空搜索、原QueryPath粘贴、包含包内目录的列表根复制、覆盖确认/取消、切书/关闭取消、路径保护和正式Headless菜单。既有文件/目录复制、归档租约和整书传输继续回归。

设备、Windows、无损Retina、焦点/弹出层、跨卷/NAS及长期内存随P3/P4集中验收；触控板跳过、多屏无环境。后续嵌套归档接入时仍提供正确原书籍条目；不得扩大移动范围或把临时提取地址保存为书籍定位。
