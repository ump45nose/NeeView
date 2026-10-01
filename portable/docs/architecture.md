# NeeView macOS 迁移架构

状态：实施基线；首版目标为 macOS 15+ / Apple Silicon。Windows 46.3 保留为行为对照，跨平台 solution 不引用其主工程。许可沿用仓库 MIT，复用规则注明原出处。

## 固定基线

- 共同基线：`686a43362dc4b3c9f2ea014240dbba2d0e9fbcaa`。
- 分类 A：`801eab4842b9dbfc18eae7c96006f64eb7b80c30`。
- 分类 B：`84449934c86a2e7faba9a7c7a7d4ff9dfbea2229`。
- 真实合并：`c5c398d89`，两个父提交保留上述历史，冲突采用已核验的 A 实现与较新说明。
- 基线分支 `integration/neeview-baseline`；实施分支 `feature/macos-port`。

## 依赖与装配

```mermaid
flowchart TB
  Host[MacOS / Preview 启动层] --> Desktop
  Host --> Providers[Content / Imaging / Persistence / Platform.MacOS]
  Desktop --> Application
  Providers --> Application
  Application --> Core
```

Core 只含内容身份、排序、阅读规则、锚点和布局。Application 声明内容、图像、存储、文件、平台接口并协调状态。Desktop 为 Avalonia 自定义查看器与面板。具体实现由 Host 注册。Preview 是共享界面的开发验证入口；正式 MacOS Host 和 Platform.MacOS 使用 net10.0-macos / AppKit，须完整 Xcode。

运行时采用 C# / .NET 10、Avalonia 12.1.3、CommunityToolkit.Mvvm、Magick.NET Q8 14.17.2、SharpCompress 0.50.3、SQLite 和 JSON。依赖集中锁定，提交 NuGet lock 文件。

## 所有权与一致性

进程拥有身份登记、图像缓存、调度器、数据库、文件操作历史。窗口拥有 ReaderSession；来源、流与像素租约显式释放。会话消息串行执行，切书增加代次；打开失败不替换现有来源，旧解码结果只清理资源。UI 在 Dispatcher 线程接收快照，不能直接扫描、解压或修改文件。

ContentId 与路径分离；路径登记持久化。移动/重命名保留身份，内容版本使缓存失效。外部移动不猜测匹配。锚点包含内容、分割区域、页内比例；排序和尺寸晚到后按身份恢复。

## 显示和操作流程

打开请求 → 会话 → 来源索引 → 布局 → 可见需求 → 调度 → 解码 → 像素租约 → 绘制。分页、连续和瀑布流使用同一索引和缓存。只为可见和邻近页面解码，不创建全量图片控件。

文件目标 → 能力校验 → 串行操作 → 恢复记录 → 实际文件结果 → 身份/索引/缓存更新 → 会话响应 → 成功后修改移动历史。归档条目只读；瀑布流分类必须显式选中。

## 预算、错误与扩展

像素 512 MiB、缩略图 64 MiB、压缩数据 32 MiB、临时解压 2 GiB、磁盘缩略图 512 MiB；解码并发 2，后台最多 1。租约中的资源不能回收。归档内部路径不作为解压路径。NAS 按已挂载目录处理，有界后台读取。

错误分类：不存在、权限、暂不可访问、损坏、不支持、密码、超时、取消。正常关闭保存并等待操作；异常恢复记录保留到下次启动。后续 PDF、动图、视频、嵌套、脚本和效果通过既有接口扩展，不执行旧脚本。

验收见 `../acceptance/stages.md`；公开契约变更同步模块文档、调用方和测试。性能目标是待测目标，编译或单元测试不等于真机验收。
