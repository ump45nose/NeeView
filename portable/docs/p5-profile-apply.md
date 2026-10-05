# P5 第二批：原 Profile 实际应用与失败恢复

## 职责与原出处

在第一批只读预览上恢复原五文件的实际导入。保留 `UserSetting.json`、`History.json`、`Bookmark.json`、`Foldres.json`、`QuicAccess.json`，没有数据库或新生产项目。

原出处为固定 `c5c398d89` 的 `Importer`、`UserSettingTools`/`ObjectMerge`、历史/书签/目录/快速访问 validator 和 `JsonEnumFuzzyConverter`；逐文件 SHA256 见 source-migration.json。原 build 来自 Git 提交计数，本基线为 4340。代码是已迁业务模型上的必要适配，不声称整体原 validator 已迁入。

## 依赖与契约

- Engine：`ProfileImportSelection/Request` 是确认快照；`SaveData.ValidateProfileImportAsync/ApplyProfileImportAsync/RestoreProfileImportAsync` 共用既有五文件事务；`ProfileImportCoordinator` 只接收可等待 close/reopen 委托。
- Backends：继续使用第一批只读 `IProfileImportReader`，实际应用不重新读取来源。
- MacOS：表现模型管理选项和候选状态，正式 XAML/确认窗只返回请求。原 `ImportBackup` 和预览菜单进入同一界面。`MacApp` 经唯一 `CreateWindowAsync` 重建阅读控制、配置、布局、树和查看器；不广播通用配置刷新、不增加第二入口。

## 状态与资源生命周期

1. 预览/确认快照与运行状态分开；来源、映射、关闭使旧代次失效，后台复制结束后再次核对选项。
2. 协调器验证候选，不改 Config.Current/磁盘；验证失败不关闭旧窗口。
3. 等待旧窗口正常关闭准备，保存位置/面板状态并释放原 BookOperation/显示资源。失败保持旧窗口可重试。
4. 在 SaveData gate 内重新读取刚保存的磁盘，合并/校验候选，建立五文件持久备份，再提交实际选中且来源存在的文件。JSON 合并、序列化和哈希在后台，不把 UI 委托放到后台。
5. 同一正式装配路径加载新状态、创建/绑定窗口并恢复书籍。窗口构造/绑定失败先释放已经创建的实例；未接管资源仍由启动层释放。
6. 重建失败且资源可释放时，恢复备份后重开原状态。失败窗口无法释放时禁止回滚，避免存活引用覆盖恢复文件；恢复失败保留材料并报告两个错误。

导入任务期间 Finder/Dock 打开等待该任务；退出请求先等待导入或恢复完成，再正常关闭。旧实例不再接受命令或写回。来源流沿第一批读取预算和取消规则；本批不长期持有来源流。

## 业务规则与支持范围

默认选中设置、目录参数和快速访问；历史/阅读位置、书签需显式选中，与原 Importer 默认一致。来源独立文件优先，缺失时分别使用旧 History.Folders 字典及 Bookmark.QuickAccess；选中且没有后备的文件保持现有字节。

| 文件 | 可实际应用版本 | 迁移范围 |
|---|---|---|
| 设置、独立目录参数、独立快速访问 | 46.1–46.3 | 已迁 Config/集合及原差分默认；46.0/Alpha、更旧版本仅预览 |
| 历史、书签 | 44–46.3 | 原明确 Books/Page/Props、UNC根、去重与日期分支 |

任何 build >4340、非零第四段版本、类型不匹配或缺失格式均仅预览；可以取消不支持的设置选择，仅导入已支持集合。未知配置字段保留并报告，不等同于对应能力已实现。

设置缺省字段以原构造默认补齐，再合入完整来源 raw 与现有未知字段；命令差分整体恢复，来源未记录键位回到原默认。旧 Control 不全局替换为 Command。读取兼容原字符串枚举及早期 Mac 数值，保存不全局重写格式。已知坏参数/Props/日期/重复路径等在关闭前拒绝。

历史/书签 Page 始终为原条目名。旧 Books 合入原记录后从活动分支移除，原未知元数据留在 `MacImportedLegacyBooks`；旧 UNC 根去重前节点保存在 `MacImportedLegacyItems`，不小写尾部路径。书签根层次、名称和顺序保留。导入集合恢复为来源内容，重复应用不追加重复集合。历史的保存开关、数量/期限仍按导入后的原设置生效。

未映射路径保留，不自动搜索同名文件。附属主题、播放列表和脚本本批不导入，脚本不执行；完整旧设置升级、旧布局 V0/V1、自定义菜单/高级设置继续后续批次。

## 备份与错误恢复

`Application Support/NeeView.Mac/ImportBackups/<UTC>-<GUID>` 保存原始五文件字节与 `manifest.json`，哈希校验内容，null 记录原文件不存在。恢复先验证全部清单/哈希，再精确恢复原字节及缺失状态。

普通保存、导入、恢复共用 `.tmp`、`.save-backup`、`.save-pending.json` 原语。提交 marker 前可以取消；之后完成或回滚。部分提交失败恢复旧字节，中断在下一次 Load 恢复；恢复失败不清理唯一副本/marker。持久导入备份不会随事务副本清理删除。多进程共同写 Profile 未支持。

## 测试与验收

专项覆盖默认/显式选择、差分默认与未知字段、原枚举、未选中字节保持、缺失/后备、版本边界、旧 Books/UNC/日期、备份哈希和原缺失恢复、准备/部分提交/重建/恢复失败、取消、独立快照、再次保存不覆盖新数据，以及正式 XAML 双向绑定、确认取消、原命令和窗口关闭/重建。

[本批静默记录](../acceptance/p5-profile-apply-runtime.md)分别报告自动回归、正式 Library/ARM64 应用及本地签名。全部样本为隔离合成 Profile，用户数据与真实桌面应用未操作。Headless 不证明系统选择器、用户两分支实际导出、Windows 动态或真实设备行为。

## 后续扩展

继续逐分支迁入完整原 UserSettingValidator、旧布局/差分兼容，然后用真实导出夹具验收。附属主题/播放列表、完整设置及高级媒体分别交付；Developer ID、公证、安装测试不从开发 ad-hoc 签名继承。
