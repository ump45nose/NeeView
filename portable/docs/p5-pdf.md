# P5 第十三批：原 PDF 阅读链迁移

## 职责与出处

保留原 PDF 归档、零起始页 ID、`001.png` 条目名、原页面目录和三种尺寸规则。固定基线 `c5c398d89` 的 `PdfArchive/PdfPdfiumArchive/PdfArchiveProfile`、`PdfPageContent/PdfPictureSource`、`PdfArchiveConfig/PerformanceConfig`、`SizeExtensions/JsonSizeConverter` 为行为来源。Windows PDFium 替换为官方 .NET CoreGraphics/PDFKit 绑定；不增加 Ghostscript、PDF 图片 delegate 或第二阅读内核。

## 依赖与契约

Engine 的 `PdfArchive` 仍是原 `Archive` 子类，配置及尺寸算法为纯 .NET。Backends 的 `PdfArchiveSource` 以 `IPdfRenderer.Inspect/Render` 接入系统；`Inspect` 返回页尺寸、书签树和日期，`Render` 返回可释放 BGRA8 像素。正式 MacApp 是具体渲染器的唯一装配点。

页面目录沿原 `ContentsArchiveEntryNode` 关联已有条目，再映射当前书的全源 `Page`；排序、过滤和点击目录不创建第二页面集合。根 PDF、包内 PDF、显式内部页、封面、历史存在检查和恢复共用唯一来源工厂。

## 状态与资源生命周期

文档、页面、PDFKit 目录及 CoreGraphics 绘图上下文均为请求级，用完释放。来源互斥串行协调实际渲染与关闭；嵌套代理只在真实工作完成后释放。异步取消可以先结束等待，晚到像素由既有 SourceIo 清理，不能更新新书画面。

`PdfPageStream` 的尺寸探测不产生像素；正文/缩略直接按需渲染，不先生成 PNG 再交 Magick 解码。仅复制、提取等实际读流时惰性生成默认 PNG。惰性初始化、流位置、读取和关闭共用一把请求级锁，多个消费者只生成一次 PNG；关闭等待正在读取/导出的操作结束。像素租约在编码后立即释放。

## 业务规则

1. 默认 PDF 渲染规格 1920×1080，每轴最低256；源尺寸最大规格默认4096×4096，每轴最低1024，限制默认关闭。
2. 显示源尺寸默认保持，仅开启原限制时缩小。默认导出保留固定 PDFium 判断顺序：小页放大至规格，大页保持。正文按需渲染保留原最小规格及每轴源尺寸/MaximumSize上限关系；缩略按请求规格。
3. CropBox、页面旋转、白色纸张及 sRGB 由后端处理，输出预乘 BGRA8；用户变换和单双页继续使用原页框/查看器。
4. 实际输出缓冲上限256MiB，PDF页面/目录最多十万项，目录最多64层。输出预算不代表 PDF 内部全部原生工作内存或进程 RSS 上限。
5. PDF 页是只读逻辑条目，Length=0，PNG实体化不以逻辑长度核对，但仍执行实际字节预算。
6. 原尺寸 JSON 写为 `"Width,Height"`，读取兼容早期 Mac 对象值。PDF/Performance未知字段保留，唯一差分保存、五文件事务及失败回滚保持。

## 界面边界

原设置导航增加“归档 / PDF”页，XAML结构、独立草稿和业务配置分开。取消不提交，保存失败保留草稿并恢复原配置，成功写入后供后续探测/渲染使用；不因纯表单保存重扫已打开页面。该页进入既有设置搜索，搜索结果复用同一控件及 DataContext。

## 错误与未迁能力

损坏、页不存在、尺寸超限、取消和密码需求给出明确错误；打开失败保留当前书。密码交互与分卷尚未接入执行。`SupportFileTypes` 已沿原集合/字符串JSON接入统一工厂、嵌套、书架与历史存在检查；空集合关闭识别，压缩/列表按原顺序优先。PDF内部生成PNG仍明确是图片，不被用户配置的图片后缀重新当作子归档。没有将其列为完整 PDF 能力通过。

## 测试与扩展

相关专项覆盖原尺寸/默认/JSON、目录Page身份、根/嵌套定位、缓存/实际提取、失败保留、晚到释放、并发导出与关闭以及正式设置/查看器Headless。独立 macOS 后端测试使用官方原生 `.app` 引导、无窗口执行，实际验证 CropBox/旋转/日期/书签、像素方向/通道/纸色、PNG/提取、加密失败、超限、取消及40轮资源释放。

完整构建及数量见[验收记录](../acceptance/p5-pdf-runtime.md)。Headless截图、真实原生渲染和正式应用构建分别留证；Windows动态、设备长期内存、用户真实PDF和正式分发仍独立验收。后续密码交互在已有原归档关系下扩展。

## 第十四批：原扩展名集合与必要边界修复

`SupportFileTypes` 默认 `.pdf`，沿原 FileTypeCollection/StringCollectionParser 的分号、引号、排序、去重、克隆和字符串 JSON；显式空/null不补默认。扩展名输入是独立草稿，解析失败沿既有设置错误和回滚，成功保存后进入唯一归档工厂。

记录两项必要改造：原 StringCollection 的构造/Restore/批量操作没有调用 ValidateItem，本轮统一规范点号、大小写与实际文件系统非法字符，避免输入 `PDF;DOCUMENT` 后查询不能匹配；普通字符串集合不改变大小写。第二项为迁移新增边界：PDF内部虚拟PNG的子书资格由真实ArchiveEntry判定，路径解析与条目入口不能仅凭扩展名再次打开它。压缩/列表保留原优先顺序。

原差分写出按实际JSON值区分对象和字符串converter类型，不再仅按CLR类递归。默认、空集合、未知字段与唯一五文件事务保持。验收见[第十四批记录](../acceptance/p5-pdf-filetypes-runtime.md)。
