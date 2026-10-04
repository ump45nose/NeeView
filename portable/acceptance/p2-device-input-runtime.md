# P2 Mac 同夹具动态复演与修复

日期：2026-10-04。使用正式 ARM64 `.app`，在用户已安排的前台验收时段通过 CUA 发送键盘、点击及拖放。阅读/布局样本为共用合成夹具；Windows 参考来自已采集的 dirty 安装包，未证明等同固定 `c5c398d89`。本记录只判断实际复演项目，不封板 P2。

## 构建、隔离和证据

- 产品改动基于 `b52f4f769`。数字菜单修复及半页恢复修复的正式构建通过 372 项回归后执行真机复验。审查追加同书 LastBookV2 未知字段/Props 保留，以及反向跨书末半页回归后，最终 **375/375、0失败/跳过**；Engine、正式 Library、正式 `.app` 构建及本地 ad-hoc 严格签名五步通过，见[原始输出](p2-device-input-validation.json)。追加的 JSON 保留修改尚未再次真机运行，不把早先截图归到最终构建。
- 正常关闭应用，备份默认 Application Support 的四个文件并逐文件核对 SHA256；原目录整体移出，使用隔离测试目录。结束后正常退出，确认进程停止，测试目录留存，原目录还原；四个文件的路径、大小及 SHA256 与原快照/备份完全相同。
- [Mac 证据清单](p2-macos-device-evidence.json)记录 36 张 CUA 截图的实际 JPEG 类型、尺寸和 SHA256。与 34 张允许留证的 Windows 截图一起归档到本机持久目录，排除含私人缩略图的 Windows inventory。原图不加入 Git，不向外发布；清单不能替代图像本体。
- Headless 本轮重绘的 27 张 PNG 和三个指标/命令 JSON 留在本机归档。没有修改此前已提交的阶段证据；不为重复渲染新建一套截图副本。375 项验证 JSON 单独入库，Headless 与真机结果分开。

## 阅读规则复演

目录与 CBZ 使用相同 9 张编号图片。Mac 导航使用原命令快捷键；少数开关在隔离 Profile 临时绑定 Ctrl+Shift+F1…F11，不修改用户键位。原输入反转仍生效，菜单明确方向与物理方向键不能混为一个入口。

| 操作/状态 | Mac 实际显示 | Windows 同夹具对照/证据 |
|---|---|---|
| 双页右开 | 左002、右001，1/9 | 一致；`mac-reading-double-rtl-001-fixed.jpg` |
| NextOnePage | 002独占，2/9 | 一致；`mac-reading-double-one-step-002.jpg` |
| NextPage | 003宽图独占，3/9 | 一致；`mac-reading-double-wide-003.jpg` |
| 再前进 | 左005、右004，4/9 | 一致；`mac-reading-double-rtl-004.jpg` |
| 改左开 | 左004、右005，主4/9 | 一致；`mac-reading-double-ltr-004.jpg` |
| 首页单独 | 001独占 | 一致；`mac-reading-cover-001.jpg` |
| 关闭宽图独占、转尾部 | 008+009，主8/9 | 一致；`mac-reading-last-pair-008.jpg` |
| 开尾页单独、前进 | 008独占，再009独占 | 一致；`mac-reading-last-008-isolated.jpg`、`mac-reading-last-single-009.jpg` |
| 单页分割、左开 | 003(L)→003(R) | 一致；`mac-reading-split-ltr-003L/R.jpg` |
| 单页分割、右开 | 003(R)→003(L) | 一致；`mac-reading-split-rtl-003R/L.jpg` |
| 新 CBZ 打开、双页及两种步进 | 默认001；左002/右001→002→003 | 一致；四张 `mac-cbz-*.jpg` |

排序/完整历史书签导航不在本轮已通过项目。曾分别使用 `/private/var/…` 和 `/var/…` 打开同一来源，历史中存在两条路径；显式打开某图时恢复另一条路径的设置，不能据此断言方向或恢复规则错误。该路径别名归一尚未单独设计/验收。误操作截图按实际结果命名并保留，未纳入一致性证明。

## 两处差异与修复

**数字菜单提示。** Avalonia `KeyGesture.Parse("2")` 将数字当作 Key 枚举值，提示成 Back。菜单与 MainWindow 改用同一个 `KeyboardGestureParser`，0–9 转为 D0–D9，Control 仍为 Control，Command 别名转为 Meta。真机 AX 显示正确的 ⌃+1/⌃+2，实际输入可切单/双页；Headless 还验证真实 KeyEvent、菜单 ClickEvent、Back 不匹配及无效/鼠标绑定。独立 Avalonia 弹出菜单无法由当前 CUA 稳定观测，因此不把 Headless ClickEvent 当真机菜单点击通过。

