# P2 第十二批：原历史列表结构化搜索

2026-10-04。承接第六批历史导航和第十一批原查询库，替换历史面板的路径Contains子集。保持三个生产项目、同一阅读内核和原JSON权威，不增加数据库或第二套查询语法。

## 职责、依赖与契约

Engine.SearchHistoryCollection按原BookHistory.GetValue与HistoryList.Filter适配原查询库；HistoryList.SearchAsync捕获历史/书签成员快照并后台执行。GetViewItems只投影当前访问倒序、直接父目录和已提交路径结果，UI刷新及PrevHistory/NextHistory不执行正则或文件读取。新搜索成功才更新SearchKeyword；旧直接赋值入口改为可等待SearchAsync，调用方和原导航测试已同步。

HistorySearchViewModel独立管理草稿、Trim/Analyze、500ms输入合并、增量与确认、错误和可等待关闭。ReaderWorkspaceViewModel只装配契约并发布列表/选择表现；MainWindow.History处理焦点、键路由与菜单，XAML/主题可独立调整。元数据经BookOperation.GetFileMetadataAsync进入既有两槽/15秒SourceIo能力，不由控件读取。

SaveData.BookHistorySearchHistory使用原HistoryStringCollection；EditBookHistorySearchHistoryAsync与书签搜索复用同一三文件事务及原地回滚。原两个JSON字段独立，未迁BookshelfSearchHistory/PageListSearchHistory及未知字段保留。没有新的历史文件。

## 原业务规则与必要适配

| 原出处 | 当前行为 |
|---|---|
| BookHistory.GetValue(text) | 只匹配书名，保留原默认模糊/精确、引号、逻辑和正则；目录路径中的词不替代书名 |
| BookHistory.GetValue(date) | 日期是LastAccessTime，不读磁盘，也不使用LastWriteTime |
| BookHistory.GetLength / size | 大小查询才按需后台探测；真实文件长度，目录/不存在为-1；权限/暂不可访问不能冒充不存在 |
| BookHistory.GetValue(bookmark/history) | 书签成员按原真实来源；历史标志恒真；不遍历书签虚拟文件夹作为历史条目 |
| HistoryList.Filter | 先限制当前直接父目录，再逐项SearcherFilter；没有书签递归开关，不以集合Union改写布尔顺序 |
| SearchBoxModel / SearchBox.xaml.cs | Trim、校验、可选500ms增量；Enter/失焦确认有效非空表达式并先登记历史，不等慢匹配成功；关闭不确认草稿 |
| BookHistoryCollection.CreateMemento/Restore | 原字段BookHistorySearchHistory；去重前置、默认容量8、零容量及删除沿原集合；History.IsKeepSearchHistory统一控制已迁两组落盘 |
| FocusHistorySearchBox / SidePanelFrame | 明确显示历史面板与搜索框，聚焦全选，不重复切换关闭，不激活系统应用 |
| LoosePath.GetDirectoryName/GetFileName | 明确盘符/UNC记录保留原双分隔符逻辑；Mac真实路径遵循本机规则，合法反斜杠文件名保留；不改实际来源路径，不代替P5导入映射 |

原历史使用逐项布尔过滤，与书签集合查询分别适配，共用原Profile和解析器。日期/大小排序、Page/元数据属性不在本批范围。原正则250ms单项上限沿用上一批必要适配。

## 状态与资源生命周期

每个窗口的HistoryList持有已提交结果、查询代次及请求取消；每次只拥有请求快照和短期元数据。输入替换、环境改变和关闭使旧结果失效，不能中断的系统调用仍由后端观察/释放。提交前再次检查直接目录、历史成员和实际使用的日期/书签属性。普通进度Page变化不重复触发大小读取。

无效草稿保留，历史删除/书签改变仅重筛上次有效表达式。关闭增量取消尚未确认输入，文字保留供Enter确认。空输入且已是普通列表时确认无需重刷，避免失焦在指针按下/释放间替换列表容器。历史菜单删除不改变查询或访问记录；晚到筛选不能再次登记被删除表达式。

正常关闭先拒绝输入、取消筛选、等待已开始的搜索/表达式事务，再保存阅读并释放来源；保存失败恢复查询入口。旧表现模型释放后拒绝回报，HistoryList由BookOperation最终释放。菜单配置保存失败恢复原运行时字段。

## 错误与测试

语法/正则错误、权限、来源超时和取消不覆盖当前有效列表；历史保存失败单独提示、原集合回滚并允许同词重试，已成功匹配的结果可保留。默认分析器与依赖版本不变。

自动回归覆盖原属性/访问倒序、书名与路径差异、直接/嵌套目录、日期无I/O、真实大小/目录/缺失、旧路径与Mac反斜杠、语法/权限/取消、晚到/环境/释放、原历史隔离/容量/重启/失败回滚、500ms增量与手动确认、无效草稿重筛、关闭等待、同词重试、菜单失败恢复和正式XAML输入/焦点/失焦。原历史单/双击、多选/空白右击和书签查询回归继续执行，截图使用本批独立前缀。

构建、自动测试、离线界面、正式应用运行、Windows对照及用户验收分别记录，见[本批静默记录](../acceptance/p2-history-search-runtime.md)。未测性能P95、NAS断线或长期内存，不以后台执行替代测量。

## 扩展点

四种历史显示模板、完整缩略图、原保留限制、无效清理、ClearHistoryInPlace、完整外部关键词/书架联动和原搜索帮助仍待迁。Windows动态查询样本及实际中文IME、焦点、弹出历史菜单、触控板待集中真机验收。P2尚未整体完成，P5完整Profile导入/路径映射与分发独立推进。
