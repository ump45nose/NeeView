# P5 第一批：原 Profile 与备份只读预览

当前附属文件读取/选择/实际应用与动态恢复清单以[P5第七批](p5-profile-assets.md)为准；下文保留原批次范围。

本文件保留第一批实施与验收边界；当前实际应用扩展见[P5第二批](p5-profile-apply.md)，原 ImportBackup 已由第二批接通。

## 职责与出处

本批把旧数据的来源读取、路径映射和兼容预览接入正式应用。沿用原五文件，不增加数据库、身份模型或生产项目。没有实际导入、旧版本升级或附属文件执行入口。

固定基线 `c5c398d89` 的 `NeeView/SaveData/Exporter.cs` 将 UserSetting、History、Bookmark、Foldres、QuicAccess 放在标准 ZIP 根级；Playlists、Themes、Scripts 位于子目录。`SaveDataProfile` 的特殊拼写保持。原 `Importer.cs` 的顺序和后备分支作为后续实际应用依据。

原格式核验：Bookmark.Nodes 是根对象，Children 为有序树；History.Items/旧 Books 为数组；History 的旧 Folders 是路径键字典（CLR 名 FoldersLegacy）；Bookmark 的旧 QuickAccess 为含 Items 的对象（CLR 名 QuickAccessLegacy）。Page 是条目名，Props 不重解释。本批预览这些真实形状，不把旧 CLR 属性名误当 JSON 字段。

## 依赖与契约

- Engine：ProfileImportFiles/Source/Bundle、IProfileImportReader、ProfilePathMapper、ProfileImportService、ProfileImportPreview。
- Backends：Content/ProfileImportReader 在既有 SourceIo 两槽中只读 Profile 或 BCL ZipArchive；没有落盘解压。
- MacOS：MacApp 装配读取器；MainWindow.ProfileImport 只接入 Engine 契约；独立 ProfileImportViewModel 和 ProfileImportWindow XAML 提供来源选择、映射草稿、三类报告。

PreviewAsync 接收来源和本次映射快照，返回完整候选 JSON、路径、命令和兼容提示。GetDocument 返回复制，界面不能修改内部候选。单个服务串行准备，解析在后台执行。没有调用 SaveData.LoadAsync/Config.SetCurrent，也不向当前运行集合发布状态。

## 状态与资源生命周期

文件及 ZIP 流由单次读取持有并释放。取消/超时沿 SourceIo 规则，不能即时中断的系统调用仍占槽到真正返回；晚到内容不更新界面。预览 VM 持有来源、映射草稿和候选；换来源、编辑映射、关闭立即增加代次并取消需求，旧候选失效。来源选择器在关闭后返回时不继续读取。

Profile 只枚举根级，包中只打开五个 JSON；其他项目列为未导入，不读取脚本内容。限额为单 JSON 32 MiB、五文件总计 64 MiB、来源 10000 个条目、预览 100000 个记录/路径、JSON 深度 64。声明长度和实际读取字节都限制；UTF-8 支持 BOM、原注释和末尾逗号。重复关键文件拒绝，不猜测哪个版本正确。

## 业务规则

- 映射只处理已知 Path/Place/Select、启动快照、目标目录和列表目录字段，及旧目录参数的路径键；未知字符串保持。
- Windows 盘符/UNC 前缀按最长匹配及路径边界选择，前缀比较忽略 Windows 大小写；Mac 尾部大小写保留，分隔符转换为 `/`。
- 相对页名、bookmark:/quickaccess: 等逻辑位置不映射。Page/Props 原值保留，归档逻辑尾部不枚举验证实体。未映射 Windows 位置保留并计数，不自动找同名文件。
- 未知字段、命令、参数、书签层次和顺序保留；不把缺失差分当成删除。缺省键位从同一原命令 manifest 和 DefaultInputScheme 补齐，旧 Control 不改为 Command。
- 对当前 Config 投影外的字段逐项列出兼容待核对提示；只检查已知配置对象的前三层，不声称深层字段全量可执行。原 ContextMenu/SusiePlugins/DragActions 保留并明确未核对，不加载 Windows 插件。
- 命令报告区分现有执行入口、未迁移和未知/脚本，现有入口不等于参数兼容已验。
- 识别 `NeeView/版本` 和 `NeeView.类型/版本`，旧版/未来版提示待迁移或核对；识别并不证明完整兼容。
- “文件”菜单新增明确的“旧数据导入预览…”；原 ImportBackup 保持禁用占位，235 原命令实例计数不变，预览是 Mac 宿主入口。

## 错误与验收

损坏 JSON、错误形状、重复根文件、超限、无可识别文件、无效映射、访问失败和取消均不写入任何状态。失败后保留来源和草稿供重试；未知格式只作报告，不擅自升级。

专项覆盖目录/原导出包等价、五文件特殊拼写、原书签树/旧后备形状、版本名、未知字段、差分键位、最长前缀/边界/UNC/归档页名、损坏/UTF-8/BOM/重复/超限/取消、现有配置与源字节不变、晚到结果拒绝及正式 XAML/菜单装配。合成兼容样本不冒充用户两分支真实导出；真机系统选择器和用户旧数据实测留后续。

## 下一批扩展

先核对旧版本 UserSettingValidator 和差分转换，再设计实际应用：备份、选择项目、原五文件事务、失败恢复及运行集合刷新。必须处理现有 Config/BookOperation/布局/树的持有引用，不能只替换磁盘文件后被正常保存覆盖。附属播放列表/主题独立迁移，旧脚本仅保留并报告，不执行。配置完整界面、高级媒体、分发签名公证按独立增量实施。
