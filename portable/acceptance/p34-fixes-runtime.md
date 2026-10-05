# P3/P4 缺陷顺序修复

2026-10-05，依已知问题表顺序处理。单项代码/自动回归/设备复验分别记载，P3/P4未封板。私人配置、截图及原始测试结果保留本机，匿名数值与哈希见[p34-fixes-evidence.json](p34-fixes-evidence.json)。

## MAC-AX-001

核验最新官方12.1.3及master，仍未找到已释放 `recalculateChildren` 返回COM引用的发布版。12.1.3提交为 `8eeda4f6f546165b3f72e63c9f42247abb306905`；相关上游 `3f73cc926cfed776b921eef5a63652e44aace74a` 仅改成员指针，并未修复返回数组/子项引用。保留无障碍能力、锁定依赖和原失败证据；临时单文件源码补丁会增加Native构建维护，已提出选择，尚未收到答复，未擅自引入。

## MAC-FLOAT-002

Avalonia 12.1.3 [Screens.mm](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/native/Avalonia.Native/src/OSX/Screens.mm#L60-L70) 使用Cocoa桌面坐标和Scaling=1；[TopLevelImpl](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Avalonia.Native/TopLevelImpl.cs#L116-L136) 分开维护DesktopScaling与RenderScaling。旧恢复/保存使用不同单位。修复在窗口显示后使用实际绘制缩放恢复客户端设备像素，工作区/位置另按桌面单位约束；恢复事件不覆盖原位置。

12项FloatingPanelTests通过，正式.app构建和 `codesign --verify --deep --strict` 通过。新增回归覆盖桌面缩放1/绘制缩放2、1/1、2/2、1/1.5的十次往返及Retina工作区限制。原停靠/拖回、内容唯一所有者、输入、最大化、退出失败与重启测试继续通过。

正式单屏Retina设备使用隔离Profile，原种子 `Normal,650,150,360,500`。首次、进程重开、同进程系统关闭按钮后重开、正常退出后再启动截图均为360×564设备像素（含64像素标题栏），保存客户端位置始终360×500。最初Command+W与侧栏重开观察未确认浮窗身份，未作为独立通过证据；后续逐动作系统关闭按钮、打开历史图标、完整AX及截图验证了重开，进程重启另验。多屏和完整停靠真机范围仍待验。

本轮未访问或修改用户原图，原Profile已移入完整备份；全部缺陷复验结束后再恢复并核对哈希。
