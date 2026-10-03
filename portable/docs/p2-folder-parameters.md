# P2 第十四批：原每目录参数与书籍排序命令

职责：迁入原 FolderParameter/FolderParameterMemento 与 FolderConfigCollection 的参数分支。BookshelfFolderList 共用 SaveData 持有的唯一集合；面板排序改为当前目录参数，不再修改全局 DefaultFolderOrder。未支持的路径/登记时间排序仍保留菜单占位。

契约：FolderParameter 保留排序、随机种子、递归及原默认归一。非随机种子归零，随机缺种子补非零值；刷新、返回及重启使用相同种子，明确重新选择随机才洗牌。递归继承取最近显式祖先，普通路径按 Mac 分隔符，bookmark 虚拟路径保留原逻辑分隔符。递归读取内容的执行链尚待下一增量，不把保留参数当作该能力已完成。

原文件名是 **Foldres.json**（SaveDataProfile 的特殊拼写），Format 为 NeeView.Folders，Folders/Place/Parameter/Thumbs 沿用原结构。根、条目、参数未知字段和缩略配置保留；保存关闭 IsKeepFolderStatus 只省略副本已识别参数，运行集合仍可使用。Windows FileResolver 外部移动猜测和旧版本导入不在本批。

SaveData 在同一事务准备 History/UserSetting/Bookmark/Foldres 四文件、备份、标记和原子替换。兼容旧双/三文件标记；第四文件准备失败不改变权威文件，异常中断启动回滚。目录编辑失败恢复原集合，再以原路径重排恢复选择；取消排队不执行候选。资源仅为 JSON/元数据，不持有流或界面对象。

原 SetBookOrderByFileName/Type/TimeStamp/Size（升降）及 Random、ToggleBookOrder 接通。切换顺序来自原 Normal 能力表，不选择当前来源不支持的类别。RandomBook 从当前独立书架排除正文当前书籍后打开，成功才更新选择；它不改变排序或种子，也不代替 SetBookOrderByRandom。

错误与测试：损坏参数/重复路径/错误数组拒绝加载，来源不被覆盖；目录不可访问沿用有界来源后台队列。专项覆盖目录隔离、往返/刷新/重启种子、nullable默认/递归、未知字段/缩略配置、取消/四文件失败重试/中断恢复及原命令。正式 XAML/构建、本地签名与真机验收分别记录，见验收文件。

扩展：书架 bookmark scheme 的展示/互联、原真实父子书、递归内容加载、巡回/书架搜索、元数据书签排序、外部文件监视仍按后续增量迁入。只维护三项目及原 JSON，没有新增数据库、来源身份或第二阅读内核。
