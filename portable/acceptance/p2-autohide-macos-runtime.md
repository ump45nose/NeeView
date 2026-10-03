# P2 第九批：自动隐藏正式 Mac 运行记录

执行于2026-10-03，正式入口为默认输出目录的 `NeeView.MacOS.app`，基于 `b13a2569d` 后的本批源码。设备为 Apple Silicon Mac，macOS27.0.1、Xcode27/workload27；最低支持要求仍为 macOS15。应用没有 Preview 入口。

## 构建与自动回归

最终执行 `validate.py --phase p2-autohide --macos-source --macos`，SDK为本机独立 .NET10。Engine、正式入口Library编译、正式 `.app` 构建及本地ad-hoc严格签名均通过；全量133通过、0失败、0跳过，独立输出见 [validation](p2-autohide-validation.json)。新增12项覆盖原五区资格/延迟、焦点/弹出/捕获、键路由、JSON兼容、独立胶片条、设置、边角及原生手势排除覆盖栏。清单45文件/74子集只表示记录数。

初版运行后补上原 SidePanelMargin 并重跑全量及正式构建。`normal-hidden/show-all/show-toggle-observed`属于补余量前版本；全部 `final-*`属于最终版本。[事件索引](p2-autohide-runtime-events.json)保留版本区分，不声称两个构建的全部交互互相等价。

## 最终构建运行范围

用户随后说明验收期间持续切换到其他应用；当时未记录每次切换的时点。下表保留截图/AX与文件实际观察，焦点保持/释放、悬停和捕获相关项目带有并发操作条件，需在约定的可控制前台时段复验，不作为独占环境的完整通过结论。133项Headless回归、构建、签名和退出文件核验不依赖桌面前台，结果分别保留。

临时配置启用普通窗口自动隐藏、0.4秒隐藏/0秒显示以及胶片条，临时键位只用于触发原命令；正文鼠标翻页临时解绑，以便焦点点击不改变页。打开原中文CBZ第三页、单页/RTL。

| 验收动作 | 实际观察与证据 |
|---|---|
| 普通窗口隐藏、ShowHiddenPanels一次显示 | 隐藏时原两侧图标仍存在，展开后原组合栏、导航器、菜单/地址、胶片条及滑条恢复，当前3/6；[隐藏](p2-autohide-final-normal-hidden.png)、[展开](p2-autohide-final-show-all.png) |
| 地址文本焦点及移回正文 | 文本焦点只保留菜单/地址，其他自动隐藏区收起；移回正文后菜单收起，仍显示第三页。[文本焦点](p2-autohide-final-text-focus-menu-held.png)、[正文](p2-autohide-final-viewer-focus-released.png) |
| 原F11全屏与取消 | macOS全屏下窗口按钮消失，ShowHiddenPanels能展开原区域，3/6保持；取消后普通窗口按钮恢复。[全屏](p2-autohide-final-fullscreen-shown.png)、[取消](p2-autohide-final-fullscreen-cancelled.png) |
| 左边缘展开 | 合成指针拖至左边缘后组合栏展开，原第三页保持。[边缘](p2-autohide-final-left-edge.png)。拖回/点击后的持续状态受工具影响，只作为探索，不判定边缘离开通过 |
| 普通设置取消 | 新页普通窗口由on编辑为off后取消；重新打开仍为on。[草稿](p2-autohide-final-settings-cancel-draft.png)、[原值保持](p2-autohide-final-settings-cancel-retained.png) |
| 设置保存与真实分页 | 将普通窗口模式保存为off后区域常显；正文Left命令实际到4/6。[保存](p2-autohide-final-settings-saved-visible.png)、[第四页](p2-autohide-final-page-four.png) |
| 退出和未修改JSON的重启 | 文件保存 IsAutoHideInNormal=false、LastBookV2为中文/004.png；未改JSON重启仍常显及4/6。[重启](p2-autohide-final-restart-saved-visible-four.png) |
| 原数据还原后再启动 | 原第三页、信息/页面列表组合及比例、右导航器恢复，临时键位撤回。[最终状态](p2-autohide-final-user-restored.png) |

同资格弹出/收起未改变阅读页，精确 Viewer Bounds/不发布正文刷新由正式XAML回归断言；本次未测完整帧P95。胶片条绘制/滑条与正文仍沿既有唯一加载链。

## 数据保护

正常退出后备份三JSON到被Git忽略的 `artifacts/p2-autohide-smoke/state-before`，中途及最终验收状态另存。最终应用退出后原子还原，逐字节及SHA256均一致；六PNG与中文CBZ哈希保持，原本不存在的Playlists目录仍不存在。[状态核验](p2-autohide-runtime-state-checks.json)只保存哈希/结果，不提交用户JSON。重开后正常访问时间保存可以变化，哈希比较发生在重开前。

## 工具与待验边界

期间原Mac执行插件返回应用配置阻断，调用未执行；改用官方Remote Desktop Commander并重新核验同一设备、UID与路径。两执行器进程会话独立，未混用PID。界面操作全部通过cua。

cua某些菜单/对话框动作后返回上一窗口树；以单独刷新后的截图、AX和退出文件结果确认。菜单点击/键盘探索未作为可靠设置入口证据，正式设置采用临时命令键打开。初版Show命令超时后先观察，确认隐藏才继续，未盲目重复；最终边缘离开探索保存于忽略目录raw-evidence。

Headless已覆盖菜单/ContextMenu锁、Slider捕获、左右组合拖动和Allow/AllowPixel/Deny；原菜单真实弹出层连续导航、真人边缘离开/拖动捕获、IME/鼠标/触控板、Retina像素1:1、多屏及标题安全区坐标仍待验。设置失败后取消当前不撤销已应用运行时值，本次只验普通取消/成功保存。窗口位置/FullDesktop/浮动窗口仍占位，Windows动态对照未执行。

P2未封板。长期原生内存、NAS、完整显示性能、用户新增交互验收、Developer ID/公证和干净安装独立待验。本批仅自动本地提交，未推送或发布。

后续采用[静默优先验证流程](../docs/validation-workflow.md)，未安排前台时段时不自动执行真实界面输入。
