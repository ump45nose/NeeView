# P2 第十六批静默验收

2026-10-04，feature/macos-port。未启动或激活正式应用，未注入真实键鼠，未修改用户 Application Support 数据。

- 全量266通过、0失败、0跳过；相对前批新增25项原输入方案、组合鼠标、滚轮、正式事件路由及设置失败恢复用例。
- 原A/B/C、方向交换、共享参数与差分JSON保留；普通滚轮由绑定解析，精确触控板继续使用原生桥接。
- 独立复核后补齐非左键捕获、移出控件取消释放点击和捕获转移清理，并以正式Headless事件验证。原Avalonia额外按钮变化从PointerMoved处理。
- 设置应用等候原保存事务同步屏障，再在导航锁内拍快照；写入失败原地回滚已知配置/命令/书籍设置，允许同草稿重试。外观和输入保存不重建正文。
- Engine、正式Library、正式ARM64.app构建与本地ad-hoc严格签名五步结果见[p2-mouse-input-validation.json](p2-mouse-input-validation.json)，使用默认目录串行。
- 已离线审查[输入设置](p2-mouse-input-input-settings-layout.png)。独立前缀保留旧验收图，原Windows和用户.DS_Store保持。

真实扩展鼠标、双击时序、触控板、IME/焦点/弹出菜单、Windows动态对照、NAS和长期原生内存另验。Headless不替代这些结果。P2继续迁移，无推送、公证或发布。
