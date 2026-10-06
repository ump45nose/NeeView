# P5 第二十批：备份、保存与重载设置

## 职责与出处

固定Windows基线c5c398d89的Exporter、ExportDataPresenter、SaveSetting/ReloadSetting/ExportBackup命令、ExportBackupCommandParameter、UserSettingTools及BookSource.DirtyBook为依据。迁入唯一SaveData/BookOperation，不沿用ProfileImport的关闭重建流程处理普通重载。

## 契约与依赖

SaveAllAsync取消阅读防抖、等待既有列表flush和JSON写入、保存当前书架及阅读。ExportBackupAsync先执行同一SaveAll，再在原导航锁及SaveData锁内后台串流导出。ReloadSettingAsync仅读取UserSetting，在调用线程原地恢复已迁Config分支及命令差分，不重载或改写History/Bookmark/Foldres/QuicAccess。缺失UserSetting保持Config，命令恢复原默认；损坏/未来版本/坏参数/权限错误拒绝并保留运行状态。

原Config及顶级分支引用保留，当前Book.Setting与Config.BookSetting共享。排序只在SortMode变化时提交，原Page身份/主页面及Part恢复沿原页框。普通配置重载保持Book/Source打开；递归、来源收集模式或PDF尺寸变化按原DirtyBook重新收集，成功才替换来源。重开memento在新排序完成后捕获，实际旧索引保持旧递归值；失败明确报告并保留旧可读来源，其他已恢复配置不退回旧文件。新打开优先；开始重收集前比较并交换打开代次，晚到失败不误报新打开为重载失败。

## 导出范围与资源

必需UserSetting，存在则History/Bookmark/Foldres/QuicAccess；Playlists一级.nvpls、Themes一级.json、Scripts一级.nvjs，含实际Default/Pagemark.nvpls，不包含根Pagemark.json或子目录。主题/列表使用原当前目录，脚本目录由唯一原JSON未迁分支取得。原关闭脚本执行开关不禁止复制备份材料，脚本不装载/执行。

沿已有导入预算：单文件32MiB、总64MiB、10000条目；64KiB缓冲及逐块取消，读取期间增长也校验。读取UserSetting同样动态限额。材料链接、规范名称碰撞、目标覆盖来源拒绝；只有可靠不存在可略过。ZIP写完中心目录后durable flush，同目录随机临时文件提交；失败/取消保持旧备份并清理临时文件，不宣称多来源的跨进程一致快照。

## 界面与关闭

原FileName空字符串默认：空时Documents保存选择器、.nvzip过滤、覆盖确认和NeeView46.3-yyyyMMdd建议名；明确路径直接交由Engine。参数窗口支持字符串草稿，保存/取消不影响原命令差分之外的配置。布局/字体/主题由独立表现恢复，唯一面板内容复用，视图不读写JSON/目录或ZIP。

同窗口动作互斥，加载/实体动作期间不可执行。关闭取消未提交准备并等待动作，原生选择器没有取消API时停止等待；晚到文件句柄只释放，不续写或访问窗口。系统sheet实际关闭行为仍须真机单独验，后台Headless不声明其通过。成功提交后的取消不撤销已完成备份；重收集期间取消明确取消，不给成功提示。

## 测试、错误与扩展

隔离合成Profile/图片验证五根和一级材料、Default/Pagemark、未知字段往返、脚本不执行、自定义目录、超限/权限类型/链接/取消/旧目标保持；保存当前阅读、只设置重载的对象引用、缺失/损坏恢复、递归+排序、来源失败重试、新打开/取消及原生选择器等待退出。正式Headless验证原布局重载及字符串参数，截图用于布局检查。

完整结果见[静默验收](../acceptance/p5-profile-commands-runtime.md)。未知未迁设置继续保留；真实两分支导出、系统选择器、Windows动态、设备/长期资源及Developer ID分发另验，P5整体未完成。
