# P5 第三十二批：整阅读视口与空间效果

## 职责、依赖与原出处

保留原 EffectUnit、层、参数、预设及唯一 JSON；将 Embossed、Pixelate、Sharpen、Magnify、Ripple、Swirl 和 Blur 接到既有 Skia 后端。至此十四类原效果均有实际实现。Magnify 是效果参数，不是查看器 Loupe。

原 PageFrameBox 将整个 ScrollViewer 包入 EffectPanel，原 ViewImageExporter 同样处理合成后的画布。collection[0] 最外层，实际执行逆序。分页、双页、原帧全景、连续/瀑布以及 View 导出都先合成输入，再按同一逆层序处理。窗口画布背景与导航 UI 在效果外；页背景、书籍卡片、缺图提示和 Image 网格在输入中。导出沿原 contentCanvas 排除网格，CopyImage 保持源图。

六类空间公式由固定原 Expression DLL 的 shader 指令、常量及 managed 参数打包核验后实现；原 DLL 与 bytecode 不进入 Mac 包。Blur 使用固定 dotnet/wpf 提交 e89a851c5792c8777a7115cc7a797fd0c045fdd4 的官方离散核，MIT 原文、源码指纹和目标登记在 source-migration.json；开发与发行包保留许可证，发行 dependencies.json 单独登记源码算法来源。

## 契约与业务规则

- Sharpen 保留 opposite diagonal、Height×.001 和中心 alpha；默认 Height=.5 不改。Embossed 保留 R+G+B/3 的原权重。
- Pixelate 使用已合成面设备像素的 ddx/ddy、奇数行半格偏移及 p≥1 的 .999 替代。Magnify 保留原半径边界和 cosine 过渡；Ripple/Swirl 保留原 atan2 多项式、象限和角度归一化；零中心显式避免 NaN。
- 原 ShaderEffect SamplingMode.Auto 在 WPF GPU 为 bilinear，在 CPU 为 nearest（官方 ShaderEffect.cpp 627–640、1096–1103）。按实际 GrContext 选择，不强制统一两后端。软件采样测试不代表真 GPU 已验。默认 Sharpen 的小偏移在 CPU 可能合法地没有像素变化；实图测试使用 Height=10。
- 原 Blur 先截非负逻辑半径，再按较小设备缩放截设备半径，最大100；sigma=radius/3。权重先 float、double 累加，每项加相同修正，不能改为除总和。半径1有负边权。两次一维整数采样、透明扩边，累计边界保持中心位置。
- 原图绘制质量沿 Avalonia 12.1.3：None nearest，Low linear，Medium linear mipmap，High 上采样 Mitchell、下采样 linear mipmap；不拿图像插值配置替代效果输入采样。
- 输入 clip 与外层 Blur 输出边界分开；外层 operation 扩边，分页/浏览不在提交前裁掉外扩输出。

## 状态、资源和错误

UI 线程同步记录 SKPicture，只捕获已生成的绘图与纯参数快照。渲染线程不访问视图、布局或配置；无效果时保持原普通绘图路径。像素通过 SKImage.FromPixels 借用并固定数组，原生 release callback 持有额外显示引用，picture/GPU 真正归还后才释放，继续计入同一 BitmapFactory 预算。关闭、重绘和失败记录均释放引用。已被合成链替代的逐图空间 shader 路径删除。

每层源/目标设备像素 BGRA 工作表面的显式字节合计最多128 MiB；不把此值声明成原生驱动、shader、picture 或进程 RSS 总限额。Blur 核最多101×2份缓存。参数错误、工作预算或记录失败返回明确错误，屏幕显示失败块和说明，View 导出显式失败，不能悄悄输出无效果原图。

## 测试和扩展

专项覆盖六类公式、原逆序、双页接缝、Blur 透明间隙与外扩/累计中心、半页/旋转、1×/2×设备几何、非零原点、外部 opacity、页背景、两种 Auto 子像素策略、记录失败和 scene-graph 生命周期。实际 View 导出、源 Copy、源文件和位置保持、JSON 重启、瀑布重排/切书/关闭沿同一正式控件验证。

用户挂载目录只读挑单样本，八类效果的尺寸/裁剪 View 导出输出均变化、源文件哈希与时间保持。结果和完整构建见 [验收](../acceptance/p5-spatial-effects-runtime.md)。真实 Windows 动态、GPU 透明边缘、长时间原生内存独立待验；resize、查看器 Loupe、视频、脚本及完整设置继续迁移。
