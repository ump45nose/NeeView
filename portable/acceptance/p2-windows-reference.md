# P2 Windows 动态参考采集

日期：2026-10-04。用户已连接 RDP 并授权前台验收。本节点在 Windows 独立程序副本和临时 Profile 中采集行为；Mac 同步复演尚未执行，不能据此判定跨端一致或 P2 封板。

## 程序、隔离与证据边界

| 项目 | 实际结果 |
|---|---|
| 原安装 | `P:\Program\NeeView`；原进程保留，未主动读取或修改原 Profile |
| 参考包 | ProductVersion `1.0.0+ddc857b511e9f04cbc356bdabb1f748963e4f9e1`；FileVersion `1.0.0.0` |
| 包设置 | PackageType `Zip`；Revision `ddc857b511e9f04cbc356bdabb1f748963e4f9e1-dirty`；UseLocalApplicationData=false；PathProcessGroup=true |
| EXE SHA256 | `B40321BF42E1AAA484E8E6EE4B715B2DAF672705810D52B3D2C6DD56EEC181EA` |
| 固定基线 | 目标为 46.3 fork 合并 `c5c398d89`；实际安装包未证明与该基线一致，dirty 差异未知 |
| 数据 | 共用夹具 ZIP SHA256 `22c41948935d14623f56fc5b9358374141d474f8425b3238fa67286974728cc6`；两端 ZIP 及 24 张 PNG 已核对 |
| 隔离 | 副本排除原 Profile/Logs；只在测试子进程环境设置 `NEEVIEW_PROFILE`；未修改系统环境变量 |
| 退出 | 两次测试进程均正常关闭；末次确认原进程仍运行 |
| RDP 环境 | API 报告测试窗口 DPI=96、主屏 Bounds=1496×939；客户端截图 2578×1682。这些不是物理屏幕性能或原生分辨率证据 |

同路径单实例转发按原 `MultiBootService` 行为处理，独立 EXE 路径用于隔离原实例。启动过程出现一次焦点竞态，随后明确点击终端重试；无法仅凭当时画面确定误输入落到哪一实例，不能声称全程没有向原实例发送输入。没有在原实例主动打开测试书籍。

原始截图留在本机临时目录，文件名和 SHA256 见 [证据清单](p2-windows-evidence.json)。本节点不把截图加入 Git。包含私人缩略图的 `windows-package-inventory.png` 被明确排除；其余截图即使只含测试内容，也需在公开发布前检查路径、用户名和系统界面。

文件头检查发现：CUA 返回的是 JPEG 字节，原采集文件使用 `.png` 后缀。清单明确记录实际类型，原文件不转换、不覆盖；这些截图可核对页面行为，不能当作无损设备像素映射证据。临时文件可能随系统清理失效，Git 中的清单本身不能恢复图像。

## 阅读目录

打开 `01-reading/001.png`，实际书籍为所在目录，9 张图片。初始设置为单页、右开、名称升序；分割/首单/末单关闭，宽图视为双页开启。以下导航由菜单执行，不证明真实快捷键已验。

| 操作 | Windows 实际结果 | 证据文件 |
|---|---|---|
| 切双页右开 | 左002、右001，主位置1/9 | `windows-reading-double-rtl-001.png` |
| 前进一页 NextOnePage | 002独占，2/9；003宽图占两页 | `windows-reading-double-one-step-002.png` |
| 前进 NextPage | 003独占，3/9 | `windows-reading-double-wide-003.png` |
| 再前进 | 左005、右004，4/9 | `windows-reading-double-rtl-004.png` |
| 改左开 | 左004、右005，仍4/9 | `windows-reading-double-ltr-004.png` |
| 转首页、开启首页单独 | 001独占 | `windows-reading-cover-001.png` |
| 关闭宽图视为双页、转尾页 | 左008、右009，主位置8/9 | `windows-reading-last-pair-008.png` |
| 开启尾页单独、前进 | 008先独占；前进后009独占，9/9 | `windows-reading-last-single-009.png` |
| 单页、选003、开启分割、左开 | 003(L)红边；前进后003(R)蓝边 | `windows-reading-split-ltr-003L.png`、`windows-reading-split-ltr-003R.png` |
| 右开、先选002再连续前进 | 003(R)蓝边 → 003(L)红边 | `windows-reading-split-rtl-003R.png`、`windows-reading-split-rtl-003L.png` |

## CBZ、切书与状态保存

书架打开 `01-reading.cbz`，显示001、1/9，恢复该新书的默认设置，没有继承目录刚设置的分割/首单/末单。双页右开、前进一页、前进分别显示左002/右001、002独占、003独占（四张 `windows-cbz-*.png`）。

