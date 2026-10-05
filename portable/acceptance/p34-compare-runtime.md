# P3/P4 后续原版对照与浮窗验证

2026-10-05，正式 Mac 运行版本 `51b8aab4d`，Windows 固定源码 `c5c398d89a9dd872382980769065e80c4592f4c2`。本批先登记既有长期内存问题，再完成固定 Windows 副本构建及阅读、分类动态子集，与新增 Mac 阅读样本对照；没有修改产品源码、替换框架二进制或重跑已经失败的长期负载。**P3/P4 未封板。**

匿名文件清单、SHA256、用例边界与恢复回执见[证据清单](p34-compare-evidence.json)。原始截图、AX、测试 Profile 和远端日志不提交仓库。

## Mac 真机子集

单个 Retina 显示器，隔离原四文件 Profile，使用九图合成目录及对应 CBZ。历史浮窗起始状态通过原 JSON 的 `Windows.Panels` / `WindowPlacement` 设置，本项不证明真实右键“浮动”入口通过。

- 文本焦点：历史浮窗搜索输入 `01` 后按 Left，正文仍为 `001.png` / `1 / 9`。
- 列表焦点：Tab 进入历史列表，Down / Left 后正文仍为第一页。
- Enter：主窗口先打开 CBZ；历史浮窗选中目录后 Enter，主窗口地址实际回到目录，仍为第一页。
- Command+W：只关闭历史浮窗，主阅读窗和书籍保留。
- 重开：点击主窗口历史侧栏入口重新出现浮窗，搜索 `01` 及选中目录保留。
- 正常退出/重启：仍打开的历史浮窗恢复，原历史项保留；不要求跨进程保留未持久化搜索文本。
- 尺寸恢复失败：三次真实 PNG 宽度由 720 → 1440 → 2880 像素，登记为 [MAC-FLOAT-002](../docs/known-issues.md#mac-float-002retina-浮窗关闭重开及重启后尺寸持续放大)。打开集合恢复通过不等于尺寸恢复通过。

部分坐标/右键返回 `AXError.notImplemented`；菜单调用无错误却未见实际命令结果的尝试也不计通过。原生弹出菜单可由键盘出现于 AX 树，但本次没有完成稳定的菜单命令、停靠/拖回、hover/popup 自动隐藏回路。因此上述范围继续待验，不能将工具错误直接认定为产品缺陷。以下阅读样本用隔离 Profile 中的临时 F 键绑定触发真实业务，不代表原默认键位和菜单入口全部通过。

### Mac 阅读动态子集

| 用例 | 真实结果与对照边界 |
|---|---|
| RTL 页组与单页步进 | 初始左 `002` / 右 `001`；F6 下一页组左 `004` / 右 `003`；F8 返回首组；F7 下一单页左 `003` / 右 `002`，与固定 Windows 相同 |
| RTL 分割页 | `003` 右半 → 左半 → 完整 `004`，与 Windows 相同 |
| LTR 分割页及键位反转 | `003` 左半 → 右半 → 完整 `004`；开启原配对反转后 F8 推进、F6 后退，与 Windows 相同。方向切换保留当前运行时 Part，不能把瞬间保持的半页当新起点 |
| 排序保持内容 | 降序后仍是 `004`、`6 / 9`，与 Windows 相同 |
| 正常退出与启动恢复 | History 保存 `Page=004.png`；无参数启动从显式 LastBookV2 恢复 `004`、单页 LTR、分割及降序。Windows 重启确认 `004` 锚点；双方同入口的完整字段恢复策略仍未动态逐项对照 |
| 同路径普通打开 | 地址栏 Enter 按字段策略恢复双页 RTL、升序，仍定位 `004`、`4 / 9`。这与显式启动快照是两种入口，结果不同不登记为缺陷 |
| 首页、末页与宽页 | 配置种子启用首/末单页、宽页：`001` 和 `009` 单独，`003` 宽图独占；真实滑条点击定位末页。证明配置读取与规则，不证明菜单勾选入口 |
| 末页前的组合 | Mac 种子 `wide=true` 时 `008` 独占；Windows 该样本 `wide=false` 时 `007/008` 双页。两种设置组合分别记录，不作为同配置一致性证明 |
| 半页显示提示 | 图像切换正确，但 Mac 两半均 `3 / 9`、标题只显示书名；Windows 标题显示 `3(R)/9`、`3(L)/9`，登记 [MAC-READ-003](../docs/known-issues.md#mac-read-003分割页缺少原版标题中的-lr-提示) |

Mac AX `slider.setValue` 的尝试只改变控件值，没有提交正文导航；随后用实际点击验证末页。`mac-slider-ax-value-only`、`mac-slider-uncommitted-prev`、`mac-menu-action-unverified` 均明确排除通过证据。部分 AX 文件是差分，半页截图承担实际图像变化的核验。

## 固定源码静态对照

| 能力 | 固定 Windows 出处 | Mac 出处及判断 |
|---|---|---|
| 半页位置编码、范围前后边界 | `NeeView/Book/PagePosition.cs`、`PageRange.cs` | Engine 同名迁入文件，关键实现一致，原位置测试保留 |
| 下一页组 / 下一单页 | `BookPageMoveControl`、`PageFrameBox.MoveToNextPage` | `BookOperation.MoveAsync` 的范围推进与 `onePage` 分支保持区别 |
| 原历史页面 | `Book.CreateMemento`、`BookMemento.Page` | 只保存条目名；运行时 Part 不另写 MacPagePart。不能由 JSON 字段推导完整动态恢复已验 |
| 排序保持当前内容 | 原 memento 条目名定位 | Mac 原 Page 对象重新找索引，保留运行时 Part；现有排序/渐进索引测试覆盖 |
| Once / All / AllLeftToRight | `BookPageActionControl.CollectPages:312–336` | 当前页组 Distinct、Once 首项、RTL 时 AllLeftToRight 反转，普通分页场景一致 |
| 复制与移动历史 | 原 `TryCopyAsync` / `DestinationMoveService.TryMoveAsync` | Copy 释放临时结果，不入移动双栈；Move 实际成功后入栈 |
| 批量移动撤销粒度 | 原成功移动逐文件登记 | `DestinationMultiPageTests.BatchMoveUsesCapturedGroupAndRecordsEachActualSuccess:65–80`：两文件成功后 `UndoCount=2`，一次只恢复最后成功文件；本轮 Windows 动态也确认该粒度 |
| 阅读方向配对反转 | `CommandTools.ResolveCommand:156–157` | `DefaultInputScheme.ResolveCommand:29–32`；`IsReversePageMove` 开启且滑条/预设方向不同时交换配对命令，两端 LTR 动态一致 |

路径去重不能直接套 Windows 忽略大小写：固定 Windows 移动服务按 `OrdinalIgnoreCase`，Mac 移动前按完整路径 `Ordinal`。尚未取得同一 Mac 实体的不同大小写别名重复提交样本，保留为文件系统语义核验缺口，不登记为已经复现的缺陷。瀑布显式选择与原分页 CurrentPages 的取目标差异是已采用的交互边界。

## 自动回归

使用原默认测试输出、`--no-build` 串行运行：原位置、分页分类、浮窗、文件剪贴板、目录/归档复制、页面搜索、选择历史及渐进索引，**163 通过 / 0 失败 / 0 跳过**。这是已有测试二进制的针对性回归；本批没有重新构建。Headless 截图独立放在本机 `headless` 目录，不充当原生菜单/屏幕验收。

## Windows 固定副本构建与动态子集

现有安装版不替代固定基线。通过 RDP 会话准备独立临时 checkout，固定提交及四个 submodule、工作区 clean；官方 .NET 10.0.401 x64 SDK 的 SHA512 核验后，在原默认 bin/obj/publish 完成 restore/publish。原安装目录和 Profile 不参与构建。

- 构建 EXE SHA256：`3BCFBF2EA68A06AD45F6E23C926107DFF7175145E2E2A4C718962B2788AFE28A`。
- ProductVersion：`46.3+c5c398d89a9dd872382980769065e80c4592f4c2.c5c398d89a9dd872382980769065e80c4592f4c2`。
- 初始 `receipt.json` 是 `built_not_run` 的构建时快照；之后实际启动了该 EXE，独立 Profile 完成动态子集，最终另写 `dynamic-receipt.json=dynamic_subset_verified`，不能继续报告尚未运行。
- 九图 CBZ 的 Mac/Windows SHA256 一致：`614f935568339eac6bcc80f624598f25268e610a9e1e7642e83e82ec2ab242c4`。

| 用例 | 固定 Windows 实际结果 |
|---|---|
| RTL 页组 / 单页、宽图 | 左 `002` / 右 `001` → 左 `004` / 右 `003`；单页步进得到左 `003` / 右 `002`；宽图 `003` 独占 |
| 分割页两方向 | RTL 右 → 左 → `004`；LTR 左 → 右 → `004`。LTR 时原配对键位反转：F6 后退、F8 推进 |
| 排序及重启 | 降序仍 `004`、`6 / 9`；退出 History.Page 为 `004.png`，重启仍 `004` |
| 首页 / 末页单独 | `001`、`009` 单独；该边界样本 `wide=false`，末页前组为 `007/008` |
| 数字 1/2/3 Copy | Once 复制 `001`；All、AllLeftToRight 都复制 `001/002`，五份目标哈希匹配，源九张、当前页不变、Undo 禁用。最终文件集合不证明 AllLeftToRight 的执行顺序，顺序依据静态与自动回归 |
| Once Move / Undo / Redo | 移走 `003` 后显示 `004/005`；Undo 恢复并定位 `003`，Redo 再移，随后 Undo 恢复 |
| All Move / 清 Redo / 逐文件 Undo | 新移动 `003/004` 清除已有 Redo；第一次 Undo 恢复 `004`，第二次恢复 `003` |
| 冲突取消 | 对已存在的 `001` 到达系统冲突并点 X 取消，当前页和历史栈保持；本轮没有执行 Windows 覆盖，不能写覆盖通过 |

Windows 文件操作子集与前批 Mac 真机结果及本批静态/自动回归交叉核验，不代表本轮在 Mac 又逐项重跑，也不外推整书操作、删除及所有失败组合均已完成固定 Windows 动态对照。

## 恢复与剩余范围

Windows 测试副本已正常退出，原安装实例保留；源九张经真实 Undo 恢复，逐图 SHA256 与 CBZ 九条目匹配 **9/9**。三组复制结果保留在独立临时验收目录，不属于原用户图片。PowerShell 的 `NEEVIEW_PROFILE`、`DOTNET_ROOT_X64` 两个临时环境变量已恢复，LAN 夹具服务已停止。RDP 键盘模式已点击切回扫描码，但没有带勾选截图，不能把操作后画面当设置状态证明。

Mac 最终测试进程正常退出；正式 Profile 的四文件路径集合、类型、权限、大小、SHA256 及根权限与原始快照完全匹配，测试 Profile 完整保留到本机 artifacts，`profile_isolated=false`。用户指定图片根未写入。

三个未解决问题为 [MAC-AX-001](../docs/known-issues.md#mac-ax-001macos-无障碍查询持续保留回调数组)、[MAC-FLOAT-002](../docs/known-issues.md#mac-float-002retina-浮窗关闭重开及重启后尺寸持续放大)、[MAC-READ-003](../docs/known-issues.md#mac-read-003分割页缺少原版标题中的-lr-提示)。完整停靠/弹出层、ZIP 真实永久删除、真 NAS 断线、屏幕 P95、Finder alias 独立真机仍待验；纯 HTML、file-promise-only 未因此验证。触控板按用户要求跳过，多屏无环境。构建、子集通过与配置恢复不解除 P3/P4 的剩余验收条件。
