# P5 第二十九批：原书架路径与登记顺序排序

## 职责与依赖

Engine从固定基线c5c398d89迁入FolderOrderClass资格表，接通SetBookOrderByPathA/D和SetBookOrderByEntryTimeA/D。唯一BookshelfFolderList、BookmarkFolderList、FolderCollection及Foldres.json继续承担排序/位置/保存；三个生产项目不变。Mac表现只映射标签、菜单可用性和勾选，不枚举来源或写配置。

## 契约和原行为

| 来源 | 原类别 | 路径 | 登记顺序 |
|---|---|---|---|
| 普通目录及普通归档列表 | Normal | 不开放 | 不开放 |
| 普通书架搜索 | WithPath | 升/降序 | 不开放 |
| 书架中的书签目录 | Full | 升/降序 | 升/逆序 |
| 快速访问 | None | 不开放 | 不开放 |

普通搜索保留原目录分组，随后按完整目标路径自然比较；相等路径保持来源相对顺序，不新增名称二次排序。书签登记顺序沿既有原父Children索引及反转，与EntryTime日期无关；书签Path沿真实目标路径而非自定义显示名。

原独立书签面板已有搜索排序，继续共用BookmarkFolderList算法；本批四命令作用于书架，没有新增书架书签搜索入口，也不将两个面板合为同一位置状态。

## 状态及生命周期

当前已提交来源和搜索条件计算资格；请求加载中禁止排序。导航锁获取前、获取后和SaveData状态锁内均复核来源，排队中的旧动作不能写入新的目录。重排保持真实路径/书签节点选择，不打开或解码书籍，不重复扫描。

排序保存沿EditFolderParametersAsync和原五JSON事务，失败还原目录参数并ReloadParameter，允许重试。搜索中的Path参数仍按原目录保存；清空搜索只将当前普通列表回退FileName，不覆盖保留字段，重新搜索和重启后搜索可恢复Path排序。全局默认不改变。快速访问按原树顺序发布，FolderOrder显示FileName，不写伪排序参数。

CommandTable.IsAvailable只表示已登记执行入口；BookOperation.CanChangeFolderOrder表示当前来源可执行。菜单和下拉框消费同一Engine资格，换来源与开始/结束加载即时刷新。未知命令仍报未迁移。

## 错误与验证

不支持排序、来源加载、关闭或旧来源动作直接忽略；保存失败传播并回滚，读取失败沿原列表保持。专项覆盖四命令、日期与树序不同、显示名与路径不同、双向搜索路径、稳定同路径、分组、选择保持、重启、清空恢复、失败重试、状态锁换来源及正式XAML菜单/下拉框。

构建、自动测试、Headless和本地签名分别记录；本批不激活正式应用，不修改用户Profile或用户图片。Windows动态排序及真实交互仍另验。

## 扩展

新增来源应指定原FolderOrderClass，复用既有排序与目录参数。扩展标签/主题无需改排序规则；完整设置、高级效果、视频和脚本沿P5清单继续迁移。
