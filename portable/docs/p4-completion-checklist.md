# P4 开发收尾与集中验收

开发范围沿原BookPageActionControl/BookControl及fork分类，只有三个生产项目、原JSON与唯一阅读/文件链路。原235命令保留，167执行入口/68占位，数量不是功能覆盖率。

| 项目 | 开发状态 | 设备/原版边界 |
|---|---|---|
| 两区面板、管理/新建/刷新、九数字/Index | 已接入 | 数字分类及真实跨栏组合/拆组通过；完整焦点/浮动与固定Windows分类样本待验 |
| 原Once/All/AllLeftToRight、固定移动/复制 | 已接入 | 原页组、方向、部分成功静默回归；Windows动态另验 |
| 移动UndoRedo/容量/覆盖/中断恢复 | 已接入 | 真实覆盖Undo/权限失败通过；整书APFS→SMB通过，真断线/中断恢复仍待验 |
| DeleteFile及页面列表显式多选 | 已接入 | AppKit普通实体多选废纸篓/列表只删登记通过；ZIP真实永久删除取消，未验 |
| ZIP原独立写权限、不可逆确认 | 已接入，默认关闭 | ZIP流式重建和存活ID/注释回归；真机只到确认并取消，RAR/7z只读 |
| DeleteBook及邻书、RenameBook和原路径联动 | 已接入 | 整书系统废纸篓通过；改名及固定Windows动态继续按原清单待验 |
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

全量807通过/2跳过，指定资源补跑2/2通过；正式构建/严格ad-hoc签名通过。长期自然资源测试失败，静态100次无障碍查询独立确认回调数组保留；关闭归零不替代稳定性。Mac浮动/完整弹出层、固定Windows构建与动态、ZIP真实永久删除、真NAS断线和屏幕P95仍待验，Finder alias未继承为通过。原Mac配置已完整校验恢复，私人原始材料只留本机。
