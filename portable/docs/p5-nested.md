# P5 第十二批：原嵌套归档关系

## 职责与出处

固定基线 `c5c398d89` 的 Archive.Source/Parent、ArchiveManager.CreateArchiveAsync(source)、ArchiveEntryUtility、ArchiveEntryCollection、BookAddress 和 BookMementoControl 是行为依据。沿现有 Archive/ArchiveEntry/BookSourceFactory 迁入嵌套 ZIP/CBZ、RAR/CBR 和 7z，不增加来源身份、阅读内核或持久状态。

## 依赖与契约

- Engine 的 Archive 保留实际 Source、Parent、NestingDepth 和最外层 RootArchivePath；逻辑目录包装不增加嵌套层。CreateBookEntry 返回原源条目，分类/复制仍识别真实所属来源。
- IArchiveFactory.OpenAsync(ArchiveEntry,token) 使用实际条目 ID，区分重复名称；返回调用方拥有的子来源，借用的父来源不随其关闭。
- 路径入口逐层解析内部目录/压缩文件/图片，拥有所打开的父链；内部缺失返回不存在，损坏、权限和能力错误不能转换成不存在。
- Backends 将实际条目流复制到应用随机命名的临时代理。CompressedArchive 的逻辑路径用于定位，物理路径仅用于读取和固实 Reader 重开。UI 与像素契约不变。

## 状态与资源生命周期

原 Book 的来源集合反向关闭递归子来源，封面请求按请求关闭子来源；路径入口的子来源在释放时关闭完整拥有父链。先完成来源读取，再排队临时复制，避免有界 I/O 槽内等待另一来源任务。

取消等待不能关闭实际工作仍使用的输入流；排队失败或真实工作结束后释放输入。晚到输出关闭来源及代理。所有活动嵌套代理共享 2 GiB 预算，最多16层；该额度与原固实缓存/独立请求流额度分开，不能称为进程磁盘总预算。流式复制验证长度，失败清理；删除失败保持额度并允许释放重试。应用异常退出后的临时清理仍按后续分发/资源工作处理。

## 业务规则与错误

1. 原三种收集模式保持：CurrentDirectory/IncludeSubDirectories 将压缩文件作为子书；IncludeSubArchives 展平真实子归档，内部目录不重复递归。
2. 显式内部图片定位、封面、排序与恢复沿原条目名和 Page，不新增锚点格式。按条目打开不以同名路径重新猜测 ID。
3. IncludeSubDirectories 的子书父级返回父归档；IncludeSubArchives 按原规则返回最外层归档所在目录。
4. IsInnerArchiveHistoryEnabled 基于真实 Parent 判断，普通包内逻辑目录不能误当嵌套压缩书籍。
5. 嵌套包只读，即使根 ZIP 写权限开启也不能改临时代理或父包。包内 `..` 定位在路径规范化前拒绝，避免误打开归档外文件。
6. 损坏、缺失、取消、超时、深度和预算超限保留真实错误；打开失败保持旧书。密码与分卷仍按已有明确能力提示，不宣称已迁入。

## 测试与扩展点

专项包含原三模式、普通/固实 RAR/RAR5、普通/固实7z、重复名称ID、两层显式图片/历史定位、父书/关闭恢复、内部历史开关、损坏/缺失、只读能力、深度/预算/失败复制、取消晚到、正式Headless查看器及关闭释放。验收见[本批运行记录](../acceptance/p5-nested-runtime.md)。

后续 PDF/媒体沿原来源和解码边界接入。格式后端不承担菜单、界面布局或 JSON 保存；用户真实导出、Windows动态、设备性能和正式分发仍独立验收。
