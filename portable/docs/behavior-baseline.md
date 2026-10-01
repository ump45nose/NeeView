# 原版行为对照

| 能力 | 原基线出处 | 迁移方式 | 验收 |
|---|---|---|---|
| 半页位置/有向范围 | `NeeView/Book/PagePosition.cs`、`PageRange.cs`；`NeeView.UnitTest/PagePositions.cs` | 纯值模型；覆盖负位置和有向范围 | 自动测试；Windows 对照待运行 |
| 分割仅单页 | `NeeView/PageFrames/PageFrameContext.cs:116`、`PageFrameFactory.cs:146` | Core ReadingRules | 自动测试 |
| 横向独占、首尾单页 | `NeeView/PageFrames/PageFrameFactory.cs:170` | frame 组合规则 | 自动测试；实际 Windows 对照待运行 |
| 字段设置恢复 | `NeeView/BookSetting/BookSettingPolicyConfigExtensions.cs` | 按字段 Default/Continue/Restore 策略 | 自动测试 |
| 自然排序 | `NeeView/Book/BookPageSort.cs:12` | 自然数字比较，重排保持 ContentId | 自动测试 |
| 九数字命令/固定移动 | `NeeView/Command/Commands/MoveToFolderAsCommand.cs:25` | 稳定命令名与模式分离 | 自动测试 |
| 分类双区、只换目录刷新 | `NeeView/SidePanels/DestinationFolder/DestinationFolderPanelViewModel.cs:121` | 应用服务独立目录集合 | 自动测试；最新拆分界面待解锁复测 |
| 历史成功才入栈 | `NeeView/DestinationFolder/DestinationMoveService.cs:142` | 文件协调和进程内栈 | 集成测试 |
| 移动后推进 | `NeeView/BookOperation/BookPageActionControl.cs:268` | 原版依赖来源更新；迁移显式按方案推进 | 集成测试 |
| History/Bookmark/Props | `NeeView/BookHub/BookHistoryCollection.cs`、`NeeView/Bookamrk/BookmarkCollection.cs`、`BookMemento.cs` | 独立 DTO 和树解析，Page 作为 entry 字符串 | 导入夹具测试 |
| 差分快捷键 | `NeeView/Command/CommandTable.cs:881`、`CommandElement.cs:626` | Windows 固定默认值/输入方案补齐；null 与空串分离；Control 保留 | 自动测试 |
| 特殊拼写 | `NeeView/SaveData/SaveDataProfile.cs:8` | 识别 Foldres.json、QuicAccess.json | 自动测试 |

本表引用的是合并后的固定 Windows 源码，不用静态比较代替 Windows 构建和分类运行回归。

补充：PageSortMode 的 Windows 数值顺序固定为 FileName、FileNameDescending、FileType、FileTypeDescending、TimeStamp、TimeStampDescending、Size、SizeDescending、Entry、EntryDescending、Random；不能按 portable SortMode 序号转换。LegacyCompatibilityTests 固定此契约。

原命令默认来源：各 Command/Commands 构造器；CommandElement.CreateMemento/Restore 的 null 代表默认，空串禁用；CommandTable.CreateDefaultMemento 先应用输入方案和方向，再应用差分。九分类参数 Index 是 1 基。
