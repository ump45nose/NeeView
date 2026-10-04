# P3 第七批：原页面目录与导航搜索

本批沿原 BookPageCollection、BookTableOfContents、PageSearchProfile、FileItem、FolderSearchCollection、SearchBoxModel 和 Config 迁移。搜索使用已内嵌的原 NeeLaboratory.IO.Search，不以界面字符串筛选替代原规则。只有 Engine、Backends、MacOS 三个生产项目，JSON 是唯一状态来源。

## 职责与依赖

- Engine.BookPageCollection 保存未过滤的 SourcePages 与当前可读 Pages；BookOperation 在唯一导航锁中提交搜索、原排序、页框和位置。
- Engine.BookTableOfContents 从 SourcePages 构建目录节点；节点引用同一 Page，不创建另一套页面或内容身份。
- Engine.SearchBookshelfCollection 和 BookshelfFolderList 管理普通书架搜索、来源元数据、递归范围和活动搜索监视；Backends 只实现既有来源枚举与文件监视接口。
- Mac.NavigationSearchViewModel 只管理输入草稿、确认、历史菜单、500ms增量、取消及错误。页面与书架拥有独立实例；匹配和持久化均调用 Engine。
- MainWindow.PageNavigation 管理目录树 Top/Left 分隔、焦点和菜单反馈；SettingsWindow.Navigation 编辑独立草稿。结构、主题、输入表现与业务规则分开。

## 契约与业务规则

`SearchPagesAsync(keyword, expectedBook, token)` 在导航锁中捕获书籍和原当前位置，后台执行 `SourcePages → Searcher.Search → BookPageSort`；候选邻页尺寸探测成功并再次核对书籍/代次后才提交正文集合。失败、取消和切书不提交半完成结果。排序和清空搜索复用原 Page 对象；搜索后重新编号 Index，EntryIndex 仍表示来源顺序。

渐进目录批次追加到完整 SourcePages，再执行当前搜索和原排序；被过滤的旧页不会因后续批次丢失。空结果保持原 CurrentPage 和 LastBook 条目，但正文页框为空；清空后恢复来源页及可用位置。全局播放列表从来源页映射标记，滑条和书内标记导航仅使用当前可读集合，隐藏页旧 Index 不能参与正文跳转。

PageNameFormat 保留 Smart/NameOnly/Raw/PageNumber 原数值。Smart 计算全源公共字符前缀并截到目录边界，显示逻辑分隔符为 ` > `；不因正文搜索改变公共前缀。分组标题依据当前正文相邻页目录，名称和模板可独立调整。

目录树沿原临时 FileName 排序，不随正文反序或搜索重建；每个目录首项及中间分支引用首次插入的代表页。仅书籍或 SourceVersion 变化触发后台重建。点击只跳代表页，不过滤正文；代表页被搜索隐藏时提示清空搜索。树构建失败允许下一次刷新重试。

普通书架 `SearchAsync(keyword, expectedPlace, token)` 沿原 BookSearchProfile：名称、修改时间、大小、真实书签成员及历史成员。原递归开关决定目录范围，搜索只改变书架，不切换正文。递归来源枚举不进入符号链接目录，避免循环；目录/归档条目继续按原书架分组和排序。归档逻辑位置不创建文件监视，使用手动刷新。QuickAccess/书架书签虚拟位置明确提示能力边界，书签搜索仍使用原独立书签面板。

原页面日期、大小和 playlist 谓词已接入。metadata/rating 属性及别名保留登记，但后端尚未迁移，表达式预检返回明确能力错误，不能返回假值或静默显示全部页。相关高级图像信息属于 P5。

## 状态与资源生命周期

确认有效表达式才写原搜索历史；增量筛选不登记，保存错误独立提示并允许重试。四个原字段 BookmarkSearchHistory/BookHistorySearchHistory/PageListSearchHistory/BookshelfSearchHistory 使用同一 SaveData 事务、同一总保存开关和各自集合；失败原地回滚，未知字段保持。

换书/书架位置取消旧输入及结果；普通翻页不重新搜索。关闭取消未确认输入、等待已确认历史保存和查询结束；退出保存失败后恢复同一表现模型的输入资格。目录树后台结果核对请求、书籍与生命周期，关闭取消并解除订阅。

搜索监视最多一个活动普通目录根，按递归配置监视名称/增删/修改/大小，250ms合并；普通树仍使用第六批最多32个展开节点的一级监视。两者目的独立。隐藏、换位及关闭释放搜索监视，重显示补齐隐藏期间变化；旧ID回报拒绝，正式宿主将回报投递UI线程。用户导航进行中，旧监视事件只记录补刷新需求，不能创建请求取代该导航；完成后按最终已提交位置处理。虚拟目录跳转失败保留原搜索条件、列表和监视。流和像素资源仍由既有来源及 BitmapFactory 管理。

## 错误与测试

坏语法、正则超时、未迁属性、来源读取或权限错误保留已提交结果；取消不提示为失败。新输入、树布局和设置均经原入口保存，失败恢复原配置和界面布局，不写第二套状态。

PageNavigationSearchTests 覆盖搜索/排序/空结果/清空、LastBook、来源与可读标记隔离、嵌套CBZ Smart名称/代表页和正式窗口搜索焦点/树/设置回滚。BookshelfSearchTests 覆盖递归/属性/排序、真实文件创建/重命名/隐藏删除/重显示监视、失效虚拟目录保持查询、导航前/在途旧监视不取代用户请求、四类历史往返及失败恢复、设置草稿保存/取消。ProgressiveIndexTests 补充搜索中追加来源批次。完整验证和构建见 [P3 收尾记录](../acceptance/p3-completion-runtime.md)。

## 扩展点

继续在原搜索 Profile 和来源契约中接入 metadata/rating；高级脚本参数、Profile导入在 P5，真实文件操作在 P4。普通导航树、目录组、搜索、名称与相关配置已进入同一产品链路；动态 Windows、设备焦点/弹出层和长期性能验收仍独立，见 [P3 收尾清单](p3-completion-checklist.md)。
