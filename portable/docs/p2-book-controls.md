# P2 第十八批：原书籍锁定、页尾与关闭

## 职责、依赖与契约

沿原BookHub锁定/Unload、BookPageTerminator及PageFrameContext循环开关接入唯一BookOperation。PageEndDialogAsync只返回原三种选择，不接受界面类型；Mac独立呈现弹窗。原PageEndAction、ResetNextBookPageMode、PageFrameOrientation数值保持。

## 业务与生命周期

- 锁定按后端解析的书籍地址拒绝跨来源，同地址/同目录图片重载允许；提交前及历史事务回调再次检查锁定。Toggle命令保留菜单与固定On/Off参数区别，锁状态仅进程内。
- None提示首页/末页；Loop按方向定位首/尾；SeamlessLoop直接开启原PageFrameFactory.IsLoopPage，保留跨端双页而非普通clamp；Dialog提供下一书/循环/保持位置，重复终止不重入。
- 页尾NextBook使用原书架选择和Reset/Continue/None三策略，普通前后书不强制重置。延迟弹窗返回后核对原书、代次及位置，失效选择不改新页。
- Unload无条件解锁并取消打开/防抖；在原导航锁下保存历史和清除LastBookV2，成功才清正文、临时选择和来源。服务不Dispose，可再次打开；失败保留当前来源供重试。不可中断的晚到来源仍释放且不得提交。
- 页帧方向保存到原Book.Orientation，与双页横排及BookReadOrder分开。分页默认原PageMoveDuration=0，方向命令不旋转图像；连续/全景容器滚动方向在P3接同一字段，非默认动画插值尚待表现完善。

## 状态与错误

四JSON事务保留未知字段；明确关闭清除原启动快照，退出保存不删除历史。设置页提供页尾/位置/循环提示/帧方向草稿，经原ApplyOptionsAsync失败回滚。循环通知独立于来源失败；取消对话框不添加末页提示。关闭窗口主动结束页尾弹窗，解除宿主回调。

## 测试与验收

BookControlTests覆盖锁定/同书重载/同目录图片、两方向循环/无缝双页、指定步长端点、三种页尾切书策略、锁定NextBook、弹窗晚到/重复、Unload失败重试/晚到来源释放/重开、正式宿主参数/菜单/方向/弹窗/显示租约释放。采用真实临时目录/CBZ、正式Avalonia控件和独立Headless图，不操作用户数据。

全量构建和本地签名独立记录；Windows动态、真实弹窗焦点/触控板/Retina、NAS、动画插值及性能仍不由Headless替代。P2继续书架书签互联、历史与浮动宿主。

## 扩展点

P3连续/全景仍使用原Page/Frame与Orientation，不新增阅读内核。高级媒体/幻灯片页尾由原接口继续迁入P5。
