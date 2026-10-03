# P2 第七批：原底部页号输入与滑条设置

日期：2026-10-03。承接共享 PageSelector/FilmStrip/PageSlider，不新增阅读控制或状态格式。原页标记依赖全局播放列表，随完整播放列表链迁入；本批不另建 Book 私有标记。

## 职责、依赖与契约

`SliderTextBox.axaml` 负责显示/编辑结构；主题负责悬停边框及输入外观；控件只处理数字转换、焦点、草稿和轮滚。`MainWindow` 把原始索引传给唯一 `BookOperation.JumpAsync`，不调用滑块的双页对齐。Engine 保存原 SliderConfig 字段，不引用 Avalonia。

| 契约 | 行为 |
|---|---|
| SliderTextBox.Value/Maximum | 共用临时选择及最大索引；显示一起始页号，数字不随阅读方向反转 |
| SliderTextBox.SourceKey | 当前书对象身份，切书即撤销未提交草稿 |
| SliderTextBox.RequestPageAsync | 宿主可等待定位入口，携带来源和原始索引 |
| SliderTextBox.CommitAsync | Enter/失焦强制确认，联动胶片条仍立即定位正文 |
| BookOperation.JumpAsync(expectedBook) | 既有互斥内再次核对来源，拒绝排队的旧书输入 |
| SliderConfig.SliderIndexLayout 等原字段 | 原字段名/枚举顺序/默认值，沿用三 JSON 合并与事务保存 |

## 原业务规则

- 点击页号或 Tab 进入编辑，全选当前页号。Enter 提交并继续编辑；普通失焦提交并隐藏输入。Escape 返回主查看器，因此也经失焦提交，不能改为取消。
- 原 `SliderValueConverter` 接受当前区域格式的数字、小数和科学计数。先限制一起始数值到 1–int.MaxValue，减一并按当前最大索引限制，再保持 WPF 默认数值绑定 `Convert.ToInt32` 的舍入。非法文本不修改选择，原 ValueChanged 仍确认已有临时选择。NaN 明确拒绝，避免损坏索引。
- 输入走原 SelectedIndexRaw，不使用滑块 GetFixedIndex；双页同步和静态对齐不改变指定页。正文同步后仍显示原显示范围的最小页索引。
- 页号框滚轮按一页改变并立即定位。滑条滚轮默认按正文帧前后移动，CommandDependent 进入原命令绑定；小增量累积到完整轮格，不重复进入框架默认滑块动作。
- 设置提供滑条显示、页号 None/Left/Right、厚度 15–50 DIP、透明度、方向、胶片条联动、双页同步及滚轮动作。取消不应用；只改表现字段不重建正文、不重新扫描来源。
- 未迁入的自动隐藏、播放列表标记及全局显隐命令继续保留配置与禁用入口。原菜单和235条命令未减少，也不把设置入口当成完整全局显隐命令迁入。

## 状态、生命周期与错误

编辑草稿由单个控件持有，不写 JSON。普通保存/选择刷新不覆盖同书草稿；来源变化取消，关闭准备取消未提交输入。已提交请求进入既有阅读代次和互斥控制，过期来源不能作用于新书。异步定位失败回报窗口错误，保留原可用书籍。输入文本隔离阅读和数字命令，Command+O/W/Q 继续为系统操作。

原 SliderConfig 的新增字段按原名称写回；未支持字段及未知 JSON 通过既有递归合并保留。Opacity 显示端限制0–1；Thickness 按原范围和精度保存，非有限数值回到默认。没有新增数据库、定时器、后台队列或通用 WPF 兼容层。

## 测试与扩展

专项覆盖实际点击/键盘/失焦与Escape、原转换范围/舍入、双页raw索引、滚轮两模式、切书及关闭、设置取消/保存/前端独立性、未知JSON。测试编译正式控件和XAML；本批独立截图与构建记录以 `p2-slider` 保存，旧阶段材料不覆盖。

Fluent滑条轨道留白及滑块尺寸由主题资源覆盖，修复薄槽裁切而不复制完整模板。最薄15 DIP的Thumb边界由正式Headless布局断言，真机15/37 DIP及还原默认25 DIP另留截图。最终107项通过、正式构建及本地签名通过；运行范围单独记录。

原转换点来源见 source-migration.json。WPF 数值绑定依据 [官方 DefaultValueConverter 源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/MS/Internal/Data/DefaultValueConverter.cs)，通过 SystemConvertConverter 的 Convert.ChangeType 完成 double/int 转换。

真机结果见 [独立运行记录](../acceptance/p2-slider-macos-runtime.md)，构建与测试见 [验证记录](../acceptance/p2-slider-validation.json)。Windows 动态对照、真人滚轮/触控板/IME、多屏、NAS、长期内存和完整显示P95仍独立待验。P2尚未封板。后续播放列表负责原页标记数据/标记展示/导航；完整自动隐藏与全局显隐继续按原窗口控制迁入。
