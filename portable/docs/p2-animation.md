# P2 第二十四批：原滚动与分页动画

## 职责与依赖

Engine保留原PageMoveType与PageFrameContext时长裁决，迁入PageFrameContainerLayout方向/定位分支。Mac ReaderMotionPresenter仅计算点/透明度插值；ReaderView继续使用唯一ReaderTransformPresenter绘制与命中，不建立第二阅读内核。

## 契约与生命周期

ScrollDuration默认0.2秒；普通分页PageMoveDuration默认0，零时长强制Scroll。Scroll沿水平阅读顺序或垂直前后方向排列，相邻静态分页容器原FrameMargin=1；Fade同位渐变。四向/预置/N型滚动采用Quadratic EaseOut；连续轮滚线性。重复滚动从当前可见点开始，直接平移/缩放/旋转等取消动画并回存当前点。

非零分页只保留一个退出帧快照，目标解码准备后开始过渡；快照复用现有Bitmap和租约，不复制像素，实际资源仍计入原工厂预算。相邻相同页通过引用计数共用显示资源；完成、快速翻页替换、切书、尺寸改变、显示构造失败和关闭释放退出引用。零时长不持有退出帧。动画计时只在活跃期间运行，完成停止。

原IsHoverScroll默认false、Sensitivity=2、Duration=0.5。悬停按相对视口中心与超出尺寸映射到半幅；四向/预置/N型命令禁滚，滚动翻页仍可翻页。ToggleHoverScroll沿原开关语义。原IsMouseWheelScrollEnabled默认false、Sensitivity=1、Duration=0.2，仅无修饰/按钮优先消费；横轴符号和120 delta单位适配Avalonia轮滚。真实精确触控板仍走平台连续平移，不由滚轮幅度猜设备。

## 配置/前端与错误

所有字段沿原View/Mouse JSON分支，未知字段保留，独立设置草稿经原事务回滚。XAML只编辑时长/类型/开关，动画表现不改分页、书籍位置或命令含义。非法非有限时长安全回退零，合法原值不因编辑范围截断。

## 验证与扩展

专项11项覆盖六种相邻方向/位置、零时长/Fade/线性与EaseOut、取消、快速翻页/切书/关闭和预算归零，正式宿主验证连续轮滚优先与Hover命令禁滚/保留分页。离线Scroll/Fade截图和五步验证独立保存。不启动正式应用，不使用用户数据。

原Linked容器首次几何布局后的250ms调整动画与普通分页时长是不同机制。本批单个可见页框和有界退出快照没有长期相邻容器；改变视口取消退出快照并重新布局。P3全景FrameSpace、幻灯片专有动画/自动滚动与复杂拖动动作属于对应后续阶段，未宣称已迁。Windows动态/真实触控板/Retina及显示帧率待真机验收。
