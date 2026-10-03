# P2 第二十五批：资源测量与开发收尾

日期：2026-10-04。沿唯一BitmapFactory、ReaderView和原JSON/BookOperation链处理测量热点，不新增工厂、调度器或持久化模型。P2开发范围完成，整体验收未封板。

## 资源改造及所有权

- BitmapFactory.Trim先计算主图/缩略总字节；两者均在预算内时直接返回，避免热租约反复分配并排序全缓存。超预算仍沿原LRU、等待者和显示租约保护规则回收，没有增加另一套缓存计数状态。
- ReaderView对同一真实Page图片、DecodeRequest、Length及LastWriteTime复用已有显示缓冲。新Page/新来源/规格或版本改变继续请求。Folder/Archive封面不走该快捷路径，保留原封面选择与刷新语义。
- 显示Bitmap先释放，再归还租约；原动画退出帧继续使用引用计数租约，最多一个快照，完成/替换/切书/关闭后释放。显示资源和像素仍统一计入预算。

## 固定夹具与测量边界

8张应用生成的纯色JPEG，3840×2160；目录与ZIP，640×480 Headless，真实Magick原生解码。先warm-up，再每来源10次打开、28次预取分页、30次同帧刷新，以及30轮目录/ZIP切书。租约微测量使用256/1024缓存条目和1000次热请求。

完整帧计时从操作开始，到DisplayCompleted回报后CaptureRenderedFrame的软件输出完成；下一页后台预取不计入当前帧终点。不是仅记录加载事件，也不是macOS屏幕/GPU呈现。图像易压缩、样本少，不能外推复杂图片、Retina、慢NAS、固实解压、长期native或60Hz交互性能。

## 优化前后证据

原始输出：[优化前缓存](../acceptance/p2-resources-before-cache.json)、[优化后缓存](../acceptance/p2-resources-cache.json)、[优化前显示](../acceptance/p2-resources-before-render.json)、[优化后显示](../acceptance/p2-resources-render.json)。

| 测量 | 优化前 | 优化后 |
|---|---:|---:|
| 256条目/1000热租约托管分配 | 73,736,040 bytes | 1,336,040 bytes |
| 1024条目/1000热租约托管分配 | 288,776,040 bytes | 1,336,040 bytes |
| 同帧刷新30次新建显示缓冲 | 30 | 0 |
| 同帧刷新30次新增解码 | 0 | 0 |

独立优化后测量的软件完整帧P95：目录39.88ms、ZIP76.38ms、预取分页3.03ms、缓存帧2.25ms。最终全量回归复测：目录40.19ms、ZIP51.72ms、预取分页2.80ms、缓存帧2.53ms；见[最终原始输出](../acceptance/p2-completion-render.json)。时间受同进程测试顺序及机器负载影响，不将单次前后时间差宣称为稳定性能收益。

测试主图预算8MiB，切书10/20/30轮缓存均为8,294,400 bytes，未超预算。关闭Reader后仍有预算内复用像素（7,776,000 bytes），工厂Dispose后归零。独立专项RSS约258.1/259.4/259.7MB；最终全量测试进程约531.1/536.2/533.6MB，包含其他测试已分配的资源，不能与应用RSS等同，也不能从这三点判断长期泄漏通过。产品默认512MiB主图/64MiB缩略预算不是进程RSS上限。

## 原命令与参数收尾

RemoveUnlinkedHistory接入既有CleanupHistoryAsync/可靠存在检测，异步清理结果仍由原SaveData事务回报，不加新服务。TogglePageMode与TogglePageModeReverse保留原+1/-1，迁入原IsLoop默认true；关闭循环时首末模式停止，正反命令共享原Commands.TogglePageMode.Parameter。参数类型/默认值出处登记在source-migration.json，表现端编辑独立草稿；差分键位和恢复专项验证。

原235条命令运行导出为138个入口接入、97个占位，已接入但仍标“待迁”的53条元数据已纠正；[完整表](command-migration.md)与[运行导出](../acceptance/p2-completion-commands.json)逐字段核对。入口数量不表示功能覆盖率；ToggleVisiblePageSlider/ToggleVisibleAddressBar等未迁入口保留占位，不将现有近似命令或设置可用当作原入口已实现。

## 验证与后续

最终355项自动测试零失败/跳过、Engine/正式Library/ARM64应用构建及本地ad-hoc严格签名通过。离线查看正式主窗口、输入设置、浮动导航器和固定样本截图；本轮没有启动/激活正式应用、注入系统输入或访问用户Application Support。完整证据见[收尾记录](../acceptance/p2-completion-runtime.md)。

真实鼠标/触控板/IME/Retina/多屏、Finder/NAS、长期原生内存、屏幕P95、Windows动态及用户新增交互仍待集中验收。P3/P4/P5按原范围保留，未将它们或高级占位算作本次完成；没有推送、公证、发布。
