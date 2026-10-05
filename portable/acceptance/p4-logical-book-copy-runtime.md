# P4 第十一批：原逻辑书籍复制静默验收

本批核验并接入原BookControl → DestinationFolder/ClipboardUtility → ArchiveEntryUtility复制链。Book.Path对应条目由已打开来源提供；RequestedArchive保持书籍根，包内目录保留底层归档关系。CopyBook使用原四策略，固定复制先LimitedRealization，再复用既有整书后端；不将CurrentPage作为书籍目标。

## 最终结果

| 检查 | 结果及边界 |
|---|---|
| 源码及依赖 | 三个生产项目；62个源码文件、232个局部适配登记、26个原依赖源码指纹检查通过，数量不是功能覆盖率 |
| 全量自动回归 | **779/779通过，0跳过**；新增22个逻辑书复制用例 |
| 专项自动回归 | **173/173通过，0跳过**；逻辑书、整书传输、文件剪贴板、归档实体化及目录复制 |
| Engine与正式Library | 默认输出串行编译通过；不是运行验收 |
| 正式Mac应用 | 默认输出构建通过，Mach-O ARM64 |
| 本地签名 | 严格深层codesign校验通过，开发ad-hoc；非Developer ID/公证 |
| 完整命令 | 235个原实例，167个执行入口、68个占位；本批扩展已有能力，数量不是功能覆盖率 |
| 正式Headless菜单 | 逻辑书CopyBook/CopyBookToFolderAs可用，逻辑移动禁用，命令执行通过 |
| 用户资源只读 | 最新3个子目录59/81/78页，共218页；瀑布12位置、缩略9位置通过；两组关闭后租约/缓存均0 |

完整输出见[全量验证](p4-logical-book-copy-validation.json)、[专项计数](p4-logical-book-copy-targeted.json)、[完整命令](p4-logical-book-copy-commands.json)和[本批资源采样](p4-logical-book-copy-resource.json)。正式应用位于 `portable/src/NeeView.MacOS/bin/Debug/net10.0-macos/NeeView.MacOS.app`。其他生成截图及初次失败TRX保留在本机忽略的artifacts，不进入提交。

## 行为与回归

四策略分别覆盖剪贴板及固定复制；包内目录SendArchiveFile传根归档，SendArchivePath剪贴板传逻辑书地址、固定复制降为提取。原内部目录提取TODO保留，跳过时明确提示。None仅传QueryPath或不生成固定复制实体。移动仍限定根实体，不能因SendArchiveFile而移动容器。

测试覆盖无效CopyFile参数隔离、显式图片定位、空搜索、逻辑QueryPath粘贴、当前Book/Page/位置/锁定保持、列表包含内部目录与实体图片时整书只复制.nvpls、整书覆盖确认/取消、切书/关闭取消、Profile保护及分类Undo/Redo保持。两名独立只读审查未发现明确可行动缺陷；建议补充的列表根固定复制用例已加入。

初次新测试两处JumpAsync调用签名错误已修正。首轮专项164/172通过，8项失败均为测试准备问题：7项在阅读状态一秒防抖保存前假设LastBook已写入，现先保存基线再验证复制不改变定位；1项新建.nvpls误用Version字段，已使用原Format。补充列表根固定复制后最终173/173专项、779/779全量及两种正式构建/签名通过。没有构建目录占用或输出目录改写。

## 验收限制

本批没有启动或激活正式应用、访问Windows、读写真实系统剪贴板/Finder/废纸篓或用户图片。NSPasteboard仅QueryPath输出已有明确实现，仍需真实系统互操作验收。Headless/只读抽样不证明无损Retina、完整焦点/弹出层、Windows动态、跨卷/NAS/权限或长期原生内存；继续随P3/P4集中验收，触控板跳过，多屏无环境。

本批只覆盖现有可打开的包内目录书；嵌套归档仍为后续内容能力，原内部目录递归提取不列为迁移丢失。P4尚未完成，链接、其他删除类型/范围和Paste内容继续迁移。按最新AGENTS规则，校验完成后自动提交并推送当前分支，实际结果单独报告；公证与发布未执行，用户.DS_Store保留。
