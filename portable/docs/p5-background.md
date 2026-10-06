# P5 第二十二批：原背景与像素保持

## 职责、依赖与出处

固定基线 c5c398d89 的 BackgroundConfig、BackgroundType、BrushSource、CanvasBackgroundSource、PageBackgroundSource、ImageDotKeepConfig 和八个原命令。Engine 保留原配置/枚举/双轴判断及唯一保存链；Backends 提供首像素和源尺寸；Mac 独立适配刷、绘制和表单。不新增生产项目、状态存储或图片工厂。

## 契约与业务规则

画布六背景按原 Black/White/Auto/Check/CheckDark/Custom 顺序循环。自定义保持 SolidColor/ImageTile/ImageFill/ImageUniform/ImageUniformToFill 五模式；底色和前景刷分开，空文件名无前景、读取失败 LightGray。画布棋盘为16设备像素周期，平铺为源图片尺寸除以 RenderScaling，其他模式按视口拉伸/包含/裁满。自动背景采用 EXIF/色彩处理后、缩略前的首像素 RGB，并强制不透明；显示像素单独预乘 Alpha。

透明页背景独立于画布；Alpha 为0不绘制，单色或原 HSV 明暗棋盘内缩1 DIP，随图像变换。分页、全景、连续/瀑布及书籍封面共用 ReaderImageRenderer；布局和主题不承担背景或阅读业务。

nearest 保留原默认关闭、Threshold=1、双轴比较及一像素容差；判定输入为实际矩阵和设备像素。条件成立时正文沿既有工厂申请源尺寸，否则保持原可见规格；插值只作用于当前图片绘制。ToggleNearestNeighbor 菜单取反，快捷键沿原 ToggleMode。背景命令不导航、不重新加载正文；像素保持变化才更新显示需求。

## 状态、资源生命周期与错误

背景请求使用唯一归档工厂及 BitmapFactory，来源/流请求级释放，共享解码槽、缓存版本和主像素/显示预算。切背景或关闭取消旧等待，UI发布前再次核验请求引用与取消状态；晚到租约清理，显示 Bitmap 先释放、像素租约后归还。背景失败只显示回退刷，不影响书籍或页框。

命令在原配置锁内保存，失败恢复原值，成功才通知表现。设置使用独立草稿，保存进入原五文件事务。原颜色保持字符串/Alpha及命名、hex、scRGB解析；未知JSON字段及数值型未知枚举保留。阈值原值超出decimal控件范围时仅限制草稿表示，未编辑阈值的保存保留原double。无效颜色不部分提交；失败可重试、取消不改运行配置。

## 测试、验收与扩展

合成像素验证六背景、16像素棋盘、四图像刷、透明页底色、矩阵插值、半透明首像素及不可即时取消的晚到任务；JSON验证颜色/Alpha、未知字段/枚举、默认差分、重载失败、阈值边界和草稿/搜索回归。主窗口验证八命令勾选、菜单参数、正文不重解码和保存失败回滚。启动侧栏200ms缩略防抖独立于正文解码，分别核验。

正式设置XAML Headless截图和实际Mac后端回归分别留证；Retina/Windows动态和长期资源不由合成测试代替。具体结果见[静默验收](../acceptance/p5-background-runtime.md)。图像效果、完整预设/编辑、裁剪/放大镜/视频/脚本及其余原设置继续迁移，P5整体未完成。
