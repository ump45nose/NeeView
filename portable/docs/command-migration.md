# 完整命令迁移表

固定基线235个原命令实例全部保留。当前为 **208个执行入口接入、27个能力占位**；入口登记不是完整功能覆盖率。原帧全景、普通/内容目录树显隐及页面/书架/正文焦点已接入；脚本On/Off/ByMenu等完整参数仍在P5，见[P3清单](p3-completion-checklist.md)。Mac额外打开目录/窗口入口不计入原235项。

原命令名、默认键位、方向手势、参数及菜单节点继续保留。未迁入口显示禁用/能力提示；已迁入口的参数、来源和交互限制以模块契约及验收记录为准。实际键位/手势按原A/B/C、阅读方向和用户差分计算。

对照[P4运行导出](../acceptance/p4-completion-commands.json)及[P5导入证据](../acceptance/p5-profile-apply-evidence.json)、[输入契约](modules/M05.md)。ToggleVisiblePageSlider与ToggleHidePageSlider语义独立，前者及ToggleVisibleAddressBar已于第二十一批接入。参数拥有者映射不等于执行能力；P4分类/P5高级能力继续逐项迁移。

P5第三批迁入原旧命令/numbered实例及上下文菜单改名、Type/Value参数、默认键位冲突和参数退役规则。预览显示升级后的清单，实际应用使用同一候选。没有新增执行入口，Cut仍按用户要求占位，见[兼容契约](p5-legacy-compatibility.md)。

