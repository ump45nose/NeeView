# P2 第十五批：原真实书籍页与父子书导航

## 职责与依赖

在原 Book/Page/Archive/BookOperation 关系下迁入真实 Folder/Archive 页、三种页面收集模式、目录递归、按需封面及父子书。Engine不引用界面，Backends实现实际目录/归档读取；独立ArchivePageRenderer转换原卡片表现和主题资源。没有新增阅读内核或身份体系。

## 契约

- BookSourceFactory/ArchiveEntryCollection保留原Image/ImageAndBook/All及CurrentDirectory/IncludeSubDirectories/IncludeSubArchives数值。默认ImageAndBook，非递归归档默认IncludeSubArchives。
- PageType保留Folder/Archive/File/Empty数值；IsFolder包含目录和归档。非图像页框为原480×640，IsFileContent禁止图片自动旋转。封面图片自身保持比例，不改变书籍页框尺寸。
- BookAddress保存真实目标和父书位置；MoveToParentBookAsync明确定位真实子项，MoveToChildBookAsync使用当前主页面，不使用独立书架选择。封面双击OpenChildBookAsync核对所属书籍并打开实际命中页。
- ToggleRecursiveFolderAsync沿唯一OpenCore重新收集，原memento保持设置/位置；从展开图片收敛到真实祖先书籍页，从书籍页展开到其内容。
- ArchivePageUtility按原指定目标、regex、自然首图、有限子书深度与Take(depth)查封面。包内目录的整个严格前缀范围参与首图选择，depth只限制另行打开子书，不能误改为“只看直接图片”。

## 状态与资源生命周期

书籍集合拥有根和已打开递归子来源，失败/取消/关闭反向释放。封面仅在可见图像需求中打开来源，请求拥有ArchivePageCover；解码后或取消后关闭。不可中断解码晚到时清理像素及来源，不进入新画面。缓存、缩略图仍共用原BitmapFactory/预算/后台槽。

ZIP逻辑目录只过滤原条目并保存物理ID映射，不解压为文件夹；映射完整快照一次替换。CurrentDirectory补齐隐式直接目录；严格分隔边界避免chapter匹配chapter-long。书架读取逻辑目录时共用来源集合，不把虚拟路径交给DirectoryInfo。非当前目录模式同步到根包所在目录并定位根包。

## 业务、错误与前端

原WherePageAll去除有子内容的书籍项，空书和shortcut保留。递归跳过shortcut防环；坏子书仍是可识别页面，其余子书继续。空书可进入/返回；坏包或父书定位失败保持原书和位置，并可用同一入口重试。Mac真实路径中的反斜杠保持原值。

空封面为正常卡片，未知类型为明确未迁移提示；逐页处理不终止双页或可见胶片条其他图像。原卡片上3/下1区域、叠页及等比封面在独立表现类中，颜色在主题资源；原Windows像素级动态对照未执行。封面单击不触发阅读鼠标命令，双击打开实际项。信息区仍遵循查看器输入。菜单根据当前主页面/加载状态启用，迁移状态与运行时无目标分开。

原显式Thumbs目标和BookThumbnailRegex字段只读接入；目标编辑/重命名联动后续迁入。无效regex回退首图；新增250ms单项匹配预算避免异常配置阻塞。密码/分卷/真正包内嵌套归档继续P5，嵌套打开明确提示，不回根包冒充成功。

## 测试与扩展

BookHierarchyTests新增17项：真实混合目录、三模式、父子往返/空书/书架独立、反斜杠、递归/shortcut/来源释放、ZIP显式/隐式/空目录与物理流、两归档模式、坏包/父书失败重试、嵌套能力、指定封面/regex/深度及候选数量、晚到解码、正式Headless目录/归档卡片及单/双击。

全量/正式构建和签名见独立验收JSON；真实键鼠、NAS、Windows动态对照、长期资源和显示完成P95仍单独验收。高级媒体和嵌套在既有来源/页面链扩展，P3大量图片另行迁入。
