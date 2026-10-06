# P5 第二十一批：原窗口与导航命令

## 职责、依赖与出处

固定基线 c5c398d89 的七个同名 Command、PageSortModeClass、BookPageMoveControl、MainWindowModel 和 WindowStateManager。Engine 保留排序资格、随机和唯一 BookOperation；Mac 表现适配窗口、显隐与既有 IPlatformService。无新增生产项目、JSON 或平台抽象。

## 契约与业务规则

JumpRandomPage 从当前过滤后的真实 Pages 随机选择，允许当前页，空书无动作。锁内捕获 Book，沿 expectedBook 定位，晚到请求不能跳转新书。ToggleSortMode 按原枚举顺序，普通书籍跳过登记顺序，播放列表保留所有模式；原地排序、Page 身份及主页面保持。未知排序值按原规则回退文件名，纠正此前 Mac 保留未知值的差异。

ToggleVisibleAddressBar/PageSlider：菜单忽略固定 ToggleMode 并切换持久开关，勾选反映该开关；快捷键按实际可见状态及原参数求值。具备自动隐藏资格时强制启用控件，只临时改变对应菜单/地址或状态/滑条区域，再沿原悬停、焦点及延迟规则隐藏；不锁定其他区域。关闭滑条后仍可由快捷键启用。

ToggleWindowMinimize 恢复实际最小化前的 Normal/Maximized/FullScreen，包括系统触发的最小化。ToggleWindowMaximize 在最大化与普通间切换，从全屏进入最大化。真实系统状态切换另验。

OpenSettingFilesFolder 打开唯一 Mac Profile 的目录内容，不创建或写入文件。加载期间仍可用，同窗口并发去重。使用现有系统契约，不在视图读写文件。

## 状态、资源与错误

显隐只发布 ChromeRefreshed，不重排或刷新正文；持久配置沿已有 JSON 保存。设置目录动作共享可等待任务；关闭取消尚未完成请求并等待收尾。失败显示错误、观察异常并清理任务，允许重试或正常关闭。窗口状态只由主宿主持有，不进入阅读模型。

## 测试与扩展

隔离合成图片/配置验证四类排序循环及非法值、真实排序保持 Page、过滤后随机/空书、菜单与参数/临时区域隔离、正文不刷新、最小化恢复、Finder 成功/失败及关闭取消。正式 XAML Headless 截图仅作布局证据。构建、全量与原生测试见[静默验收](../acceptance/p5-original-commands-runtime.md)；不继承为真实窗口/Finder、Windows 或 P5 完成证明。
