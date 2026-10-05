# P4 第五批：原文件剪贴板与粘贴加载

本文件描述当批增量；当前删除、图片接收与链接范围以[P4收尾契约](p4-completion.md)为准。
沿固定 Windows 基线 `c5c398d89` 的 `CopyFileCommandParameter`、`BookPageActionControl.CollectPages/CopyToClipboard`、`BookControl.CopyBookToClipboard`、`ClipboardUtility` 及 `ContentDropReceiver` 迁入。保留原命令、页组选序及 JSON，使用 NSPasteboard 替换 Windows 剪贴板，不增加阅读内核、存储或通用平台框架。

## 职责、依赖与契约

- `BookOperation.CopyFilesAsync(book, token)`：复制当前页组的真实实体路径，或整个书籍目录/根归档地址。只写剪贴板，不复制或移动内容文件。
- `CanCopyFiles(policy)`、`CanCopyBook`、`CanPasteFiles`：按当前来源、加载/索引、关闭及文件动作忙碌状态返回实际能力。复制不要求 `IsFileWriteAccessEnabled`。
- `IFileClipboard`：只探测支持类型，读取有限地址快照、写入业务准备好的地址与可选文本；无窗口或 AppKit 类型，不负责选页或打开。
- `FileClipboardContent`：分别保存系统实体文件、原 QueryPath 逻辑定位和可选文本。未来归档实体化后两类地址可以不同，不要求它们逐项相同。
- `FileClipboardCodec`：本机 `file:` URL 解码、路径与私有 JSON 数量/长度校验。最多 1024 项，单路径最多 16384 字符，私有 JSON 最多 1 MiB；非法项整组拒绝，不能过滤后误开剩余单项。
- `MacFileClipboard`：主线程使用 `public.file-url`、`org.neeview.query-paths`、可选 `public.utf8-plain-text`。能力查询只在 UI 线程使用，不从后台同步等待 UI；读取检查 ChangeCount，不消费剪贴板或执行文件操作。

具体实现仅在 `MacApp` 装配。Engine 选择原页组、检查真实实体和代次；MainWindow 只转交可等待命令及关闭取消。菜单展开/重新激活重查能力，布局、主题及参数表单可独立调整。

## 状态和资源生命周期

每个窗口最多一个剪贴板调用，登记完成任务及当前准备令牌；与分类、删除、改名及卸载互斥。复制在原导航锁内捕获并复核来源、地址和元数据，切书取消尚未提交的复制；粘贴读取后核对打开代次，旧结果与旧错误不能覆盖新书。

宿主退出在等待文件动作前取消当前剪贴板准备。Engine 关闭也取消并等待同一任务，然后才保存并释放来源。排队到主线程的原生调用可及时取消，晚到回调不执行；回调开始后在系统提交前再次检查取消。完整准备 `NSPasteboardItem` 后才清空并写入，检查真实返回值；提交点后不以晚取消伪装失败，关闭等待实际结果。

当前调用完成后释放 linked CTS、清除忙碌并完成等待任务。关闭保存失败重建关闭令牌，当前书籍和复制能力保留，允许重试。不对显示像素或缓存创建额外租约。

## 业务规则与兼容范围

CopyFile 复用唯一 `CollectFileActionPages` 的 Once、All、AllLeftToRight、原阅读方向和分割页去重；瀑布须明确选中图片，不将滚动可见集合当作文件目标。第五批允许普通目录真实项；归档文件实体化已由第六批接入，见[p4-realization.md](p4-realization.md)。缺失或链接不写入剪贴板。CopyBook 使用整个实体目录或根归档，不读取 CopyFile 参数，单图定位打开仍复制所在书籍目录。

原 `TextCopyPolicy` 的 None=0、CopyFilePath=1、OriginalPath=2 保持，默认 None。第五批均为真实实体路径，后两项文本相同；第六批核对原ClipboardUtility实际OriginalPath行为后仍保留实体输出，QueryPath与文件输出独立。设置表单只编辑草稿，参数沿 `CopyFile.MultiPagePolicy` 保存在原 Commands 差分，最终进入唯一 ApplyOptions 保存/回滚；未知配置及弃用参数字段继续保留。

**Paste 是加载剪贴板内容。** QueryPath 优先于系统文件地址，单图片、目录或归档进入原 `OpenCoreAsync`，打开失败保留可用书籍。多个项目需要原临时播放列表来源，本批明确拒绝且不打开任何项；不静默选择第一或最后项。文本、位图、FileContents 和其他原接收类型继续待迁，不把路径文本冒充文件对象。

用户已决定 CutFile/CutBook 暂保留禁用占位，移动继续使用分类/移至文件夹；不生成 Windows 私有剪切标记，不把剪切变成复制。235 个原命令和原菜单节点保持，新增三个实际执行入口后为 162 接入、73 占位；计数不代表完整功能覆盖率。

## 错误与测试

缺失、权限、链接、无效 URL/私有 JSON、数量超限、读取期间剪贴板改变及系统拒绝写入分别报告；失败/取消不改源图、索引或移动历史。原生写入在清空之后若被系统拒绝，不能保证还原原剪贴板，明确报告实际失败。

`FileClipboardTests` 使用自建 PNG/ZIP/Profile 与 fake IFileClipboard，覆盖两方向三策略、分割页/瀑布选择、根书籍、文本数值、缺失/链接、失败/取消/晚取消、切书、导航锁等待/关闭、关闭失败重试、单来源/QueryPath 优先、多项拒绝、过期读取、URI/JSON 限额、正式菜单/输入/参数/保存事务。真实系统剪贴板不读写；AppKit API 通过正式 macOS 构建核验，Finder 互操作及真实剪贴板仍随 P3/P4 集中设备验收。

第六批已沿原归档链迁入文件实体化复制；后续继续临时多文件播放列表和其他 Paste 类型；本批不证明 P4 全部完成或设备封板。触控板跳过、多屏无环境，签名公证/发布仍为 P5。

当前目录页复制范围由[P4第十批](p4-directory-copy.md)补齐：普通目录直传/整树固定复制及内部目录四策略已接入；内部目录提取经基线核验为原版TODO，保留跳过并明确提示，不计作迁移丢失。逻辑书按策略复制、链接和其他范围继续按P4清单推进。

P4第十一批[逻辑书复制](p4-logical-book-copy.md)以Book.Path对应条目接入CopyBook原四策略，QueryPath保持逻辑书；显式图片定位、空搜索和列表别名不把当前页面变成书籍目标，根.nvpls仍复制列表文件。
