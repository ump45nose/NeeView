# P2 第十四批静默验收

2026-10-04，feature/macos-port。正式窗口未启动或激活；没有真实键鼠输入，未修改 Application Support 用户文件。

- 原算法/资源/正式XAML回归：224通过、0失败、0跳过；新增11项目录参数/事务用例。
- Engine、正式Library源码、正式ARM64.app构建和本地ad-hoc严格签名通过。原始输出及TRX计数见[p2-folder-parameters-validation.json](p2-folder-parameters-validation.json)，没有替换默认构建目录。
- 四文件准备失败保留旧文件与选择/种子，移除障碍后可重试；四文件中断恢复/旧标记兼容、原特殊文件名、未知字段、关闭保存副本、路径大小写及损坏文件只读验证通过。
- 独立只读复核后修正虚拟书签父级递归和空缩略字段回滚快照，最终版本全量复测。
- 正式XAML Headless图采用独立p2-folder-parameters前缀；已离线查看reading-layout，原顶栏/左右/正文/底部区域保持。

待真机：菜单排序选择/勾选、连续切换目录后的当前参数、保存失败反馈和正常退出重启。Headless不代替触控板、IME、NAS、Windows动态对照或用户验收。性能P95/长期native内存未测，无推送、远端CI、公证或发布。
