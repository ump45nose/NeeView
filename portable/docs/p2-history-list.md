# P2 第六批：原历史列表导航与管理

日期：2026-10-03。本批保留 HistoryList、BookHistoryCollection 及原命令的职责，扩充现有 JSON/BookOperation 适配，不新增持久化模型或通用集合框架。P2 尚未整体完成。

## 职责与依赖

Engine.HistoryList 计算只读过滤列表、前后目标及日期分组名；SaveData 修改原 History.Items 并提交现有三 JSON 事务；BookOperation 负责原加载链与位置恢复。Mac 的 HistoryRow、XAML 和 MainWindow.History 只处理展示、焦点、指针及菜单。历史面板刷新不发布正文 Refreshed，不枚举目录或申请图像。结构、资源和主题可以独立调整。

## 契约、状态与资源生命周期

- GetViewItems 按原访问时间倒序，IsCurrentFolder 仅匹配当前书籍的直接父目录，不递归；无当前书时显示全部。原路径子串子集已由第十二批替换为名称/Date/Size/Book结构化查询，见[p2-history-search.md](p2-history-search.md)。
- GetTarget(-1) 是较旧项，GetTarget(1) 是较新项；当前书不在筛选序列时，后退选择首项、前进无目标，首尾不循环。目标使用原 Path，不按数组下标持久化。
- OpenHistoryAsync 使用 KeepHistoryOrder/SkipSamePlace。当前书不重复加载；成功恢复原 Page 条目与 Props；失败保留旧书，目标仍可重试。PrevHistory/NextHistory 使用此过滤序列，不替代 PrevHistoryPage/NextHistoryPage 和 PrevBookHistory/NextBookHistory 的进程游标。
- RemoveHistoryAsync 对选中路径精确比较，多选先复制批次；ClearHistoryAsync 清空全部访问条目，不受显示过滤限制。源图片、压缩包、书签、搜索历史和未知根字段保留。
- 原集合的 Remove/Clear 与位置更新分离。适配中使用进程内登记抑制，防止当前书翻页、防抖、切书和退出保存把已移除历史加回；成功显式重新打开该路径开始新访问并解除抑制。首次打开尚无防抖记录时清空也适用。不新增删除恢复栈，不持久化抑制集合。
- 启动/无窗口重开沿原 FirstLoader → BookHub 显式传入完整 LastBookV2，页位置、阅读设置与排序种子优先于缺失/过期的历史及普通字段恢复策略。共用原 Props 解析，独立保存 MacPagePart/MacIsSupportedWidePage 补值；自动恢复属于新访问，进程内抑制不跨启动保留。正式运行发现的“删除历史后重启落到首图”已修复，缺失/过期历史两项正式视图回归及最终 Mac 空历史重启通过。
- 编辑与阅读保存共用 SaveData gate 和原三文件事务。准备/提交失败恢复 JSON、登记抑制和原展示状态；取消不提交。保存回报只通知导航表现，关闭期间拒绝界面动作/晚到菜单刷新，保存失败可重试。

## 原业务与表现适配

| 行为 | 原出处 | Mac 适配 |
|---|---|---|
| 当前目录、前后列表目标 | SidePanels/History/HistoryList.Filter/CanPrevHistory/PrevHistory/CanNextHistory/NextHistory | 保留原直接父目录和目标顺序，WPF CollectionView 改为只读投影 |
| 日期分组 | HistoryListGroupDescription | 今天/昨天/当地完整日期；首条真实行附分组标题，不创建伪书籍 |
| 条目打开 | HistoryListBoxViewModel.Load | 原 KeepHistoryOrder/SkipSamePlace；目标进入同一 BookOperation |
| 单击/双击、键盘、上下文 | HistoryListBox.xaml.cs | Panels.OpenWithDoubleClick 原默认 false；无修饰单击同一行释放打开，双击模式才响应双击；Enter 打开、Delete 移除；搜索文本隔离 |
| Remove/Clear | BookHistory/BookHistoryCollection | 精确路径批次、真实成功才更新；保留未知字段及书签 |
| 更多菜单 | HistoryListViewModel.HistoryListMoreMenuDescription | 四种样式、分组、当前目录、数量/搜索开关、无效清理与全部清空入口；未支持项禁用占位 |
| 清空确认 | HistoryListViewModel.RemoveAll / ClearHistoryCommand | 面板菜单先确认、取消不写；原 ClearHistory 命令直接清空，加载中不执行 |
| 搜索焦点 | FocusHistorySearchBox / SidePanelFrame | 明确显示历史面板并聚焦，已显示时不切换关闭 |

History 配置仅迁入 IsGroupBy/IsCurrentFolder/IsVisibleItemsCount/IsVisibleSearchBox 四字段。显示样式和其他保存策略仍保留；数量/保留期限由第十三批按原文件/载入边界接入，见[p2-history-retention.md](p2-history-retention.md)。不能把未知字段往返当作相应策略已执行。列表稳定行复用保留未变化的选择；过滤后不再显示的项不作为隐藏删除目标。右击已有多选成员保留批次，右击其他行选择该行，右击空白清空旧选择并禁用菜单。

## 错误与验证

专项测试覆盖：直接/嵌套目录、过滤缺失当前书与首尾、日期、数组/时间/位置恢复、当前书跳过、坏来源/重试、源文件与书签独立、未知字段、删除抑制/显式重新登记、清空前首次防抖、取消/失败回滚、退出重载、正式 XAML 菜单开关/确认取消、稳定多选/Delete/文本隔离、实际单/双击及右击多选/空白。全量构建、自动测试、正式应用、本地签名分别记录在 ../acceptance/p2-history-validation.json。

真机证据单独见 ../acceptance/p2-history-macos-runtime.md。Windows 动态对照、真人鼠标/触控板/IME、NAS、长期 native 内存和完整显示 P95 不由 Headless 证明。

## 未迁移与扩展点

无效历史清理暂不启用，不能用 File.Exists 的 false 将断线 NAS/权限问题误判为应删除记录；后续接入原 ArchiveEntryUtility 的可靠存在检查与有界后台任务。ClearHistoryInPlace 依赖书架查询位置，继续占位。结构化搜索/搜索历史由第十二批接入；四种显示模板/缩略图、其余保存/登记策略、自动清理和动态日期跨日回报后续迁入。页标记依赖播放列表，书签查询与书架互联、直接页号文本框（已有 JumpPage 对话框）、真实子书/父书、完整输入及自动隐藏仍是后续目标。
