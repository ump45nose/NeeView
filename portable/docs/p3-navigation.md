# P3 第二批：普通目录树与逐页缩略图

原 FolderTree/FolderListConfig/PageListConfig 和 PageListBox 是行为与结构出处。本批在既有三个生产项目内迁入普通目录树子集，页面模板复用既有列表表现，不增加读取服务、来源身份、状态库或第二阅读内核。P3 未封板。

## 职责与依赖

- Engine 的 FolderTreeNodeBase/FolderTreeNodeDelayBase/DirectoryNode 保留原父子、展开占位、直接子目录及自然名称排序关系；原同步 Windows I/O 改为既有 IArchiveFactory.ListFoldersAsync。FolderTreeModel 持有独立选择与路径同步，BookshelfFolderList 拥有并释放唯一树。
- Mac 的 FolderTreeView 只绑定原节点并处理点击/Enter/焦点。MainWindow.DirectoryTree 负责原 FolderPanel 内 Top/Left、分隔条、更多菜单及配置提交。不在界面枚举文件。
- PageList 四模板复用 PanelListPresentation/PanelListItemView/VirtualizingThumbnailPanel/ListCoverImage。页面请求显式携带当前书原 Page，进入同一 BitmapFactory.GetAsync；不能把归档内部路径交给路径封面入口重新打开归档。
- XAML、主题、输入和资源适配各有入口。外观切换不修改阅读规则、书籍排序、来源或配置保存格式。

## 契约与业务规则

构造树、读取占位及构造子节点都不做 I/O。展开只枚举当前节点一级子目录；已加载分支再次展开复用缓存。显式刷新成功才替换集合，同名子节点引用及展开后代保留；失败保留旧子树并显示错误，移除当前选中节点退回仍有效的父节点。Mac 根是实际 `/`，显示“Mac”，不建立 Windows 驱动器模拟层。

目录树和普通书架列表有独立选择。方向键只选择，展开不改变书架/正文；普通行点击或 Enter 进入原 Bookshelf.SetPlaceAsync，浏览书架不把目录打开为新书。展开箭头、背景、滚动条和修饰点击不确认旧目录。

SyncDirectoryAsync 只沿目标祖先链生成子项，不递归扫描兄弟分支。自动同步仅在原显隐/自动同步字段开启且实际面板可见后执行；树有键盘焦点时保留用户选择，显式同步可以更新。归档内部地址和 bookmark scheme 不参与普通目录树。路径使用来源实际返回名称及 .NET 路径组合，不统一转小写；路径别名、大小写/Unicode 规范形式差异尚待来源能力完善。

页面列表保留 Normal/Content/Banner/Thumbnail，默认 Content。方向键和修饰多选只改变列表选中项，普通无修饰按下及 Enter 才进入唯一 JumpAsync；保留原 PageListBox 的按下定位、释放 MoveEnd 顺序。按下时记录修饰状态，提前释放修饰键不能误触正文焦点。FocusMainView 默认 false；为 true 时有效普通点击/Enter 后回正文。目录/归档页双击走原 OpenChildBookAsync；确认前在宿主核对 Page 属于当前 Book，导航锁内继续核对 expectedBook。

列表排序和页面定位继续由 Engine 管理，表现数组仍持有原 Page。模板切换、树布局/宽度及列表选择不切书、不扫描来源或重解码正文。书架普通选择/枚举回报不重建树分隔GridLength，保持拖动中的尺寸；只有书签/普通位置导致树显隐实际改变才重新分配布局。快速鼠标释放等待该次按下导航完成，再核对当前Page决定焦点。

## 状态与资源生命周期

节点枚举与路径同步分别有取消令牌和代次。折叠取消当前节点需求；树或其祖先面板隐藏/脱离、窗口关闭取消当前树需求；不能中断的后台枚举晚到时不提交结果。折叠和隐藏保留已生成的轻量元数据供再次显示。关闭递归释放，解除书架与选中节点订阅。

缩略网格仅实现视口及前后邻行容器，跳到离屏项直接实现目标；不为一万页生成全部控件或逐项解码。ListCoverImage 依据实际可见区域读取，Normal 不申请缩略资源，预实现的离屏邻行不读取。回收、换 Page、模板切换、隐藏及退树取消旧需求，晚到租约只归还；先释放 Avalonia Bitmap，再归还像素租约。解码及显示缓冲共用原独立 64 MiB 缩略预算和背景槽，不增设缓存。

原 Bookshelf 的树显隐、Top/Left、宽高及手动/自动同步写回 UserSetting.json；PageList 分支保存四模板与 FocusMainView。未知配置字段继续合并。设置保存失败恢复内存字段和布局；已保存成功后的目录读取失败属于独立树错误，不能造成内存/磁盘显隐不一致。关闭等待已经授权的设置保存。

## 错误与测试

目录暂不可访问保留已加载子树和书架；取消不作为错误。坏图片保留占位，其他可见缩略继续。旧 Page/关闭后的动作忽略，不能定位新书。设置失败显示现有错误并允许重试。

DirectoryTreeTests 覆盖懒加载/一级/自然排序、刷新引用与选择修复、折叠/隐藏/关闭晚到、祖先链/焦点/替代同步、确认与正文隔离、原 JSON 未知字段、真实 TreeView 键盘和布局保存失败。PageListThumbnailTests 覆盖万页虚拟化/租约有界、旧 Page 晚到、键盘/点击/焦点及原书籍身份、真实 ZIP 四模板/排序与设置回滚。

PageThumbnailResourceTests 沿用户约定从 `/Volumes/Picture/YY/秀人/5001-6000` 取三个子目录，检查首段/中段/末段缩略、模板切换、切书及路径树同步。只读来源、临时 Profile、正式 XAML Headless，实图截图留忽略 artifacts；不启动前台应用。每个位置还对实际绘制帧的目标缩略区域采样，拒绝纯色/空白，不能据此证明原图色彩准确性。数字/哈希证据见[运行记录](../acceptance/p3-navigation-runtime.md)。

## 未迁范围与扩展点

本批不标“完整目录树已迁”。原 QuickAccess、系统图标、驱动器/文件系统监视、树文件拖放/上下文高级操作、页面目录组树/搜索/智能名称格式与完整 Profile 编辑仍待逐项迁移；未知字段保留不表示执行支持。ToggleVisibleFoldersTree 只接入当前宿主普通显隐，完整脚本 On/Off/ByMenu 参数仍待输入/脚本迁移。

目录索引仍完整建立后打开，本批只使普通目录树展开延迟、页面控件/解码可见化。P3第三批已完成万项元数据测量及布局检查点/后台重排，见[p3-performance.md](p3-performance.md)；渐进目录索引及原帧级全景仍为P3后续目标。Headless 不证明正式 Mac 焦点/Retina/真实拖动/屏幕帧耗时或长期原生内存，真机和用户验收分别记录。
