# P4 第六批：原归档文件实体化复制

本文件描述当批增量；当前删除、图片接收与链接范围以[P4收尾契约](p4-completion.md)为准。
迁入固定 `c5c398d89` 的 `ArchivePolicy`、`ArchiveEntryUtility.RealizeArchiveEntry`、`ArchiveEntry.RealizeAsync/GetFileProxyAsync` 及原 `ClipboardUtility` 调用关系。使用既有 Archive/ArchiveEntry、BookOperation、DestinationMoveService 和原 JSON，不建立另一来源或文件身份体系。

## 职责与依赖

Engine 的 ArchiveEntryUtility 按原选页顺序解析策略、保序去重并拥有准备批次。IArchiveEntryRealizer 仅替换临时实体生成和持有；Backends.ArchiveEntryRealizer 读取原 Archive.OpenEntryAsync，生成独立文件租约。MacApp 唯一装配此进程级实例；视图只转交命令及设置草稿。

| 原策略 | CopyFile 剪贴板 | CopyToFolderAs 固定复制 |
|---|---|---|
| None = 0 | 归档项仅 QueryPath；不生成文件 URL/文本 | 归档项不生成复制请求 |
| SendArchiveFile = 1 | 所在根归档文件，多个页指向同一归档时保序去重 | 复制根归档实体一次 |
| SendArchivePath = 2 | 原归档内虚拟路径；QueryPath 同时保留 | 原 LimitedRealization 转为提取文件 |
| SendExtractFile = 3，默认 | 独立临时文件 URL + 原 QueryPath | 提取后复制真实文件 |

普通文件不受归档策略影响，保持其实体地址。CopyBook 仍复制整个根实体目录/归档。当前可实体化文件页包括目录中的普通文件/归档书籍文件和已支持 ZIP/RAR/7z 内文件；链接、实体目录复制及归档内目录提取仍按实际能力禁用，后续独立迁移。数字分类和固定移动继续限真实普通图片，不能移动提取缓存。

原 `ClipboardUtility.SetDataAsync` 的 `OriginalPath` 分支再次调用相同的提取策略，实际返回提取文件路径；本批保留该基线行为，未因选项名称改为 QueryPath。None 时没有文件文本；CopyFilePath 与 OriginalPath 均使用策略输出。未认识的旧策略保持 JSON 原值并明确报错，不静默改为默认。

## 契约与资源生命周期

- ArchiveEntry.CanRealize：当前已接入文件能力，不修改 SystemPath/FilePath。
- ArchiveEntryUtility.RealizeArchiveEntry：返回 RealizedFilePathList，包含保序路径与临时租约；准备失败/取消释放所有已生成文件。
- IArchiveEntryRealizer.ExtractAsync：预算、长度与取消校验；随机独立目录仅保留原叶文件名，不沿归档内部路径创建目录。读取流归请求，独立输出不依赖原固实缓存路径。
- RetainClipboardAsync：仅在 WriteAsync 真实成功之后转交整个批次；切书、Unload、关窗驻留均不释放。下一次成功复制（包括普通文件、根书籍或 None）替换旧批；失败复制保留旧剪贴板及旧材料。正常进程退出清理，失败可重试。
- RealizedFilePathList.DisposeAsync：固定复制在全部真实结果返回后释放提取文件；不释放原来源路径。单项清理失败仍继续其他项；后端独立登记失败目录，调用方批次离开作用域后仍由进程退出重试，不冒充新剪贴板。

每个提取批次最多 2 GiB；成功剪贴板最多保留一批，替换准备期间可同时存在旧批和新批。固实来源缓存沿既有每来源 2 GiB 规则独立计费。不是全进程磁盘总预算；异常退出遗留缓存跨启动清理尚待完善。任意请求或旧剪贴板材料清理失败时暂停新的提取，避免失败材料无界累积。

## 状态和业务规则

CopyFile 在原导航锁内捕获/复核 Book、代次及原页组；解压准备可取消，晚到租约清理后不发布到新书。NSPasteboard 允许只有 QueryPath 的快照，标准 URL 与私有逻辑地址分别保存。虚拟文件 URL 不保证 Finder 能作为真实文件复制，默认提取策略用于系统互操作；设备验证独立记录。

固定复制在 DestinationMoveService 原忙碌锁中完成整组选区的存在性检查和提取准备，再执行原文件协议。整组准备失败不复制任何项；真实传输中失败/取消仍保留并回报前项成功。复制无源修改权限要求，不入移动历史，不改变归档当前页或分割位置；同目录普通文件复制按既有索引协调。切书/退出取消未生成请求的准备，已开始传输仍等待真实结果。过期错误不能清除新打开错误。

文件设置的原 ArchiveCopyPolicy 四选项沿 SettingsWindow 草稿及 ApplyOptions JSON 事务，取消不应用，失败回滚，未知字段保留。主题、菜单和表单不承担策略或解压算法。

## 错误、验证与扩展

报告缺失、链接、未知策略、尺寸/长度不符、预算超限、取消、系统写入拒绝及临时清理失败。真实 NSPasteboard/Finder、Windows 动态、真实跨卷/NAS/权限、无损 Retina 与长期内存仍按 P3/P4 集中验收，不由静默回归替代。

专项覆盖四策略×三文本、两方向三页组、固定复制 LimitedRealization/归档去重、查询回贴、固实与普通 RAR/7z、归档内目录定位、同叶名与路径逃逸、准备失败/晚取消/切书/关闭/系统提交点、临时租约替换和进程退出、预算/坏长度、覆盖取消、正式菜单/设置取消与保存失败。后续继续原多来源临时播放列表、其他 Paste 类型、基础书籍文件菜单和其他删除范围。Cut 保留用户决定的禁用占位。

当前目录页复制范围由[P4第十批](p4-directory-copy.md)补齐：普通目录直传/整树固定复制及内部目录四策略已接入；内部目录提取经基线核验为原版TODO，保留跳过并明确提示，不计作迁移丢失。逻辑书按策略复制、链接和其他范围继续按P4清单推进。
