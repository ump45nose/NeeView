# P5第二十八批：原图像效果、预设与阅读几何

## 职责、原出处与依赖

固定基线c5c398d89的EffectUnit/EffectLayer/EffectUnitCache、六分支EffectProfile/Collection、ImageCustomSize/ImageTrim/ImageGrid、PageCustomSize/PageViewSizeCalculator及原六区域侧栏。迁移指纹见source-migration.json。

Engine保留参数、通知、层/预设控制与阅读几何，原四个自有HLSL方程在Mac现有Avalonia.Skia中转换为SkSL。没有新增生产项目、语言、图片工厂、状态数据库或第二导出链。前端XAML组织原预设栏/六区域，表现模型只编辑克隆草稿；样式不参与业务。

## 契约与业务规则

- 保留14类参数、短`$type`、五位ToEven舍入、原Level raw端点及UI端点联动、Colorize五点/字符串和原默认值。默认参数差分写出；未知类型/扩展字段原样保留，不能转换为None冒充执行。
- 四类自有shader实际执行：Level、Hsv、ColorSelect、Colorize。Colorize沿原256点结点强度/二次插值/8位截断；现有Skia ES2限制用常量索引二分查表，未改变插值表。
- Blur使用WPF内置；其余九类依赖原Microsoft.Expression.Effects.dll，仓库未含算法源码。此十类参数可编辑及保存，但运行提示待迁，启用时显示明确待迁占位，View导出拒绝。不引入通用D3D翻译层，不用近似算法标记原版通过。缩放滤镜仍保存原JSON材料并明确禁用待迁。
- 原层序列外到内，像素执行逆序；新层插入0，最多10层，删除仍保留至少1层。导入已有层不截断。切换和删除必须传入所在配置/缓存，草稿不能读取或污染全局缓存。原有序值比较在差分保存中按完整JSON值执行；空缓存/滤镜及默认层/预设省略，未知层/预设/缓存材料不能因此丢失。
- 原六分支预设：custom、trim、dotkeep、resize、grid、effect。ID0/空名默认项保护，唯一名称/ID，新建默认/克隆当前、重命名、删除、前后切换及SetEffectProfile.Id保持；每书EffectProfileId恢复仍用原BookMemento/JSON。
- DPI开关沿原Image.Standard.IsAspectRatioEnabled，顺序DPI尺寸→自定义尺寸→裁剪→分割。Magick仅Ping探测方向和DPI，96DIP/DPI换算，厘米单位转换及旋转交换两轴，未声明DPI普通格式保持像素尺寸；原WebP缺省72DPI保持。实际不同格式的Windows元数据差异待动态对照。
- 原九种自定义宽高比、应用比例、长边对齐、裁剪对边总量0.9保持。宽高UI范围16–4096；原setter没有范围截断，不为此改写旧导入数值。裁剪保留原“尺寸减去尺寸乘对边总量”的运算顺序，避免整像素边界浮点尾差增加导出行；源矩形与布局共同计算，清晰度需求取变形后的两轴最大倍率，仍等比解码且受原预算控制。
- 原图像网格覆盖整个页框，双页不分别分格；方格取较大单元尺寸、边框和不足1DIP末端规则保持。Screen网格覆盖查看器。连续/瀑布扩展中Image网格按单图；导出取原contentCanvas语义，不包含Image/Screen网格。
- 菜单/快捷键仍使用原ToggleCommandParameter的fromMenu区别；文本输入不触发数字分类，原快捷键及未迁菜单清单保持。

## 状态与资源生命周期

唯一BookOperation.ApplyOptions事务串行编辑/保存；失败恢复六分支、缓存、预设和原阅读状态。面板以实际效果参数及预设身份判变化，普通翻页不重建草稿或丢编辑文本。

参数快照在配置/层/点变化时失效；只在快照生成时克隆和预检。四种shader各编译缓存一份，每帧组合单个color filter/SaveLayer；不修改原Bitmap、不重复解码、不做整图像素复制。透明像素按预乘→直alpha计算→预乘输出；WPF透明像素动态等价另验。

ICustomDrawOperation持有显示引用直到scene-graph回收。显示引用使用Interlocked，晚到绘制不访问已被查看器关掉的Bitmap。scene-graph提交失败时调用方立即归还租约；离屏View导出通过同一DrawFrame，Custom同步执行后显式释放；源CopyImage仍编码原Bitmap，不含效果/裁剪/变换。渲染临时可见表面按真实设备clip计费，独立128MiB工作限额不等于图像缓存或进程RSS上限。

跨Skia lease的依据是固定Avalonia12.1.3源码：PlatformDrawingContext.Custom同步Render且不Dispose；DrawingContextImpl.ApiLease仅恢复matrix/leased标记，不恢复canvas save stack或替换canvas。遵守lease内不可框架绘图的限制：设置SaveLayer→归还lease→原DrawBitmap→再lease RestoreToCount。真实Skia像素、离屏及scene-graph释放测试覆盖；更换Avalonia版本须重验此边界。

## 错误、测试和扩展

坏参数/不支持/Skia不可用或工作预算超限不能静默画原图。参数快照保留错误，渲染绘制明确失败占位；View导出预检与可等待结果再次校验，失败不编码成功文件。框架会捕获Custom异常，所以结果显式回传，不能仅靠throw。

自动回归覆盖原参数默认/多态/未知字段、原舍入/Level联动、实际四种Skia shader/输出像素/半透明、逆层顺序、Colorize点变化、草稿缓存隔离、原几何/DPI/分割、层/预设/JSON/保存失败、真正View导出/CopyImage、scene-graph/离屏租约释放、瀑布裁剪重排和切书/关闭。工作预算边界使用纯数值测试，不为测试分配超限位图。

后台验收及正式构建分别见[p5-image-effects-runtime.md](../acceptance/p5-image-effects-runtime.md)。本批不继承此前设备验收为效果Windows动态/Retina/长期原生性能通过。十类效果、resize、放大镜、视频/脚本、完整设置及正式分发继续按原迁移清单推进。
