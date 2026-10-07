# P5 第二十四批：原版本窗口

后续[兼容收尾](p5-compatibility.md)已用注入服务接通Mac公开发布检查；以下待接入说明为第二十四批历史范围。当前版本窗口尊重原网络许可，不自动安装。

## 职责、依赖与出处

固定基线c5c398d89的OpenVersionWindow、VersionWindow.xaml/代码、VersionWindowViewModel及原App.ico。Mac保留图标、名称/版本、更新区域与底部链接布局；ViewModel仅持有表现/系统动作。Engine增加实际系统URI替换点，Backends使用NSWorkspace，正式启动与菜单仍唯一。

## 契约与业务规则

原菜单命令和macOS“关于NeeView”进入同一owner/CenterOwner/ShowDialog窗口。重复命令复用附属窗口，Escape关闭，Control+C或Command+C及原上下文菜单复制完整版本信息。图标直接取原ICO内的256px PNG，不重绘。

名称标识独立Mac维护；显示版本与复制的构建版本来自实际Engine程序集，与生产Version属性保持同源。复制信息包含固定Windows基线、实际运行时/系统/架构，不伪称46.3 Windows二进制。许可打开随包LICENSE.md，项目指向用户fork，原项目单独保留链接。开发与Release包均包含许可；Release实际依赖许可仍由既有打包清单生成。

原VersionChecker检查neelabo的Windows发行包，Mac未发布对应版本；更新检查和更改记录区域保留待接入占位，不把Windows版本号比较结果当成Mac更新。不新增在线升级后台服务或网络请求。

## 状态、生命周期与错误

表现动作单槽，失败显示消息并允许重试；关闭取消尚未提交动作，晚到结果不更新已关闭窗口。主退出通过OwnedWindows关闭附属窗口；不修改Config、历史、当前书或缓存。纯文本复制用Avalonia正式系统剪贴板适配，文件剪贴板业务保持独立。

URI仅允许http/https和本机file，系统拒绝与许可不存在返回真实错误；不用shell。界面结构在AXAML，动作与元数据在独立VM，AppKit只在后端，不把平台对象传入阅读业务。

## 测试、验收与扩展

6项专项覆盖实际版本/完整复制、精确链接、失败重试、关闭/晚到、原结构/键位和主菜单附属窗口/退出；Headless使用内存剪贴板。4项macOS后台入口用例验证非法协议/远程file/取消/许可不存在，不启动浏览器或用户应用。完整结果及截图见[静默验收](../acceptance/p5-version-window-runtime.md)。

实际浏览器/Finder链接与真机系统剪贴板待单独验收；Mac更新源在真实发布后接入，不能把版本窗口入口计数当成完整更新功能覆盖。
