# P4 第十批：原普通目录页复制与归档目录策略

固定出处为 `c5c398d89`：`ArchiveEntry.cs:581–611` 的文件系统直传及四归档策略、`Archive.cs:537–543` 的 CanRealize、`ArchiveEntryUtility.cs:275–300` 的保序/去重，以及 `DestinationFolder.cs:52–111` 的固定复制链。原内部归档目录的 SendExtractFile 分支明确写有 `TODO: ArchiveDirectory 対応` 并返回 null，**内部目录递归提取属于原版未完成能力，不列为迁移丢失**。不通过重新设计增加目录提取内核。

## 职责、依赖与契约

- ArchiveEntry/ArchiveEntryUtility：普通目录与普通文件均直接返回真实 FilePath；播放列表沿 TargetArchiveEntry 解析真实类型/名称。内部目录仍可传根归档/虚拟地址，不能提取为临时目录。
- BookOperation：沿原 CollectPages 收集 Once/All/AllLeftToRight，整组准备、类型/路径核对、结果与来源协调。CopyFile 写系统协议；CopyToFolderAs 固定复制，不要求源写权限，不读取面板模式。
- DestinationMoveService：保持唯一进程忙碌锁。目录请求携带既有 BookTransferPlan，经独立整树覆盖确认、应用路径保护复核后调用 IBookTransferBackend。拒绝目录 Move，不注册或清空分类 Undo/Redo；先前成功项仍回报。
- FileOperationBackend：复用第九批整树指纹、验证复制、暂存、覆盖副本、journal/恢复及 ReleaseAsync；单图片入口仍拒绝目录，没有第二文件内核。
- Mac 视图：只呈现目录覆盖范围与确认/取消；停止宿主时解除回调。结构、主题与业务仍分离，视图不枚举或执行传输。

FileTransferRequest 的 DirectoryCopyPlan 只用于固定目录复制，来源/落点必须与快照一致；普通文件请求保持原协议。RealizedFilePathList 记录原提取策略跳过的目录并提供能力提示，不把空输出解释为完成实体复制。

## 状态与资源生命周期

普通目录输出仅引用源地址，不生成租约。CopyFile 成功后可替换旧归档临时批次；切书/关窗/进程清理都不能删除用户目录。归档文件继续使用第六批有界提取和进程剪贴板租约。

固定复制在原导航锁内复核 Book/代次/捕获页组，整组索引与目录指纹准备成功后才执行传输。linked 准备令牌持续覆盖确认等待；切书/关闭可取消尚未授权的项。已经提交的实体结果继续完成必要保存与恢复材料清理；关闭等原导航锁。确认后再检查 Profile/临时根/卷根及实际路径，后端复核来源/目标完整性快照。

当前书籍目录外的复制保持原 Book/Page/分割位置。实际目标位于当前目录内时，目录覆盖可能改变多个后代，通过唯一 OpenCoreAsync 重建真实索引，保留 memento、页面搜索、锁定及仍存在页面的 Part；页面被覆盖删除时按原打开定位回退，不捏造图片项。只有原代次仍有效才重载；后续明确打开优先。实际路径检查解决 `/var`、`/private/var` 与目标目录别名，不改 JSON 定位。受影响旧页面像素和目录封面经同一 BitmapFactory 失效。

## 业务规则

| 条目 | 剪贴板策略 | 固定复制 |
|---|---|---|
| 普通目录（含列表别名） | 四策略均传真实目录地址；QueryPath 保持原定位 | 完整内容、非图片、空目录；目标名为真实叶名 |
| 归档内部目录，None | 仅 QueryPath | 无实体请求 |
| 归档内部目录，SendArchiveFile | 根归档地址，保序去重 | 复制根归档一次 |
| 归档内部目录，SendArchivePath | 虚拟原路径及 QueryPath | LimitedRealization 后跳过提取并提示 |
| 归档内部目录，SendExtractFile | 仅 QueryPath，并提示未提取 | 跳过并提示，其他可复制项仍沿原规则执行 |

混合选组保留原页序，两别名指向同一真实目录只产生一次实体请求；QueryPath 保留原页组定位。数字分类/固定移动仍限真实普通图片，Cut 保留用户决定的禁用占位。

Mac 目录冲突明确确认后整体替换或取消、不合并。目录复制不改变历史/书签/列表引用路径。自包含、祖先覆盖、类型变化、链接树、Profile/应用临时根/卷根拒绝。完整 Finder 元数据保真与 Windows Shell 合并/冲突动态不在本批静默证明内。

## 错误、测试与扩展点

缺失、类型/指纹变化、权限、链接、路径保护、取消和恢复清理失败分别回报。内部目录提取返回原 null 结果，但增加可见能力提示，这是明确的表现改善。原 None 的主动不输出策略保持。源目录不因剪贴板租约清理、复制失败或分类撤销而被删除。

DirectoryCopyTests 使用真实临时目录/ZIP/.nvpls/Profile、模拟剪贴板和正式 Headless 菜单，覆盖四策略、中文/非图片/空目录、真实名称/重复别名、混合 All/AllLeftToRight 保序、原分类 redo 保留、目录确认/取消/变化、目标别名确认后重定向、链接/保护/类型变化、关闭/切书/晚取消、失败阅读保持、当前目录重载及独立覆盖窗口。既有文件、归档租约、整书、分类与列表测试继续回归。

真实剪贴板/Finder、Windows 动态、跨卷/NAS/权限、Retina、焦点与长期原生内存仍按 P3/P4 集中验收；触控板跳过、多屏无环境。原命令数量不变。下一批继续链接适配、其他删除范围和 Paste 内容；内部目录递归提取可作为后续扩展单独评估。