切回目录恢复003及目录阅读设置，显示003(R)。前进到003(L)后正常关闭，独立 History 中目录和 CBZ 的 Page 均为 `003.png`；目录 Props 为 `IsDivide IsSingleFirst IsSingleLast`，CBZ Props 为 `WidePage IsWide IsRecursive`。重开测试副本后先显示空书籍；随后明确打开目录，恢复003(R)及设置，见 `windows-isolated-history.png`、`windows-restart-directory-003R.png`。本次没有验收“自动启动恢复上次书”的完整配置条件。

**分割页持久化存在静态差异，待 Mac 动态复核。** 原 `BookMemento`/`Book.CreateMemento` 保存 EntryName，没有 Part；Windows 的上述重开行为与此相符。当前 Mac 的 `SaveData.ReadMemento` 读取独立 `MacPagePart`，`SaveAsync` 写入历史及 LastBookV2，`BookOperation` 将其用于非显式条目打开。该字段不在 Props 中；只查看 BookMemento 类会漏掉这条实际保存链。因此不能沿用“当前 Mac 也只保存条目名”的判断，也不能将这项列为跨端通过。暂不修改产品行为，先在同夹具中确认打开入口及实际显示，随后按源码基线处理差异。

## 侧栏、浮动与自动隐藏

| 操作 | Windows 实际结果 | 证据文件 |
|---|---|---|
| 左页面列表图标拖到右信息下部 | 垂直组合：信息上、页面列表下 | `windows-dock-cross-side-vertical.png` |
| 调整组合分隔线 | 比例改变 | `windows-dock-ratio.png` |
| 成员标题菜单“浮动” | 仅页面列表独立浮窗，信息保留 | `windows-dock-floating.png` |
| 浮窗菜单“停靠” | 右侧独立面板，不恢复原组合 | `windows-dock-redock.png` |
| 右图标拖回左图标栏 | 成功插入；不必立即改变左侧选中面板 | 操作观察，未独立截图 |
| 左页面列表图标拖到右信息左半区 | 水平组合：页面列表左、信息右 | `windows-dock-cross-side-horizontal.png` |
| 水平组合成员再次浮动 | 仅该成员拆出，信息单独保留 | `windows-dock-member-float-split.png` |
| 右自动隐藏开启、离开 | 右内容和图标栏隐藏，查看器扩大 | `windows-autohide-right-away.png` |
| 点击右边缘图标位置 | 右侧面板重新显示 | `windows-autohide-right-reveal.png` |
| 正常重开后点击右边缘 | 右独立页面列表可唤回，自动隐藏仍勾选 | `windows-restart-panel-reveal.png`、`windows-restart-autohide-setting.png` |

标题拖动尝试没有改变布局，不据此断言原产品失败；成功样本采用图标拖动。重启前已拆组，因此没有动态验证仍处于组合状态时的组合关系/比例恢复。悬停唤出、文本焦点/菜单显示锁等也未完成参考采集。

## 尚未完成与工具中断

- Mac 正式应用本节点未启动，Application Support 未替换；阅读、侧栏、Retina、真实触控板、长期浏览资源均未执行。多屏无环境。
- Windows 排序、历史/书签完整导航、组合状态重启、真实键位及更多自动隐藏条件仍待采集。
- RDP 键盘临时切至 Unicode 以准确输入命令；结束时已向“扫描码”菜单项发送选择动作，但之后截图不可用、CUA 连续超时，无法观察确认当前模式。恢复工具后优先核实。
- 这是 UI 工具中断；显式重置并重新绑定 Windows App 后 AX/截图恢复，远程桌面仍可读取。没有证据证明 RDP 服务断开或之前的 0x104 复发。不采用其他输入脚本绕过。

本节点只整理记录和证据清单，不重复产品构建/测试、不改阅读行为、不推送或发布。P2 整体验收仍未封板。

## 后续 Mac 复演

上述状态为 Windows 采集节点当时结果。Mac 后续已完成阅读及部分侧栏复演，确认并修复半页持久化差异；结果和剩余项见[p2-device-input-runtime.md](p2-device-input-runtime.md)。允许留证的34张Windows原图已按原SHA256归档至Mac证据清单所列持久目录；含私人缩略图的inventory未复制。参考包固定基线未获证明，历史采集边界保持。

后续[设备资源节点](p2-device-resources-runtime.md)使用同一安装包的隔离副本补采名称降序：003(R)保持，主位置3/9→7/9；Mac页面列表刷新差异已修复，最终构建升降序复验通过。三张新Windows JPEG及关闭记录列于[新清单](p2-device-resources-evidence.json)，不修改原34张历史清单。测试PID19704正常关闭，Get-Process确认仅原PID28680；查询终端退出。ScanCode恢复选择已发送，组合键传输仍未充分验证。触控板按用户要求跳过、未验，多屏无环境，固定源码版本及其余动态限制继续保留。
