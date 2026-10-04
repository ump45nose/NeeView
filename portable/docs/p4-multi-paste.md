# P4 第七批：多来源加载与原播放列表来源

固定基线 `c5c398d89` 的 BookHubTools、ContentDropReceiver、PlaylistArchive/PlaylistArchiveEntry 和 PlaylistSourceTools 是本批行为依据。沿用 BookOperation、Archive、Page、PlaylistHub 与原 JSON，三个生产项目及唯一窗口入口保持。

## 职责、依赖与契约

Engine.PlaylistSourceTools 是原 v1/v2 的唯一解析入口，全局面板与来源共用。BookOperation.OpenFilesAsync 接收绝对路径序列，一项仍普通打开，两项以上由 ITemporaryPlaylistService 创建原格式 .nvpls，再进入同一 OpenCoreAsync。Paste 优先使用原 QueryPath，其次标准文件 URL；Finder、启动参数和主窗口多项拖入共用这个入口。文件选择器保持现有单选交互。

Backends.PlaylistArchive 实现原列表来源；PlaylistArchiveEntry 保留列表的显示名和连续 Id，实际读取、内容类型、定位及实体化由 InnerEntry/TargetArchiveEntry 决定。别名不需要图片扩展名，合法反斜杠和斜杠不当作归档内目录。工厂明确区分压缩格式和 .nvpls，不将列表 JSON 交给 SharpCompress。

## 状态与资源生命周期

临时创建、打开、索引、探测及提交共用一次代次和取消链。较新请求或关闭使旧结果失效；晚到来源在原加载 finally 中释放，不能覆盖新书或错误。来源 owns 实际目录/归档，重复路径共用实际条目，代理仍保持各自身份；条目流由读取请求释放。关闭列表只释放来源，不删除用户列表或临时列表文件。

TemporaryPlaylistService 由进程持有，每实例使用随机独立目录。成功列表切书和关窗仍保留，支持进程内导航历史；正常退出删除本实例目录。取消清理部分文件；清理失败阻止继续创建，退出可重试。链接替换拒绝删除外部目录。列表读取最多 8 MiB/一万项，进程生成列表总量最多 32 MiB；不是跨启动缓存清理保证，异常退出遗留回收待完善。

## 业务规则

- 按原接收顺序保存路径，保留重复项和混合目录，不提前递归展开、不登记或选择全局 PlaylistHub。
- 索引依列表顺序解析，缺失/不支持项跳过，成功 Id 连续；显示排序继续采用原阅读设置。仅 Entry/EntryDescending 按登记顺序排序，普通来源不开放这两个入口。
- 三种页面收集沿原规则；All 的 WherePageAll 按真实 SystemPath 排除已包含子项的目录/归档，完整来源索引仍保留它们。递归沿原设置处理真实子书，链接/列表引用不递归，嵌套压缩仍未接入。
- 原临时书运行中可以登记历史；保存历史副本时排除应用临时根。LastBook 可保留临时地址，FirstLoader 恢复入口过滤临时来源，启动不尝试打开已经删除的列表。
- CopyFile/固定复制根据真实 TargetArchiveEntry 复核和提取。多个代理指向同一归档条目共用实际源和输出文件。分类、移动和删除不扩大到引用文件，不把临时列表的路径冒充实体图片目录。

## 前端、错误与验收

视图只转交多项路径和稳定命令，菜单依据来源能力启用 Entry 排序；临时文件创建、解析、过滤、状态保存和解压均在 Engine/Backends。主题和窗口结构可独立调整。

列表格式/版本或整体读取预算错误保留旧书；逐项错误跳过并记录来源诊断；取消不报新书错误。Windows 路径转换属于 P5 导入，不声称本批可直接解析任意旧 Windows .nvpls。位图、文本/网络地址等其他 Paste 内容、基础书籍文件菜单和其他删除范围仍待迁移；Cut 继续禁用。

专项覆盖原三格式只读、重复/混合/缺失、真实类型/别名/封面、归档去重、排序、全局 Hub 不变、历史写出/启动过滤、准备及索引晚取消、退出失败重试，以及正式窗口入口/菜单能力。真实 Finder/NSPasteboard、Windows 动态和 P3/P4 设备/长期性能集中验收，静默 Headless 不代替这些项目。
