# P5 第五批：原配置与命令差分保存

本文件保留对应批次的交付范围。当前旧效果层/缓存/预设纯数据已由[第六批](p5-effect-compatibility.md)接入；实际执行继续待迁，差分保存的当前范围见[第五批](p5-difference-settings.md)。

## 职责、依赖与源码出处

迁入固定 `c5c398d89` 的 `DiffJsonConverter<T>`：公开可读写属性、`Equals`、`DiffJsonDefault/IDefaultable`、原 JSON 名称及默认实例规则保持。只增加属性比较结果的访问入口，并排除 `JsonExtensionData`，避免将 Mac 的 `Extra` 字典写成普通字段。原源码和 SHA256 记录在 [源码清单](source-migration.json)。

`SaveData.Difference` 是唯一 SaveData 的写出适配。没有第二状态存储、数据库、生产项目或界面解析入口。原 `UserSettingTools.CreateUserSetting`、`CommandElement.CreateMemento` 和 `JsonCommandParameterConverter` 是配置/命令语义参考；未迁移原分支继续保持原始 JSON，不宣称全 Windows schema 已转换。

## 契约与状态/资源生命周期

普通保存先将已提交的类型值合并到原 `_setting`；`CreateSettingMemento` 再从该节点建立独立配置投影和 JSON 副本，按原默认裁剪副本，交给原五文件临时写入、备份、marker 和恢复事务。运行节点不因裁剪被修改。

副本不能使用尚未提交的 `Config.Current`：历史数量/期限候选和仅修改搜索历史等入口必须使用本次准备的设置。失败保留旧文件与运行节点，原表单草稿可重试。元数据按类型缓存；不持有窗口、图像资源、流或每书对象。

## 默认比较与未知字段

| 对象 | 写出规则 |
|---|---|
| 已迁入的 Config 分支 | 默认标量省略；递归处理已迁配置对象；未知顶层、分支和嵌套键保持 |
| 四种列表模板 | 默认实例来自 `new PanelsConfig()`，沿原 Normal/Content/Banner/Thumbnail 的尺寸及形状；不使用统一空 Profile 默认 |
| 可空对象 | 明确对象不被误当成 null；既有完整布局快照保留 |
| 已知字段大小写 | 沿现有读取的大小写兼容收归原 JSON 字段；未来键和动态字典继续精确比较，不将它们统一转小写 |
| 目标目录数组 | 原集合 SequenceEqual 默认判断；条目 extension data 随类型编辑和 Clone 保留 |
| 布局 | 保留完整 `Panels.Layout` 快照，包括关闭所需的 `SelectedItem:null`、Docks/Panels 动态键和未知窗口材料；不套通用对象裁剪 |
| 未迁移原设置 | 原 JSON 保持；保存不把这些字段静默删除，也不把它们列为已支持 |

嵌套已知字段输出实际 getter 值。旧 setter 可能覆盖同节点已有的新字段，不能只删除旧字段而不保存实际转换结果。

## 命令差分

- 快捷键及鼠标手势按本次配置的输入方案/阅读方向判断默认。空字符串仍表示明确解绑；已知默认空命令省略，全部为空时省略 Commands。
- 235 个原实例在既有命令清单补充 TouchGesture/IsShowMessage 默认元数据。触摸区域不随方案/阅读方向交换；本批保留数据，不新增触摸执行或通知能力。
- 元数据核对实际构造器链：PrevPlaylist 继承 NextPlaylist 的通知默认；NextEffectProfile/PrevEffectProfile 来自 MoveEffectProfile。不能将装配 Source 文件中的缺省赋值当成这三个实例的默认。
- 已迁参数只写原 owner。早期 Mac alias 在 owner 缺失或 null 时承接到 owner；非空 owner 优先。alias 原材料保存在 `MacImportedSharedParameter`，不参与执行。
- 九个数字目录的默认 Index 分别为1–9；用户明确选择0仍是自定义差分。
- 非默认参数沿原 `$type` 首字段，名称去掉 `CommandParameter` 后缀。只有 `$type` 的已知默认参数整体省略；含未来字段时保留。未来 discriminator 和未迁参数不按已知类型重写。
- 原 memento 的 null 字段省略；未来空命令与未知字段保留。

## 旧字段退役与错误

ScrollPage 的 `IsNScroll/PageMoveMargin` 先转换为实际 ScrollType/停顿值，再移到 `MacImportedLegacyParameterFields`。旧自动隐藏 HitTestMargin/Confrict 拼写字段移到根兼容材料 `MacImportedLegacyConfigFields.AutoHide`，防止新字段恢复默认后被旧读取别名再次覆盖。兼容材料是原 JSON 的非执行扩展，没有新的权威配置。

损坏类型仍由现有解析/事务报错，原文件不被替换。未知字段与未来类型保持，不猜测其行为。多进程共写仍未支持。

## 测试、验收与扩展

隔离合成 Profile 覆盖：全已迁配置默认恢复、修改后恢复默认、四模板默认、六种输入方案/方向组合、九数字 Index、owner/alias 顺序及 null 后备、未来类型、旧 setter 与现代字段冲突、自动隐藏别名、未知数组/嵌套键、闭合布局重启、保存失败重试、准备候选和再次导入验证。

旧断言改为默认字段省略后仍恢复相同行为；没有放宽未知字段或阅读语义断言。[静默验收](../acceptance/p5-difference-settings-runtime.md)分别记录构建、自动回归、设备/Windows边界和发布状态。

旧效果层/预设、附属主题/播放列表/脚本、完整设置和高级格式继续按原源码及既有导入契约接入。真实两分支导出未验；本批不启用脚本，不执行真机或 Windows 动态对照。
