# P5 第三十一批：Bloom、Monochrome、ColorTone实际颜色后端

## 职责与依赖

原EffectUnit/层/预设/JSON保持；仅替换Microsoft.Expression颜色执行后端，进入唯一ImageEffectRenderer/Skia颜色滤镜链。固定DLL只用于只读核验，不进入Mac发布包；没有D3D兼容层、新语言、依赖或图像资源体系。XAML和参数表单未变。

## 原行为依据

固定原资源bloom.ps（SHA256 33f9368b767de284b314fdfb787b47692ad9b8a500d62232c5ab6d2b6ad535c6）、monochrome.ps（1b6713ec063d290e8952419a9cada9a472881b5a51759546c6e2f8fdebd51d47）、colortone.ps（e599919def63a1351a4c54b6812145bf5ec9a1bf963283016fc7657e35a0126b）为ps_2_0。仓库外标准PEReader/离线token解码核验常量、指令和managed回调；核验材料在`/Users/yuwk/.codex/artifacts/neeview/expression-resource-audit/`与`expression-il-audit/`。它们不是产品依赖。

Bloom：c0=(BaseIntensity,BloomIntensity)，c1=(BaseSaturation,BloomSaturation)，c2=Threshold；先阈值饱和，再按0.30/0.59/0.11调饱和/强度，原图乘(1-saturate(bloom))后加bloom。运算包含alpha四分量；不能将它替换为高斯模糊光晕。阈值1时归一输入均不产生高亮，避免0乘无穷在不同GPU产生NaN；负阈值沿原setter保留。

Monochrome：同一0.30/0.59/0.11灰度乘filterColor RGB，源alpha乘filterColor alpha。ColorTone：先RGB乘LightColor，再以该结果求灰度及Desaturation；灰度混合Dark/Light，最后按ToneAmount混合，RGB再乘源alpha，源alpha保持。颜色常量沿原WPF ShaderEffect.ConvertValueToMilColorF的R/G/B/A除255，不使用scRGB或Rec.709替代。

## 资源、状态与错误

三类各缓存一份编译结果，每份参数快照只预检一次；参数变化沿现有快照失效。运行逆层序，组合现有ColorFilter/SaveLayer；不改源Bitmap、不复制像素、不重新解码。scene-graph/离屏资源仍按原显示引用计数释放，128MiB工作表面限制保持。无效参数/编译失败显式报告，View导出不能成功写出原图冒充效果；CopyImage始终为源图像。

## 验收与扩展

像素专项核验原权重/打包分量、去色/调色顺序、阈值端点及半透明四通道输入。正式窗口集成核验三类真实View导出、源复制、源文件/位置保持、JSON保存恢复及关闭归零。全量、正式Library/ARM64和签名分别记录。仍未完成的Blur/Embossed/Pixelate/Sharpen/Magnify/Ripple/Swirl继续显示待迁，不以近似算法冒充；Windows动态/真实GPU透明边界、长期性能另验。
