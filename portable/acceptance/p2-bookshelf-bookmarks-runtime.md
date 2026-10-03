# P2 第十九批静默运行记录

本批未启动或激活正式应用，未注入系统键鼠，也未访问用户 Application Support。正式视图由 Avalonia Headless 与临时图片/CBZ/JSON 运行。

全量307通过、0失败、0跳过；Engine、正式Library、ARM64应用及本地ad-hoc严格签名五步通过，见p2-bookshelf-bookmarks-validation.json。修正旧搜索测试的全局排序假设，补充每目录参数返回及元数据补齐不取消搜索回归。

离线截图发现布局前打开留下零视口原点，修复首次有效布局重新定位，专项断言整图边界并重新审查bookshelf-bookmarks-layout图；正文、书签树及两个独立列表无裁切。原节点/参数共享、位置独立、别名选择、保存失败重试和启动恢复分别自动验证。

Headless与构建不替代Windows动态、真机焦点/Retina/触控板、NAS、长期内存和完整帧P95。P2继续历史策略/模板、浮动宿主及剩余动画，不推送或发布。
