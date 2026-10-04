# P3 第四批：渐进普通目录索引

当前状态：P3开发范围已完成，见[P3收尾清单](p3-completion-checklist.md)；本文件记录该批契约和当时测量，后续接入点以下述链接为准，静默验证不等同真机/Windows封板。

沿原 Archive/BookSourceFactory/Book/Page/BookOperation 增加批次入口。普通非递归目录在枚举完成前发布首批，已知图片先单项产出；后续最多128项/批、最多两批排队。目录枚举仍由有界 SourceIo 后台执行，未新增读取服务、身份或状态库。

## 契约、排序与位置

Archive.EnumerateEntryBatchesAsync 默认返回完整归档索引；FolderArchive 提供真正流式枚举。原 Image/ImageAndBook/All 在直接目录使用同一过滤。递归展平有内容目录和归档的全局过滤依赖完整结果，继续完整提交，不能称为递归渐进索引。

初批沿原 generation/导航锁/历史提交替换旧书，提交后 IsLoading=false、Book.IsIndexing=true，阅读和排序可用。显式条目到达前保留旧书；反向末页和历史末页重置先等完整索引，避免误解释末端。后续持有同一本Book/Page，锁内捕获原页面及排序参数、后台计算原BookPageSort、锁内核对代次后提交。保留原Page身份及Part；随机键按原收集顺序和固定种子生成。

未完成索引不能将已收集末项当作真实页尾跨书。每批提高PageOrderVersion；查看器/页面列表继续核对当前Page/书籍/顺序。保存仍用Path/Page/Props，不持久化新索引标识。

## 资源与错误

打开调用观察整个枚举，切书/卸载/关闭取消。首批前失败保留旧书；首批后失败保留可阅读页面，Book.IndexError和现有错误提示说明索引未完成。取消后部分书籍可重新载入补齐。晚到批次禁止发布。Folder来源不持有长期文件流；同步NAS调用无法立即中断时，SourceIo槽保持到真正退出，后台任务继续观察清理，不能通过超时释放槽制造无界并发。

## 验证与限制

ProgressiveIndexTests覆盖确定性阻塞的后续批次、首批可导航、原末端保护、插入前页后的身份、排序改变、显式晚到目标、后续失败、新打开取消，以及真实305文件目录的首图/128项批量/唯一ID。完整418项回归、正式Library/ARM64.app构建及严格ad-hoc签名通过，原始输出见p3-index-validation.json；既有三目录实图和缩略用例继续静默执行。未启动前台应用。

这里证明流式提交及生命周期，不代表NAS首图P95、屏幕帧率、长期RSS、Retina或Windows动态对照。原帧全景及普通导航收尾随后已交付，见p3-panorama.md、p3-quickaccess.md、p3-page-search.md。
