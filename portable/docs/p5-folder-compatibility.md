# P5 第四批：旧目录、快速访问与差分参数

## 职责、依赖与出处

沿固定 `c5c398d89` 的 `FolderConfigCollectionValidator`、`FolderConfigUnit.Validate`、`Restore(Dictionary)`、`QuickAccessCollectionValidator` 和 `QueryPath.GetParent/SimplePath` 适配原 JSON 候选。出处及 SHA256 在 [源码清单](source-migration.json)。原 Windows 文件不修改；三个生产项目、唯一 SaveData 和原 JSON 权威链不变。

`LegacyFolderConfigUpgrade` 只处理目录 JSON 与逻辑父路径。视图、表现模型、后端没有新的解析职责；目录升级不扫描文件系统，不猜测旧路径的实际位置，也不改变 Config.Current。

## 契约与状态生命周期

只读来源 → 文件版本检查 → 独立目录递归升级 → 已知路径映射 → 预览/确认副本 → 原五文件候选 → 按选项准备最终配置 → 默认排序归一 → 集合验证 → 备份及原事务。验证与应用共用 `PrepareImportAsync`，未选择的文件保持原字节。

独立目录与快速访问、History/Bookmark 的来源版本保存在 `MacImportedSourceFormat`。`MacImportedFolderOrderNormalization` 仅是候选中待归一标记：最终配置确定后消费，在实际写入前移除。实际文件是46.3格式；再导入已应用文件不重新执行旧版本升级。未知节点、缩略配置和随机种子仍保留。

## 原业务规则

| 来源/版本 | 处理 |
|---|---|
| 独立 Foldres ≤46.0.4065 | 空 Thumbs 归为 null；按最终配置对应的普通/书签/播放列表默认排序清除重复 FolderOrder |
| 独立 Foldres ≤46.0.4209 | 按原数组顺序逐条修改参数；当前缺省为 false；查询任一明确 true 的祖先；当前与祖先默认一致或为 false 时清为 null |
| 独立 Foldres >46.0.4209 | 保留明确 false，不套用旧转换特例 |
| History.Folders 旧字典 | 原 Restore(Dictionary) 只将路径键转换为 Place/Parameter 并归一排序；**不执行独立文件递归 validator**，明确 false 保持 |
| Bookmark.QuickAccess | 保留内嵌 Format，作为快速访问单独校验；仅缺失字段按来源版本补值，明确 null/未来/错误类型不能被外层升级掩盖 |
| 同时有独立与内嵌文件 | 原独立文件优先，包括独立文件为空；不自动以旧内嵌数据替换失败或未支持的独立文件 |

父路径匹配保持原 Ordinal 与 SimplePath：盘符根、UNC、虚拟逻辑路径和 search 后缀在映射前处理，Mac 路径使用对应本地父级规则。升级查询采用索引指向原节点，保留原 FirstOrDefault 与逐项修改语义；最近 false 祖先不截断**旧 validator**的 true 搜索。当前运行时继承仍以最近显式 true/false 为准，两者不能混为同一规则。

默认排序使用本次候选 UserSetting；没选择来源设置则使用当前磁盘配置，磁盘缺失用默认实例，预览不能借用 Config.Current。书签和 `.nvpls` 分别使用原分类默认。绝对 Windows 缩略目标同样按最长前缀映射；相对归档页名、名称键、未知非字符串缩略材料保持。

## 差分编辑修复与明确边界

原 ScrollPage 的 `IsNScroll`、`PageMoveMargin` 是兼容 setter，正常写出省略。原 JSON 递归 Merge 曾使它们留在新参数中，重启时覆盖用户刚编辑的 ScrollType/停顿。现在编辑时将这两个原字段移至命令的 `MacImportedLegacyParameterFields`，仅新参数参与恢复；未知参数、快捷键和命令元数据保持。缺省/空字符串、命令共享 owner、实例默认索引沿既有实现。

本批没有引入整套 DiffJsonConverter，也没有把“全量配置写出”标为“原差分序列化已完成”。完整原 Config/参数差分写出、旧效果层/预设、附属主题/播放列表/脚本和完整设置/高级能力仍需后续迁入。显式 `SelectedItem:null` 清除旧选择的 Mac 合并约定继续保持。

## 支持范围、错误与资源

- UserSetting 38–46.3，38非零字体尺寸仍受来源 Windows MessageFontSize 门槛限制。
- History/Bookmark/原 QuickAccess 树格式44–46.3；真实旧导出仍待验。
- 独立 Foldres 数组格式46.0–46.3；46前独立文件只预览。旧目录字典通过已支持 History 后备恢复。
- build >4340、非零第四段、未知/未来版本及错误 Format 保持只预览；独立和内嵌格式校验各自生效。

错误数组、Place、参数、映射重复目录在原备份及写入前拒绝；取消、原五文件失败恢复与窗口重建沿 [第二批契约](p5-profile-apply.md)。本批仅持有有界 JSON 候选和轻量索引，不打开图像流、网络连接或真实用户 Profile。

## 测试、验收与扩展

精准覆盖4065/4209边界、false/缺省、原列表顺序、盘符/UNC/虚拟/Mac路径、大小写与边界、来源配置选项、三类别默认、独立优先、内嵌未来版本、未知字段、缩略目标、普通保存重载、备份恢复及旧滚动参数编辑。全部写入隔离合成 Profile，后台 Headless 不激活桌面应用。

[本批静默验收](../acceptance/p5-folder-compatibility-runtime.md)分别记录自动回归、正式构建、本地签名与未执行验收。用户两分支真实导出、真机/Windows动态及正式分发不能从合成测试继承。后续完整差分仍以原 Config/Command 与 SaveData 为契约，不新增状态框架。
