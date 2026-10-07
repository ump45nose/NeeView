# P5 原中央查看器浮动与窗口贴合

## 职责、依赖与出处

保留原 MainViewConfig、MainViewManager、MainViewWindow 和 StretchWindow 的宿主关系；出处及基线指纹记入 source-migration.json。Engine 只持有原配置和值，MainViewPresenter 管理 Avalonia 宿主与借用页面列表，MainViewWindow 管理原位置/关闭语义。没有第二阅读器、图像工厂、播放器或业务状态。

## 契约与业务

- ToggleMainViewFloating 保留 F12 与原 IsFloating；菜单勾选、脚本 MainView.Open/Close/Window 共用同一宿主。关闭脚本中央窗口不关闭主窗口。
- 将唯一 MainViewContent（ReaderView 与原幻灯计时条）移入浮窗。主窗口原区域按 AlternativeContent 放空白或借用唯一 PageList；归还后恢复原停靠/浮动位置与选中项，原 LayoutPanel JSON 不改变。
- 默认关闭浮窗只最小化；IsFloatingEndWhenClosed 才结束浮动并归还。空书自动隐藏、内容恢复自动显示按原开关；显式打开恢复并聚焦。
- 顶置、尽量前置、隐藏标题栏及自动贴合沿原字段。位置以设备像素持久化，窗口尺寸以实际绘制比例转换，屏幕边界仍复用已有位置恢复规则。
- StretchWindow 作用于 ReaderView 当前宿主，保留窗口外的其他区域。自动贴合使用 ReferenceSize，避免每页反复收缩；手动调整窗口更新参考，自动调整期间锁定。正常状态才允许贴合。
- 输入、拖放、幻灯输入重置、Loupe 与平台手势继续通过原路由；平台来源窗口身份和命中测试改为实际浮动宿主。Command+W 使用浮窗关闭语义。
- Window.State 的 Normal 是普通窗口，None 仅无窗口/写入不动作；FullDesktop和LastState已在[收尾批次](p5-completion.md)接入；混合缩放多屏仍待设备验收。

## 生命周期与资源

重挂前取消当前指针序列与 Loupe 捕获，先解除父级再归还。宿主关闭退订状态事件，但不释放唯一 ReaderView。主窗口退出先冻结浮窗、记录位置，保存成功后销毁宿主并释放原阅读资源；保存失败恢复相同窗口供重试。窗口和配置重载采用原 JSON 事务，没有第二状态目录。

## 设置与前端边界

设置表单只持有草稿，取消不写入，保存失败恢复原配置引用。XAML/颜色可独立调整；MainViewPresenter 只处理控件所有权、焦点和宿主生命周期，不枚举、读取、排序或解码。

## 错误、测试与扩展

正式XAML/真实后端隔离测试覆盖浮动/停靠、原浮动页面列表借用、选区和书籍身份、最小化/恢复、关闭结束浮动、脚本状态、保存失败/重启、设置取消/回滚及窗口贴合。原生窗口装饰、真实聚焦/手势、多屏与 Windows 动态对照另验。后续高级窗口状态沿此唯一宿主接入，不增加通用窗口模拟层。
