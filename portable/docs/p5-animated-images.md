# P5 第十七批：原图像动画和媒体控制

## 职责与出处

固定基线 c5c398d89 的 AnimatedMediaPlayer、AnimatedPageContent、PictureProfile、MediaPlayerOperator、MediaControlViewModel/View、ImageConfig、ImageStandardConfig 和 MediaArchiveConfig 是业务对照。迁移帧编号位置、播放意图/启用状态、循环和三个媒体命令，替换 WPF 动图资源，不建立第二阅读内核。

## 依赖与契约

Engine 的 IAnimatedImageDecoder/IAnimatedImageSource 提供完整合成画布、真实逐帧时长和原生资源计费；输入沿原 Page/ArchiveEntry 流。GIF/WebP 经锁定 Magick.NET 一次 Coalesce 后按需输出当前 BGRA8 预乘帧。实际探针证明 Magick 默认 PNG 只识别首帧，因此 APNG 经已有官方 ImageIO/CoreGraphics 绑定读取，MacApp 是唯一装配点。

AnimatedMediaPlayer 是纯播放状态，不持有控件或后端。ReaderView 拥有来源/时钟/当前显示，BookOperation 只借用当前播放器。MediaControlView 的 XAML、表现模型和输入与阅读规则分开，沿原底部 SliderArea 覆盖关系呈现。无音轨音量入口禁用，未实现的视频/字幕/速率不列为完成。

## 状态与资源生命周期

动画打开与帧输出共享原 BitmapFactory 两个解码槽。原生集合、当前帧像素和实际 Avalonia 显示缓冲计入主图预算；来源准备峰值上限 min(256MiB, 主图预算)，最多4096帧。APNG 输入有界且计费。只输出当前托管帧，没有全部托管帧缓存或逐帧重复解压；原生完整合成集合仍随帧数增长，超限明确提示。

来源租约在所有原生读取真正完成后才释放。关页/切书/关闭取消需求，拒绝晚到结果；显示 Bitmap 先释放，再归还像素租约。取消的首帧不缓存成功判定，下一代次可再次打开；静态判定按原页/规格/版本复用。16ms timer 按单调时钟推进、帧更新串行，积压位置只保留最新需求。暂停/禁用后不空转。显示成功不代表屏幕帧率通过。

## 业务规则

原 Image.Standard 三格式开关和 Image.IsMediaRepeat 默认 true，媒体秒数 Archive.Media.PageSeconds 默认10并五位舍入；视频 Archive.Media.IsRepeat 是另一字段，保留未知配置且不覆盖。设置草稿、循环按钮均经唯一 JSON 事务提交，失败回滚；退出等待已提交动作。

位置 getter 为 frame/(count-1)，setter 为 clamp((int)(count*position))；秒数按总时长归一后增加位置，保留原 MediaPlayerOperator 行为。ToggleMediaPlay/PrevMediaPosition/NextMediaPosition 受当前媒体能力限制，保留原名称/参数/绑定。前后命令各自保留原 Delta 参数（非负、五位精度），零值回退到全局秒数；沿现有参数编辑、导入和差分JSON，不误设为共享参数。菜单、按钮和 slider 操作同一播放器，slider 自动回显不触发 seek，拖动合并不丢最后需求。

暂停保留播放位置；IsEnabled 只阻止推进、不丢播放意图。原单周期由循环开关重启。长停顿按周期和二分帧位置折算，EOS 数量保留跨越周期数但每次 Advance 最多发一次事件，这是有界执行的明确适配。

## 错误与测试

超限、损坏和后端不支持时保留静态首帧，同时显示动画未播放的真实原因。取消不误报。合成格式夹具定义颜色和时长；GIF Previous 局部帧、实际 WebP、APNG Background/Previous/偏移/半透明及随机读分别验证。APNG 测试在真实官方绑定后台 `.app` 执行，不由通用 Headless 代替。

目录及ZIP通过原来源、工厂及正式 ReaderView 验证画面红/绿像素、播放、暂停、seek、模式切换、静态页及关闭释放；可控晚到用例验证工厂关闭和首帧取消；JSON差分、未知字段、重启、表单取消、失败回滚和重试独立覆盖。完整结果见[验收](../acceptance/p5-animated-images-runtime.md)。

## 边界与扩展

本批分页及原帧全景播放可见图片；缩略图、连续和瀑布使用首帧。每图过大或过长动画拒绝自动播放，未实现流式逐帧合成。格式循环次数按原应用循环设置处理，不强制跟随文件内循环次数。GIF Background、WebP局部透明/销毁的更多专用夹具、动画ICC/EXIF组合和大型动画长期原生内存仍待扩展验收；APNG纯格式样本已验证。

原媒体结束驱动幻灯/自动换书等高级自动播放尚未迁入，当前不以接通三个命令代表所有媒体能力。视频、脚本、效果、完整设置、真实导出和正式签名公证继续迁移/验收；P5整体未完成。
