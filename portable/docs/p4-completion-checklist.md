# P4 开发收尾与集中验收

开发范围沿原BookPageActionControl/BookControl及fork分类，只有三个生产项目、原JSON与唯一阅读/文件链路。原235命令保留，167执行入口/68占位，数量不是功能覆盖率。

| 项目 | 开发状态 | 设备/原版边界 |
|---|---|---|
| 两区面板、管理/新建/刷新、九数字/Index | 已接入 | 数字分类及真实跨栏组合/拆组通过；Windows数字1/2/3分类子集通过；popup方向键及浮窗尺寸恢复失败，完整停靠/自动隐藏待验 |
| 原Once/All/AllLeftToRight、固定移动/复制 | 已接入 | 原页组、方向、部分成功静默回归；固定Windows Copy/Move子集通过，AllLeftToRight实际执行顺序仍依静态/自动证据 |
| 移动UndoRedo/容量/覆盖/中断恢复 | 已接入 | 真实覆盖Undo/权限失败通过；整书APFS→SMB通过；真断线时目录检查阻塞失败，部分写入/恢复未到达 |
| DeleteFile及页面列表显式多选 | 已接入 | AppKit普通实体多选废纸篓/列表只删登记通过；隔离ZIP单条真实永久删除及重启通过 |
| ZIP原独立写权限、不可逆确认 | 已接入，默认关闭 | 单条真删除后八条哈希/CRC/注释/属性及重启定位通过；中断重建另验，RAR/7z只读 |
| DeleteBook及邻书、RenameBook和原路径联动 | 已接入 | Mac整书系统废纸篓通过；固定Windows普通目录改名、当前页/整书删除及恢复子集通过，其他组合另验 |
| CopyBookToFolderAs/MoveBookToFolderAs | 已接入 | 整书本地复制和APFS→SMB移动三文件哈希通过；逻辑/覆盖静默回归，固定Windows另验 |
| CopyFile/CopyBook、归档实体化、目录复制 | 已接入 | Finder标准文件双向往返通过；其他范围仍按用例另验，内部目录提取为原版TODO |
| Paste/Drop多来源、位图/HTML/URL与失败回退 | 已接入 | Safari图片/网页选区/HTTP图片链接接收通过；纯HTML独立格式未证明，只有file promise无标准数据明确提示 |
| Mac符号链接自身操作、Finder别名打开 | 已接入 | 链接真实临时文件回归，Foundation Finder alias真机另验 |
| Finder定位 | 已有桥接 | 当前页/书真实定位与失败另验 |
| CutFile/CutBook | 用户决定禁用占位 | 移动使用既有分类/移至文件夹 |

P4开发收尾及本轮实际结果见[契约](p4-completion.md)与[验收记录](../acceptance/p4-completion-runtime.md)。只有Windows专属.lnk/COM协议不在Mac模拟；file promise专门接收、目录树任意对象文件管理为后续扩展；完整旧数据导入和高级内容仍为P5。不得把它们误标为本阶段已迁。

## 与P3合并的集中设备验收

- 临时分类副本/Profile；用户指定图片根只读，挑几个子目录做瀑布/缩略速览。
- Mac菜单/焦点/弹出层、侧栏组合/自动隐藏/浮动，分类成功/失败/覆盖/撤销定位。
- 标准剪贴板图片/文件与Finder、系统废纸篓、Finder alias；还原验收数据。
- 无损Retina像素1:1、屏幕显示完成P95及持续原生内存/资源回收。
- Windows动态先确认实际安装包与固定基线；已有dirty包不能外推固定源码一致性。
- 真跨卷/已挂载NAS断线/权限和恢复材料，临时夹具不写用户图片。
- 前台集中约定时段，未收到时段答案不启动或激活应用；触控板按用户要求跳过，多屏无环境。
- 签名公证/分发安装属于P5，ad-hoc构建不继承为发布通过。

未完成设备项不妨碍可运行增量提交，但P3/P4整体验收不能封板。

## 2026-10-05 集中设备结果

[设备记录](../acceptance/p34-device-runtime.md)覆盖真实分类/覆盖Undo、权限/缺失目标、整书跨卷/废纸篓、Finder标准文件双向、Safari图片/链接接收、列表多选/播放列表登记删除、面板组合/拆组、文本焦点隐藏子集与Retina无损ROI。Finder file reference URL、Retina半像素边界及列表Delete作用域三处已修复并复验。

