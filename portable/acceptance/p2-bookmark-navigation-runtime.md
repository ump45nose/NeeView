# P2 第十批书签导航：后台验收记录

2026-10-04。本批按用户要求只做后台构建、自动测试、正式 XAML Headless 渲染与离线审查。不启动/激活正式 NeeView，不操作真实窗口，所有测试状态和图片均来自自动生成的临时夹具；用户原 JSON 和来源不作为测试数据。

## 范围

本批补齐原书签列表的目录进入、逐级返回/定位、根、当前书同步及无磁盘探测排序，保留已有书签编辑树。原搜索语法、书架 bookmark scheme 互联、显示模板和来源元数据排序继续占位，详见[模块契约](../docs/p2-bookmark-navigation.md)。

## 自动验证

原始构建、测试与签名输出见[p2-bookmark-navigation-validation.json](p2-bookmark-navigation-validation.json)。最终全量 **145通过、0失败、0跳过**，本批新增12项回归。Engine、正式入口 Library、正式 `.app` 构建及本地 ad-hoc 严格签名均通过；默认目录串行构建。

后台专项先发现并修复新列表不可直接聚焦，以及底部拖动提示换行导致下沿树的位置变化、释放命中另一行的问题。列表明确拥有焦点；可选树固定在顶部，既有实际 Headless 拖动/取消/失败回归继续覆盖。

原注册顺序独立复核疑点已顺源码追查：BookmarkFolderCollection.Sort 先按 ConstOrder 后按原索引，但 FolderItemType.Directory/File 的 ConstOrder 同为2，因此普通书签仍保持原注册顺序，不按文件夹/书籍分组。排序测试包含这一组合，不删除复杂原判断。

## 离线界面审查

已审查[书签导航布局](p2-bookmark-navigation-bookmark-navigation-layout.png)：地址、返回、同步及排序位于可选树上方，树与列表共用原节点；默认树隐藏。另留存[整体窗口](p2-bookmark-navigation-window-layout.png)与[阅读布局](p2-bookmark-navigation-reading-layout.png)。这些是 Headless 离线图，不代表真机交互通过。源码清单为45文件、82项子集适配，数量不代表功能覆盖率。

## 正式运行与待验

**本批未执行正式 Mac 窗口运行验收。** Headless 属于正式 XAML 的后台验证，不能作为真实焦点、鼠标/触控板、IME、Retina、多屏和弹出菜单通过的证据。

待约定前台时段集中检查：窄侧栏列表与可选树的可操作性、真实单/双击及多选打开、Enter/Backspace/Delete、目录返回定位、同书别名同步、列表/树编辑联动、拖动提示换行、排序/显示保存重启和关闭恢复。继续保留 Windows 动态对照、NAS、长期原生内存和性能P95待验。

本增量按后台验证结果本地提交；P2未整体完成，未推送、公证或发布。
