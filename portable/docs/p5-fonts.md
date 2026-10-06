# P5 第九批：原字体设置与资源

## 职责与出处

固定基线 `c5c398d89` 的 FontsConfig 六字段、FontParameters 字号公式、Fonts.xaml 资源角色和 SettingPageFonts 顺序迁入同一产品链路。沿原设置窗口左导航/右内容，字体作为独立页面；不新增命令、配置文件、阅读控制或长期系统监听。

## 依赖与契约

- Engine 的 FontsConfig 保留 FontName、FontScale、MenuFontScale、FolderTreeFontScale、PanelFontScale、IsClearTypeEnabled。FontNameRaw 使用原 JSON 名 FontName，运行默认名不持久化；未知字段继续由 SaveData 保留。
- FontEnvironment 只包含默认字体名及消息/菜单 DIP。FontParameters.Calculate 沿原公式返回尺寸，无 WPF、AppKit、控件或应用资源引用。
- Backends.MacFontEnvironment 以官方 AppKit 默认 message/menu 字体点大小替换 Windows SystemFonts 度量，包装按调用释放。Avalonia 默认字体名由启动装配提供，以保证同一字体后端能够解析；点大小不再乘 Retina 倍率。
- FontPresenter 发布原 DefaultFontFamily、ArrowFontFamily、SystemFontSize 及 Normal/Large/Huge、DefaultFontSize、MenuFontSize、FolderTreeFontSize、PanelFontSize、FontIconSize 资源。样式决定哪些控件使用角色，业务模型不依赖样式。
- FontSettingsViewModel 只维护字体族选择及百分比草稿，字体列表由显示端提供。SettingsWindow.Fonts 只绑定，应用进入现有 ApplyOptions/五文件事务；MainWindow 仅在实际保存成功后发布资源。

## 生命周期与资源

启动 Load 唯一 JSON 后，由 MacApp 装配实际字体环境并在显示前应用资源。导入后沿同一窗口重建流程绑定新 Config.Fonts。无字体 watcher、单例订阅或原生字体常驻句柄；窗口关闭释放 presenter 引用。主题服务和字体服务独立，配色切换不重置字体。

资源提交前先计算完整尺寸并检查合法性，避免部分更新。动态资源触发已有控件测量/绘制；面板文本高度按两行需求调整，不重新构造封面。字体服务不枚举内容、不重新打开书籍、不请求像素。字号改变导致视口或可见缩略需求改变时，仍由原查看器和缓存按正常需求处理，不宣称所有场景解码数不变。

## 原规则与必要改造

1. 默认比例为常规1.25、菜单1、树1、面板1.25，ClearType 默认 true；比例 setter 五位舍入，getter 对非正值回默认。原 setter 不夹到1–2，有效导入比例保持；表单常用范围100–200%，范围外原值也可原样显示/无改动保存。
2. 常规、树、面板以消息字号为基准，菜单以菜单字号为基准。系统标题尺寸沿原24上限规则，图标尺寸为 max(常规字号+15,28)。窗口/浮窗/设置使用常规角色，菜单和弹出菜单使用菜单角色，目录/书签树使用树角色，四模板/播放列表使用面板角色。
3. 原 FontName 空白或系统默认写回 null。未安装的 Windows 字体保留原名称，当前显示使用同一后端系统默认字体并提示；字体列表包含缺失的当前值。原 Calibri 箭头专用映射在 Mac 使用系统字形回退，没有模拟 WPF 字体映射层。
4. 原 ClearType=true 对应 WPF Enabled、false 对应 Auto，Mac 没有等价选项；保留原字段及禁用占位，应用不修改此偏好，也不以近似开关替代。
5. Fonts 进入原读取、合并、默认差分、未知数据保留、备份/导入及失败回滚链。取消不应用草稿，保存失败保持配置分支引用、运行默认值和显示资源；成功重试后才更新外观。
6. 38版非零 FontSize/FolderTreeFontSize 仍需来源 Windows MessageFontSize，继续阻止应用而允许预览；不猜测为当前 Mac 基准。仅旧字体名与零尺寸的已核对升级仍可迁入。现代39–46.3比例可使用。

## 错误与验收

非法/溢出尺寸在资源发布前报错；缺失字体是显示回退提示，原配置不被改写。测试分别覆盖原默认/舍入/非正回退/范围外值、不同基准公式、唯一 JSON/未知字段/差分恢复、失败回滚/重试/加载，以及正式设置按钮和已有主/浮/设置/树/列表/菜单资源更新。Headless 小图相同解码规格验证阅读身份/位置保持和没有额外解码；不外推大图/虚拟列表重排后的解码需求。

构建、自动回归、静默截图、真机/Retina/Windows、提交推送及正式发布分别记录。结果见[验收记录](../acceptance/p5-fonts-runtime.md)。

## 扩展点

完整设置搜索、原主题Sample目录动作、实际效果/高级媒体、脚本、真实用户导出和正式分发继续按 P5 清单推进。macOS 实际字体家族/字形回退、系统文本尺寸及 Retina 应用效果需要独立真机验收；不重启此前已跳过的 P3/P4 项，不关闭 AX/NAS 既有问题。
