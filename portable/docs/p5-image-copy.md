# P5 第二十六批：原图像源复制

## 职责、出处与依赖

固定基线 c5c398d89 的 ViewCopyImage、CopyImageCommand、ClipboardUtility.CopyImage。沿唯一ReaderView/PageFrame/BitmapFactory，Engine仅定义IImageClipboard有限PNG边界，Backends替换系统写入，MacApp唯一装配。没有新读取/解码或显示缓存。

## 原行为与能力

原GetSelectedPageFrameContent → ViewContents.First → IHasImageSource.BitmapSource，ViewContents按Frame.Elements顺序建立。复制选中原帧的首元素，不调用GetDirectedSources反转，不跳过dummy/非图像。分页/全景沿当前原帧；目录封面/文件不是图像源。动画strategy没有BitmapSource，播放动画时禁用；关闭动画配置后静态首帧可复制。GIF实际没有动画时仍视为静态图像。

复制当前已解码的完整图像源（可能采用既有安全降采样），不是源文件字节，也不是窗口截图。分割裁剪、自动/手工旋转、翻转、缩放矩阵、背景及显示效果不参与PNG编码。EXIF/色彩等解码结果保持，透明alpha保留。不要宣称强制原始分辨率或复制当前动画帧。

Mac连续/瀑布是明确扩展：只复制显式选中且已显示的图片；滚动锚点不作为目标。全部菜单与原快捷键调用同一CopyImage，未加载/错误/无选择时禁用，入口不删除。

## 状态与资源生命周期

UI线程捕获现有Display并Retain，后台PNG编码期间仍计入原工厂显示租约。可见需求/切书可释放自身引用，最后编码引用结束才释放实际资源；主线程归还引用。编码用有限流，输出上限64MiB，跨系统await只传独立字节，不传Bitmap/原生对象。

宿主单槽防重入。切书/改选和关闭取消尚未提交的旧图像，编码原生调用完成后清理、拒绝过期结果；关闭等待真实任务。NSPasteboard主线程准备完整PNG对象，检查取消后清空并写入public.png；清空是系统提交点，此后不中断整份写入。回调开始后不以晚取消提前报告已提交写入失败。写入失败在原状态区提示，可重试且不阻止关闭；不改变文件/阅读位置/移动历史。

## 测试、错误与扩展

12专项覆盖目录/ZIP、双页方向与首元素、分割/透明/旋转/背景隔离、单槽/失败重试/切书/关闭、动画禁用和瀑布显式选择。原生验收使用随机命名pasteboard，验证PNG真实字节/类型、非法与取消保持原值、取消排队回调不清空。后台测试宿主只泵送系统回调，不创建或激活窗口。

系统拒绝、非法PNG或超过预算明确失败。PNG签名校验是内部有限协议，不作为任意外部图片验证器。正式实际系统GeneralPasteboard、外部粘贴应用和Windows动态另验；[静默验收](../acceptance/p5-image-copy-runtime.md)分别记录自动/原生与正式构建。图像导出使用独立原导出链，不能用本功能冒充。