**半页持久化。** 修复前 Mac 停003(L)后切书回来仍003(L)；Windows 明确重开返回右开003(R)。原 BookMemento 只保存条目名，额外 MacPagePart 导致差异。收回该字段的读取/写入，Find/GetLastBook 直接返回 BookMemento，普通打开从 part0 恢复；当前翻页、方向及反向页尾 part1 保持。

修复后真机确认：带旧记录启动→003(R)；停003(L)切书回来→003(R)；再次停003(L)正常退出/启动→003(R)。当前记录及 LastBookV2 不写 MacPagePart，其他未更新历史的旧字段留存但不读取。对应 `mac-restart-half-page-fixed-003R.jpg`、`mac-half-page-before-switch-fixed-003L.jpg`、`mac-switch-back-half-page-fixed-003R.jpg`、`mac-before-restart-combined-and-half003L.jpg`、`mac-restart-combined-ratio-and-half003R.jpg`。

目录/ZIP × 左右阅读方向四组合验证真实页框裁剪、切书及重启。同书 LastBookV2 未知嵌套字段与 Props 保留，不转移到下一本书；反向页尾跨书仍进入末半页，分别补充自动回归。

## 左右栏拖拽

| 真机操作 | 结果/证据 |
|---|---|
| 左页面列表图标→右信息底部 | 垂直组合；`mac-dock-cross-side-vertical.jpg` |
| 调整组分隔条 | 信息约1/3、页面约2/3；`mac-dock-ratio.jpg` |
| 正常重启 | 组合、选择及比例恢复；`mac-restart-combined-ratio-and-half003R.jpg`；落盘为 `Vertical:FileInformationPanel,PageListPanel`，GridLength 约0.333816/0.666184 |
| 成员标题→左图标栏 | 成员拆出；`mac-dock-member-split-to-left-rail.jpg` |
| 左页面列表→右信息左侧 | 水平组合；`mac-dock-cross-side-horizontal.jpg` |
| 组 leader 图标→左栏 | 整组移动，水平关系保持；`mac-dock-leader-moves-whole-group.jpg` |

跨栏水平/垂直组合及比例变化与 Windows 样本一致。Mac 成员拆组、整组移动和组合状态重启属于 Mac 真机通过；Windows 未采集整组移动及仍组合时重启，不能将这三项列为跨端动态对照通过。

浮动/停靠、自动隐藏本轮未完成 Mac 动态复演。右击标题后弹出层不在当前主窗 AX/截图中，后续 Down/Return 回到书架并误开 Retina 目录；已停止盲试。早先自动测试继续有效，但本轮菜单/浮动真机结果待验。

## Retina 与剩余设备项

800×600 的8像素棋盘格在导航器100%下占 CUA 图像约800×600像素，与适合窗口明显不同（`mac-retina-100-checker-8px.jpg`、`mac-retina-fit-checker-8px.jpg`）。1像素样本也留证。JPEG 有损编码及 CUA 捕获链不能证明无损设备像素1:1，未直接观测实际 RenderScaling，故仅为预检。

真实触控板滚动/惯性/捏合、无损 Retina 映射、30–60分钟持续真实浏览/原生内存、多屏及更多原版菜单/历史/书签/焦点条件仍待验。多屏没有环境；其他需按用户可用时段安排。后台采样器预检不能继承为长期 NeeView 资源通过。Windows 参考版本与固定源码的对应关系仍未证明。

最终状态：本节点构建、375项自动回归、本地签名、上述正式运行及配置还原分别通过；P2 开发范围完成，整体验收未封板。自动本地提交，不推送、公证或发布。

## 后续设备资源节点（2026-10-04）

以上是本记录原375项节点的历史范围。后续已完成[设备资源节点](p2-device-resources-runtime.md)：补充Windows/Mac名称排序对照，修复页面列表原地排序未回报问题；最终376项自动回归、正式构建与本地签名通过，真机升降序和隐藏/一次显示命令子集通过。375项诊断构建的Retina运行时RenderScaling/100%比例已直接观测，无损像素采样继续待验；30分钟间歇浏览完成，缓存/句柄/关闭释放子集通过，RSS稳定性仍待复测。触控板按用户要求跳过，未验；多屏无环境。两个Mac测试进程退出，原四文件Profile再次完整还原并校验；Windows测试副本关闭、原实例保留。新证据见[清单](p2-device-resources-evidence.json)，不回填成此前构建已经执行这些项目。
