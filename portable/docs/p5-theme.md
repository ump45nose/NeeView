# P5 第八批：原主题配置与应用

## 职责与出处

沿固定基线 `c5c398d89` 的 `ThemeConfig`、`ThemeSource`、`ThemeColor`、`ThemeProfile`、`ThemeProfileTools` 和 `ThemeManager` 迁入原主题业务。五个 `Libraries/Themes/*Theme.json` 与 `CustomThemeTemplate.json` 原字节内嵌。原六个选择为 Dark、DarkMonochrome、Light、LightMonochrome、HighContrast、System；Custom 使用 `Custom.FileName` 标识。

原功能入口在 `SettingPageWindow`，235 条原命令登记表没有独立主题切换命令。本批不虚构新命令，也不改变阅读窗口区域。三个生产项目、唯一 JSON/书籍/图片工厂保持。

## 依赖与契约

- Engine：`ThemeRgba` 是实际 WPF Color 替换点，仅含 ARGB 数据。标准命名色使用 .NET 的 System.Drawing.Primitives 值表；没有 System.Drawing.Common 或第二图像后端。`ThemeColor` 保留字符串、不透明度及链接，`ThemeProfile` 保留标准角色、默认色和覆盖/解析规则。
- `ThemeManager.CollectThemesAsync` 在单槽后台扫描配置目录的一级 JSON；Windows 大写扩展名也可见。`LoadAsync` 接受原标识、目录及纯值 `SystemThemeState`，返回完整颜色表、实际预设和错误。Engine 不引用 Avalonia、AppKit、Bitmap、控件或系统主题监听。
- MacOS：`ThemePresenter` 将原颜色键转换为应用级 SolidColorBrush；管理 Fluent 亮暗变体、原特殊按钮/边框和 Mac Gallery 角色映射。系统颜色事件统一转到 UI；仅 System 选择刷新。
- `ThemeSettingsViewModel` 管理表单选择、目录草稿、扫描取消/代次和提示；不读文件。`SettingsWindow.Theme` 只绑定草稿、转交刷新和系统目录选择器。`WasSaved` 只在原 ApplyOptions/五文件事务成功后置为真，主窗口此时才应用配色。

## 状态与资源生命周期

启动层先 Load 唯一 SaveData，再绑定对应 ThemeConfig、读取主题并显示正式窗口。关闭/导入重建退订平台颜色事件、取消当前需求，晚到结果不得替换新资源。旧应用资源保持到新窗口接管，不初始化第二入口。

主题加载流在请求完成后关闭。来源和继承文件只读；没有 watcher 或长期保持的文件句柄。一个 ThemeManager 同时执行一个任务，取消排队和过期结果；已进入的系统文件调用不能保证即时中断，真实 NAS 原生 open 仍沿已有未解决边界，不宣称本批修复。

替换资源只失效现有可见控件；主题服务不接触 Book、Page、排序、位置、阅读访问或 BitmapFactory。主/浮/设置窗口及弹出层使用同一应用资源。Reader 固有黑色画布、原书籍封面配色保持各自原角色，主题不重新解码图片。

## 业务规则与适配

1. Config.Theme 默认 Dark，自定义目录缺省为独立 Mac Profile/Themes。ThemeType 仍是原字符串，CustomThemeFolder 仍是原键；现代 ThemeType 优先，PanelColor 仅作旧读取后备。未知字段在唯一 JSON 合并/差分中保持，恢复默认去掉已知差分和旧别名。
2. 自定义颜色支持原默认、具体色、角色引用和 `/opacity`；支持短/长 ARGB、原标准命名色及 scRGB。颜色引用和不透明度按原规则计算。
3. BasedOn 为空与原默认表合并；`themes://File.json` 读取原内嵌预设；绝对路径或当前文件相对路径递归加载并覆盖。内嵌预设分支按原版单层规则，它们本身没有 BasedOn。保持原允许外部只读继承的语义，不把附属导入的写入路径限制套到主题阅读。
4. 必要可靠性改造：规范绝对文件定位检测循环，最多32层/每文件4 MiB；颜色默认角色跳转也保留引用链，最多256次跳转，防止坏主题造成递归溢出。正常原预设与有效样本不改变结果。
5. System 按平台亮暗或高对比选择原预设，普通系统主题覆盖 accent；实际系统事件和平台高对比支持需真机验收。Custom 的 Fluent 亮暗变体按实际 Window.Background 色值选择，避免固定深色控件混入浅色主题。
6. 缺失、坏 JSON、颜色/继承循环和超限沿原规则回退 Dark，显示错误且保留用户 Custom 选择。非法 ThemeType 是配置校验错误，不能当作成功加载的主题。文件目录扫描失败返回预设和提示；不创建目录。
7. 原附属导入继续保存完整原字节。仅同时选中设置及同包主题、配置确实引用该文件时，将目录绑定 Mac Profile/Themes 并恢复实际大小写；设置和材料共同备份/恢复。只选主题材料不更改未选配置。未选材料时仅执行显式 Windows 路径映射，未映射路径保留和报告。
8. 主题设置取消不应用，保存失败恢复同一配置分支及已显示配色；重试成功再应用。刷新列表不应用草稿，不执行任何脚本。

## 错误与验收

隔离测试覆盖原实际配色、三种父来源、BOM、默认/引用/透明度、循环/超限、系统值快照、扫描/取消、旧别名/未知差分、保存失败/重试、导入选择/路径/实际名称/备份恢复。正式 Headless 通过主窗口的设置命令和真实保存按钮验证取消/失败/成功；已打开的主、浮、设置、菜单和下拉实时更新。稳定后切换主题保留 Book/Page/Position 和图片解码计数。

静默截图和结果见[验收记录](../acceptance/p5-theme-runtime.md)。构建、自动测试、Headless、真机/Windows、提交推送和正式发布分别报告，合成材料不是用户两分支导出验收。

## 扩展点与范围

本批完成预设/自定义选择、目录选择、刷新和实际加载；原 Finder 打开主题目录并首次生成 Sample、主题文档链接尚未接入设置页。模板已保留为原材料。完整设置搜索、字体、效果/媒体、脚本执行、真实导出和正式分发仍按 P5 清单推进。原 P3/P4 已跳过验收与 AX/NAS 问题不自动关闭。
