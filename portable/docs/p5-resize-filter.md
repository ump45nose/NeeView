# P5 第三十四批：原缩放滤镜

## 职责、出处和依赖

保留原 ImageResizeFilterConfig、UnsharpMaskConfig 和 ResizeInterpolation 的属性、通知、默认值、枚举顺序及六分支效果预设关系。MagicScaler 0.15.0 的固定提交为 `9af284f48de413e52e7b7aa7076fcaa3bc0d9d7c`。MIT 核公式及完整许可登记到 source-migration；WIC、MagicScaler 包及新原生工程不进入 Mac 构建。

Engine 只持有配置和不可变参数。Backends 的既有 Magick 解码器承担静态图像处理。原效果面板位置、独立草稿及事务保持；没有新增配置入口或图像服务。

## 契约及状态

默认总开关关闭、Lanczos、锐化开启、Amount=40/Radius=1.5/Threshold=0。Radius 五位舍入；配置 setter 不按编辑器范围裁剪导入值。Threshold 保留原 byte 转换。分支、嵌套锐化和预设的未知 JSON 字段保留；保存使用唯一 MergeTyped/差分事务，失败恢复运行值。

ToggleResizeFilter 保留原命令名、默认输入和 ToggleMode，菜单操作切换。普通/全景/连续静态页面及 View 导出需求携带参数快照，规格比较与工厂缓存键均包含全部执行参数。晚到需求沿原 revision/取消/租约链裁决；源字节导出和 CopyImage 不因此修改原材料。

缩略图、瀑布缩略预算、动图帧和 PDF 原生栅格仍采用其已有处理路径，本批不声明这些后端执行 MagicScaler 滤镜。原 Picture 的非空目标尺寸/HighQuality 规则对应静态需求；后续后端需逐项补齐与 Windows 动态对照。

## 核与资源生命周期

十一种核共用一个分离卷积算法：Nearest 保留点采样，Average 为 Box，Linear 为 Triangle，Quadratic 保留 r=1；Hermite/Mitchell/CatmullRom/Cubic 的 B/C 分别为 0/0、1/3与1/3、0/.5、0/1；CubicSmoother 为 0/.625、窗口1.15；Lanczos 三瓣；Spline36 保留原三段系数。

权重按目标像素中心生成，边缘按实际存在的源样本归一。sRGB 正反转换采用标准分段函数；线性光预乘 alpha 累积后再输出 BGRA8，透明像素的隐藏 RGB 不混入边缘。源行先转换一次，滑动行缓冲复用，避免每个重叠 tap 重复转换。

源/输出/备用锐化输出、权重、累加行和滑动行纳入 128MiB 托管工作预算，不创建源高度×目标宽度的完整浮点中间面。Magick 原生工作限额独立保留，不把二者当作进程 RSS 上限。按行检查取消；不能安全处理的尺寸明确失败。安全 JPEG 不先粗采样；超预算 JPEG 继续原安全采样保护。

## 锐化替换与兼容边界

后端复用现有 Magick UnsharpMask，sigma=Radius、amount=Amount/100、threshold=Threshold/255，仅处理 RGB。Amount 非正时沿固定0.15.0的缩放率默认规则解析。非法类型/半径或工作预算超限返回错误，不写回配置。

原核方程相同不意味着整条 WIC/MagicScaler 管线逐像素相同：边界处理、编码、Q8舍入和 Magick RGB 锐化与原亮度 mask 有差异。固定 Windows 样本对照继续待验；不把同名滤镜或合成测试作为等价证明。

## 测试和扩展

覆盖所有核的常量/通道守恒、透明边缘、区分原核与近似的样本、取消/源预算、默认/导入精度/未知字段、参数通知、缓存规格隔离、原命令和面板失败回滚。指定图片只读，三次后端耗时单独保存，不代表首图或屏幕 P95。正式 Library、ARM64、签名、发布包许可及真机/Windows 分开记录。

视频、脚本、完整设置和其余 P5 清单继续推进；本批不代表 P5 完成。