全量807通过/2跳过，指定资源补跑2/2通过；正式构建/严格ad-hoc签名通过。长期自然资源测试失败，静态100次无障碍查询独立确认回调数组保留；关闭归零不替代稳定性。后续固定Windows构建与动态子集已完成，见下节；完整弹出层、ZIP真实永久删除、真NAS断线和屏幕P95仍待验，Finder alias未继承为通过。原Mac配置已完整校验恢复，私人原始材料只留本机。

长期资源缺陷登记为[MAC-AX-001](known-issues.md#mac-ax-001macos-无障碍查询持续保留回调数组)。按用户要求先保留问题、继续其他对照，不把它改记为通过。

## 2026-10-05 后续固定原版对照

[后续记录](../acceptance/p34-compare-runtime.md)与[匿名证据](../acceptance/p34-compare-evidence.json)分别记录固定 Windows 构建、动态阅读/分类、Mac 阅读、163/163 针对性回归及恢复。Windows 原默认目录 restore/publish 成功，独立副本完成两种阅读方向、半页、页组/单页、排序锚点、重启锚点、Once/All Copy/Move、逐文件 Undo/Redo 和冲突取消；没有执行 Windows 覆盖，不外推整书及删除对照。

Mac 新样本确认页组/单页、左右半页顺序、排序锚点、显式 LastBook 启动恢复、普通打开按字段策略恢复及首/末/宽页。不同入口和不同 wide 设置分别记录；菜单调用未提交与 AX slider 只改控件值的尝试不计通过。

- [MAC-FLOAT-002](known-issues.md#mac-float-002retina-浮窗关闭重开及重启后尺寸持续放大)：真实 Retina 浮窗关闭/重开及启动后宽度累积放大，未修。
- [MAC-READ-003](known-issues.md#mac-read-003分割页缺少原版标题中的-lr-提示)：导航正确，原标题 L/R 提示未迁入，未修。
- [MAC-AX-001](known-issues.md#mac-ax-001macos-无障碍查询持续保留回调数组)：长期资源失败状态保留。

Mac 测试进程退出，原 Profile 四文件及权限完整校验恢复；Windows 测试副本退出，原实例保留，九张源图与 CBZ 条目哈希 9/9 匹配，临时环境恢复。剩余完整停靠/hover/popup、真 NAS 断线、ZIP真实永久删除、屏幕 P95 和 Finder alias 等仍按独立用例验收。**P3/P4 未封板。**

## 2026-10-05 剩余设备验收结果

最新[运行记录](../acceptance/p34-final-runtime.md)和[匿名证据](../acceptance/p34-final-evidence.json)补齐 ZIP 单条永久删除、八条完整性/元数据及重启定位；固定 Windows 新增覆盖/Undo、整书复制/改名/移动、当前页及整书废纸篓删除和九图恢复。Windows 覆盖 Undo 没有恢复旧目标，Mac 覆盖备份恢复明确记为改进，不能写成完全等价；整书冲突及所有命令仍未全部动态对照。

- 真 NAS 验收失败，登记 [MAC-NAS-004](known-issues.md#mac-nas-004nas-目录检查阻塞且重连后无法结束文件操作)：写目标前的目录检查卡住，重连后未返回并拖住正常退出；部分写入回滚/重启恢复未到达。
- 展示方式下拉框展开/取消通过，但已展开时 Up 触发全局切书，登记 [MAC-INPUT-005](known-issues.md#mac-input-005展示方式弹出层的方向键触发全局切书)。完整 popup/自动隐藏锁与停靠/拖回仍未通过。
- 指定来源 12 张本地副本瀑布/缩略/锚点滚动通过，只证明本地缓存绘制；本轮 NAS 59 图来源打开阻塞。Instruments 数据是否有有效目标屏幕区间单独记录，没有有效值时 P95 留空。
- 长期资源、Retina 浮窗尺寸及半页提示三个既有问题未修；触控板跳过、多屏无环境。

原 Mac 四文件及根目录权限完整恢复，测试应用退出；本轮 NAS 空目录/代理已清理，十个原挂载保留；Windows 测试副本退出、原实例保留、删除样本九图恢复。只有验收记录变化，无新构建/自动测试/签名结论。**P3/P4 未封板。**
