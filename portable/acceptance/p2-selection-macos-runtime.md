# P2 第三批正式 Mac 运行记录

日期：2026-10-02。本机 macOS 27.0.1、Apple Silicon、Retina；Xcode 27.0（27A266a）、macOS workload 27.0.10722。固定 Windows 基线 c5c398d89，本节点未运行 Windows 对照。

唯一正式应用：`src/NeeView.MacOS/bin/Debug/net10.0-macos/NeeView.MacOS.app`。使用默认输出，本地 ad-hoc 严格签名通过；Developer ID/公证/发布未执行。

## 实际运行观察

| 操作 | 实际结果 |
|---|---|
| 设置→胶片条/滑条 | 显示、页码、居中、宽128 DIP及共享步长2保存生效，胶片条保持原底部插槽 |
| 正文005.png，胶片条 Left | 滑条变6/6，正文及信息仍005.png；Enter后才变006.png |
| BackSpace / Shift+BackSpace | 正文005.png / 006.png后退及前进 |
| 输入编辑器筛选JumpPage，临时Meta+J | 指定页对话框可真实打开；输入3并Enter定位003.png；临时键位已清空恢复 |
| 打开中文CBZ，再Alt+Left / Alt+Right | 按打开顺序恢复目录003.png、CBZ中文/004.png及各自阅读设置 |
| BackSpace / Shift+BackSpace跨书 | 重放目录003.png与CBZ中文/004.png，保持前进分支 |
| 点击首图并Ctrl+1 | 正文中文/001.png单页，胶片条所选首图居中，右侧留白；方向仍从右向左 |
| 实际拖动滑条从首图至第三页并释放 | 正文定位中文/003.png，滑条3/6 |
| Command+Q，进程查询，重新启动 | 进程确实退出；恢复CBZ中文/003.png、单页/RTL、原组合侧栏和胶片条设置 |

验收发现重开后页面列表丢失当前项高亮，已修正为先替换 ItemsSource 再恢复选择，并补实际打开/切书的 Headless 身份断言。最终正式构建重新启动后，页面列表中文/003.png 明确处于 selected 状态，正文、胶片条及滑条3/6一致；单页/RTL和原组合侧栏保持，截图与AX证据已更新。

## 证据与限度

- [构建/测试输出](p2-selection-validation.json)：64通过、0失败、0跳过；Engine、正式入口Library检查、正式.app及本地签名分别通过。
- [正式应用重启截图](p2-selection-macos-runtime.png) 与 [AX状态](p2-selection-macos-runtime.ax.txt) 保存最终运行状态，Headless [布局图](p2-selection-reading-layout.png) 单独保存。
- 三种滚轮、临时选择不翻正文、滑条按下/释放前后、端点半页不变、加载失败/被取代不动历史游标及访问排序保留均由专项自动测试验证。
- 原默认菜单占位和新增指定页/历史入口通过AX观察。子菜单合成输入的焦点存在工具限制；指定页对话框通过临时可配置键位验收，不把入口观察写成全部菜单点击通过。
- 首尾居中、方向键确认、拖动释放、配置保存/重开和跨书历史在正式应用中操作并观察；真实鼠标/触控板设备特征、惯性、Windows动态对照、NAS、长期native内存及P95未验。

应用留在运行状态供用户确认。本节点本地提交；P2尚未整体完成，无推送、远端CI或发布。
