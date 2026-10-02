# P2 第五批：原书签集合操作

日期：2026-10-03。迁入原书签移动、递归合并、颜色、登记编辑及删除恢复规则。P2 尚未整体完成；“集合操作”不代表完整原书签导航面板或任意旧 Profile 兼容。

## 职责与依赖

Engine.BookmarkCollection 对现有 BookmarkNode/Children 执行原集合算法，SaveData 负责串行编辑与既有三 JSON 事务。BookmarkPopupEdit 保留原登记上下文的名称、同父查找和 Add/Edit/Remove 含义。Mac 表现模型只维护选择和多选数量；MainWindow.Bookmarks 处理指针、目标选择和反馈，BookmarkRegistrationWindow 的 XAML 可独立调整。视图不读取目录、不解压、不写文件，不新增树框架或第二套状态。

## 契约与生命周期

- AddBookmarkFolderAsync 使用原名称校验和同父文件夹递增；RegisterBookmarkAsync 是原服务的同父路径去重入口。RegisterBookmark **命令**打开登记窗口，不能改为直接添加。
- MoveBookmarkAsync 的 index 为 null 时执行原 MoveToChild，返回实际保留节点；同父级、非文件夹或循环移入为无操作/null。数字索引执行原 Move，使用源移除后的最终索引；非法循环抛错误。脱离当前树的节点或文件夹拒绝提交。
- RenameBookmarkAsync 返回实际保留节点。同父同名文件夹先抛 BookmarkMergeRequiredException，表现端确认后传回确切目标；目标已消失/改变时重新操作。普通书籍名称不触发文件夹合并。
- RemoveBookmarksAsync 保留一批 Node/Parent/Index；选中父子时只删除最高选中节点，逆序恢复避免同父顺序错乱。RestoreBookmarksAsync 仅恢复仍在当前树中的原父级，索引超长追加末尾。记录仅进程内保存，退出清空；不是无限撤销/重做。
- 所有编辑与保存共用 SaveData 的 gate。失败原地恢复 Name/Color/Children 及恢复记录，节点身份、颜色、层次和重试引用保持。未知 JSON/旧 Page/Props 不丢失。
- 窗口关闭释放拖动捕获，最终阅读保存等待已进入同一 gate 的写入；关闭期间/之后不接受弹窗晚到结果或更新已关闭视图。

## 原业务规则及出处

| 行为 | 原出处 | 当前适配 |
|---|---|---|
| 同名目录递增、顺序/跨级移动 | Bookamrk/BookmarkCollection.AddNewFolder/Move | 原算法，ObservableCollection 通知，索引钳制首尾 |
| 文件夹移入、合并、书签去重 | BookmarkCollection.MoveToChild/Merge；Bookmark.IsEqual；BookmarkFolder.IsEqual | 文件夹按名称、书籍按显示名称＋路径；移入首位，合并追加 |
| 子节点身份 | Collections/Generic/TreeListNode.CloneChildren | 原方法仅复制列表，迁移使用 ToArray 快照，保留节点引用与未知字段 |
| 重命名与颜色 | BookmarkCollectionService.Rename/SetColor；BookmarkTools.GetValidateName | 合并确认在 Mac，颜色仅文件夹、原 #AARRGGBB；斜线转换为下划线 |
| 删除恢复 | BookmarkCollectionService.RemoveBookmark；BookmarkCollection.Restore | 原父级/索引、逆序记录；Mac 对单项也显示显式恢复按钮 |
| 登记编辑 | AddressBar/BookmarkPopupEdit；RegisterBookmarkCommand | 名称/目标/完成-添加-移除；合并后编辑实际保留节点，避免访问已删除源引用 |

拖到文件夹行中间执行 MoveToChild；行边缘执行顺序移动；树内空白移至根，树外释放取消。Esc 释放指针捕获。只有一项选中时启用移动、更名、颜色和重排；批次删除支持多选。书籍双击进入原打开/History 恢复链，文件夹只展开。书签刷新不发布正文 Refreshed 或解码需求。

## 错误与测试

保存失败保留旧完整文件和树，支持重试；取消弹窗不调用写入。合并确认目标过期、父级失效、循环与非法颜色明确处理。损坏 JSON 不覆盖原文件。测试覆盖名称规则、同父注册、Name+Path 去重、递归合并/身份/未知字段、过期确认、循环/索引、批次逆序恢复/缺失父级、准备失败/回滚/恢复重试、三文件中断启动恢复，以及正式 XAML 登记/取消、实际树拖动/Esc 捕获释放与正文独立性。

输入回归包含展开箭头的真实按下/释放：箭头不参与树级拖动捕获，双击只按实际命中行打开书籍，不能沿用旧书籍选择。新建、登记、颜色和恢复的保存错误重新选择仍有效的原节点。全量回归还发现图像刷新之后才同步书架会覆盖手动进入；现在来源变化时立即发起独立同步，后续手动请求按原代次取消它，目录读取不阻塞图像显示。

构建与自动测试原始输出使用独立 p2-bookmark-validation.json；截图按当前 phase 保存，不覆盖前四批证据。正式应用运行与自动测试分开记录，见 ../acceptance/p2-bookmark-macos-runtime.md。

## 未迁移与扩展点

原 BookmarkFolderList 的路径导航/书架互联、列表排序/搜索/递归搜索、Normal/Content/Banner/Thumbnail、监视/链接修复与移除无效、属性完整窗口尚未迁入。FocusBookmarkList 原本进入书架书签位置并联动目录树，当前继续禁用占位。登记当前采用独立模态宿主；原地址栏 Popup 定位、标签和折叠树选择器待迁，不宣称完整原弹窗已经转换。拖动多项/自动滚屏/外部新条目插入待后续。JSON 版本迁移、Profile/.nvzip 和 Windows 路径映射仍在 P5。

Windows 动态对照、真人触控板/IME、NAS、长期 native 内存、完整显示 P95、Developer ID/公证/安装未由本批自动测试证明。
