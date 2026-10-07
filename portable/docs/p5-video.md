# P5 第三十五批：原视频链路

## 职责、依赖和出处

原 MediaArchive/MediaArchiveEntry、MediaPageContent、MediaPageMoveControl、ViewContentMediaPlayer、PageMediaContext 和 IMediaPlayer 的来源、导航、播放意图及控制关系保留。Engine 定义平台无关视频边界；Backends 使用官方 AVFoundation/CoreVideo；MacApp 只在装配处创建实现，ReaderView 和媒体条使用 Engine 契约。三个生产项目、唯一阅读链和 JSON 不变。

## 契约及业务规则

直接打开媒体文件为完整单条目的单页媒体书。PageSeconds 默认10，仅用于翻页秒数，不切出虚拟页面。普通目录/ZIP 的媒体页默认关闭，打开后沿原 Page/页框导航；媒体书默认启用，递归不展开媒体书。首末命令改变同一播放器时间，旧 JSON 不新增视频时间位置。

原扩展名 .asf/.avi/.mp4/.mkv/.mov/.wmv 保留；扩展名资格不保证系统 codec 支持。播放器输出真实当前帧、音频、时钟及 seek；不以静态封面或壁钟模拟视频。默认音量 .5、静音关闭、循环关闭；根媒体书延迟 .5秒，普通页 .02秒。循环配置分别来自 Archive.Media / Image.IsMediaRepeat。原媒体缩略图保持播放图标占位。

只选中页框的第一个非 dummy 元素获得音频；全景中的其他可见视频继续播放但禁用音频。离屏禁用实际时钟，保留 IsPlaying 用户意图。延迟期间用户 Pause 不被延迟结束重新 Play 覆盖。媒体条音量和 seek 串行保留最后编辑值，绑定回显不写配置，切书后拒绝旧播放器编辑。

## 状态和资源生命周期

播放器拥有 AVAsset/AVPlayerItem/AVPlayer/VideoOutput、通知及实体化租约。Apple 调用回系统主线程，seek/帧读取/EOS/关闭经同一异步门串行；EOS通知只排队，不在主线程阻塞门。EOS先增加计数再发事件，回调请求关闭后不再发结束事件或回写位置。循环约1ms位置重播。

暂停 resize 仍输出同一时钟的当前帧；显示请求改变不重开播放器。页版本变化或退役关闭播放器；首帧取消后的新代次可以复用已准备播放器，旧结果只释放。关闭故障保留所有者及材料，可再次等待真实关闭。原生释放完成才归还工厂共享工作预算，临时材料清理故障可单独重试。

原生工作预算最多256MiB且不超过主图工厂预算，估算三份 CoreVideo 缓冲、一次源行复制及最大输出；所有播放器共用，不能按页累加。真实尺寸再次校验，BGRA帧/显示资源计入原 BitmapFactory。sRGB颜色输出、track方向及像素宽比进入原 PageDataSource；CPU变换/下采样有取消检查。预算不等于 RSS 上限。

## 错误、测试与扩展

取消、损坏/不支持codec、准备超时、原生播放失败及超工作预算返回真实错误；不删除索引或覆盖新书。资格设置改变沿原 OpenCore 重新收集并保持条目，音量/循环/延迟不扫描。未编辑的极端旧数值不因表单范围而改变，Windows VLC/字幕和未来字段仍保存。

Engine/正式Headless测试验证原默认、差分、导航、设置失败回滚、多媒体音频、延迟暂停、晚到帧/新代次、关闭重试及共享EOS幻灯。无窗口官方绑定测试编码明确sRGB/709的合成H264，再验证真实播放/暂停/seek/resize/EOS、旋转、共享预算、ZIP材料及EOS内关闭。合成视频测试不是Windows动态或用户codec集合验收。

AVPlayer系统支持之外的容器、VLC完整codec、音轨/字幕选择尚未迁入；配置字段保留且能力明确。触控板、真实声音、长视频性能、Windows动态、长期内存和正式分发分别验收。脚本及其余P5继续推进。
