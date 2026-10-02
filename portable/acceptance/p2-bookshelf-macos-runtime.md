# P2 第四批正式 Mac 运行记录

日期：2026-10-02。本机 macOS 27.0.1、Apple Silicon、Retina；Xcode 27.0（27A266a）、macOS workload 27.0.10722。固定 Windows 基线 c5c398d89；本批未执行 Windows 动态对照。

唯一正式应用：`src/NeeView.MacOS/bin/Debug/net10.0-macos/NeeView.MacOS.app`。默认输出及本地 ad-hoc 严格签名通过；没有新增 Preview 入口。

## 实际操作与结果

测试样本位于被 Git 忽略的 artifacts/p2-bookshelf-smoke，仅复制已有六张夹具图片生成两目录、两普通 CBZ 及一分章节 CBZ；没有修改用户实际图片。

| 操作 | 观察结果 |
|---|---|
| 冷启动 | 恢复原中文 CBZ 的中文/003.png、单页/RTL、信息与页面列表组合、导航器及胶片条 |
| 选择文件夹面板 | 同步到当前书父目录，混合显示图片目录及中文 CBZ，并选中当前书 |
| 打开 Book2 | 书架显示 Book2、Book10、Book3.cbz、Book20.cbz、Book30-Chapters.cbz；原目录置前及自然名称顺序生效 |
| 书架 Down，再 Enter | Down 只选择 Book10，正文仍 Book2；Enter 后才打开 Book10 |
| 查看器 Up/Down | 按书架顺序切换 Book2/Book10，Book10 恢复004.png；再 Down 打开 Book3.cbz |
| 选 Book2→进入→上一级 | 书架进入 Book2 并显示 Child，再返回父目录/选中 Book2；正文保持 Book3.cbz |
| 同步→刷新 | 回到当前书所在书架并选择 Book3.cbz，刷新仍保持选择及正文 |
| 排序降序，设置目录/归档混排 | 元数据顺序改变，当前书、页面及路径选择保持；排序控件使用关闭状态方向键操作 |
| Command+Q、进程查询、重启 | 确认进程退出；恢复 Book3.cbz 的004.png、默认降序及混排，并重新选中书籍 |
| 书架末项的查看器 Down | 保持 Book30-Chapters.cbz，提示已到末项，不循环 |
| Chapter1/002.png→NextFolderPage | 定位 Chapter2/003.png，保持当前归档，滑条3/6 |
| Chapter2/004.png→PrevFolderPage，再 PrevFolderPage | 先回本组首图 Chapter2/003.png，再回 Chapter1/001.png |
| Chapter2/003.png→NextFolderPage，再重复 | 定位 Chapter3/005.png；最后章节再执行保持5/6，不循环 |
| 最终恢复 | 默认文件名升序/目录在前、优先切书关闭；清理临时键位，恢复原中文 CBZ 第三页和组合侧栏，应用留运行 |

原文件夹页命令没有默认键位，本次通过已有键位编辑器临时配置 Meta+Shift+P/N 验收；结束后清空并保存。没有修改产品菜单或默认绑定以绕过工具。弹出窗口的部分 AX 点击/键盘输入存在工具限制，设置菜单用方向键/Enter 打开，排序用关闭状态方向键；不把所有子菜单点击标为通过。

## 验收发现及修复

书架末项提示曾持续遮蔽随后成功跳页的状态。Engine 仅在成功提交阅读导航后清除旧错误，失败、取消及过期请求保持原处理。现有回归用例补充书架边界后翻页、打开失败后章节跳页；最终构建重新启动后复测“末项提示→章节跳页”，状态正确恢复为 Chapter2/003.png。

## 证据与边界

- [最终构建/测试输出](p2-bookshelf-validation.json)：**78通过、0失败、0跳过**；Engine、正式入口 Library 检查、正式 `.app` 及本地签名分别通过，源码表45文件/24子集适配。
- [最终书架截图](p2-bookshelf-macos-runtime.png) 与 [AX](p2-bookshelf-macos-runtime.ax.txt)：最终构建、文件名升序/目录在前、归档第五页与路径选择。
- [降序混排重启截图](p2-bookshelf-macos-restart.png) 与 [AX](p2-bookshelf-macos-restart.ax.txt)：排序/阅读状态保存恢复。该截图先于提示修复，最终构建已再次重启验证章节恢复。
- [章节截图](p2-bookshelf-macos-chapters.png) 与 [AX](p2-bookshelf-macos-chapters.ax.txt)：最终构建的章节定位、列表选中及状态一致；Headless 截图 p2-bookshelf-window-layout.png / p2-bookshelf-reading-layout.png 单独保存。
- 随机稳定/循环、并发取代/晚到释放、枚举与打开失败重试、未知 JSON 和双向文件名排序主要由自动测试证明；真人触控板/IME、NAS、长期 native 内存、完整显示 P95、Windows 动态对照仍待验。

本批仅普通书架全局默认排序；每目录参数、持久随机种子、树/封面/搜索/监视和真实 Folder 页/父书定位尚未迁入。P2 尚未整体完成；本增量本地提交，无推送、远端 CI、Developer ID、公证、安装或发布。
