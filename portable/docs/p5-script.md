# P5：原脚本运行、命令及表现适配

## 职责、依赖和出处

迁入原 PropertyMap、ConfigMap、CommandAccessor/Patch、ScriptCommandSource、ScriptConfig、参数和原文本分割规则。原文件与固定基线 SHA256 记入 source-migration.json。Engine 保持纯 .NET；Backends 使用与原版一致的 Jint 4.9.3/Acornima 1.6.2，Mac 的访问器只桥接现有窗口、选区和树。三个生产项目、同一 BookOperation/SaveData/归档/文件操作链保持。

## 契约和业务规则

- UTF-8 `.nvjs` 一级扫描，大小写扩展名兼容；动态 `Script_` 命令和差分参数进入原登记表。目录监视防抖、拒绝旧代次，系统 Error 后重建同路径监视。
- 每次执行使用独立 JavaScript 引擎。`include` 共用当前引擎和相对目录栈；`sleep`、纯循环及宿主调用接收同一取消令牌。`nv.Values` 是启动层持有的进程共享字典。
- 原命令名、三个旧名替代和七个废弃命令诊断保持。参数 Patch 使用调用上下文，只覆盖当前执行，不修改持久参数。旧成员遵循原错误级别，错误保留真实脚本路径/行号。
- 五个原事件文件名进入已有启动、书籍加载、实际页面显示、主窗口状态和页尾链。当前 WindowStateChanged 只针对主窗口；独立中央查看器宿主待下一批接入，不把侧栏窗口事件假作主窗口事件。
- 真实页面、阅读范围、明确多选、媒体、效果层/预设、外部应用、目标目录、普通书架/历史/书签/播放列表使用原权威模型。选区写入实际列表，树展开写入实际表现节点；没有第二套选中集合、阅读状态或文件操作内核。
- 文件 copy/move 顺序逐项执行。实际成功项立即协调索引/路径/缓存和共享撤销栈；晚取消先完成已提交项协调，再传播取消。目录复用整书后端和恢复记录，复制不进入移动栈。DeleteFile 使用同一废纸篓/确认/权限/保护规则，不降级永久删除。
- 普通目录与 QuickAccess/Bookmark 树区别保留；目录 Add/Insert/Remove/Move 明确不支持，快捷访问与书签编辑进入既有 JSON 事务。原书签节点 Insert 追加行为保留。目录历史采用原容量 100 的环形集合，只登记成功位置。

## 生命周期和资源

ScriptManager 跟踪任务至运行时/取消源真正释放。关闭先取消并等待当前任务；保存失败保留原 manager、书籍和窗口供重试，可靠保存后才释放服务。脚本目录草稿失败回滚恢复同一配置引用、监视目录和动态命令。Console 关闭只取消自己的执行；日志拒绝已关闭回报。

每个内容流、文件事务、外部应用材料及像素仍由原模块所有；脚本不长期持有新的文件句柄。原生调用无法立即中断时等待结果并清理。`system` 由启动层转交真实进程启动，不由视图构造 shell。

## 界面、设置和帮助

独立控制台支持 Enter 执行、Shift+Enter 换行、上下历史、Tab/Ctrl+Space 补全及原 `cls/help/?/exit`。100 条历史、100 个建议、64Ki 字符输出；补全只读类型/PropertyMap/命令元数据，不执行业务 getter。帮助 HTML 按实际 Mac API 生成，经唯一平台打开。

脚本设置为独立草稿页并进入原设置搜索；取消、失败和重试走同一配置事务。原目录首次 Sample 按原字节生成，已存在目录不补写/覆盖。封面正则、原最小深度和 StartUp.IsOpenLastBook 同批补入已有表单；启动开关沿原默认关闭，显式 Finder/路径打开优先，关闭开关不删除 LastBook。封面深度不人为限制为 4。

## 当前兼容边界和错误

本批不宣称原脚本 API 已全部对齐：Bookshelf item 任意实体改名、CreationTime 和显式递归 Open；部分 Bookmark item 文件元数据；中央查看器浮动和 Window.State 的 FullDesktop 仍待迁入。MainView 使用独立代理，未装配时 Open 明确报错、Close 不关闭主窗口。Susie 属 Windows 专属能力；旧 SQLite assembly 开关保留 JSON，Mac 不装载或冒充支持。实际 API 手册已有生成入口，原完整本地化文案仍待核对。

未实现、权限/路径、来源关闭、取消和 JavaScript 错误分别传播到控制台。配置未知字段继续保留；附属导入材料现在可由本运行链装载，不执行旧源文件的隐式导入操作。

## 测试和扩展

实际 Jint、原 PropertyMap/枚举/颜色、include/路径/行号、动态参数、原别名、五事件、控制台、真实选区/树/JSON、文件提交后取消/撤销、关闭/保存失败及 watcher Error 均有隔离回归。Headless 使用正式 XAML 和产品访问器，不激活桌面或读取用户 Profile。系统选择器、外部真实应用和 Windows 动态脚本另验。

下一批只补实际缺口，复用当前契约；公开 API 变化同步本文件与帮助。没有新增 SQLite 状态库、WPF 模拟层、长期 Preview 或另一执行入口。
