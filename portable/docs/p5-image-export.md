# P5 第二十七批：原单页与整书图像导出

## 职责与原出处

固定基线 c5c398d89 的 ExportImage/ExportImageAs/ExportBookAs、ExportImageParameter/ExportBookParameter、ExportFileNameFormat/FileNamePolicy、ExportBook 和 Original/ViewImageExporter。具体出处及原文件 SHA256 登记到 source-migration.json。

Engine 沿唯一 BookOperation/Archive/PageFrame 管理来源、原命名、覆盖与写入；Mac 的唯一 ReaderView 通过 IViewImageExporter 复用 DrawFrame。独立 ExportImageViewModel 编辑原参数副本，ExportImageDialog 保留原 800×650、左350参数区、右预览与底部按钮布局。三个生产项目及原 JSON 权威保持。

## 契约与业务规则

- ExportImage 使用原命令参数及 Ctrl+Shift+S；ExportImageAs/ExportBookAs 使用 Config.Book 对应参数及克隆表单。原 ExportImageCommandParameter 的格式默认是 JPEG，Config 单页参数默认 PNG，不能合并这两个默认值。
- Original 单页取原帧 Elements.First 的完整来源字节，保持原扩展名的小写规则；不按显示方向反转，也不跳过 dummy/目录选择另一个元素。目录不能作为单页原图导出。整书 Original 按当前书籍序列读取普通文件条目，跳过目录并回报名称。
- View 输出当前原帧，不截取窗口、侧栏、错误提示或过渡动画。分割、双页、页间隔、翻转及旋转沿已有绘制几何。IsOriginalSize 属于 View：恢复 raw 页框尺寸、取消外部变换，仍保留页内分割。原尺寸请求完整像素；仍受解码安全预算约束，不能安全输出时明确失败。
- 同一帧刷新可能按最新视口重建比例，输出尺寸与绘制 targets 使用刷新后同一份帧。原尺寸按原 LayoutRounding/SnapsToDevicePixels 意图归入输出像素，避免旧比例与新几何混用导致偏移。PNG 保留透明 alpha；可包含画布背景；JPEG 使用质量参数。最近邻/原尺寸高质量/其余沿当前像素策略保持原选择顺序。
- View 画布单轴最多32768，向上取整后工作像素最多128MiB。Original 不为复制字节分配整张图像。
- 整书 View 使用原帧前进，包括双页、分割及循环边界。按原 ExportBook，帧包含最后 Page 即终止；不是按数组每项分别绘图。临时页变化不登记阅读历史，结束后恢复原 PagePosition/Part。原版变换恢复尚有 TODO，本批不宣称全面复原跨页变换。
- 单图 View 按实际目标扩展名选择 PNG（.png）或 JPEG（其余）；整书按所选格式。ZIP/Folder 使用原整书顺序命名默认 {Index:000}。
- 原字段 Book/Part/Page/EntryPath/Name/Index 与 1/2/L/R 继续调用原 Formatter；单双页模板及错误回退分开。模板可含相对子目录，拒绝绝对路径、空段、`.`、`..` 与NUL；Mac合法反斜杠保持。直接命令模板与 Folder 子路径检查中间链接，最终文件链接拒绝覆盖；用户明确选择的根目录保留系统路径语义。
- 单页支持确认中的覆盖/加编号/取消及预设加编号/禁止覆盖。整书内部仅支持 AddNumber/Disallow，原版不支持逐页 Confirm。已有外层 ZIP 或非空 Folder 的确认与内部文件策略独立，沿原 ExportBookDialogViewModel。

## 状态与资源生命周期

宿主和 Engine 各有一个实际导出槽。Engine 捕获书籍/代次，在原导航锁中读取与绘图，切书或关闭取消准备；不可即时取消的编码完成后拒绝提交，释放输出及临时文件。来源保持到真实任务完成，关闭等待任务再释放。显示租约、像素缓存与离屏位图各自明确所有权；每帧位图编码结束后释放。

单文件/ZIP 同目录 CreateNew 随机临时输出，完整关闭、刷新及取消复核后 File.Move 提交。读取、编码、冲突或提交失败保留已有目标。加编号在最终无覆盖提交时仍拒绝外部竞争写入。Folder 逐文件提交，中断保留已完成项，ExportPartialException 报告真实完成数和取消/失败；不伪称整批回滚。

对话框打开即预览；草稿变化在单槽中合并，旧版本结果及错误不发布，关闭取消并等待晚到编码。预览只传独立 PNG 字节，不拥有来源或写文件。成功导出后沿唯一 SaveAll 保存原 Config.Book 参数；保存失败原地回滚参数，并明确报告“文件已导出、参数保存失败”。

## 兼容与明确修正

原单图流式 OriginalImageExporter 只打开条目而未写目标流，本批实际 CopyToAsync。原整书 Original 吞条目异常可能产生成功但缺页的包，本批传播失败并保留旧 ZIP。这两项属于明确修正，不作为原版无差异声明。

旧 ExportImageAs 参数继续通过既有版本升级迁到 Config.Book；当前 JSON 差分、未知字段、直接命令 JPEG 默认及输出目录路径映射保持。四类效果层执行/编辑由第二十八批接入，View使用同一像素路径；未迁层、坏参数及效果工作预算失败明确拒绝输出；连续/瀑布需先返回分页或原帧全景。目录、普通 ZIP、PDF 等沿已有来源读取；本批不新增内容来源或解码后端。

## 错误、测试与扩展

覆盖拒绝、无效模板、链接目标、坏条目、解码失败、超出画布预算及取消分别回报，不静默跳过普通文件。Folder 报告部分成功；目录跳过单独列出。

专项覆盖原字节/目录与ZIP、原参数/命名/JSON、真实 Skia 分割/旋转/透明背景/双页JPEG、整书循环终止、坏条目保留旧包、文件夹部分提交、模板链接、迟到编码切书/关闭等待、最新预览和原表单截图。按用户指定资源取一张实图，只读验证原字节与完整View；图片与输出不提交仓库，只记录匿名尺寸/哈希。

构建、全量回归、实际 macOS 后端、Headless视觉与用户/Windows动态验收分别见[本批验收](../acceptance/p5-image-export-runtime.md)。正式保存选择器、真实外部阅读器及 Windows 导出动态另验。未来效果执行沿同一 DrawFrame 接入；不新增平行渲染或导出内核。