| 原命令 | 文案 | 默认输入 | 默认方向手势 | 执行入口 | 迁移说明 | 原出处 |
|---|---|---|---|---|---|---|
| LoadAs | 打开文件 | Ctrl+O |  | 已接入 | P1 宿主适配 | NeeView/Command/Commands/LoadAsCommand.cs |
| LoadRecentBook | 最近使用的书籍 |  |  | 已接入 | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/LoadRecentBookCommand.cs |
| ReLoad | 重新载入 |  | UD | 已接入 | P1 宿主适配 | NeeView/Command/Commands/ReLoadCommand.cs |
| Unload | 关闭 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/UnloadCommand.cs |
| OpenExplorer | 在资源管理器中打开 |  |  | 已接入 | P1 宿主适配 | NeeView/Command/Commands/OpenExplorerCommand.cs |
| OpenExternalApp | 在外部应用中打开 (简单) |  |  | 已接入 | P5 原外部应用/参数/来源策略；见p5-external-applications.md | NeeView/Command/Commands/OpenExternalAppCommand.cs |
| OpenExternalAppAs | 在外部应用中打开 |  |  | 已接入 | P5 原外部应用/参数/来源策略；见p5-external-applications.md | NeeView/Command/Commands/OpenExternalAppAsCommand.cs |
| CutFile | 剪切文件 | Ctrl+X |  | 占位 | 用户选择暂保留禁用；移动使用分类/移至文件夹 | NeeView/Command/Commands/CutFileCommand.cs |
| CopyFile | 复制文件 | Ctrl+C |  | 已接入 | P4 原页组/普通目录实体及归档四策略剪贴板；内部目录提取为原版TODO并提示，Mac链接复制接入 | NeeView/Command/Commands/CopyFileCommand.cs |
| CopyImage | 复制图像 | Ctrl+Shift+C |  | 已接入 | P5 原图像源复制/宿主适配 | NeeView/Command/Commands/CopyImageCommand.cs |
| Paste | 粘贴 | Ctrl+V |  | 已接入 | P4 QueryPath优先/单来源及多项临时列表加载；图片/HTML/HTTP(S)接收接入 | NeeView/Command/Commands/PasteCommand.cs |
| CopyToFolderAs | 复制到文件夹 |  |  | 已接入 | P4 原页组/目录固定复制及归档LimitedRealization；目录整体覆盖确认，内部目录提取提示原版TODO | NeeView/Command/Commands/CopyToFolderAsCommand.cs |
| MoveToFolderAs | 移动到文件夹 |  |  | 已接入 | P4 普通目录分类/原多页策略 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| MoveToDestinationFolder1 | 移动到文件夹 1 | 1 |  | 已接入 | P4 普通目录分类/原多页策略 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| MoveToDestinationFolder2 | 移动到文件夹 2 | 2 |  | 已接入 | P4 普通目录分类/原多页策略 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| MoveToDestinationFolder3 | 移动到文件夹 3 | 3 |  | 已接入 | P4 普通目录分类/原多页策略 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| MoveToDestinationFolder4 | 移动到文件夹 4 | 4 |  | 已接入 | P4 普通目录分类/原多页策略 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| MoveToDestinationFolder5 | 移动到文件夹 5 | 5 |  | 已接入 | P4 普通目录分类/原多页策略 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| MoveToDestinationFolder6 | 移动到文件夹 6 | 6 |  | 已接入 | P4 普通目录分类/原多页策略 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| MoveToDestinationFolder7 | 移动到文件夹 7 | 7 |  | 已接入 | P4 普通目录分类/原多页策略 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| MoveToDestinationFolder8 | 移动到文件夹 8 | 8 |  | 已接入 | P4 普通目录分类/原多页策略 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| MoveToDestinationFolder9 | 移动到文件夹 9 | 9 |  | 已接入 | P4 普通目录分类/原多页策略 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| UndoDestinationMove | 撤销目标文件夹移动 | Ctrl+Z |  | 已接入 | P4 分类首批 | NeeView/Command/Commands/UndoDestinationMoveCommand.cs |
| RedoDestinationMove | 重做目标文件夹移动 | Ctrl+Y |  | 已接入 | P4 分类首批 | NeeView/Command/Commands/RedoDestinationMoveCommand.cs |
| ExportImageAs | 另存为 | Ctrl+S |  | 已接入 | P5 原参数/原字节及唯一页框导出；见p5-image-export.md | NeeView/Command/Commands/ExportImageAsCommand.cs |
| ExportImage | 保存为文件 | Shift+Ctrl+S |  | 已接入 | P5 原参数/原字节及唯一页框导出；见p5-image-export.md | NeeView/Command/Commands/ExportImageCommand.cs |
| ExportBookAs | 导出书籍 |  |  | 已接入 | P5 原参数/原字节及唯一页框导出；见p5-image-export.md | NeeView/Command/Commands/ExportBookAsCommand.cs |
| Print | 打印 | Ctrl+P |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/PrintCommand.cs |
| DeleteFile | 删除文件 | Delete |  | 已接入 | P4 主页单页/页面列表显式多选；实体废纸篓、列表登记及ZIP条目，ZIP权限与永久确认 | NeeView/Command/Commands/DeleteFileCommand.cs |
| OpenBookExplorer | 在资源管理器中打开书籍 |  |  | 已接入 | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/OpenBookExplorerCommand.cs |
| OpenBookExternalAppAs | 用外部应用打开书籍 |  |  | 已接入 | P5 原外部应用/参数/来源策略；见p5-external-applications.md | NeeView/Command/Commands/OpenBookExternalAppAsCommand.cs |
| CutBook | 剪切书籍 |  |  | 占位 | 用户选择暂保留禁用；移动使用既有文件操作 | NeeView/Command/Commands/CutBookCommand.cs |
| CopyBook | 复制书籍 |  |  | 已接入 | P4 根实体/包内逻辑书按原四策略复制，QueryPath保持Book.Path | NeeView/Command/Commands/CopyBookCommand.cs |
| CopyBookToFolderAs | 复制书籍到文件夹 |  |  | 已接入 | P4 根实体/逻辑书按原策略固定复制/目标Index；内部目录提取保持原版TODO提示 | NeeView/Command/Commands/CopyBookToFolderAsCommand.cs |
| MoveBookToFolderAs | 移动书籍到文件夹 |  |  | 已接入 | P4 根实体整书固定移动/卸载/原JSON路径联动；不入分类历史 | NeeView/Command/Commands/MoveBookToFolderAsCommand.cs |
| DeleteBook | 删除书籍 |  |  | 已接入 | P4 真实根目录/文件整书废纸篓与原下一书；逻辑/临时/链接拒绝 | NeeView/Command/Commands/DeleteBookCommand.cs |
| RenameBook | 重命名书籍 |  |  | 已接入 | P4 目录/根实体及Mac链接自身改名、原路径联动；见p4-completion.md | NeeView/Command/Commands/RenameBookCommand.cs |
| SelectArchiver | 选择归档程序 |  |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/SelectArchiverCommand.cs |
| ClearHistory | 清理历史记录 |  |  | 已接入 | P2 原历史集合清空 | NeeView/Command/Commands/ClearHistoryCommand.cs |
| ClearHistoryInPlace | 删除当前位置的历史记录 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ClearHistoryInPlaceCommand.cs |
| RemoveUnlinkedHistory | 删除无效的历史记录 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/RemoveUnlinkedHistoryCommand.cs |
| ToggleStretchMode | 切换拉伸 | LeftButton+WheelDown |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ToggleStretchModeCommand.cs |
| ToggleStretchModeReverse | 切换拉伸 (反向) | LeftButton+WheelUp |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ToggleStretchModeReverseCommand.cs |
| SetStretchModeNone | 原始大小 |  |  | 已接入 | P1 宿主适配 | NeeView/Command/Commands/SetStretchModeNoneCommand.cs |
| SetStretchModeUniform | 适应窗口 |  |  | 已接入 | P1 宿主适配 | NeeView/Command/Commands/SetStretchModeUniformCommand.cs |
| SetStretchModeUniformToFill | 铺满整个窗口 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/SetStretchModeUniformToFillCommand.cs |
| SetStretchModeUniformToSize | 适应窗口区域 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/SetStretchModeUniformToSizeCommand.cs |
| SetStretchModeUniformToVertical | 适应窗口高度 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/SetStretchModeUniformToVerticalCommand.cs |
| SetStretchModeUniformToHorizontal | 适应窗口宽度 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/SetStretchModeUniformToHorizontalCommand.cs |
| ToggleStretchAllowScaleUp | 允许放大 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ToggleStretchAllowScaleUpCommand.cs |
| ToggleStretchAllowScaleDown | 允许缩小 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ToggleStretchAllowScaleDownCommand.cs |
| ToggleNearestNeighbor | 启用/禁用逐点放大 |  |  | 已接入 | P5 原画布/透明页背景与双轴像素保持；见p5-background | NeeView/Command/Commands/ToggleNearestNeighborCommand.cs |
| ToggleBackground | 切换背景 |  |  | 已接入 | P5 原画布/透明页背景与双轴像素保持；见p5-background | NeeView/Command/Commands/ToggleBackgroundCommand.cs |
| SetBackgroundBlack | 黑色背景 |  |  | 已接入 | P5 原画布/透明页背景与双轴像素保持；见p5-background | NeeView/Command/Commands/SetBackgroundBlackCommand.cs |
| SetBackgroundWhite | 白色背景 |  |  | 已接入 | P5 原画布/透明页背景与双轴像素保持；见p5-background | NeeView/Command/Commands/SetBackgroundWhiteCommand.cs |
| SetBackgroundAuto | 背景适应图像颜色 |  |  | 已接入 | P5 原画布/透明页背景与双轴像素保持；见p5-background | NeeView/Command/Commands/SetBackgroundAutoCommand.cs |
| SetBackgroundCheck | 白色方格背景 |  |  | 已接入 | P5 原画布/透明页背景与双轴像素保持；见p5-background | NeeView/Command/Commands/SetBackgroundCheckCommand.cs |
| SetBackgroundCheckDark | 黑色方格背景 |  |  | 已接入 | P5 原画布/透明页背景与双轴像素保持；见p5-background | NeeView/Command/Commands/SetBackgroundCheckDarkCommand.cs |
| SetBackgroundCustom | 自定义背景 |  |  | 已接入 | P5 原画布/透明页背景与双轴像素保持；见p5-background | NeeView/Command/Commands/SetBackgroundCustomCommand.cs |
| ToggleTopmost | 启用/禁用总是置顶显示 |  |  | 已接入 | P2 Mac 原窗口显示控制；见 p2-autohide.md | NeeView/Command/Commands/ToggleTopmostCommand.cs |
| ToggleVisibleAddressBar | 显示/隐藏地址栏 |  |  | 已接入 | P5 原窗口/导航语义；见p5-original-commands | NeeView/Command/Commands/ToggleVisibleAddressBarCommand.cs |
| ToggleHideMenu | 启用/禁用自动隐藏菜单 |  |  | 已接入 | P2 Mac 原窗口显示控制；见 p2-autohide.md | NeeView/Command/Commands/ToggleHideMenuCommand.cs |
| ToggleVisibleSideBar | 显示/隐藏侧边栏 |  |  | 已接入 | P2 Mac 原窗口显示控制；见 p2-autohide.md | NeeView/Command/Commands/ToggleVisibleSideBarCommand.cs |
| ToggleHidePanel | 启用/禁用自动隐藏面板 |  |  | 已接入 | P2 Mac 原窗口显示控制；见 p2-autohide.md | NeeView/Command/Commands/ToggleHidePanelCommand.cs |
| ToggleHideLeftPanel | 切换自动隐藏左面板 |  |  | 已接入 | P2 Mac 原窗口显示控制；见 p2-autohide.md | NeeView/Command/Commands/ToggleHideLeftPanelCommand.cs |
| ToggleHideRightPanel | 切换自动隐藏右面板 |  |  | 已接入 | P2 Mac 原窗口显示控制；见 p2-autohide.md | NeeView/Command/Commands/ToggleHideRightPanelCommand.cs |
| ToggleVisiblePageSlider | 显示/隐藏滚动条 |  |  | 已接入 | P5 原窗口/导航语义；见p5-original-commands | NeeView/Command/Commands/ToggleVisiblePageSliderCommand.cs |
| ToggleHidePageSlider | 启用/禁用自动隐藏滚动条 |  |  | 已接入 | P2 Mac 原窗口显示控制；见 p2-autohide.md | NeeView/Command/Commands/ToggleHidePageSliderCommand.cs |
| ToggleVisibleBookshelf | 显示/隐藏书架 | B |  | 已接入 | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/ToggleVisibleBookshelfCommand.cs |
| ToggleVisiblePageList | 显示/隐藏页面列表面板 | P |  | 已接入 | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/ToggleVisiblePageListCommand.cs |
| ToggleVisibleBookmarkList | 显示/隐藏书签面板 | D |  | 已接入 | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/ToggleVisibleBookmarkListCommand.cs |
| ToggleVisiblePlaylist | 显示/隐藏播放列表面板 | M |  | 已接入 | P2 第八批原播放列表/标记子集；格式、编辑和导航接入，高级来源/模板/修复待迁 | NeeView/Command/Commands/ToggleVisiblePlaylistCommand.cs |
| ToggleVisibleHistoryList | 显示/隐藏历史记录面板 | H |  | 已接入 | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/ToggleVisibleHistoryListCommand.cs |
| ToggleVisibleFileInfo | 显示/隐藏信息面板 | I |  | 已接入 | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/ToggleVisibleFileInfoCommand.cs |
| ToggleVisibleNavigator | 显示/隐藏导航面板 | N |  | 已接入 | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/ToggleVisibleNavigatorCommand.cs |
| ToggleVisibleEffectInfo | 显示/隐藏效果面板 | E |  | 已接入 | P5第二十八批原效果/预设/几何；四类执行及十类待迁，见p5-image-effects.md | NeeView/Command/Commands/ToggleVisibleEffectInfoCommand.cs |
| ToggleVisibleFoldersTree | 显示/隐藏目录树 |  |  | 已接入 | P3普通/QuickAccess目录树宿主显隐；完整脚本参数待P5 | NeeView/Command/Commands/ToggleVisibleFoldersTreeCommand.cs |
| ToggleVisibleContentsTree | 显示/隐藏内容面板 |  |  | 已接入 | P3全源目录组树宿主显隐；完整脚本参数待P5 | NeeView/Command/Commands/ToggleVisibleContentsTreeCommand.cs |
| FocusFolderSearchBox | 聚焦到书架搜索框 |  |  | 已接入 | P3普通书架搜索框显示与焦点；虚拟位置能力边界见契约 | NeeView/Command/Commands/FocusFolderSearchBoxCommand.cs |
| FocusBookmarkSearchBox | 聚焦到书签搜索框 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/FocusBookmarkSearchBoxCommand.cs |
| FocusPageListSearchBox | 聚焦到页面列表搜索框 |  |  | 已接入 | P3原页面搜索框显示与焦点，输入和正文作用域隔离 | NeeView/Command/Commands/FocusPageListSearchBoxCommand.cs |
| FocusHistorySearchBox | 聚焦到历史记录搜索框 |  |  | 已接入 | P2 原历史面板焦点 | NeeView/Command/Commands/FocusHistorySearchBoxCommand.cs |
| FocusBookmarkList | 显示书签 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/FocusBookmarkListCommand.cs |
| FocusMainView | 聚焦到主视图 |  |  | 已接入 | P3原主查看器焦点宿主接入 | NeeView/Command/Commands/FocusMainViewCommand.cs |
| ToggleVisibleFilmStrip | 显示/隐藏幻灯条 |  |  | 已接入 | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/ToggleVisibleFilmStripCommand.cs |
| ToggleHideFilmStrip | 启用/禁用自动隐藏幻灯条 |  |  | 已接入 | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/ToggleHideFilmStripCommand.cs |
| ToggleMainViewFloating | 切换主视图窗口 | F12 |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/ToggleMainViewFloatingCommand.cs |
| ToggleFullScreen | 切换全屏状态 | F11 | U | 已接入 | P2 Mac 原窗口显示控制；见 p2-autohide.md | NeeView/Command/Commands/ToggleFullScreenCommand.cs |
| SetFullScreen | 全屏 |  |  | 已接入 | P2 Mac 原窗口显示控制；见 p2-autohide.md | NeeView/Command/Commands/SetFullScreenCommand.cs |
| CancelFullScreen | 退出全屏 |  |  | 已接入 | P2 Mac 原窗口显示控制；见 p2-autohide.md | NeeView/Command/Commands/CancelFullScreenCommand.cs |
| ToggleFullDesktop | 切换全桌面 | Shift+F11 |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/ToggleFullDesktopCommand.cs |
| ToggleWindowMinimize | 最小化窗口 |  |  | 已接入 | P5 原窗口/导航语义；见p5-original-commands | NeeView/Command/Commands/ToggleWindowMinimizeCommand.cs |
| ToggleWindowMaximize | 最大化窗口 |  |  | 已接入 | P5 原窗口/导航语义；见p5-original-commands | NeeView/Command/Commands/ToggleWindowMaximizeCommand.cs |
| ShowHiddenPanels | 临时显示面板 |  |  | 已接入 | P2 Mac 原窗口显示控制；见 p2-autohide.md | NeeView/Command/Commands/ShowHiddenPanelsCommand.cs |
| ToggleSlideShow | 幻灯片播放/停止 | F5 |  | 已接入 | P5原定时/输入/EOS/页尾及Toggle参数 | NeeView/Command/Commands/ToggleSlideShowCommand.cs |
| ViewScrollNTypeUp | N 字形滚动↑ |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ViewScrollNTypeUpCommand.cs |
| ViewScrollNTypeDown | N 字形滚动↓ |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ViewScrollNTypeDownCommand.cs |
| ViewScrollUp | 滚动↑ |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ViewScrollUpCommand.cs |
| ViewScrollDown | 滚动↓ |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ViewScrollDownCommand.cs |
| ViewScrollLeft | 滚动← |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ViewScrollLeftCommand.cs |
| ViewScrollRight | 滚动→ |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ViewScrollRightCommand.cs |
| ViewPresetScroll | 预设滚动 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ViewPresetScrollCommand.cs |
| ViewScaleUp | 放大 | RightButton+WheelUp |  | 已接入 | P1 宿主适配 | NeeView/Command/Commands/ViewScaleUpCommand.cs |
| ViewScaleDown | 缩小 | RightButton+WheelDown |  | 已接入 | P1 宿主适配 | NeeView/Command/Commands/ViewScaleDownCommand.cs |
| ViewScaleStretch | 拉伸 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ViewScaleStretchCommand.cs |
| ViewBaseScaleUp | 放大基准比例 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ViewBaseScaleUpCommand.cs |
| ViewBaseScaleDown | 缩小基准比例 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ViewBaseScaleDownCommand.cs |
| ViewRotateLeft | 左旋 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ViewRotateLeftCommand.cs |
| ViewRotateRight | 右旋 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ViewRotateRightCommand.cs |
| ToggleIsAutoRotateLeft | 启用/禁用自动左旋转 |  |  | 已接入 | P2 Engine 接入；Windows动态对照待验 | NeeView/Command/Commands/ToggleIsAutoRotateLeftCommand.cs |
| ToggleIsAutoRotateRight | 启用/禁用自动右旋转 |  |  | 已接入 | P2 Engine 接入；Windows动态对照待验 | NeeView/Command/Commands/ToggleIsAutoRotateRightCommand.cs |
| ToggleIsAutoRotateForcedLeft | 启用/禁用强制左旋转 |  |  | 已接入 | P2 Engine 接入；Windows动态对照待验 | NeeView/Command/Commands/ToggleIsAutoRotateForcedLeftCommand.cs |
| ToggleIsAutoRotateForcedRight | 启用/禁用强制右旋转 |  |  | 已接入 | P2 Engine 接入；Windows动态对照待验 | NeeView/Command/Commands/ToggleIsAutoRotateForcedRightCommand.cs |
| ToggleViewFlipHorizontal | 左右翻转 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ToggleViewFlipHorizontalCommand.cs |
| ViewFlipHorizontalOn | 允许左右翻转 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ViewFlipHorizontalOnCommand.cs |
| ViewFlipHorizontalOff | 取消左右翻转 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ViewFlipHorizontalOffCommand.cs |
| ToggleViewFlipVertical | 上下翻转 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ToggleViewFlipVerticalCommand.cs |
| ViewFlipVerticalOn | 允许上下翻转 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ViewFlipVerticalOnCommand.cs |
| ViewFlipVerticalOff | 取消上下翻转 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ViewFlipVerticalOffCommand.cs |
| ViewReset | 重置视图 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ViewResetCommand.cs |
| PrevPage | 后退 | Right,RightClick | R | 已接入 | P1 Engine | NeeView/Command/Commands/PrevPageCommand.cs |
| NextPage | 前进 | Left,LeftClick | L | 已接入 | P1 Engine | NeeView/Command/Commands/NextPageCommand.cs |
| PrevOnePage | 后退一页 |  | LR | 已接入 | P1 Engine | NeeView/Command/Commands/PrevOnePageCommand.cs |
| NextOnePage | 前进一页 |  | RL | 已接入 | P1 Engine | NeeView/Command/Commands/NextOnePageCommand.cs |
| PrevScrollPage | 滚动 + 上一页 | WheelUp |  | 已接入 | P2/P3原分页及全景NScroll、边界翻页与参数接入；真机动态待验 | NeeView/Command/Commands/PrevScrollPageCommand.cs |
| NextScrollPage | 滚动 + 下一页 | WheelDown |  | 已接入 | P2/P3原分页及全景NScroll、边界翻页与参数接入；真机动态待验 | NeeView/Command/Commands/NextScrollPageCommand.cs |
| JumpPage | 转到指定页面 |  |  | 已接入 | P2 原定位/共享步长宿主接入；具体范围见验收表 | NeeView/Command/Commands/JumpPageCommand.cs |
| JumpRandomPage | 转到随机页面 |  |  | 已接入 | P5 原窗口/导航语义；见p5-original-commands | NeeView/Command/Commands/JumpRandomPageCommand.cs |
| PrevSizePage | 后退指定页数 |  |  | 已接入 | P2 原定位/共享步长宿主接入；具体范围见验收表 | NeeView/Command/Commands/PrevSizePageCommand.cs |
| NextSizePage | 前进指定页数 |  |  | 已接入 | P2 原定位/共享步长宿主接入；具体范围见验收表 | NeeView/Command/Commands/NextSizePageCommand.cs |
| PrevFolderPage | 上一个文件夹 |  |  | 已接入 | P2 普通书架/文件夹页导航接入；巡回及子书边界见验收表 | NeeView/Command/Commands/PrevFolderPageCommand.cs |
| NextFolderPage | 下一个文件夹 |  |  | 已接入 | P2 普通书架/文件夹页导航接入；巡回及子书边界见验收表 | NeeView/Command/Commands/NextFolderPageCommand.cs |
| FirstPage | 转到首页 | Ctrl+Right | UR | 已接入 | P1 Engine | NeeView/Command/Commands/FirstPageCommand.cs |
| LastPage | 转到尾页 | Ctrl+Left | UL | 已接入 | P1 Engine | NeeView/Command/Commands/LastPageCommand.cs |
| PrevHistoryPage | 后退到上一页 | Back |  | 已接入 | P2 原导航历史接入；具体范围见验收表 | NeeView/Command/Commands/PrevHistoryPageCommand.cs |
| NextHistoryPage | 前进到下一页 | Shift+Back |  | 已接入 | P2 原导航历史接入；具体范围见验收表 | NeeView/Command/Commands/NextHistoryPageCommand.cs |
| ToggleBookLock | 书籍锁定状态 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ToggleBookLockCommand.cs |
| PrevBook | 上一本书籍 | Up | LU | 已接入 | P2 普通书架/文件夹页导航接入；巡回及子书边界见验收表 | NeeView/Command/Commands/PrevBookCommand.cs |
| NextBook | 下一本书籍 | Down | LD | 已接入 | P2 普通书架/文件夹页导航接入；巡回及子书边界见验收表 | NeeView/Command/Commands/NextBookCommand.cs |
| RandomBook | 随机排序书籍 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/RandomBookCommand.cs |
| PrevHistory | 后退到上一条历史记录 |  |  | 已接入 | P2 原过滤后历史列表导航 | NeeView/Command/Commands/PrevHistoryCommand.cs |
| NextHistory | 前进到下一条历史记录 |  |  | 已接入 | P2 原过滤后历史列表导航 | NeeView/Command/Commands/NextHistoryCommand.cs |
| PrevBookHistory | 后退到上一本书籍 | Alt+Left |  | 已接入 | P2 原导航历史接入；具体范围见验收表 | NeeView/Command/Commands/PrevBookHistoryCommand.cs |
| NextBookHistory | 前进到下一本书籍 | Alt+Right |  | 已接入 | P2 原导航历史接入；具体范围见验收表 | NeeView/Command/Commands/NextBookHistoryCommand.cs |
| MoveToParentBook | 打开父文件夹 | Alt+Up |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/MoveToParentBookCommand.cs |
| MoveToChildBook | 打开本书 | Alt+Down |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/MoveToChildBookCommand.cs |
| ToggleMediaPlay | 视频播放/停止 |  |  | 已接入 | P5当前图像动画，原播放/秒数步长；视频仍待迁 | NeeView/Command/Commands/ToggleMediaPlayCommand.cs |
| PrevMediaPosition | 视频倒带 |  |  | 已接入 | P5当前图像动画，原播放/秒数步长；视频仍待迁 | NeeView/Command/Commands/PrevMediaPositionCommand.cs |
| NextMediaPosition | 视频快进 |  |  | 已接入 | P5当前图像动画，原播放/秒数步长；视频仍待迁 | NeeView/Command/Commands/NextMediaPositionCommand.cs |
| ToggleBookOrder | 切换书籍顺序 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ToggleBookOrderCommand.cs |
| SetBookOrderByFileNameA | 书名升序 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/SetBookOrderByFileNameACommand.cs |
| SetBookOrderByFileNameD | 书名降序 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/SetBookOrderByFileNameDCommand.cs |
| SetBookOrderByPathA | 书籍路径升序 |  |  | 已接入 | P5第二十九批原来源资格/排序/目录参数；见p5-book-order.md | NeeView/Command/Commands/SetBookOrderByPathACommand.cs |
| SetBookOrderByPathD | 书籍路径降序 |  |  | 已接入 | P5第二十九批原来源资格/排序/目录参数；见p5-book-order.md | NeeView/Command/Commands/SetBookOrderByPathDCommand.cs |
| SetBookOrderByFileTypeA | 书籍文件类型升序 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/SetBookOrderByFileTypeACommand.cs |
| SetBookOrderByFileTypeD | 书籍文件类型降序 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/SetBookOrderByFileTypeDCommand.cs |
| SetBookOrderByTimeStampA | 书籍日期升序 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/SetBookOrderByTimeStampACommand.cs |
| SetBookOrderByTimeStampD | 书籍日期降序 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/SetBookOrderByTimeStampDCommand.cs |
| SetBookOrderByEntryTimeA | 书籍登记时间升序 |  |  | 已接入 | P5第二十九批原来源资格/排序/目录参数；见p5-book-order.md | NeeView/Command/Commands/SetBookOrderByEntryTimeACommand.cs |
| SetBookOrderByEntryTimeD | 书籍登记时间降序 |  |  | 已接入 | P5第二十九批原来源资格/排序/目录参数；见p5-book-order.md | NeeView/Command/Commands/SetBookOrderByEntryTimeDCommand.cs |
| SetBookOrderBySizeA | 书籍大小升序 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/SetBookOrderBySizeACommand.cs |
| SetBookOrderBySizeD | 书籍大小降序 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/SetBookOrderBySizeDCommand.cs |
| SetBookOrderByRandom | 书籍随机排序 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/SetBookOrderByRandomCommand.cs |
| TogglePageMode | 切换页面模式 |  |  | 已接入 | P1 Engine | NeeView/Command/Commands/TogglePageModeCommand.cs |
| TogglePageModeReverse | 切换页面模式 (反向) |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/TogglePageModeReverseCommand.cs |
| SetPageModeOne | 单页显示 | Ctrl+1 | RU | 已接入 | P1 Engine | NeeView/Command/Commands/SetPageModeOneCommand.cs |
| SetPageModeTwo | 双页显示 | Ctrl+2 | RD | 已接入 | P1 Engine | NeeView/Command/Commands/SetPageModeTwoCommand.cs |
| ToggleIsPanorama | 全景模式 |  |  | 已接入 | P3原帧级全景、PagesAsOne/NScroll与Mac连续/瀑布接入；设备/Windows动态独立验收 | NeeView/Command/Commands/ToggleIsPanoramaCommand.cs |
| TogglePageOrientation | 切换页面方向 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/TogglePageOrientationCommand.cs |
| SetPageOrientationHorizontal | 水平页面布局 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/SetPageOrientationHorizontalCommand.cs |
| SetPageOrientationVertical | 垂直页面布局 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/SetPageOrientationVerticalCommand.cs |
| ToggleBookReadOrder | 切换右开/左开 |  |  | 已接入 | P1 Engine | NeeView/Command/Commands/ToggleBookReadOrderCommand.cs |
| SetBookReadOrderRight | 右开 (从右向左) |  |  | 已接入 | P1 Engine | NeeView/Command/Commands/SetBookReadOrderRightCommand.cs |
| SetBookReadOrderLeft | 左开 (从左向右） |  |  | 已接入 | P1 Engine | NeeView/Command/Commands/SetBookReadOrderLeftCommand.cs |
| ToggleIsSupportedDividePage | 分割横向页面 |  |  | 已接入 | P1 Engine | NeeView/Command/Commands/ToggleIsSupportedDividePageCommand.cs |
| ToggleIsSupportedWidePage | 横向页面视为双页 |  |  | 已接入 | P1 Engine | NeeView/Command/Commands/ToggleIsSupportedWidePageCommand.cs |
| ToggleIsSupportedSingleFirstPage | 首页单独显示 |  |  | 已接入 | P1 Engine | NeeView/Command/Commands/ToggleIsSupportedSingleFirstPageCommand.cs |
| ToggleIsSupportedSingleLastPage | 尾页单独显示 |  |  | 已接入 | P1 Engine | NeeView/Command/Commands/ToggleIsSupportedSingleLastPageCommand.cs |
| ToggleIsRecursiveFolder | 载入子文件夹 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ToggleIsRecursiveFolderCommand.cs |
| ToggleSortMode | 切换页面顺序 |  |  | 已接入 | P5 原窗口/导航语义；见p5-original-commands | NeeView/Command/Commands/ToggleSortModeCommand.cs |
| SetSortModeFileName | 文件名升序 |  |  | 已接入 | P1 Engine | NeeView/Command/Commands/SetSortModeFileNameCommand.cs |
| SetSortModeFileNameDescending | 文件名降序 |  |  | 已接入 | P1 Engine | NeeView/Command/Commands/SetSortModeFileNameDescendingCommand.cs |
| SetSortModeTimeStamp | 文件日期升序 |  |  | 已接入 | P1 Engine | NeeView/Command/Commands/SetSortModeTimeStampCommand.cs |
| SetSortModeTimeStampDescending | 文件日期降序 |  |  | 已接入 | P2 Engine 接入；Windows动态对照待验 | NeeView/Command/Commands/SetSortModeTimeStampDescendingCommand.cs |
| SetSortModeSize | 文件大小升序 |  |  | 已接入 | P1 Engine | NeeView/Command/Commands/SetSortModeSizeCommand.cs |
| SetSortModeSizeDescending | 文件大小降序 |  |  | 已接入 | P2 Engine 接入；Windows动态对照待验 | NeeView/Command/Commands/SetSortModeSizeDescendingCommand.cs |
| SetSortModeEntry | 文件登记时间升序 |  |  | 已接入 | P4 原Playlist来源登记顺序排序；普通来源禁用 | NeeView/Command/Commands/SetSortModeEntryCommand.cs |
| SetSortModeEntryDescending | 文件登记时间降序 |  |  | 已接入 | P4 原Playlist来源登记顺序排序；普通来源禁用 | NeeView/Command/Commands/SetSortModeEntryDescendingCommand.cs |
| SetSortModeRandom | 随机 |  |  | 已接入 | P1 Engine | NeeView/Command/Commands/SetSortModeRandomCommand.cs |
| SetDefaultPageSetting | 重置页面设置 |  |  | 已接入 | P5 原默认复制/重收集与文件权限；见p5-default-settings | NeeView/Command/Commands/SetDefaultPageSettingCommand.cs |
| ToggleBookmark | 添加/删除书签 | Ctrl+D |  | 已接入 | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/ToggleBookmarkCommand.cs |
| RegisterBookmark | 注册书签 |  |  | 已接入 | P2 第五批宿主适配；登记字段/动作接入，原Popup/标签/树选择器待迁 | NeeView/Command/Commands/RegisterBookmarkCommand.cs |
| NextPlaylist | 下一播放列表 |  |  | 已接入 | P2 第八批原播放列表/标记子集；格式、编辑和导航接入，高级来源/模板/修复待迁 | NeeView/Command/Commands/NextPlaylistCommand.cs |
| PrevPlaylist | 上一个播放列表 |  |  | 已接入 | P2 第八批原播放列表/标记子集；格式、编辑和导航接入，高级来源/模板/修复待迁 | NeeView/Command/Commands/PrevPlaylistCommand.cs |
| TogglePlaylistItem | 添加/删除播放列表项目 | Ctrl+M |  | 已接入 | P2 第八批原播放列表/标记子集；格式、编辑和导航接入，高级来源/模板/修复待迁 | NeeView/Command/Commands/TogglePlaylistItemCommand.cs |
| PrevPlaylistItem | 上一个播放列表项目 |  |  | 已接入 | P2 第八批原播放列表/标记子集；格式、编辑和导航接入，高级来源/模板/修复待迁 | NeeView/Command/Commands/PrevPlaylistItemCommand.cs |
| NextPlaylistItem | 下一个播放列表项目 |  |  | 已接入 | P2 第八批原播放列表/标记子集；格式、编辑和导航接入，高级来源/模板/修复待迁 | NeeView/Command/Commands/NextPlaylistItemCommand.cs |
| PrevPlaylistItemInBook | 书籍中的上一个播放列表项目 |  |  | 已接入 | P2 第八批原播放列表/标记子集；格式、编辑和导航接入，高级来源/模板/修复待迁 | NeeView/Command/Commands/PrevPlaylistItemInBookCommand.cs |
| NextPlaylistItemInBook | 书籍中的下一个播放列表项目 |  |  | 已接入 | P2 第八批原播放列表/标记子集；格式、编辑和导航接入，高级来源/模板/修复待迁 | NeeView/Command/Commands/NextPlaylistItemInBookCommand.cs |
| SetEffectProfile | 设置效果配置 |  |  | 已接入 | P5第二十八批原效果/预设/几何；四类执行及十类待迁，见p5-image-effects.md | NeeView/Command/Commands/SetEffectProfileCommand.cs |
| NextEffectProfile | 下一个效果配置 |  |  | 已接入 | P5第二十八批原效果/预设/几何；四类执行及十类待迁，见p5-image-effects.md | NeeView/Command/CommandTable.cs |
| PrevEffectProfile | 前一个效果配置 |  |  | 已接入 | P5第二十八批原效果/预设/几何；四类执行及十类待迁，见p5-image-effects.md | NeeView/Command/CommandTable.cs |
| ToggleCustomSize | 启用/禁用自定义大小 |  |  | 已接入 | P5第二十八批原效果/预设/几何；四类执行及十类待迁，见p5-image-effects.md | NeeView/Command/Commands/ToggleCustomSizeCommand.cs |
| ToggleTrim | 切换裁剪 |  |  | 已接入 | P5第二十八批原效果/预设/几何；四类执行及十类待迁，见p5-image-effects.md | NeeView/Command/Commands/ToggleTrimCommand.cs |
| ToggleResizeFilter | 启用/禁用调整大小滤镜 | Ctrl+R |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/ToggleResizeFilterCommand.cs |
| ToggleGrid | 启用/禁用网格 |  |  | 已接入 | P5第二十八批原效果/预设/几何；四类执行及十类待迁，见p5-image-effects.md | NeeView/Command/Commands/ToggleGridCommand.cs |
| ToggleEffect | 启用/禁用效果 | Ctrl+E |  | 已接入 | P5第二十八批原效果/预设/几何；四类执行及十类待迁，见p5-image-effects.md | NeeView/Command/Commands/ToggleEffectCommand.cs |
| ToggleIsLoupe | 启用/禁用放大镜 |  |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/ToggleIsLoupeCommand.cs |
| LoupeOn | 启用放大镜 |  |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/LoupeOnCommand.cs |
| LoupeOff | 退出放大镜 |  |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/LoupeOffCommand.cs |
| LoupeScaleUp | 提高放大镜倍率 |  |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/LoupeScaleUpCommand.cs |
| LoupeScaleDown | 降低放大镜倍率 |  |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/LoupeScaleDownCommand.cs |
| ToggleHoverScroll | 启用/禁用悬浮滚动 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/ToggleHoverScrollCommand.cs |
| ToggleAutoScroll | 切换自动滚动 | MiddleClick |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/ToggleAutoScrollCommand.cs |
| CancelScript | 中止脚本 |  |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/CancelScriptCommand.cs |
| OpenOptionsWindow | 打开设置窗口 |  |  | 已接入 | P1 宿主适配 | NeeView/Command/Commands/OpenOptionsWindowCommand.cs |
| OpenSettingFilesFolder | 打开配置文件位置 |  |  | 已接入 | P5 原窗口/导航语义；见p5-original-commands | NeeView/Command/Commands/OpenSettingFilesFolderCommand.cs |
| OpenScriptsFolder | 打开脚本文件夹 |  |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/OpenScriptsFolderCommand.cs |
| OpenVersionWindow | 显示版本信息 |  |  | 已接入 | P5 原版本/复制/许可/项目；Mac更新检查占位 | NeeView/Command/Commands/OpenVersionWindowCommand.cs |
| CloseApplication | 退出应用程序 |  |  | 已接入 | P1 宿主适配 | NeeView/Command/Commands/CloseApplicationCommand.cs |
| TogglePermitFile | 启用/禁用文件操作 |  |  | 已接入 | P5 原默认复制/重收集与文件权限；见p5-default-settings | NeeView/Command/Commands/TogglePermitFileCommand.cs |
| HelpCommandList | 显示命令帮助 |  |  | 已接入 | P1 宿主适配 | NeeView/Command/Commands/HelpCommandListCommand.cs |
| HelpScript | 显示脚本帮助 |  |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/HelpScriptCommand.cs |
| HelpMainMenu | 显示主菜单帮助 |  |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/HelpMainMenuCommand.cs |
| HelpSearchOption | 搜索选项帮助 |  |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/HelpSearchOptionCommand.cs |
| OpenContextMenu | 打开上下文菜单 |  |  | 已接入 | P2 执行入口已接入；参数/行为范围见模块验收记录 | NeeView/Command/Commands/OpenContextMenuCommand.cs |
| ExportBackup | 导出设置 |  |  | 已接入 | P5 原Profile串流备份/FileName及保存对话框；不执行脚本 | NeeView/Command/Commands/ExportBackupCommand.cs |
| ImportBackup | 导入设置 |  |  | 已接入 | P5选择/预览、确认、备份及失败恢复；受支持版本与项目限制 | NeeView/Command/Commands/ImportBackupCommand.cs |
| ReloadSetting | 重新载入设置 |  |  | 已接入 | P5 仅UserSetting原地恢复；来源规则变化重收集 | NeeView/Command/Commands/ReloadSettingCommand.cs |
| SaveSetting | 保存设置 |  |  | 已接入 | P5 原SaveAll(false)/列表flush/立即阅读保存 | NeeView/Command/Commands/SaveSettingCommand.cs |
| TouchEmulate | 模拟触控 |  |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/TouchEmulateCommand.cs |
| FocusPrevApp | 切换到上一个 NeeView | Ctrl+Shift+Tab |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/FocusPrevAppCommand.cs |
| FocusNextApp | 切换到下一个 NeeView | Ctrl+Tab |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/FocusNextAppCommand.cs |
| StretchWindow | 调整窗口大小 |  |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/StretchWindowCommand.cs |
| OpenConsole | 打开脚本控制台 |  |  | 占位 | 待 P2–P5 | NeeView/Command/Commands/OpenConsoleCommand.cs |

P4第一批原九数字、MoveToFolderAs及Undo/Redo接入，数字默认输入1–9并保留可配置Index。第一批仅Once普通目录主图；第二批已迁入原多页策略及普通目录CopyToFolderAs，归档实体化复制待后续。DeleteFile/RenameBook已分别接入，CopyFile/CopyBook/Paste由第五批接入；CutFile/CutBook按用户决定保持禁用占位。菜单能力受原写权限/当前来源/有效目标/忙碌约束；详见[p4-destination-folders.md](p4-destination-folders.md)。

P4收尾不新增命令：原235实例、167执行入口/68占位保持。DeleteFile主菜单仍单主页，页面列表Delete为显式多选，普通实体/列表登记/ZIP分别处理；ZIP独立写权限且永久删除始终确认。Paste支持标准图片/HTML/URL及原失败回退；Mac链接自身操作接入。Cut继续按用户决定禁用。详见[p4-completion.md](p4-completion.md)。

P5 第十七批接通原三个媒体命令，当前171执行入口/64占位；235原实例保持，数量不代表功能覆盖率。视频及高级自动播放继续待迁。

P5第十八批接通原ToggleSlideShow；当前172执行入口/63占位，235原实例保持。原计时/等待/输入及菜单/键位语义见[p5-slideshow.md](p5-slideshow.md)，数量不代表覆盖率。

P5第二十批：SaveSetting/ReloadSetting/ExportBackup进入唯一Profile及可等待宿主。当前175入口/60占位，数量不代表功能覆盖率，见[契约](p5-profile-commands.md)。

P5第二十一批接通7个原窗口/导航命令；当前182入口/53占位，数量不代表功能覆盖率。见[p5-original-commands.md](p5-original-commands.md)。

P5第二十二批接通8个原背景/像素保持命令；当前190入口/45占位，数量不代表功能覆盖率。见[p5-background.md](p5-background.md)。

P5第二十三批：原十二字段默认复制、实际变化历史订阅、递归DirtyBook重收集及全局文件权限接入；新打开优先，失败恢复设置/位置/历史资格，权限不刷新正文。见[契约](p5-default-settings.md)。

P5第二十四批：原版本窗口布局/图标、实际Mac构建版本、复制/许可/项目链接及macOS关于菜单接入；窗口与阅读独立，Windows更新检查保留Mac待接入区域。见[契约](p5-version-window.md)。

P5第二十五批：原三外部应用命令、Index0选择、五字段集合/差分、页组/整书归档策略及系统提交接入。Windows命令地址保留、Mac需用户改为可执行文件或.app，见[契约](p5-external-applications.md)。
