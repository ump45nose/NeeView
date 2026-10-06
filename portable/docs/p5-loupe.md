# P5 第三十三批：原查看器 Loupe

## 职责与原出处

迁入原 LoupeConfig 十二字段、LoupeContext 线性倍率及 LoupeDragTransformContext 开启基点/相对位移。原 MouseInputLoupe 的五命令、范围变化退出与全景例外分别适配到正式 MainWindow/ReaderView。Magnify 图像效果继续独立，不代替查看器 Loupe。

## 依赖与契约

Engine 只保留配置、倍率和纯坐标公式。Mac 表现拥有独立变换、鼠标起点及临时输入租约；Backends 通过 IPlatformInput.BeginRelativePointer 提供应用局部的窗口输入。没有第二阅读模型、JSON 分支别名、宿主或全局鼠标监听。

默认倍率2、最小1、最大10、步长1、速度1。保持原五位舍入，Step 非负；UI 建议范围包含导入值，不排序/截断原上下限。以原图为基准时使用当前主元素 Scale×帧 Scale×普通倍率×设备比例（原 BaseScale 由现有表现适配计入）。非法显示值回退不改写配置。

开启时 v=pointer-viewportCenter，base=center?-v:-v+v/fixedScale，之后保持 base。每次位移 point=base-delta×Speed，按设备像素舍入；矩阵先平移、再缩放。静态分页在现有内容变换之后、视口中心平移之前加入 Loupe；动态全景对包括帧间位置的完整画布加入同一中心变换。连续/瀑布是 Mac 扩展，使用逆矩阵查询可见需求，不改其滚动、列宽或原 Page 锚点。

## 状态及资源生命周期

开启时取得隐藏光标的相对鼠标租约。AppKit 局部监听仅消费实际 key window 的 MouseMoved/Dragged，源窗口句柄必须与当前应用关键窗口一致。失焦/关闭通知立即归还关联及 hide/unhide，并通知表现层同步退出；释放通知与移动回调均按开启代次拒绝旧结果。切到其他应用时不 warp 指针。设备坐标采用 CGEvent 的全局逻辑点，不把 Retina 比例再次乘入系统指针坐标。缺少官方绑定的 CoreGraphics C API 采用直接 P/Invoke，未新增原生工程。

关闭、失焦、切书、卸载、浏览模式变化和后端替换都会释放捕获。旧租约回调带开启代次，不能更新重启后的 Loupe。默认页范围变化退出，原全景例外；ResetByPageChanged=false 继续保留。退出不修改普通缩放/平移，倍率留待下次开启，仅 ResetByRestart 重置。

倍率进入既有 BitmapFactory 解码规格、显示租约和取消链。全景/浏览相对移动刷新合并为一个循环，最新需求取代旧等待；旧 native 解码按原 revision 释放。关闭回到普通规格。JSON 保存复用唯一五文件事务、未知字段合并及默认差分；设置草稿、搜索、取消及失败原地回滚共用原设置入口。

## 输入与错误

原五命令名称和 ToggleMode 保持；倍率命令仅启用时可执行。垂直滚轮默认改变 Loupe，横轴/禁用开关交还绑定，文本/菜单/popup/对话框作用域优先。仅允许的无修饰 Escape 退出。原生捕获失败明确报告，回收已准备资源。Headless 不接触真实鼠标，通过注入的租约核验释放和过期回调。

## 测试及扩展

自动回归包含默认/导入舍入、逆序上下限、固定开启基点、设备舍入、原图倍率、独立普通变换、页范围/全景/切书、五命令、ToggleMode、设置取消/保存/回滚、未知字段及浏览查询。正式源码/ARM64 构建、签名及无窗口原生测试分开记录。

真实相对指针、Retina/多屏退出落点和 Windows 动态对照未据此验收；触控板仍按用户要求跳过。视频/脚本/resize 和其余 P5 清单继续迁移。
