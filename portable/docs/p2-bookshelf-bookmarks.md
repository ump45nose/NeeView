# P2 第十九批：原书架书签位置与列表互联

## 职责、依赖与契约

BookshelfFolderList在原结构下接通bookmark scheme；普通目录仍由IArchiveFactory枚举，虚拟位置只能在SaveData的唯一BookmarkCollection中解析。书架与独立BookmarkFolderList共享原节点与Foldres目录参数，浏览位置、选择和搜索结果独立。FolderItem仅附原节点引用，普通条目为空；不创建身份数据库或第二棵树。

FocusBookmarkList沿原SidePanelFrame显示FolderPanel书签根并聚焦书架列表；FocusBookmarkSearchBox继续显示独立BookmarkPanel搜索框。书架菜单可将当前目录和选择转到独立面板。书架书签树只改变本书架，双击虚拟文件夹进入，真实条目进入唯一BookOperation打开链。未迁能力继续原菜单占位。

## 状态与资源生命周期

bookmark地址保留逻辑反斜杠，不通过GetFullPath或文件系统。目录/别名定位使用原节点，手动刷新及加载后的选择优先保留相同引用；Select启动记录仍沿原真实目标路径。目录删除退到原存活祖先，只有真实书签事务刷新集合，阅读保存不重排。

FolderParameter接原bookmark类别默认，当前目录改动不修改全局BookmarkFolderOrder；随机Seed、大小/时间/路径/登记排序均保存在Foldres。两列表重新进入读取同一参数。失败回滚同一集合并重建当前参数，选择/种子可重试。

来源元数据只在时间/大小排序按需查询，普通名称/注册排序不探测。书签文件夹时间为EntryTime，真实书籍为来源LastWriteTime/Length；目录长度为-1，缺失保留占位。相同来源路径单请求去重；纯重排复用缓存，显式刷新重新探测。元数据排序补齐不会取消正在进行的结构化搜索。树变更/导航/取消/关闭使晚到结果失效，不删除节点。

## 启动、保存与错误

StartUp原IsOpenLastFolder/IsOpenLastBookmarkFolder默认false，设置页独立草稿。原BookshelfFolderMemento沿Path/Select/FolderOrder/IsFolderRecursive/Seed保存，退出进入既有四JSON事务；LastBookV2和未知字段仍保持。独立面板和书架分别恢复，不共享浏览选择。未映射Windows路径及不存在位置保留原记录/可识别错误，完整路径导入在P5。

独立面板原IsSyncBookshelfEnabled默认true，关闭后打开其书签保留书架位置；书架自身书签开书保持该bookmark目录。来源权限/超时只显示暂不可访问，导航失败不提交新位置；失败或原生晚到不覆盖当前书籍。视图只转交节点与命令，不扫描磁盘/写JSON。

## 测试与验收

新增专项覆盖虚拟根/逐级导航、共享节点与独立位置、按目录排序/随机重启、同路径别名刷新及开书选择、真实来源元数据与目录日期、晚到取消、目录保存失败重试、书签提交与阅读保存回报区别、正式焦点命令/菜单互联/树导航及两个列表启动恢复。既有书签导航/搜索/目录参数回归保持。

新增并发排序/搜索回归；启动时先打开后布局的零视口原点改为首次有效布局重新定位，并验证整图落在视口内。正式构建、自动回归、Headless布局、本地签名、Windows动态与真机输入分别记录；默认静默，不激活正式应用或访问用户数据。P2继续历史策略/模板、浮动宿主及剩余输入/动画，不由本批测试封板。

## 扩展点

普通文件系统完整延迟树及大量浏览在P3；源监视/修复/完整旧数据导入保持既定后续清单。虚拟目录依然使用原节点与参数，不扩大生产项目数。
