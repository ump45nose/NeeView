# P2 第十五批静默验收

2026-10-04，feature/macos-port。正式窗口未启动或激活，未注入真实键鼠，未修改用户 Application Support 数据。

- 全量241通过、0失败、0跳过；相对前批新增17项真实书籍页、父子导航、递归、封面与正式视图用例。
- 首次全量暴露缺失归档页的错误提示回归，统一提示后完整复测通过。旧书、位置及来源释放断言保持。
- Engine、正式Library源码、正式ARM64.app构建及本地ad-hoc严格签名五步exit 0，见[p2-book-hierarchy-validation.json](p2-book-hierarchy-validation.json)。使用默认目录串行构建。
- 已离线查看[目录卡片](p2-book-hierarchy-book-cards-layout.png)与[归档横图封面](p2-book-hierarchy-archive-cover-layout.png)，原上下区域、等比封面和窗口布局保持；颜色由主题管理。
- 清单47文件/110子集适配/26上游文件，仅表示出处登记，不表示功能覆盖率。原Windows源码、旧阶段截图和用户.DS_Store保持。

真机双击、菜单、NAS、Windows动态/像素对照、长期原生内存及显示完成P95待独立验收。Headless不替代这些结论。P2继续开发，无推送、远端CI、公证或发布。
