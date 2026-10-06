# P5 第二十五批：原外部应用链

## 职责、依赖与出处

固定基线 c5c398d89 的 ExternalApp/Collection/IExternalApp、ExternalAppUtility、三个 OpenExternalApp 命令与参数、BookPageActionControl.CollectPages、BookControl 和 ArchiveEntryUtility。Engine保持原来源、页组、参数和JSON；Backends仅替换系统启动与临时材料；AXAML/表现草稿独立，启动仅在MacApp装配。

## 契约与业务规则

- OpenExternalApp使用原命令参数；OpenExternalAppAs/OpenBookExternalAppAs使用原一开始Index，0打开配置选择菜单，越界禁用，不自动使用首应用。整书固定Once，页面保留Once/All/AllLeftToRight及右读反转、保序去重。
- 原五配置字段Name/Command/Parameter/ArchivePolicy/WorkingDirectory、默认一个系统关联应用、默认参数 `"{File}"`、默认提取策略与原差分保持。管理集合支持新增/删除/升降排序，克隆草稿经过父设置/五JSON事务提交，取消/失败不丢失草稿；未知条目字段保留。
- 页面使用应用的归档策略；整书按原BookControl实际链路使用System.ArchiveCopyPolicy.LimitedRealization，不能根据应用参数名改掉原行为。真实文件直传，内部目录原提取TODO仍明确提示。
- `{File}`/`$File` 保持完整路径；`{Uri}`/`$Uri` 沿原Uri.EscapeDataString输出编码路径（不是file URL）；缺文件/Uri占位符时附加原默认参数。`{NeeView}`/`$NeeView` 改为实际Mac入口，按原语义先保存。先解析模板再按字面替换路径，不把文件名中的引号/shell字符再次解析；空参数和双/单引号受支持，未闭合模板明确报错。
- Command为空由LaunchServices选择默认应用，接收一个完整路径/绝对URL；显式命令支持可执行文件、PATH命令和.app的实际ExecutablePath，ArgumentList按字面传递，WorkingDirectory实际应用。不通过shell、不执行Windows.exe兼容层。系统关联没有工作目录API，需要工作目录时明确提示配置应用，不静默忽略。

## 状态、资源与错误

单槽保存准备任务与完成信号，在同一BookOperation互斥下持来源、实体化并提交。切书/关闭立即取消准备，代次拒绝晚到；关闭等待真实平台结果后释放来源。文件操作、导航和配置不会借外部应用建立第二书籍状态。

随机提取文件在系统提交前计入共享2GiB进程预算，剪贴板与外部材料共用占有计数；成功提交后保留至进程退出，后续复制/关闭窗口不提前删除。部分多页启动失败仍保留已可能打开的整批；首个提交失败释放未发布材料。零字节文件仍有租约。清理失败保留目录/预算并允许退出重试；只清理应用自建目录。

返回成功表示系统接受启动，不等于外部程序已完成读图。权限/缺失/损坏参数/错误工作目录/预算上限进入原Error表现并可重试，不导航、修改源图或写移动历史。

## 测试、验收与扩展

22项专项覆盖原默认/差分/未知字段、四占位符与字面路径、页组顺序、Index0/一开始索引、整书策略、临时驻留、部分/首项失败、关闭晚到、保存失败、共享预算回收及零字节租约；正式Headless菜单/集合重排/父设置失败重试覆盖实际AXAML。原生测试以隔离后台probe可执行文件/合成.app验证实际argv、工作目录与注入不执行；不打开用户应用/浏览器。

Windows命令路径保持原材料，需要用户选择Mac应用。真实第三方应用、默认关联/URL系统窗口及Windows动态对照另验。完整结果见[静默验收](../acceptance/p5-external-applications-runtime.md)，入口数量不是功能覆盖率。
