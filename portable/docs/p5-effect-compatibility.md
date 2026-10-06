# P5 第六批：旧效果层、缓存与预设数据

## 职责、依赖与出处

`LegacyImageEffectUpgrade` 在既有 `LegacyUserSettingUpgrade` / `ProfileImportCompatibility` 的唯一入口转换导入副本。固定 `c5c398d89` 的 `UserSettingValidator` Alpha.5、`ImageEffectConfig`、`EffectUnitCache`、`EffectProfile.Store` 和各旧参数 setter 是来源；指纹见[源码清单](source-migration.json)。这是原 JSON 的局部数据适配，不是 WPF 效果执行类的整体迁入。

本批完成效果数据转换和导入往返。实际效果解码、渲染、编辑和命令尚未接通，原面板/命令继续占位。仍只有三个生产项目，不增加平台依赖、数据库、渲染宿主或第二配置体系。

## 版本与契约

- 原版本 `≤46.0.4209` 按 Alpha.5 转换；`46.0.4210+` 的现代数据不按旧单效果解释。
- 前几批 Mac 保存的 `MacImportedLegacyEffectFormat` 可触发继续转换。当前已有现代 Layers 时保持现代数据；已有升级标记（包括未来版本）不重复或降级转换。
- 原 Format 仍沿唯一版本升级链更新。状态标记是兼容报告材料，不是可执行能力标记。
- 预览、验证和应用共用候选；原五文件事务、备份、窗口关闭/重建和失败恢复不变。

## 数据规则

沿原顺序：旧参数 setter → 本地缓存排除默认实例 → 原 EffectType 生成唯一首层 → Store 默认首项预设 → 非默认参数加入全局缓存。

| 数据 | 规则 |
|---|---|
| ImageEffect | 未知顶层字段保持；缺省 IsEnabled=false；首层 IsEnabled=true；None 的 Effect 为 null |
| 类型 | 原0–14枚举顺序；短 `$type` 为类名去 EffectUnit 后缀并置首字段 |
| 13个旧参数 | Level/Hsv/ColorSelect/Blur/Bloom/Monochrome/ColorTone/Sharpen/Embossed/Pixelate/Magnify/Ripple/Swirl；原无 ColorizeEffect setter，选择 Colorize 时创建默认类型 |
| 数值 | 原数字/Point五位ToEven舍入；Level JSON BlackRaw/WhiteRaw不舍入、不联动Center；Bloom Threshold只有上限1，不增加下限/范围裁剪 |
| Color / Point | 原invariant字符串；十六进制和非系统命名颜色规范为ARGB，Point逐坐标舍入；不引入UI值类型 |
| 本地缓存 | JSON输入顺序；默认实例删除本地该类型，null setter不改变缓存；未知参数键保留，含未来键不视为空默认 |
| 全局缓存 | 非默认旧项覆盖同类型原位置，消除该类型被覆盖的重复项；其他/未来项保持；默认旧参数不清除既有全局缓存 |
| 默认预设 | Id0、空Name；六分支为ImageCustomSize/ImageTrim/ImageDotKeep/ImageResizeFilter/ImageGrid/ImageEffect；前五分支保存来源差分/未知字段，不声称其控制/后端已执行 |
| EffectProfiles | Profiles替换为唯一默认首项；来源IdCounter及元数据保持，缺省IdCounter0 |
| 现代数据 | 层顺序、启用、预设及未来类型保持；原10层约束属于运行中新建行为，本批不截断导入数组 |

选中设置导入时，三个效果分支采用来源整体替换；来源未记录则恢复缺省，避免递归合并复活当前旧层/缓存/预设。效果兼容标记和原材料同时由来源替换，不继承当前Profile的成功标记。其他配置的投影和未知字段规则保持。

## 状态、资源和错误

先准备所有独立JSON副本，成功后一次替换三个活动分支；没有像素、native对象、流、窗口或永久任务。`MacImportedLegacyImageEffects` 保留转换前完整三分支及原缺失状态，不进入执行。旧EffectType、旧参数和IsHsvMode退出活动节点，防止重载覆盖现代层。

未知旧枚举、未来参数discriminator、scRGB及未核对Point语法保持整组原效果，并写issue报告未转换。与Windows直接拒绝未知效果的区别是：Mac尚无效果执行，允许导入其他设置；不能伪造None作为成功转换。已知字段的错误对象/数值/布尔值在写入前拒绝；事务失败走原恢复。

已转换、现代原样保持、未转换和实际执行未接入分别报告。未知类型的保留不代表能够执行；不开放脚本或新增通用WPF兼容层。

## 测试、验收和扩展

隔离合成回归覆盖13类默认、15种枚举、版本边界、数值例外、颜色/Point、缓存顺序/覆盖/null、六分支预设、未知值整组保持、大小写setter最后值、早期Mac/未来标记、现代层保护，以及预览/应用/普通差分保存/重载/再次导入和来源缺省恢复。

[本批验收](../acceptance/p5-effect-compatibility-runtime.md)分别记录测试、正式构建、本地签名和设备边界。真实两分支导出及Windows动态尚未验证；合成数据回归不能代替效果画面对照。

后续继续附属文件与高级设置/内容。效果执行将经现有图像/查看器链迁入原运行模型和控制规则，逐效果验证输出，不以本批数据转换代替执行通过。
