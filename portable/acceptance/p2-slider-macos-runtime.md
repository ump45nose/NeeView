# P2 第七批：页号与滑条正式 Mac 运行记录

日期：2026-10-03。唯一正式入口 `NeeView.MacOS.app`，默认Debug/net10.0-macos输出；macOS ARM64。本批验收运行正式构建，不使用Preview。自动测试/构建与运行证据分别记录。

## 原行为与本批范围

保留原底部SliderTextBox、一起始转换、raw页选择与SliderConfig字段；原阅读规则和唯一BookOperation不另建。Enter提交后继续编辑，普通失焦及Escape返回查看器均提交；非法文本确认已有选择，0及极大数字限制到首尾。双页输入不采用滑块对齐。外观参数独立于阅读/来源/解码。

原46.3页标记属于全局播放列表与Pagemark.nvpls，本批不以Book私有字段代替。完整自动隐藏、全局显隐命令及播放列表入口保持禁用/未迁状态。

## 两次正式构建的运行结果

首次构建覆盖核心交互，最终构建修复主题裁切并重点复测。事件明确标注`slider-initial`和`slider-theme-final`，见[22份AX/截图事件](p2-slider-runtime-events.json)。没有把两版本的运行检查合并称为最终版全量UI重跑。

| 场景 | 实际观察 | 证据 |
|---|---|---|
| 默认右侧页号，Enter | 输入5后正文第五页，输入框继续编辑 | [截图](p2-slider-enter-five.png)、[AX](p2-slider-enter-five.ax.txt) |
| Escape | 输入2后Escape，正文第二页，编辑框隐藏并返回查看器 | [截图](p2-slider-escape-two.png)、[AX](p2-slider-escape-two.ax.txt) |
| 非法/范围/失焦 | abc保持第二页，0到首图，1e20到末图；输入3点击地址栏后提交第三页 | invalid-keeps-two、zero-to-first、large-to-last、blur-three AX |
| 中文ZIP/双页 | 完整中文路径成功打开；指定第四页，双页再输入2仍定位第二页 | [双页截图](p2-slider-double-raw-two.png)、open-chinese-zip/zip-four AX |
| 取消设置 | 将位置改左后取消，主窗口仍为右侧 | settings-cancel-right AX |
| 最薄滑条 | 15 DIP、左侧页号，滑块完整位于槽内 | [最终截图](p2-slider-final-thin-left.png)；Headless另断言Thumb上下边界 |
| 表现设置 | 保存左侧/37 DIP/Opacity=.65/跟随书籍方向，仍在原第二页；RTL滑条视觉反向，页号仍2/6 | [设置截图](p2-slider-final-settings-selected.png)、[主窗口](p2-slider-final-left-opacity-rtl.png) |
| 最终版数字输入 | RTL视觉下输入3并返回查看器，正文/页号第三页 | [截图](p2-slider-final-rtl-number-three.png) |
| None与总开关 | None隐藏页号但保留滑条；关闭IsEnabled隐藏整个滑条，正文/胶片条保持；再次打开恢复左侧页号 | final-number-none/final-disabled/final-enabled-left AX及PNG |
| 退出/重启 | 退出文件保存第三页及原滑条字段，未修改JSON直接启动恢复第三页、左侧37 DIP透明滑条 | [重启截图](p2-slider-final-restart-preserved.png)、[状态核对](p2-slider-runtime-state-checks.json) |

真机截图发现Fluent默认轨道前后固定留白使25 DIP槽内滑块被裁切。最终主题通过官方资源key取消留白、缩小滑块，未复制整套模板；15 DIP及原25 DIP恢复截图均完整。依据[官方12.1.3 Slider主题](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Avalonia.Themes.Fluent/Controls/Slider.xaml)。37 DIP槽的额外高度和透明度按配置生效。

## 数据保护与还原

正常退出后备份本批验收前的UserSetting.json、History.json、Bookmark.json至Git忽略的`artifacts/p2-slider-smoke/state-before`，保留原SHA256。退出后的临时状态另存state-after-exit及state-before-restore。还原前再次验证备份，确保应用已退出，临时文件fsync后原子替换三文件，逐字节及SHA256均相同。

临时Slider.FutureSliderProbe在保存后保留；自建六PNG与来源夹具一致，中文CBZ条目与PNG字节一致。临时设置和历史访问已通过三文件还原撤回。[状态检查](p2-slider-runtime-state-checks.json)仅记录核对结果，不提交完整用户JSON或备份。

最终启动恢复原`漫画.cbz`的`中文/003.png`、3/6、单页/从右向左、左侧信息＋页面列表组合/比例和右侧导航器，应用留运行，见[还原截图](p2-slider-final-user-restored.png)。正常启动会保存新访问时间；哈希一致性是在还原完成、重新启动之前核验，不要求运行后的文件保持旧时间戳。

## 自动验证与边界

最终 **107通过、0失败、0跳过**；包括本批6项专项、原规则和资源回归、正式XAML/设置/输入/滚轮路由及15 DIP滑块边界。Engine、正式入口Library、正式.app构建、本地ad-hoc严格签名全部通过，见[p2-slider-validation.json](p2-slider-validation.json)。无输出目录冲突或切换。

页号/滑条两种滚轮模式、细增量累积、旧书请求及退出草稿由正式Headless事件与Engine测试验证；未以本次真机运行声称真人设备滚轮通过。合成文本输入也不能替代真人IME：一次中文路径typeText结果不完整，保持旧书并明确报错，另用完整文本setValue后打开成功；失败探索单列`p2-slider-chinese-path-input-failed.ax.txt`。一次paste工具超时先观察再用setValue；菜单AX单击不可靠时使用已聚焦菜单的Down/Return，没有修改产品绕过工具限制。

Windows动态布局/交互对照、真人鼠标/触控板/IME、Retina像素1:1、多屏、Finder/NAS、长期原生内存及完整显示P95仍待验。P2尚未封板；没有推送、远端CI、Developer ID、公证或发布。原Windows源码、旧验收PNG和用户`.DS_Store`保留。
