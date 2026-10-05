# P4 收尾：原删除、图片接收与 Mac 链接静默验收

P4开发范围已完成。沿固定基线的BookPageActionControl/PageFileIO、Archive删除、ContentDropReceiver及fork分类链迁移；仍只有Engine/Backends/MacOS三个生产项目，原JSON与唯一阅读/文件链路不变。范围见[收尾契约](../docs/p4-completion.md)和[完整清单](../docs/p4-completion-checklist.md)。开发完成与P3/P4集中设备验收封板分别记录。

## 最终结果

| 检查 | 结果及边界 |
|---|---|
| 源码及依赖边界 | 三个生产项目；62个原源码、239个局部适配、26个原依赖源码检查通过，数量不是功能覆盖率 |
| 全量自动回归 | **803/803通过，0失败/跳过**；原规则、来源、配置、资源与正式XAML Headless |
| 文件操作专项 | **205/205通过，0失败/跳过**；本轮删除/链接/数据快照及既有改名、整书传输、目录复制、删除和剪贴板回归 |
| 最终设置文案复核 | **1/1通过**；修正文案后装载正式设置窗口，保存/取消/未知字段与失败回滚通过 |
| Engine及正式Library | 默认输出串行编译通过；设置修正后再次编译正式Library |
| 正式Mac应用 | 最终默认输出构建通过，Mach-O arm64；没有启动或激活 |
| 本地签名 | 最终严格深层codesign校验通过，开发ad-hoc；非Developer ID/公证/安装验收 |
| 完整命令 | 235个原实例保留，167个执行入口/68个占位；本批扩展既有入口，不代表功能覆盖率 |
| 用户资源只读抽样 | 3个子目录59/81/78页，共218页；瀑布12位置、缩略9位置通过，两组关闭后租约/缓存字节均0 |

输出见[全量验证](p4-completion-validation.json)、[最终专项及重建](p4-completion-final-validation.json)、[命令清单](p4-completion-commands.json)与[匿名资源采样](p4-completion-resource.json)。正式应用位于 `portable/src/NeeView.MacOS/bin/Debug/net10.0-macos/NeeView.MacOS.app`，RID子目录是SDK中间产物。

## 行为与修复

普通实体删除进入系统废纸篓，播放列表仅删除选中登记，ZIP流式重建删除条目/目录，保留注释、时间、外部属性和幸存Page身份。ZIP原独立权限默认false，同时要求普通修改权限，永久删除始终确认；RAR/7z内部条目只读。主页Delete只处理主图片，页面列表使用显式多选，部分失败只协调真实成功项。

Paste/Drop保留原QueryPath/文件优先及图片、HTML内联、浏览器文件副本、HTTP(S)、位图回退顺序。失败阶段清理后尝试下一数据，取消/预算超限停止；普通文本不冒充文件，混合非本地文件不部分打开。视图借用Bitmap，不释放发送者资源；下载、编码和临时材料归后端。

符号链接自身复制/移动/删除/改名，树内不跟随目标；指纹、覆盖副本、UndoRedo和journal沿同一后端。Finder alias经公开Foundation API解析打开，禁止弹窗或自动挂载，真实别名互操作另验。

测试发现并修正：播放列表同路径不同别名被连带移除；目录链接用File.Move改名被.NET拒绝，改用Directory.Move；ZIP权限需沿原独立默认false且读写SaveData合并；图片接收一般失败必须保留原回退；根链接归档固定复制需保留IsRootShortcut。Foundation资源读取改用公开TryGetResource，正式构建通过。

初次专项中的ZIP设置断言对应尚未重编译的SaveData分支，修正并重编后由803项全量及最终205项专项覆盖。最终只读审查未发现明确实现缺陷；产品文案审查补充归档文件/目录提取范围。设置页旧“归档内删除、链接和页面列表多选未迁移”及“一律废纸篓”说明已更新。没有构建目录占用或输出目录改写。

## 界面证据

正式XAML Headless合成客户区截图已离线查看：[页面删除确认](p4-completion-confirm.png)、[整书删除确认](p4-completion-book-confirm.png)、[整书覆盖](p4-completion-book-overwrite.png)、[目录覆盖](p4-completion-directory-overwrite.png)、[分类布局](p4-completion-destination-layout.png)、[最终文件设置](p4-completion-settings.png)。其余重复截图移至本机忽略的artifacts；用户资源截图不入仓库。这些截图不能证明原生窗口、真实焦点或Retina效果。

## 验收限制及范围

本轮没有启动/激活正式应用、读写真实系统剪贴板/废纸篓、连接Windows或修改用户图片；用户指定图片只读挑选子目录。第一组瀑布首图样本约1265ms，其余时间见匿名报告；这是NAS/Headless抽样，不能判断屏幕P95目标已达成。

P3/P4集中设备待项包括真实剪贴板/Finder/废纸篓和Finder alias、分类/覆盖/撤销恢复、完整焦点/弹出层、无损Retina、固定Windows动态、真跨卷/NAS/权限与长期原生内存。既有P2部分运行记录保留，不外推本轮新能力。前台集中时段未收到答案，按静默流程不启动或激活应用；触控板按用户要求跳过，多屏无环境。

CutFile/CutBook按用户决定禁用。内部归档目录递归提取为原版TODO；Windows.lnk/COM私有协议不在Mac模拟。只有file promise且无标准替代数据的专门接收、目录树任意对象完整文件管理属于后续扩展；P5旧数据导入、高级内容和发布不混入本阶段完成项。

本轮验证完成后按仓库规范自动提交并推送，实际结果单独报告；Developer ID、公证与发布未执行，用户 `.DS_Store` 保留。
