# P5 第十八批：原幻灯播放

## 职责与出处

固定 Windows 基线 `c5c398d89` 的 SlideShow、SlideShowInput、SlideShowConfig、ToggleSlideShowCommand、PageFrameContext、PageFrameBox.AutoScroll、MainView 和 SimpleProgressBar 是行为依据。原默认命令由 CreateCommandName 去掉 Command 后缀得到 NextPage；迁入值保持。默认5秒、最小0.1秒、五位精度、输入重置、自动滚动、Loop书尾、Fade/0.5秒及默认关闭的启动开关保持。

## 依赖与契约

Engine.SlideShow 属于唯一 BookOperation；持有原播放、挂起、计时及媒体等待状态，不持有窗口/位图。单调秒数时钟可注入；TickAsync 接收原命令存在判断、实时能力和可等待执行入口。窗口40ms计时器只回报时钟及进度，命令仍走原宿主路由。已迁入的235实例表保持，未知命令沿原规则回退NextPage，存在但未迁入的命令明确报错并停止。

PageFrameContext.IsAutoScroll/AutoScrollDuration、PageEndAction和PageChangeType/Duration动态读取幻灯覆盖，普通阅读选项不被改写。ReaderView沿原DragArea向阅读方向终端和底部执行线性Pan，100ms以下不启动。顶部4 DIP SimpleProgressBar沿原宽度比例绘制，不改变正文视口；侧栏覆盖时与正文同宽。表单结构/主题、中文选项/独立草稿、计时/导航业务分别管理。

## 状态与资源生命周期

播放/停止、输入重置、显示新原页框及书籍切换沿原顺序处理。相同PageRange的资源重绘不重新计时；真实显示新页框后重置，避免把异步首图等待当作阅读时间。优先时间按起点+计数×间隔补偿，每次回报最多执行一步，不批量追页。每次Played的进度从1到0，持续补偿后的Interval，与原MainView动画相同。

单个在途Tick拒绝重入；epoch拒绝停止/重置后的晚到状态更新。原MoveAsync在等待导航锁前捕获打开代次，排队导航不能作用到新书。来源、尺寸、显示和缓存继续由原阅读工厂所有，幻灯不持有另一个内容集合。关闭先停，保存成功退订/释放；保存失败恢复同一播放和自动滚动并允许重试。

## 业务规则

等待动画只在周期到期、IsWaitAnimation且非优先时间、当前播放器首周期尚未结束时生效；普通动图结束不会自行翻页。EOS借用当前AnimatedMediaPlayer，随后由下一次UI回报执行命令（至多40ms调度间隔，是替换原AppDispatcher回调的明确适配）；替换、停止、重置、卸载及关闭退订旧播放器。暂停动图保持等待；循环及非循环均等待首完整周期。

键盘、鼠标按下及轮滚按原TimerResetGesture重置并取消滚动；鼠标移动只按选项重置；优先时间不被输入重置。页尾沿原Loop/SeamlessLoop/NextBook/None/Dialog，None停播，Dialog临时挂起后恢复。分页及原帧全景沿已有展示能力；Mac连续/瀑布不声明原自动滚动等价。视频媒体书专属循环限制随视频模块迁入，当前BookContext仍不是媒体书。

## JSON、错误与测试

原十个字段及StartUp.IsAutoPlaySlideShow进入唯一SaveData差分JSON与设置事务，不保存临时播放状态。旧IsSlideShowByLoop转换后退役；IsCancelSlideByMouseMove沿原setter无论bool都变MouseMove。明确现代值优先，旧别名归档到MacImportedLegacyConfigFields，未知字段/未来枚举保留。启动恢复/明确打开完成后才处理自动启动，不每次切书重启。

合法旧数值不以编辑范围截断；非有限/负或TimeSpan溢出值安全处理为无动画，这是损坏数据容错改造。自动滚动时长只从页框上下文取得。取消草稿和保存失败保持原配置，菜单忽略固定Toggle参数，键位保留Toggle/On/Off。

SlideShowTests覆盖计时、补偿进度、输入、串行、晚到、EOS、原页尾、JSON别名/未知字段、默认差分/精度、失败回滚、目录/ZIP实际导航、正式表单和关闭失败自动滚动恢复。AnimationTests补充原Fade/Scroll及显示资源闭环。完整结果见[静默验收](../acceptance/p5-slideshow-runtime.md)。

## 扩展点与验收边界

视频继续复用当前媒体控制和原页尾关系；幻灯没有引入第二播放列表/阅读内核或通用事件总线。Headless截图和自动回归不代表屏幕帧率、Retina、Windows动态或长期内存通过。视频/效果/脚本、完整原设置、真实两分支导出及正式分发仍列于P5剩余清单。
