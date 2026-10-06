# P5 第十五批：原归档口令与 PDF 解锁

## 职责与出处

迁入固定基线 c5c398d89 的 ArchiveKey、ArchiveKeyCache 及 Controls/PasswordDialog。保留 None/Completed/Canceled、逻辑路径查找、错误候选重试、非空确认及取消；WPF Dispatcher/Window 替换为可等待输入契约。首批执行范围是根 PDF 和明确打开的嵌套 PDF，ZIP/RAR/7z 密码尚未接入。

## 依赖与契约

IArchiveFactory 增加带 ArchiveKeyRequest 回调的 OpenAsync 重载；仅 BookOperation 明确阅读打开传入回调。ArchiveKeyRequest 只有逻辑来源和重试状态，Engine 不引用窗口。后台封面、索引及历史存在检查沿无交互入口，可以复用已验证的进程缓存，未解锁时明确失败，不弹窗口。

MacPdfRenderer.WithPassword 产生来源私有渲染器，CoreGraphics 与 PDFKit 都验证解锁，共享后端不修改其他书的口令。PdfArchiveSource 以真实逻辑路径回报密码需求；应用随机代理路径不能作为缓存键。

## 状态与资源生命周期

本次打开持有 ArchiveKey 候选。失败尝试完整释放来源、父链及嵌套代理后，在 SourceIo 槽之外等待输入，再重走同一打开链。实际 Inspect 解锁、索引成功且取消检查通过后才进入原进程缓存；此成功点不是 Book 历史事务或画面显示完成。错误输入、取消与迟到文本不提交候选。

原 AES 缓存只在进程内，不写 JSON、文件、诊断或钥匙串。删除原口令调试输出；增加并发保护、加密对象释放和临时字节擦除。正常退出清理密文，关窗口后进程仍驻留时允许继续复用。不可变托管字符串按运行时生命周期回收，不承诺进程内不存在明文。

## 业务规则与错误

无密码或错误密码进入同一拥有窗口；错误后显示重试提示。保持输入空白，不 Trim；空输入不能确认。取消保留当前可用书籍。切书或关闭取消拥有弹窗，迟到文本不能用于新书。损坏、超限和其他错误保持原类别，不伪装成密码错误。

自动递归遇未解锁子 PDF 不启动输入；保留原子书候选，明确按逻辑路径打开后才能输入。ArchiveEntry 无交互入口可使用进程缓存，不声称递归自动收集已经提供完整密码交互。

## 界面边界

PasswordDialog.axaml 只负责文字、遮罩、布局及按钮反馈；MainWindow.ArchiveKeys 将后台输入送往 UI 线程。主窗口布局、ReaderView、页框、排序与唯一 JSON 不变，样式可以独立调整。关闭清空输入文本。

## 测试与扩展

隔离合成测试覆盖状态、精确路径、缓存覆盖与清理、错误到正确、嵌套代理释放、取消保持旧书、JSON 不包含口令、正式弹窗 Enter/Escape、主窗口切书及关闭。真实 macOS 测试验证实际加密 PDF 的索引/目录/像素/PNG、重试/缓存、ZIP 内逻辑定位和取消。

验收见[静默记录](../acceptance/p5-pdf-password-runtime.md)。后续压缩密码继续接在原归档来源与本次可等待契约，不引入第二密码或阅读体系。Windows 动态、用户真实 PDF、设备长期内存及 Developer ID 分发仍独立待验。
