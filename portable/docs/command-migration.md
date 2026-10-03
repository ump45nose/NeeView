# 完整命令迁移表

固定基线 235 个实例；数量是登记清单，不代表功能覆盖率。当前包含 P1 与 P2 十一批增量，阶段标记仅说明执行入口已接入，完整参数/手势及交互范围见验收记录。未迁入命令保留原菜单节点、键位和参数，禁用占位。原复杂手势可以原样保存，不意味着其执行已迁入。

第七批底部直接页号进入既有JumpAsync；第八批接通原全局播放列表与页标记；第九批接通自动隐藏和窗口显示控制；第十/十一批接通书签导航和结构化搜索；第十二批接通历史结构化搜索与原焦点全选。已支持入口仍以具体模块契约和验收表为准，不能把登记数视为完整功能覆盖率。

| 原命令 | 文案 | 默认输入 | 当前实现 | 原出处 |
|---|---|---|---|---|
| LoadAs | 打开文件 | Ctrl+O | P1 宿主适配 | NeeView/Command/Commands/LoadAsCommand.cs |
| LoadRecentBook | 最近使用的书籍 |  | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/LoadRecentBookCommand.cs |
| ReLoad | 重新载入 |  | P1 宿主适配 | NeeView/Command/Commands/ReLoadCommand.cs |
| Unload | 关闭 |  | 待 P2–P5 | NeeView/Command/Commands/UnloadCommand.cs |
| OpenExplorer | 在资源管理器中打开 |  | P1 宿主适配 | NeeView/Command/Commands/OpenExplorerCommand.cs |
| OpenExternalApp | 在外部应用中打开 (简单) |  | 待 P2–P5 | NeeView/Command/Commands/OpenExternalAppCommand.cs |
| OpenExternalAppAs | 在外部应用中打开 |  | 待 P2–P5 | NeeView/Command/Commands/OpenExternalAppAsCommand.cs |
| CutFile | 剪切文件 | Ctrl+X | 待 P2–P5 | NeeView/Command/Commands/CutFileCommand.cs |
| CopyFile | 复制文件 | Ctrl+C | 待 P2–P5 | NeeView/Command/Commands/CopyFileCommand.cs |
| CopyImage | 复制图像 | Ctrl+Shift+C | 待 P2–P5 | NeeView/Command/Commands/CopyImageCommand.cs |
| Paste | 粘贴 | Ctrl+V | 待 P2–P5 | NeeView/Command/Commands/PasteCommand.cs |
| CopyToFolderAs | 复制到文件夹 |  | 待 P2–P5 | NeeView/Command/Commands/CopyToFolderAsCommand.cs |
| MoveToFolderAs | 移动到文件夹 |  | 待 P2–P5 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| MoveToDestinationFolder1 | 移动到文件夹 |  | 待 P2–P5 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| MoveToDestinationFolder2 | 移动到文件夹 |  | 待 P2–P5 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| MoveToDestinationFolder3 | 移动到文件夹 |  | 待 P2–P5 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| MoveToDestinationFolder4 | 移动到文件夹 |  | 待 P2–P5 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| MoveToDestinationFolder5 | 移动到文件夹 |  | 待 P2–P5 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| MoveToDestinationFolder6 | 移动到文件夹 |  | 待 P2–P5 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| MoveToDestinationFolder7 | 移动到文件夹 |  | 待 P2–P5 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| MoveToDestinationFolder8 | 移动到文件夹 |  | 待 P2–P5 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| MoveToDestinationFolder9 | 移动到文件夹 |  | 待 P2–P5 | NeeView/Command/Commands/MoveToFolderAsCommand.cs |
| UndoDestinationMove | 撤销目标文件夹移动 | Ctrl+Z | 待 P2–P5 | NeeView/Command/Commands/UndoDestinationMoveCommand.cs |
| RedoDestinationMove | 重做目标文件夹移动 | Ctrl+Y | 待 P2–P5 | NeeView/Command/Commands/RedoDestinationMoveCommand.cs |
| ExportImageAs | 另存为 | Ctrl+S | 待 P2–P5 | NeeView/Command/Commands/ExportImageAsCommand.cs |
| ExportImage | 保存为文件 | Shift+Ctrl+S | 待 P2–P5 | NeeView/Command/Commands/ExportImageCommand.cs |
| ExportBookAs | 导出书籍 |  | 待 P2–P5 | NeeView/Command/Commands/ExportBookAsCommand.cs |
| Print | 打印 | Ctrl+P | 待 P2–P5 | NeeView/Command/Commands/PrintCommand.cs |
| DeleteFile | 删除文件 | Delete | 待 P2–P5 | NeeView/Command/Commands/DeleteFileCommand.cs |
| OpenBookExplorer | 在资源管理器中打开书籍 |  | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/OpenBookExplorerCommand.cs |
| OpenBookExternalAppAs | 用外部应用打开书籍 |  | 待 P2–P5 | NeeView/Command/Commands/OpenBookExternalAppAsCommand.cs |
| CutBook | 剪切书籍 |  | 待 P2–P5 | NeeView/Command/Commands/CutBookCommand.cs |
| CopyBook | 复制书籍 |  | 待 P2–P5 | NeeView/Command/Commands/CopyBookCommand.cs |
| CopyBookToFolderAs | 复制书籍到文件夹 |  | 待 P2–P5 | NeeView/Command/Commands/CopyBookToFolderAsCommand.cs |
| MoveBookToFolderAs | 移动书籍到文件夹 |  | 待 P2–P5 | NeeView/Command/Commands/MoveBookToFolderAsCommand.cs |
| DeleteBook | 删除书籍 |  | 待 P2–P5 | NeeView/Command/Commands/DeleteBookCommand.cs |
| RenameBook | 重命名书籍 |  | 待 P2–P5 | NeeView/Command/Commands/RenameBookCommand.cs |
| SelectArchiver | 选择归档程序 |  | 待 P2–P5 | NeeView/Command/Commands/SelectArchiverCommand.cs |
| ClearHistory | 清理历史记录 |  | P2 原历史集合清空；具体范围见验收表 | NeeView/Command/Commands/ClearHistoryCommand.cs |
| ClearHistoryInPlace | 删除当前位置的历史记录 |  | 待 P2–P5 | NeeView/Command/Commands/ClearHistoryInPlaceCommand.cs |
| RemoveUnlinkedHistory | 删除无效的历史记录 |  | 待 P2–P5 | NeeView/Command/Commands/RemoveUnlinkedHistoryCommand.cs |
| ToggleStretchMode | 切换拉伸 | LeftButton+WheelDown | 待 P2–P5 | NeeView/Command/Commands/ToggleStretchModeCommand.cs |
| ToggleStretchModeReverse | 切换拉伸 (反向) | LeftButton+WheelUp | 待 P2–P5 | NeeView/Command/Commands/ToggleStretchModeReverseCommand.cs |
| SetStretchModeNone | 原始大小 |  | P1 宿主适配 | NeeView/Command/Commands/SetStretchModeNoneCommand.cs |
| SetStretchModeUniform | 适应窗口 |  | P1 宿主适配 | NeeView/Command/Commands/SetStretchModeUniformCommand.cs |
| SetStretchModeUniformToFill | 铺满整个窗口 |  | 待 P2–P5 | NeeView/Command/Commands/SetStretchModeUniformToFillCommand.cs |
| SetStretchModeUniformToSize | 适应窗口区域 |  | 待 P2–P5 | NeeView/Command/Commands/SetStretchModeUniformToSizeCommand.cs |
| SetStretchModeUniformToVertical | 适应窗口高度 |  | 待 P2–P5 | NeeView/Command/Commands/SetStretchModeUniformToVerticalCommand.cs |
| SetStretchModeUniformToHorizontal | 适应窗口宽度 |  | 待 P2–P5 | NeeView/Command/Commands/SetStretchModeUniformToHorizontalCommand.cs |
| ToggleStretchAllowScaleUp | 允许放大 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleStretchAllowScaleUpCommand.cs |
| ToggleStretchAllowScaleDown | 允许缩小 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleStretchAllowScaleDownCommand.cs |
| ToggleNearestNeighbor | 启用/禁用逐点放大 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleNearestNeighborCommand.cs |
| ToggleBackground | 切换背景 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleBackgroundCommand.cs |
| SetBackgroundBlack | 黑色背景 |  | 待 P2–P5 | NeeView/Command/Commands/SetBackgroundBlackCommand.cs |
| SetBackgroundWhite | 白色背景 |  | 待 P2–P5 | NeeView/Command/Commands/SetBackgroundWhiteCommand.cs |
| SetBackgroundAuto | 背景适应图像颜色 |  | 待 P2–P5 | NeeView/Command/Commands/SetBackgroundAutoCommand.cs |
| SetBackgroundCheck | 白色方格背景 |  | 待 P2–P5 | NeeView/Command/Commands/SetBackgroundCheckCommand.cs |
| SetBackgroundCheckDark | 黑色方格背景 |  | 待 P2–P5 | NeeView/Command/Commands/SetBackgroundCheckDarkCommand.cs |
| SetBackgroundCustom | 自定义背景 |  | 待 P2–P5 | NeeView/Command/Commands/SetBackgroundCustomCommand.cs |
| ToggleTopmost | 启用/禁用总是置顶显示 |  | P2 Mac 原窗口显示控制 | NeeView/Command/Commands/ToggleTopmostCommand.cs |
| ToggleVisibleAddressBar | 显示/隐藏地址栏 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleVisibleAddressBarCommand.cs |
| ToggleHideMenu | 启用/禁用自动隐藏菜单 |  | P2 Mac 原窗口显示控制 | NeeView/Command/Commands/ToggleHideMenuCommand.cs |
| ToggleVisibleSideBar | 显示/隐藏侧边栏 |  | P2 Mac 原窗口显示控制 | NeeView/Command/Commands/ToggleVisibleSideBarCommand.cs |
| ToggleHidePanel | 启用/禁用自动隐藏面板 |  | P2 Mac 原窗口显示控制 | NeeView/Command/Commands/ToggleHidePanelCommand.cs |
| ToggleHideLeftPanel | 切换自动隐藏左面板 |  | P2 Mac 原窗口显示控制 | NeeView/Command/Commands/ToggleHideLeftPanelCommand.cs |
| ToggleHideRightPanel | 切换自动隐藏右面板 |  | P2 Mac 原窗口显示控制 | NeeView/Command/Commands/ToggleHideRightPanelCommand.cs |
| ToggleVisiblePageSlider | 显示/隐藏滚动条 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleVisiblePageSliderCommand.cs |
| ToggleHidePageSlider | 启用/禁用自动隐藏滚动条 |  | P2 Mac 原窗口显示控制 | NeeView/Command/Commands/ToggleHidePageSliderCommand.cs |
| ToggleVisibleBookshelf | 显示/隐藏书架 | B | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/ToggleVisibleBookshelfCommand.cs |
| ToggleVisiblePageList | 显示/隐藏页面列表面板 | P | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/ToggleVisiblePageListCommand.cs |
| ToggleVisibleBookmarkList | 显示/隐藏书签面板 | D | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/ToggleVisibleBookmarkListCommand.cs |
| ToggleVisiblePlaylist | 显示/隐藏播放列表面板 | M | P2 第八批原列表/标记子集 | NeeView/Command/Commands/ToggleVisiblePlaylistCommand.cs |
| ToggleVisibleHistoryList | 显示/隐藏历史记录面板 | H | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/ToggleVisibleHistoryListCommand.cs |
| ToggleVisibleFileInfo | 显示/隐藏信息面板 | I | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/ToggleVisibleFileInfoCommand.cs |
| ToggleVisibleNavigator | 显示/隐藏导航面板 | N | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/ToggleVisibleNavigatorCommand.cs |
| ToggleVisibleEffectInfo | 显示/隐藏效果面板 | E | 待 P2–P5 | NeeView/Command/Commands/ToggleVisibleEffectInfoCommand.cs |
| ToggleVisibleFoldersTree | 显示/隐藏目录树 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleVisibleFoldersTreeCommand.cs |
| ToggleVisibleContentsTree | 显示/隐藏内容面板 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleVisibleContentsTreeCommand.cs |
| FocusFolderSearchBox | 聚焦到书架搜索框 |  | 待 P2–P5 | NeeView/Command/Commands/FocusFolderSearchBoxCommand.cs |
| FocusBookmarkSearchBox | 聚焦到书签搜索框 |  | P2 原书签搜索焦点；范围见第十一批 | NeeView/Command/Commands/FocusBookmarkSearchBoxCommand.cs |
| FocusPageListSearchBox | 聚焦到页面列表搜索框 |  | 待 P2–P5 | NeeView/Command/Commands/FocusPageListSearchBoxCommand.cs |
| FocusHistorySearchBox | 聚焦到历史记录搜索框 |  | P2 原历史搜索聚焦/全选；范围见第十二批 | NeeView/Command/Commands/FocusHistorySearchBoxCommand.cs |
| FocusBookmarkList | 显示书签 |  | 待 P2：书架书签位置/目录树互联 | NeeView/Command/Commands/FocusBookmarkListCommand.cs |
| FocusMainView | 聚焦到主视图 |  | 待 P2–P5 | NeeView/Command/Commands/FocusMainViewCommand.cs |
| ToggleVisibleFilmStrip | 显示/隐藏幻灯条 |  | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/ToggleVisibleFilmStripCommand.cs |
| ToggleHideFilmStrip | 启用/禁用自动隐藏幻灯条 |  | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/ToggleHideFilmStripCommand.cs |
| ToggleMainViewFloating | 切换主视图窗口 | F12 | 待 P2–P5 | NeeView/Command/Commands/ToggleMainViewFloatingCommand.cs |
| ToggleFullScreen | 切换全屏状态 | F11 | P2 Mac 原窗口显示控制 | NeeView/Command/Commands/ToggleFullScreenCommand.cs |
| SetFullScreen | 全屏 |  | P2 Mac 原窗口显示控制 | NeeView/Command/Commands/SetFullScreenCommand.cs |
| CancelFullScreen | 退出全屏 |  | P2 Mac 原窗口显示控制 | NeeView/Command/Commands/CancelFullScreenCommand.cs |
| ToggleFullDesktop | 切换全桌面 | Shift+F11 | 待 P2–P5 | NeeView/Command/Commands/ToggleFullDesktopCommand.cs |
| ToggleWindowMinimize | 最小化窗口 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleWindowMinimizeCommand.cs |
| ToggleWindowMaximize | 最大化窗口 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleWindowMaximizeCommand.cs |
| ShowHiddenPanels | 临时显示面板 |  | P2 Mac 原窗口显示控制 | NeeView/Command/Commands/ShowHiddenPanelsCommand.cs |
| ToggleSlideShow | 幻灯片播放/停止 | F5 | 待 P2–P5 | NeeView/Command/Commands/ToggleSlideShowCommand.cs |
| ViewScrollNTypeUp | N 字形滚动↑ |  | 待 P2–P5 | NeeView/Command/Commands/ViewScrollNTypeUpCommand.cs |
| ViewScrollNTypeDown | N 字形滚动↓ |  | 待 P2–P5 | NeeView/Command/Commands/ViewScrollNTypeDownCommand.cs |
| ViewScrollUp | 滚动↑ |  | 待 P2–P5 | NeeView/Command/Commands/ViewScrollUpCommand.cs |
| ViewScrollDown | 滚动↓ |  | 待 P2–P5 | NeeView/Command/Commands/ViewScrollDownCommand.cs |
| ViewScrollLeft | 滚动← |  | 待 P2–P5 | NeeView/Command/Commands/ViewScrollLeftCommand.cs |
| ViewScrollRight | 滚动→ |  | 待 P2–P5 | NeeView/Command/Commands/ViewScrollRightCommand.cs |
| ViewPresetScroll | 预设滚动 |  | 待 P2–P5 | NeeView/Command/Commands/ViewPresetScrollCommand.cs |
| ViewScaleUp | 放大 | RightButton+WheelUp | P1 宿主适配 | NeeView/Command/Commands/ViewScaleUpCommand.cs |
| ViewScaleDown | 缩小 | RightButton+WheelDown | P1 宿主适配 | NeeView/Command/Commands/ViewScaleDownCommand.cs |
| ViewScaleStretch | 拉伸 |  | 待 P2–P5 | NeeView/Command/Commands/ViewScaleStretchCommand.cs |
| ViewBaseScaleUp | 放大基准比例 |  | 待 P2–P5 | NeeView/Command/Commands/ViewBaseScaleUpCommand.cs |
| ViewBaseScaleDown | 缩小基准比例 |  | 待 P2–P5 | NeeView/Command/Commands/ViewBaseScaleDownCommand.cs |
| ViewRotateLeft | 左旋 |  | 待 P2–P5 | NeeView/Command/Commands/ViewRotateLeftCommand.cs |
| ViewRotateRight | 右旋 |  | 待 P2–P5 | NeeView/Command/Commands/ViewRotateRightCommand.cs |
| ToggleIsAutoRotateLeft | 启用/禁用自动左旋转 |  | P2 Engine 接入；Windows动态对照待验 | NeeView/Command/Commands/ToggleIsAutoRotateLeftCommand.cs |
| ToggleIsAutoRotateRight | 启用/禁用自动右旋转 |  | P2 Engine 接入；Windows动态对照待验 | NeeView/Command/Commands/ToggleIsAutoRotateRightCommand.cs |
| ToggleIsAutoRotateForcedLeft | 启用/禁用强制左旋转 |  | P2 Engine 接入；Windows动态对照待验 | NeeView/Command/Commands/ToggleIsAutoRotateForcedLeftCommand.cs |
| ToggleIsAutoRotateForcedRight | 启用/禁用强制右旋转 |  | P2 Engine 接入；Windows动态对照待验 | NeeView/Command/Commands/ToggleIsAutoRotateForcedRightCommand.cs |
| ToggleViewFlipHorizontal | 左右翻转 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleViewFlipHorizontalCommand.cs |
| ViewFlipHorizontalOn | 允许左右翻转 |  | 待 P2–P5 | NeeView/Command/Commands/ViewFlipHorizontalOnCommand.cs |
| ViewFlipHorizontalOff | 取消左右翻转 |  | 待 P2–P5 | NeeView/Command/Commands/ViewFlipHorizontalOffCommand.cs |
| ToggleViewFlipVertical | 上下翻转 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleViewFlipVerticalCommand.cs |
| ViewFlipVerticalOn | 允许上下翻转 |  | 待 P2–P5 | NeeView/Command/Commands/ViewFlipVerticalOnCommand.cs |
| ViewFlipVerticalOff | 取消上下翻转 |  | 待 P2–P5 | NeeView/Command/Commands/ViewFlipVerticalOffCommand.cs |
| ViewReset | 重置视图 |  | 待 P2–P5 | NeeView/Command/Commands/ViewResetCommand.cs |
| PrevPage | 后退 | Right,RightClick | P1 Engine | NeeView/Command/Commands/PrevPageCommand.cs |
| NextPage | 前进 | Left,LeftClick | P1 Engine | NeeView/Command/Commands/NextPageCommand.cs |
| PrevOnePage | 后退一页 |  | P1 Engine | NeeView/Command/Commands/PrevOnePageCommand.cs |
| NextOnePage | 前进一页 |  | P1 Engine | NeeView/Command/Commands/NextOnePageCommand.cs |
| PrevScrollPage | 滚动 + 上一页 | WheelUp | P2 原NScroll/边界翻页接入；全景与完整参数编辑待后续 | NeeView/Command/Commands/PrevScrollPageCommand.cs |
| NextScrollPage | 滚动 + 下一页 | WheelDown | P2 原NScroll/边界翻页接入；全景与完整参数编辑待后续 | NeeView/Command/Commands/NextScrollPageCommand.cs |
| JumpPage | 转到指定页面 |  | P2 原定位/共享步长宿主接入；具体范围见验收表 | NeeView/Command/Commands/JumpPageCommand.cs |
| JumpRandomPage | 转到随机页面 |  | 待 P2–P5 | NeeView/Command/Commands/JumpRandomPageCommand.cs |
| PrevSizePage | 后退指定页数 |  | P2 原定位/共享步长宿主接入；具体范围见验收表 | NeeView/Command/Commands/PrevSizePageCommand.cs |
| NextSizePage | 前进指定页数 |  | P2 原定位/共享步长宿主接入；具体范围见验收表 | NeeView/Command/Commands/NextSizePageCommand.cs |
| PrevFolderPage | 上一个文件夹 |  | P2 原文件夹页分组导航；仅文件名排序 | NeeView/Command/Commands/PrevFolderPageCommand.cs |
| NextFolderPage | 下一个文件夹 |  | P2 原文件夹页分组导航；仅文件名排序 | NeeView/Command/Commands/NextFolderPageCommand.cs |
| FirstPage | 转到首页 | Ctrl+Right | P1 Engine | NeeView/Command/Commands/FirstPageCommand.cs |
| LastPage | 转到尾页 | Ctrl+Left | P1 Engine | NeeView/Command/Commands/LastPageCommand.cs |
| PrevHistoryPage | 后退到上一页 | Back | P2 原导航历史接入；具体范围见验收表 | NeeView/Command/Commands/PrevHistoryPageCommand.cs |
| NextHistoryPage | 前进到下一页 | Shift+Back | P2 原导航历史接入；具体范围见验收表 | NeeView/Command/Commands/NextHistoryPageCommand.cs |
| ToggleBookLock | 书籍锁定状态 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleBookLockCommand.cs |
| PrevBook | 上一本书籍 | Up | P2 原普通书架前后项；失败保留选择 | NeeView/Command/Commands/PrevBookCommand.cs |
| NextBook | 下一本书籍 | Down | P2 原普通书架前后项；失败保留选择 | NeeView/Command/Commands/NextBookCommand.cs |
| RandomBook | 随机排序书籍 |  | 待 P2–P5 | NeeView/Command/Commands/RandomBookCommand.cs |
| PrevHistory | 后退到上一条历史记录 |  | P2 原过滤后历史列表导航；具体范围见验收表 | NeeView/Command/Commands/PrevHistoryCommand.cs |
| NextHistory | 前进到下一条历史记录 |  | P2 原过滤后历史列表导航；具体范围见验收表 | NeeView/Command/Commands/NextHistoryCommand.cs |
| PrevBookHistory | 后退到上一本书籍 | Alt+Left | P2 原导航历史接入；具体范围见验收表 | NeeView/Command/Commands/PrevBookHistoryCommand.cs |
| NextBookHistory | 前进到下一本书籍 | Alt+Right | P2 原导航历史接入；具体范围见验收表 | NeeView/Command/Commands/NextBookHistoryCommand.cs |
| MoveToParentBook | 打开父文件夹 | Alt+Up | 待 P2–P5 | NeeView/Command/Commands/MoveToParentBookCommand.cs |
| MoveToChildBook | 打开本书 | Alt+Down | 待 P2–P5 | NeeView/Command/Commands/MoveToChildBookCommand.cs |
| ToggleMediaPlay | 视频播放/停止 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleMediaPlayCommand.cs |
| PrevMediaPosition | 视频倒带 |  | 待 P2–P5 | NeeView/Command/Commands/PrevMediaPositionCommand.cs |
| NextMediaPosition | 视频快进 |  | 待 P2–P5 | NeeView/Command/Commands/NextMediaPositionCommand.cs |
| ToggleBookOrder | 切换书籍顺序 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleBookOrderCommand.cs |
| SetBookOrderByFileNameA | 书名升序 |  | 待 P2–P5 | NeeView/Command/Commands/SetBookOrderByFileNameACommand.cs |
| SetBookOrderByFileNameD | 书名降序 |  | 待 P2–P5 | NeeView/Command/Commands/SetBookOrderByFileNameDCommand.cs |
| SetBookOrderByPathA | 书籍路径升序 |  | 待 P2–P5 | NeeView/Command/Commands/SetBookOrderByPathACommand.cs |
| SetBookOrderByPathD | 书籍路径降序 |  | 待 P2–P5 | NeeView/Command/Commands/SetBookOrderByPathDCommand.cs |
| SetBookOrderByFileTypeA | 书籍文件类型升序 |  | 待 P2–P5 | NeeView/Command/Commands/SetBookOrderByFileTypeACommand.cs |
| SetBookOrderByFileTypeD | 书籍文件类型降序 |  | 待 P2–P5 | NeeView/Command/Commands/SetBookOrderByFileTypeDCommand.cs |
| SetBookOrderByTimeStampA | 书籍日期升序 |  | 待 P2–P5 | NeeView/Command/Commands/SetBookOrderByTimeStampACommand.cs |
| SetBookOrderByTimeStampD | 书籍日期降序 |  | 待 P2–P5 | NeeView/Command/Commands/SetBookOrderByTimeStampDCommand.cs |
| SetBookOrderByEntryTimeA | 书籍登记时间升序 |  | 待 P2–P5 | NeeView/Command/Commands/SetBookOrderByEntryTimeACommand.cs |
| SetBookOrderByEntryTimeD | 书籍登记时间降序 |  | 待 P2–P5 | NeeView/Command/Commands/SetBookOrderByEntryTimeDCommand.cs |
| SetBookOrderBySizeA | 书籍大小升序 |  | 待 P2–P5 | NeeView/Command/Commands/SetBookOrderBySizeACommand.cs |
| SetBookOrderBySizeD | 书籍大小降序 |  | 待 P2–P5 | NeeView/Command/Commands/SetBookOrderBySizeDCommand.cs |
| SetBookOrderByRandom | 书籍随机排序 |  | 待 P2–P5 | NeeView/Command/Commands/SetBookOrderByRandomCommand.cs |
| TogglePageMode | 切换页面模式 |  | P1 Engine | NeeView/Command/Commands/TogglePageModeCommand.cs |
| TogglePageModeReverse | 切换页面模式 (反向) |  | 待 P2–P5 | NeeView/Command/Commands/TogglePageModeReverseCommand.cs |
| SetPageModeOne | 单页显示 | Ctrl+1 | P1 Engine | NeeView/Command/Commands/SetPageModeOneCommand.cs |
| SetPageModeTwo | 双页显示 | Ctrl+2 | P1 Engine | NeeView/Command/Commands/SetPageModeTwoCommand.cs |
| ToggleIsPanorama | 全景模式 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleIsPanoramaCommand.cs |
| TogglePageOrientation | 切换页面方向 |  | 待 P2–P5 | NeeView/Command/Commands/TogglePageOrientationCommand.cs |
| SetPageOrientationHorizontal | 水平页面布局 |  | 待 P2–P5 | NeeView/Command/Commands/SetPageOrientationHorizontalCommand.cs |
| SetPageOrientationVertical | 垂直页面布局 |  | 待 P2–P5 | NeeView/Command/Commands/SetPageOrientationVerticalCommand.cs |
| ToggleBookReadOrder | 切换右开/左开 |  | P1 Engine | NeeView/Command/Commands/ToggleBookReadOrderCommand.cs |
| SetBookReadOrderRight | 右开 (从右向左) |  | P1 Engine | NeeView/Command/Commands/SetBookReadOrderRightCommand.cs |
| SetBookReadOrderLeft | 左开 (从左向右） |  | P1 Engine | NeeView/Command/Commands/SetBookReadOrderLeftCommand.cs |
| ToggleIsSupportedDividePage | 分割横向页面 |  | P1 Engine | NeeView/Command/Commands/ToggleIsSupportedDividePageCommand.cs |
| ToggleIsSupportedWidePage | 横向页面视为双页 |  | P1 Engine | NeeView/Command/Commands/ToggleIsSupportedWidePageCommand.cs |
| ToggleIsSupportedSingleFirstPage | 首页单独显示 |  | P1 Engine | NeeView/Command/Commands/ToggleIsSupportedSingleFirstPageCommand.cs |
| ToggleIsSupportedSingleLastPage | 尾页单独显示 |  | P1 Engine | NeeView/Command/Commands/ToggleIsSupportedSingleLastPageCommand.cs |
| ToggleIsRecursiveFolder | 载入子文件夹 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleIsRecursiveFolderCommand.cs |
| ToggleSortMode | 切换页面顺序 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleSortModeCommand.cs |
| SetSortModeFileName | 文件名升序 |  | P1 Engine | NeeView/Command/Commands/SetSortModeFileNameCommand.cs |
| SetSortModeFileNameDescending | 文件名降序 |  | P1 Engine | NeeView/Command/Commands/SetSortModeFileNameDescendingCommand.cs |
| SetSortModeTimeStamp | 文件日期升序 |  | P1 Engine | NeeView/Command/Commands/SetSortModeTimeStampCommand.cs |
| SetSortModeTimeStampDescending | 文件日期降序 |  | P2 Engine 接入；Windows动态对照待验 | NeeView/Command/Commands/SetSortModeTimeStampDescendingCommand.cs |
| SetSortModeSize | 文件大小升序 |  | P1 Engine | NeeView/Command/Commands/SetSortModeSizeCommand.cs |
| SetSortModeSizeDescending | 文件大小降序 |  | P2 Engine 接入；Windows动态对照待验 | NeeView/Command/Commands/SetSortModeSizeDescendingCommand.cs |
| SetSortModeEntry | 文件登记时间升序 |  | 待 P2–P5 | NeeView/Command/Commands/SetSortModeEntryCommand.cs |
| SetSortModeEntryDescending | 文件登记时间降序 |  | 待 P2–P5 | NeeView/Command/Commands/SetSortModeEntryDescendingCommand.cs |
| SetSortModeRandom | 随机 |  | P1 Engine | NeeView/Command/Commands/SetSortModeRandomCommand.cs |
| SetDefaultPageSetting | 重置页面设置 |  | 待 P2–P5 | NeeView/Command/Commands/SetDefaultPageSettingCommand.cs |
| ToggleBookmark | 添加/删除书签 | Ctrl+D | P2 宿主接入；具体范围见验收表 | NeeView/Command/Commands/ToggleBookmarkCommand.cs |
| RegisterBookmark | 注册书签 |  | P2 第五批宿主适配；完整范围见书签契约 | NeeView/Command/Commands/RegisterBookmarkCommand.cs |
| NextPlaylist | 下一播放列表 |  | P2 第八批原列表/标记子集 | NeeView/Command/Commands/NextPlaylistCommand.cs |
| PrevPlaylist | 上一个播放列表 |  | P2 第八批原列表/标记子集 | NeeView/Command/Commands/PrevPlaylistCommand.cs |
| TogglePlaylistItem | 添加/删除播放列表项目 | Ctrl+M | P2 第八批原列表/标记子集 | NeeView/Command/Commands/TogglePlaylistItemCommand.cs |
| PrevPlaylistItem | 上一个播放列表项目 |  | P2 第八批原列表/标记子集 | NeeView/Command/Commands/PrevPlaylistItemCommand.cs |
| NextPlaylistItem | 下一个播放列表项目 |  | P2 第八批原列表/标记子集 | NeeView/Command/Commands/NextPlaylistItemCommand.cs |
| PrevPlaylistItemInBook | 书籍中的上一个播放列表项目 |  | P2 第八批原列表/标记子集 | NeeView/Command/Commands/PrevPlaylistItemInBookCommand.cs |
| NextPlaylistItemInBook | 书籍中的下一个播放列表项目 |  | P2 第八批原列表/标记子集 | NeeView/Command/Commands/NextPlaylistItemInBookCommand.cs |
| SetEffectProfile | 设置效果配置 |  | 待 P2–P5 | NeeView/Command/Commands/SetEffectProfileCommand.cs |
| NextEffectProfile | 下一个效果配置 |  | 待 P2–P5 | NeeView/Command/CommandTable.cs |
| PrevEffectProfile | 前一个效果配置 |  | 待 P2–P5 | NeeView/Command/CommandTable.cs |
| ToggleCustomSize | 启用/禁用自定义大小 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleCustomSizeCommand.cs |
| ToggleTrim | 切换裁剪 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleTrimCommand.cs |
| ToggleResizeFilter | 启用/禁用调整大小滤镜 | Ctrl+R | 待 P2–P5 | NeeView/Command/Commands/ToggleResizeFilterCommand.cs |
| ToggleGrid | 启用/禁用网格 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleGridCommand.cs |
| ToggleEffect | 启用/禁用效果 | Ctrl+E | 待 P2–P5 | NeeView/Command/Commands/ToggleEffectCommand.cs |
| ToggleIsLoupe | 启用/禁用放大镜 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleIsLoupeCommand.cs |
| LoupeOn | 启用放大镜 |  | 待 P2–P5 | NeeView/Command/Commands/LoupeOnCommand.cs |
| LoupeOff | 退出放大镜 |  | 待 P2–P5 | NeeView/Command/Commands/LoupeOffCommand.cs |
| LoupeScaleUp | 提高放大镜倍率 |  | 待 P2–P5 | NeeView/Command/Commands/LoupeScaleUpCommand.cs |
| LoupeScaleDown | 降低放大镜倍率 |  | 待 P2–P5 | NeeView/Command/Commands/LoupeScaleDownCommand.cs |
| ToggleHoverScroll | 启用/禁用悬浮滚动 |  | 待 P2–P5 | NeeView/Command/Commands/ToggleHoverScrollCommand.cs |
| ToggleAutoScroll | 切换自动滚动 | MiddleClick | 待 P2–P5 | NeeView/Command/Commands/ToggleAutoScrollCommand.cs |
| CancelScript | 中止脚本 |  | 待 P2–P5 | NeeView/Command/Commands/CancelScriptCommand.cs |
| OpenOptionsWindow | 打开设置窗口 |  | P1 宿主适配 | NeeView/Command/Commands/OpenOptionsWindowCommand.cs |
| OpenSettingFilesFolder | 打开配置文件位置 |  | 待 P2–P5 | NeeView/Command/Commands/OpenSettingFilesFolderCommand.cs |
| OpenScriptsFolder | 打开脚本文件夹 |  | 待 P2–P5 | NeeView/Command/Commands/OpenScriptsFolderCommand.cs |
| OpenVersionWindow | 显示版本信息 |  | 待 P2–P5 | NeeView/Command/Commands/OpenVersionWindowCommand.cs |
| CloseApplication | 退出应用程序 |  | P1 宿主适配 | NeeView/Command/Commands/CloseApplicationCommand.cs |
| TogglePermitFile | 启用/禁用文件操作 |  | 待 P2–P5 | NeeView/Command/Commands/TogglePermitFileCommand.cs |
| HelpCommandList | 显示命令帮助 |  | P1 宿主适配 | NeeView/Command/Commands/HelpCommandListCommand.cs |
| HelpScript | 显示脚本帮助 |  | 待 P2–P5 | NeeView/Command/Commands/HelpScriptCommand.cs |
| HelpMainMenu | 显示主菜单帮助 |  | 待 P2–P5 | NeeView/Command/Commands/HelpMainMenuCommand.cs |
| HelpSearchOption | 搜索选项帮助 |  | 待 P2–P5 | NeeView/Command/Commands/HelpSearchOptionCommand.cs |
| OpenContextMenu | 打开上下文菜单 |  | 待 P2–P5 | NeeView/Command/Commands/OpenContextMenuCommand.cs |
| ExportBackup | 导出设置 |  | 待 P2–P5 | NeeView/Command/Commands/ExportBackupCommand.cs |
| ImportBackup | 导入设置 |  | 待 P2–P5 | NeeView/Command/Commands/ImportBackupCommand.cs |
| ReloadSetting | 重新载入设置 |  | 待 P2–P5 | NeeView/Command/Commands/ReloadSettingCommand.cs |
| SaveSetting | 保存设置 |  | 待 P2–P5 | NeeView/Command/Commands/SaveSettingCommand.cs |
| TouchEmulate | 模拟触控 |  | 待 P2–P5 | NeeView/Command/Commands/TouchEmulateCommand.cs |
| FocusPrevApp | 切换到上一个 NeeView | Ctrl+Shift+Tab | 待 P2–P5 | NeeView/Command/Commands/FocusPrevAppCommand.cs |
| FocusNextApp | 切换到下一个 NeeView | Ctrl+Tab | 待 P2–P5 | NeeView/Command/Commands/FocusNextAppCommand.cs |
| StretchWindow | 调整窗口大小 |  | 待 P2–P5 | NeeView/Command/Commands/StretchWindowCommand.cs |
| OpenConsole | 打开脚本控制台 |  | 待 P2–P5 | NeeView/Command/Commands/OpenConsoleCommand.cs |
