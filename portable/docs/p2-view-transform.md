# P2 第十七批：原查看器变换与参数编辑

## 职责、依赖与契约

Engine迁入原PageFrameTransformMap/IShareTransformContext、数值变换与滚动限制/预置算法；ReaderTransformPresenter单独管理Mac表现矩阵及原变换图。阅读与持久化仍使用BookOperation/SaveData，不创建第二状态体系。CommandParameterEdit只持有原共享拥有者的克隆草稿，参数窗口可独立调整。

## 业务与生命周期

- BaseScale → 自动旋转 → 翻转与手工缩放 → 手工旋转 → 视口中心/平移。绘制、包围盒、导航器和书籍卡片逆矩阵命中共用这一路径。
- 原Page/Part作为变换键；默认切页清除，KeepPageTransform分别记忆。保持锁定选择共享值，切书继承同时要求普通保持和Books开关。Reset清当前书变换图，BaseScale仍是原每书设置。
- 参数化缩放跨100%吸附、旋转频率/角度归一、翻转屏幕轴及中心、六种Stretch、循环/许可、四向/预置/纯NType滚动沿原算法；纯NType终端不翻页。None在Retina只做一次设备比例换算。
- 窗口跟随保留相对于原Stretch目标的手工倍率；开关关闭回到1，保持缩放锁阻止跟随。Pan/拖动使用原ScrollLock/ScrollAreaLimit，Snap只在对应限制模式生效。
- 像素需求包含BaseScale和设备比例，解码到原尺寸封顶，放大由绘制层执行；避免无细节收益的像素放大及缓存重复。显示租约/revision沿原资源边界释放。

## 状态、错误与参数表单

缩放、旋转、四向/预置/NType/滚动翻页、步长、Stretch和Toggle参数可编辑，原共享owner及未知字段/$type继续合并保留。弹窗取消不改变父草稿，父设置取消不落盘；保存失败原地恢复Commands/View/BookSetting及运行ValidStretchMode。正常UI范围包含已存在数值，未编辑的LineBreakStopTime、EndMargin、AngleFrequency、CenterRatio和BaseScale不会被截断。PagesAsOne保留值但分页不启用P3全景。

## 测试与验收

ViewTransformTests覆盖原数值组合/map、默认重置/每页/跨书保持、参数化宿主、翻转菜单/输入区别、基准缩放Props、旋转卡片双击、导航器中心、草稿/未知字段/重启、失败回滚、Retina数值与窗口跟随。旧鼠标测试的翻页后缩放预期改为原默认1，依据原PageFrameTransform生命周期，不保留此前Mac无条件跨页缩放。

本批默认静默，专项及全量/正式Library/.app/本地签名分别记录。合成Retina数值不代表多屏真机；真实触控板、动画插值、窗口焦点及Windows动态对照独立待验。P2继续书籍控制与列表/浮动宿主，未封板。

## 扩展点

P3全景/连续布局继续共用原变换数据。主题和参数字段布局只改Mac表现，不改变缩放/导航规则。原WPF动画对象不迁入Engine，表现插值按原时长另验证。
