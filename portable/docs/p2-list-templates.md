# P2 第二十一批：原列表四模板与可见封面

## 职责、依赖和契约

保留原PanelListItemStyle的Normal/Content/Banner/Thumbnail数值；分别在Bookshelf、History、Bookmark原分支保存，默认Content。不新增FolderList权威分支。原Panels四个Profile保持Normal Square/0、Content Square/64、Banner Banner/200、Thumbnail Original/128，以及文字/标签/详情/图像弹层开关；差分JSON在对应默认Profile上合并，未知字段继续保留。

PanelListPresentation负责模板和虚拟面板，PanelListItemView.axaml负责原内容/横幅/网格结构及主题，ListCoverImage拥有显示资源。DataContext/容器仍为原FolderItem、BookmarkNode、HistoryRow，不创建第二业务集合。书签原地名称/颜色变更订阅随视觉生命周期解除。历史详细日期始终LastAccessTime；目录来源时间、书签登记时间分别保持本来含义。

## 状态与资源生命周期

Normal不提交封面。Content/Banner使用VirtualizingStackPanel；Thumbnail使用Avalonia VirtualizingPanel适配固定单元、日期分组新行、物理滚动锚点、滚动定位、方向键及容器回收。只为实际可见封面提交150ms防抖需求，邻行容器不读封面；隐藏、离屏、回收和关闭取消需求并先释放Bitmap再归还像素租约。祖先显隐订阅只属于本控件视觉链，不使用全局扫描。

BookOperation.GetCoverAsync把原路径交给同一BitmapFactory与ArchivePageUtility；指定单图与归档内部条目优先精确定位，原Foldres指定目标、BookThumbnailRegex/Depth及自然首图顺序共用。相对封面仍以bookPath为基准，这是原FolderConfig.GetThumbnailFullPath规则，可指向归档内部。请求来源在解码结束逆序关闭，不进入长期缓存或Page字典。

缓存键包含稳定路径、真实来源版本、封面选择和设备像素规格；包内定位解析根归档版本。显式刷新失效路径封面并重提可见控件；重排和切模板复用预算内像素。统一缩略预算/后台槽/合并与取消规则不增加第二调度器。刷新后仍被显示的旧租约继续计入预算；原生晚到结果不提交新画面。

## 业务规则和错误

三个列表四入口独立保存；配置保存不登记阅读访问或改变正文。保存失败恢复原模板、选择和配置，关闭等待已经开始的动作。副作用仍由原SaveData四JSON事务完成。列表多选、Enter/单或双击、文本作用域和原节点编辑继续由各自宿主管理。

空书显示正常书籍图标；损坏、缺失、权限或超时显示该项错误，不能清空历史/书签。详情与预览使用现有字段/已租图像，不单独请求原图。现代化主题不承担排序、封面选择或阅读规则。

## 测试、验收与扩展

专项覆盖Profile差分/未知字段、三列表字段、路径缓存共享/来源关闭、显示旧租约预算、单图/包内/指定封面、空书、原生刷新晚到、隐藏晚到、万条目虚拟网格/方向键/远端定位/显隐、正式菜单保存失败回滚、历史访问日期。原资源/导航与全量回归独立运行，正式Library、ARM64.app及ad-hoc严格签名独立记录。

静默渲染检查Content、Banner和多列网格；未启动正式应用或操作用户数据。Windows动态像素对照、真实触控板/IME/Retina/NAS、长期native内存及完整显示P95仍单独待验。完整Profile设置编辑和系统图标细节后续随设置/平台能力完善；当前原字段保留且主要显示开关生效。
