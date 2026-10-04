# P3 第五批：原帧级全景与精确缩放锚点

当前状态：P3开发范围已完成，见[P3收尾清单](p3-completion-checklist.md)；本文件记录该批契约和当时测量，后续接入点以下述链接为准，静默验证不等同真机/Windows封板。

原 PageFrameFactory、PageFrameContainerLayout、PageFrameBox.CreatePanoramaContentRect 和 NScroll 是规则出处。全景扩展唯一 ReaderView，复用原帧生成、变换、BitmapFactory、显示租约和 JSON；连续/瀑布仍是 Mac 展示扩展。

## 契约与生命周期

BrowseLayoutMode 保留原三个数值，新增 Panorama=3。原 Book.IsPanorama 与 MacPanoramaLayout 共用开关；未保存扩展的原配置默认原帧全景，已明确保存的连续/瀑布保持。BookOperation.IsFrameReading 统一分页和全景的主页面/历史语义。

PageFramePanorama 只构建当前帧及两侧最多各32帧，不持有像素；沿原容器方向、FrameSpace、自动拉伸及首尾视口膨胀排列。帧内单双页、宽页、分割和 dummy 判断仍来自唯一 PageFrameFactory。极端小帧可能超过65帧覆盖范围，窗口边界保留滚动余量而不作为实际书籍末端；显示需求最多128个原 Page。原 PagesAsOne 选择全景矩形，否则选择当前帧矩形；NScroll 先滚到边界，终止后才执行原页尾动作。

滚动回报选中帧沿原导航锁提交原 PagePosition，补偿新参考帧中心以保持画面；动画结束才换参考原点。同一 Page/Part 的无缝循环仍恢复表现原点。晚到需求、切书和关闭继续原 revision/租约释放链。邻近书籍页使用本帧矩阵命中封面，进入原子书打开流程。

原全景保留旋转/翻转/缩放和水平/垂直阅读方向；连续/瀑布指针缩放记录同一 Page 的页内坐标及视口点，后台布局发布后恢复该点。退出浏览清空旧 BrowseLayout，不让旧几何参与帧阅读；返回的浏览锚点独立保留。

## 验证与边界

专项覆盖水平/垂直、左右方向、原双页/分割/首末/dummy、万页有界帧、原查看器同租约/变换/模式切换，以及页尾 None/Loop。Loop 测试先走完原边界吸附，不要求首次滚动立即循环。合成色块截图可入Git；用户指定三个子目录继续只读静默实图/缩略采样，私人图留忽略 artifacts。

完整回归、正式Library/ARM64.app及严格ad-hoc签名见 p3-panorama-validation.json。Headless不证明屏幕帧率、原生焦点、Retina、多屏、触控板或Windows动态。QuickAccess/监视和页面目录/搜索随后由第六/七批交付；高级效果及媒体保持P5清单。
