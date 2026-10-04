# 源码迁移阶段验收（P0/P1 与 P2 分批增量）

更新：2026-10-04。本表取代旧重写方案阶段状态；旧Preview/56项测试/性能记录只作历史。源码实施、构建、自动测试、正式运行、原版对照、用户验收、提交与发布分别判断。后续默认采用[静默优先流程](../docs/validation-workflow.md)，前台交互另行约定时段。

| 阶段 | 本轮状态 | 交付/尚待验证 |
|---|---|---|
| P0迁移校准/骨架 | 源码、正式构建/启动通过；原版动态对照待验 | 固定基线、40文件迁入清单、235命令、原左右3/6布局表、三项目、原位置测试；正式Mac启动已验证 |
| P1核心阅读 | 模块/Headless及核心真机流程通过，未封板 | 目录/ZIP、原页框/单双/宽图/分割、缩放平移、导航信息、JSON恢复；真机已验证打开/分页/退出恢复，完整交互与原版视觉对照待验 |
| P2阅读导航 | 开发范围完成；整体验收未封板 | 25批原阅读/归档、历史/书签/搜索、胶片条/导航、父子书/每目录参数、原输入/手势/变换/动画、列表模板/封面、侧栏组合/自动隐藏/浮动及资源优化；开发收尾355项回归；设备输入修复375项、最新资源/排序节点376项回归与五步构建/签名通过。235原命令保留、138入口接入/97占位；目录/CBZ阅读及侧栏部分真机复演通过，完整Windows固定基线/无损Retina/长期RSS/屏幕性能仍待验；触控板用户要求跳过，多屏无环境，见p2-device-resources-runtime.md |
| P3大量图片 | 第一/二/三批静默增量通过，未封板 | 连续/瀑布、普通目录树/逐页四模板、单槽后台检查点重排及万项索引测量共用原链路；413项回归、正式构建/签名及三目录218张实图采样通过，见[p3-performance-runtime.md](p3-performance-runtime.md)；渐进目录索引、完整树/原帧级全景及设备性能仍待迁/验 |
| P4fork分类 | 待实施 | 原双区面板/数字/文件操作/UndoRedo，真实失败恢复 |
| P5兼容高级交付 | 待实施 | 完整原版本迁移/Profile导入、高级内容后端、签名公证与安装 |

## P0/P1 历史证据

- 新Engine.Tests自动测试：27通过、0失败、0跳过。原PagePosition/PageRange测试、页框关键组合、按字段恢复、Props、真实中文目录/CBZ/解码和重开定位。
- 资源/失败测试：共享请求取消、显示租约计费和预算回收、native晚到关闭清理、EXIF/透明/GIF/JPEG、不成功保存不销毁阅读器、双文件中断恢复。
- 正式XAML/主题Headless：区域和栏宽、Skia真实像素绘制、文本/查看器作用域、侧栏不请求图片、列宽调整/隐藏保持、共享关闭任务、旧Control不变为Meta。
- Engine/Backends与正式Mac入口源码Library检查通过。该检查不是正式.app或系统交互验收。
- 本机Xcode27.0（27A266a），macOS27.0.1 ARM64；正式Mac构建及最终.app本地ad-hoc签名严格校验通过。没有增加Preview入口。
- 正式运行：目录/单图定位、中文CBZ、Ctrl+1/2模式、原Left/Right帧导航、宽图独页、系统打开事件、Command+W保存/驻留/重开恢复、Command+Q退出、完整重启恢复及冷启动明确文件优先。修复非文件激活重建窗口不恢复旧书的问题；[详细记录](p1-macos-runtime.md)与[真机截图](p1-macos-runtime.png)分别留证。
- 最新分步骤原始输出：p1-validation.json；TRX位于tests/NeeView.Engine.Tests/TestResults/engine.trx（构建输出，不提交）。
- [布局截图](p1-layout.png)：正式XAML/原转换颜色图标/Skia绘制。尚未取得Windows固定截图动态对照，不能宣称布局细节全部一致。

## 未执行的验收

Finder实际双击/拖放及定位、菜单子项/原生打开对话框、IME、多屏、NAS断线/权限/跨卷、长期RSS稳定性、原Windows构建及完整动态阅读/分类回归、真实旧Profile导入、用户细节验收均待执行（用户已认可总体布局）。当前Retina运行时100%映射子集已验证，无损像素1:1采样和多屏缩放未验；触控板按用户要求跳过、未验。Developer ID、公证和干净安装未执行。无推送、远端CI或发布。

性能目标仍为首图屏幕显示完成P95≤1秒、预取翻页P95≤100ms、缓存60Hz帧P95≤16.7ms。P2收尾已测固定小样本的软件完整帧，方法/限制见p2-resources.md；真实屏幕性能未验，旧服务链路benchmark不得转作新实现性能结论。

## 本轮清理与保留

旧Core/Application/Content/Imaging/Persistence/Desktop/Platform.MacOS/Preview/Host及旧测试已退役；必要解码/原生系统资源代码迁入Backends，通用资源测试迁入新测试，RAR/7z夹具保留供P2。旧SQL没有继续参与产品，JSON是唯一权威状态。旧成果在Git历史，旧验收材料保留并标为历史，不维护两套实现。

原NeeView Windows源码未修改。用户portable/.DS_Store保持原样且不提交。已验收增量自动本地提交；提交号通过git log核查。正式运行门槛已满足，但完整交互、Windows动态对照及用户已认可总体布局，细节/新增交互验收未完成，不标记整体P0/P1封板。

## P2 首批与用户新增目标

- 用户已认可总体布局，要求原菜单未迁移能力保留占位，左右侧栏支持拖拽自动组合。
- 完整原八组菜单树已迁入，执行能力控制禁用状态，原节点/分隔/层级不因未实现而删除；中文文案补齐。
- RAR/7z 普通和固实读取，独立顺序 Reader 与每来源 2 GiB 缓存；五个真实夹具乱序重复解码/关闭流释放。
- 历史搜索/打开、原书签树基础编辑、共享阅读状态，三 JSON 文件保存及失败回滚；未知字段保留。
- 可见胶片条和导航器共用主加载工厂；缩略图独立 64 MiB 预算。全命令键位编辑保留原复杂默认绑定，新增错误/冲突阻止；菜单方向键与阅读作用域隔离。
- 最新自动测试 38 项通过；Engine、正式入口 Library、正式应用构建及本地 ad-hoc 严格签名校验分别记录在 [p2-validation.json](p2-validation.json)。正式运行证据单列 [p2-macos-runtime.md](p2-macos-runtime.md)，不替代 Windows 动态对照/用户验收。
- 首批尚未包含侧栏组合与原 NScroll；已由下述第二批接入，证据单独保存。
- 完整胶片条预览/鼠标模式、完整书签/导航业务仍未迁完。触控板桥接已接入，真人精确滚动/捏合待验。

本批保留 P1 证据，不用 P2 测试数覆盖历史结果，不把阶段首批标记为 P2 全部完成。推送/远端 CI/Developer ID/公证/发布未执行。

## P2 第二批：原侧栏组合与滚动翻页

- 原 LayoutPanel/Collection/DockPanelContent/Manager 子集适配，无 Avalonia 依赖；原默认左右 3/6、leader 整组跨栏、成员拆组、内容分半组合、组内顺序/比例、选择和 V2 JSON 恢复。未知 Windows 字段保留，浮动窗口及 V0/V1 导入未迁入。
- SidePanelPresenter 只管理唯一内容控件、指针捕获和拖放预览；拖动锁定自动隐藏，Escape/捕获丢失取消。纯布局刷新不触发阅读刷新；脱离视觉树的缩略图不继续申请资源。
- 原 NScroll/ScrollResult/DragArea.SnapView 迁入，PageFrameBox 计时/停顿顺序适配，PrevScrollPage/NextScrollPage 接入原分页链。原参数默认、限幅、舍入与旧字段映射保留；全景 PagesAsOne、完整参数编辑待迁入。
- 自动测试 **49 项通过、0 失败、0 跳过**，包含五模式/RTL/容差/停顿、原组算法/JSON、实际 Headless 拖放和取消、查看器先滚动再翻页/反向进入尾部、视口重算保留分割位置与小图边界约束。
- Engine、正式 Library 检查、正式 `.app` 构建和本地 ad-hoc 严格签名通过，独立输出 [p2-docking-validation.json](p2-docking-validation.json)。没有增加 Preview 入口，也没有更换构建目录。
- 真机组合/拆组/跨栏、非等分比例保存及重启恢复、占位选择和读取位置恢复见 [运行记录](p2-docking-macos-runtime.md) 与 [截图](p2-docking-macos-runtime.png)。合成滚动观察只能证明此次显示结果，不能替代真实鼠标/触控板验收。Windows 动态对照与新增交互用户验收仍待执行。
- 当前源码表为 43 个迁入文件和 12 项子集适配；不能用数量折算整体功能覆盖率。原 Windows 源码及历史验收图保留，用户 `.DS_Store` 不提交。

P2 尚未整体完成。其余常用导航、完整胶片条、书签和输入能力继续迁移；性能 P95、长期内存、真实设备和 Windows 动态对照仍按独立证据验收。本增量自动本地提交，不推送或发布。

## P2 第三批：原页选择、胶片条及导航历史

- 原PageSelector共享临时选择，胶片条/滑条方向、静态双页校正、中心优先可见请求和100项环形历史迁入；没有新增数据库或阅读内核。
- 胶片条三滚轮、点击/Enter确认、详情、首尾居中、200ms防抖/同窗口去重；滑条联动预览和释放确认、配置页及一起始JumpPage接通。NextSizePage/PrevSizePage沿用原共享参数与周期对齐/首尾终止。
- 两种导航历史分别按页面条目和打开顺序重放，保持前进分支；失败/被取代不提交游标，跨书保留访问排序/时间。未知JSON和命令参数保留，列表选择刷新修复。
- 自动测试 **64通过、0失败、0跳过**；原49项回归加本节点专项用例。Engine、正式Library检查、正式.app构建与本地ad-hoc签名分别通过，见 [独立输出](p2-selection-validation.json)。
- 正式Mac选择确认、页输入、跨书后退/前进、实际滑条、配置及重开观察见 [真机记录](p2-selection-macos-runtime.md)。工具合成输入不替代真实设备或Windows动态对照。
- 源码表44项算法/文件迁入、18项子集适配；数量不代表功能覆盖率。原Windows源码及旧验收图保持；应用留打开，自动本地提交，无推送/发布。

P2剩余为常用兄弟书/子书导航、完整书签/输入、页标记/直接页号文本框/历史列表菜单和完整自动隐藏等。播放列表标记/全局自动隐藏/Windows边界反馈本批只保留字段。64项测试不等同P2封板或用户新增交互验收。

## P2 第四批：原普通书架与文件夹页导航

- 原FolderOrder/FolderCollection/BookshelfFolderList/BookPageCollection子集与算法迁入，目录/归档混合元数据列表、自然排序/目录分组、普通端点及随机循环、独立浏览/同步/刷新接通；浏览和重排不请求正文。
- PrevBook/NextBook成功打开才提交选择，失败保留旧书并可重试；默认忽略加载中的切书，原IsPrioritizeBookMove可取代旧请求。PrevFolderPage/NextFolderPage按当前书目录组跳页，保留组内回首图和正向帧入口。
- 沿用Bookshelf原JSON及未知字段，全局DefaultFolderOrder/FolderSortOrder保存；每目录FolderParameter/FolderConfig.json、持久随机种子及真实Folder页/父书定位仍待迁，不以浏览上一级替代原命令。
- 累计自动测试 **78通过、0失败、0跳过**；混合候选/坏包、排序/同值、恢复/边界/失败重试、取消/晚到/关闭、优先切书、双向章节导航、JSON及正式ListBox键盘作用域验证通过。
- 真机发现旧书架末项提示遮蔽后续成功导航，已在Engine成功提交时清除，补回归并在最终正式应用复测。最终Engine、正式Library、正式.app及本地ad-hoc签名均通过，见 [独立输出](p2-bookshelf-validation.json)。
- 正式混合书架、Enter/方向键、目录到归档、前后书位置恢复、独立浏览、排序混排保存/重启及章节跳页见 [运行记录](p2-bookshelf-macos-runtime.md)。证据使用新文件，不覆盖前三批截图；临时键位清理，原中文CBZ第三页/组合侧栏恢复。
- 当前源码表45文件/24子集适配，235命令完整清单持续保留；数量不折算功能覆盖率。原Windows代码及用户.DS_Store未修改；自动本地提交，无推送/发布。

P2剩余为完整书签/输入、标记/直接页号/历史列表菜单、真实子书/父书定位和书架详细能力等。下一独立节点迁入书签合并/移动/恢复。Windows动态对照、真人设备/NAS、长期内存、完整帧P95和新增交互用户验收仍待执行，P2不封板。

## P2 第五批：原书签集合操作

- 原 BookmarkCollection/CollectionService、BookmarkPopupEdit 和删除 memento 子集迁入，保留同父注册、名称规则、Move/MoveToChild 区别、名称＋路径判等、递归合并及实际子节点引用；未知 JSON/Page/Props 保持。
- 接通单项移动/拖入/重排、颜色、同名合并确认、登记 Add/Edit/Remove 和多选批次删除/逆序恢复。记录只在进程内，原父级失效不重建；保存失败原地恢复树、恢复批次及有效选择。视图仅处理输入/选择/对话框，Engine 不依赖控件。
- 累计自动测试 **89通过、0失败、0跳过**。新增原规则、引用/未知字段、过期确认/循环/索引、批次恢复、失败/取消/三文件中断及正式 XAML 登记/真实指针/正文独立性；最终 Engine、正式 Library、.app 与 ad-hoc 签名通过，见 [独立输出](p2-bookmark-validation.json)。
- 真机发现并修复拖动捕获/展开按钮及箭头双击误开书籍；全量回归修复晚到图像刷新触发书架同步覆盖手动浏览。最终拖入/回根、目标选择、颜色、取消/确认合并、含子项删除/恢复、登记编辑/取消及重启保存通过，见 [运行记录](p2-bookmark-macos-runtime.md)。
- 退出前恢复按钮启用，重启后禁用，符合原进程内记录规则；验收前三 JSON 已原样恢复，原中文第三页/侧栏组合/导航器恢复，应用留运行。原 Windows、旧验收图及用户 .DS_Store 保留。
- 源码表45文件/33子集适配，235命令清单保留；数量不代表覆盖率。RegisterBookmark 保留打开登记编辑的含义，FocusBookmarkList 仍是禁用占位，不能以聚焦当前树替代书架书签导航。

完整书签导航/搜索/排序/显示模式/属性/链接修复、原地址栏 Popup/标签/树选择器、完整输入/标记/直接页号/历史菜单及真实子书/父书仍待迁入。Windows动态对照、真人设备/NAS、长期内存、完整显示P95和用户新增交互验收独立待验；P2不封板。本节点自动本地提交，无推送或发布。

## P2 第六批：原历史列表导航与管理

- 原 HistoryList 过滤后前后规则与 KeepHistoryOrder/SkipSamePlace 接入，PrevHistory/NextHistory 与两种进程游标独立；日期分组、直接父目录过滤、项目数/搜索框显示与原四开关保存。
- 迁入原多选 Remove/Clear、更多菜单和条目上下文；原默认单击或配置双击、Enter/Delete 与文本作用域隔离。右击已选成员保留批次、空白禁用操作；四样式/无效清理继续禁用占位。
- 当前书历史移除后，防抖/翻页/切书/退出位置保存不重新登记；成功新访问才解除进程内抑制。原 JSON/Page/Props、未支持配置、未知字段、搜索历史及书签独立，失败完整回滚。
- 自动测试 **101通过、0失败、0跳过**：原99项加两项启动快照优先/历史缺失与过期回归；Engine、正式Library、.app及本地ad-hoc签名通过，见 [独立记录](p2-history-validation.json)。
- 用户解锁后完成正式单/双击、Enter、过滤导航、日期/面板开关、缺失来源、多选右击/移除、清空确认/取消及退出文件核验。真机发现恢复入口只传路径而丢失 LastBook 页位置；按原 FirstLoader/BookHub 显式快照优先修复，最终版本空历史启动/清空/真实翻页/退出/重启通过，见 [运行记录](p2-history-macos-runtime.md)。其余交互在修复前构建执行，全量自动回归通过；菜单独立窗口视觉和非空列表空白指针仍待真人验收。
- 每轮临时三 JSON 均独立备份并逐字节/SHA256还原；最终正式版本恢复原中文第三页/组合侧栏/导航器并留运行。自动启动仍属于新访问，进程内删除抑制不作为永久黑名单。
- 旧书签测试的截图路径改为当前阶段标签，11项书签专项复测通过；原第五批图片已原样保留，新图写入p2-history-bookmark-layout.png。
- 源码清单45文件/44子集适配（含FirstLoader/BookHub启动快照出处），235命令完整保留；数量不代表功能覆盖率。旧验收图片、原Windows源码及用户.DS_Store保留；最终Headless重绘另存p2-history-startup-regression-*，自动本地提交，无推送/发布。

完整结构化搜索/搜索历史交互、历史四显示模板/保留策略/无效清理与按书架位置清理待迁；完整书签查询/书架互联、页标记、直接页号文本框、真实子书/父书和完整输入仍待迁。P2不封板；Windows动态对照、真人设备/NAS、长期内存、完整帧P95和用户新增交互验收独立待验。

## P2 第七批：原底部页号输入与滑条设置

- 迁入原SliderTextBox/SliderValueConverter及PageSliderViewModel raw定位关系。一起始数字、小数/科学计数、范围与原WPF舍入保持；Enter继续编辑，普通失焦及Escape均提交，不受静态双页滑块对齐或胶片条联动延迟影响。
- 独立表现控件管理草稿/焦点，来源身份进入既有JumpAsync互斥校验；切书与关闭取消未提交草稿，同书保存/刷新不覆盖文本。页号滚轮按单页立即确认，滑条默认按正文帧，CommandDependent使用原绑定。
- 原SliderConfig显隐、SliderIndexLayout、15–50 DIP厚度、Opacity和滚轮动作接入设置副本/JSON，取消不应用，外观保存不重建正文。未迁/未知字段保留，主题与结构分别调整。
- 真机发现Fluent默认固定轨道留白造成薄滑条滑块裁切，通过官方资源key覆盖修复，不复制Slider模板。增加最小15 DIP视觉边界断言；最终全量 **107通过、0失败、0跳过**，Engine、正式Library、.app及本地ad-hoc严格签名通过，见[独立输出](p2-slider-validation.json)。
- 首次正式构建验证Enter/失焦/Escape、非法/首尾、中文ZIP与双页raw输入、设置取消。最终主题构建复测15 DIP完整滑块、左侧页号/37 DIP/透明度、跟随RTL视觉但页号不反转、None和总开关、无JSON修改的退出重启；版本边界和工具输入探索失败见[运行记录](p2-slider-macos-runtime.md)。不声称最终版全部UI重新执行。
- 临时验收前后三JSON独立备份；验收前备份逐字节/SHA256原子还原，原中文第三页/组合侧栏/导航器恢复并留运行。PNG及CBZ条目字节核对通过；原Windows源码、旧验收图及用户.DS_Store保持。
- 当前清单45文件/48子集适配，不折算覆盖率；235命令完整保留。本批不新增命令，原全局显隐仍禁用；46.3页标记属于全局Playlist/Pagemark.nvpls，后续整体迁入，不另建Book标记体系。

完整播放列表/标记、全局自动隐藏与窗口控制、书签导航查询、真实子书/父书及详细输入仍待迁。P2不封板；Windows动态对照、真人IME/鼠标/触控板、Retina像素1:1/多屏、NAS、长期内存及完整显示P95独立待验。本节点自动本地提交，无推送/发布。

## P2 第八批：原播放列表与全局页标记

- 原PlaylistSource/Item、Playlist集合、PlaylistHub、BookPlaylist/BookPageMarker与命令参数按出处子集迁入，保留全局`.nvpls`、v1/v2/alpha修正、未知字段、短配置路径及别名省略；不新增书籍私有标记、数据库或阅读内核。
- 原当前主图登记、书内前后标记、循环/首尾参数、列表文件循环及过滤/分组后的前后登记接通。单双/分割共用原实体路径，归档内部定位进入唯一加载链，过期列表结果不能提交新书。
- 独立PlaylistView/表现模型启用原组合框、更多菜单、登记、多选移除/逆序恢复、别名、单项重排、自然排序及新建/打开。四模板、列表文件管理、无效清理、OpenAsBook/链接修复保持禁用占位。即时可等待保存、临时文件原子提交及失败原地回滚；提交前外部指纹检测仍无跨进程互斥保证。
- 胶片条/滑条使用原标记显隐字段，标记回报只重绘，不重建正文。真机发现并修复单击Toggle累加、集合刷新丢焦点/Enter被控件消费、前后登记高亮滞后三项问题；对应正式XAML指针/键盘回归通过。
- 最终全量 **121通过、0失败、0跳过**，Engine、正式Library、`.app`及本地ad-hoc严格签名通过，见[独立输出](p2-playlist-validation.json)。新增14项播放列表用例覆盖格式/编辑/落盘、取消/回滚/晚到、原主图/半页目标及输入隔离，未更换输出目录。
- 最终Mac版本复测单击/方向键/Enter、前后登记与书内标记、标记绘制、失效旧书、多选Delete/恢复、文件循环/组合框、新建/登记/上移、退出及所选列表重启，见[运行记录](p2-playlist-macos-runtime.md)。部分弹出菜单视觉及分组/过滤/别名/打开文件的完整真机交互仍待验，不以Headless替代。
- 三JSON逐字节/SHA256原子还原，原播放列表目录/文件状态及六PNG/CBZ哈希保持，临时键位撤回；正式应用留原漫画中文第三页、组合侧栏与导航器。原Windows及旧验收图保持，用户.DS_Store不提交。
- 清单45文件/63子集适配，235命令完整保留；数量不折算覆盖率。自动本地提交，无推送或发布。

完整播放列表文件监视/路径修复/高级模板/PlaylistArchive、全局自动隐藏、书签书架互联与查询、真实子书/父书及详细输入仍待迁入。P2不封板；Windows动态对照、真人设备/Retina/NAS、长期内存与完整显示P95及用户新增交互验收独立待验。

## P2 第九批：原窗口自动隐藏与显示控制

- 原 AutoHide/Window/MenuBar 配置、Panels/Slider 隐藏资格、五区延迟/焦点/弹出/捕获锁和一次显示记忆迁入；原 SidePanelMargin 保留上32/下20 DIP默认余量。Engine 仅保存规则配置，AutoHidePresenter 独立管理 Mac 表现，不新增阅读内核或 WPF 模拟层。
- 菜单/左右栏/滑条改为覆盖区域，胶片条按原滑条宿主联动或独立插槽显示。隐藏/弹出不重建正文；原 ToggleHidePanel/Left/Right 保留自动隐藏含义，原面板选择仍独立，重复 Mac 入口退役。原全屏/取消恢复前状态和 Topmost 接通，精确手势按实际命中排除覆盖栏。
- 兼容旧边缘字段、冲突拼写和早期 Mac 别名，新原字段优先，未知字段保留；设置独立表单支持普通取消和保存。写入失败沿用当前设置应用后持久化语义，运行时撤销仍待后续设置完善，不扩大本批取消验收范围。
- 最终全量 **133通过、0失败、0跳过**，新增12项包含固定时钟、JSON、正式文本/弹出/捕获、显示锁键路由、覆盖尺寸、边角冲突及精确手势隔离。Engine、正式 Library、`.app`及本地 ad-hoc 严格签名通过，见[独立输出](p2-autohide-validation.json)。默认目录串行构建。
- 最终正式 Mac 验证普通隐藏/一次显示、文本焦点保持与移回正文、进入/退出全屏、左边缘展开、设置取消/保存、实际方向键及不改 JSON 的重启恢复，见[运行记录](p2-autohide-macos-runtime.md)。边缘离开、原菜单独立弹出层与真人捕获/多屏坐标仍待验，不能以 Headless 或工具不稳定观察替代。
- 用户随后说明验收期间持续切换其他应用；上述焦点/悬停/捕获观察需在可控制前台时段复验，不能由工具持续返回状态推断未受影响。133项自动回归和文件核验保留独立结论。
- 三 JSON 逐字节/SHA256原子还原，六 PNG/CBZ哈希保持，无播放列表目录新增，临时键位撤回；正式应用留原中文第三页、信息/页面列表组合与右导航器。清单45文件/74子集适配，235命令保留，数量不代表覆盖率；原Windows、旧验收图及用户.DS_Store保持。

完整浮动窗口/FullDesktop、窗口位置/多屏与详细输入、书签书架互联/查询、真实子书/父书、高级播放列表及完整设置失败后取消仍待迁入或完善。P2不封板；Windows动态对照、真人设备/NAS、长期内存及完整显示P95独立待验。本节点自动本地提交，无推送或发布。

## P2 第十批：原书签列表目录导航与排序

- 独立 BookmarkFolderList/表现模型/列表共用原 BookmarkCollection/BookmarkNode，不新增身份、数据库或阅读内核。进入/返回、固定根、当前目录优先同步、注册顺序、名称/路径/类型/随机排序保留原规则。
- 原 Bookmark 排序/数量/树字段读取保存；未知搜索和布局字段保留，时间/大小排序未支持时明确提示并保留原配置。列表/可选树与已有编辑/删除恢复联动；原结构化搜索、模板和书架互联保留占位。
- 后台专项修复直接聚焦及提示换行导致树释放命中变化；覆盖旧树拖动、列表键路由、同级新建、排序保存失败重试和关闭等待。最终全量 **145通过、0失败、0跳过**，本批新增12项回归；Engine、正式 Library、`.app` 构建及本地 ad-hoc 严格签名通过，见[p2-bookmark-navigation-validation.json](p2-bookmark-navigation-validation.json)。默认目录串行构建。
- 已离线审查[书签导航布局](p2-bookmark-navigation-bookmark-navigation-layout.png)，地址与导航始终位于可选树上方；清单45文件/82子集适配不代表功能覆盖率。
- 本批执行后台自动验证和正式构建/本地签名，未启动正式窗口、未修改用户数据；真实鼠标/触控板、弹出菜单与焦点按约定前台时段另验，见[本批记录](p2-bookmark-navigation-runtime.md)。

完整搜索/书架互联、真实子书/父书、详细输入、高级列表/设置与完整树布局仍待迁；Windows 动态对照、真人设备/NAS、长期内存及完整帧/P95独立待验。P2不封板，本地提交、不推送发布。

## P2 第十一批：原书签结构化搜索

- 原NeeLaboratory.IO.Search按固定gitlink迁入Engine，保留原语法、归一、逻辑/正则/比较及Date/Size/Book Profile；单次快照筛选返回唯一书签节点。仍为三个生产项目，未新建阅读内核、数据库或Preview。
- 搜索框、清空、历史选择/删除、递归/增量/显示/保存配置及FocusBookmarkSearchBox接通。日期/大小仅按需经后台来源读取，重复别名去重；注册排序保留原父级局部索引，普通目录/文件ConstOrder均为2。
- 有效语法确认先登记原搜索历史，晚到查询不能重新追加已删除表达式；原三文件事务失败回滚并允许同词重试。节点编辑自动重筛且保留无效草稿，/history只依赖真实书籍访问成员变化；取消/导航/关闭后旧查询不得提交。
- 最终全量 **175通过、0失败、0跳过**，相对前批新增30项（原查询库参数展开13项、书签业务/正式视图17项）。Engine、正式Library、`.app`构建和本地ad-hoc严格签名均通过，五步exit 0，见[独立输出](p2-bookmark-search-validation.json)；应用包含原MIT和第三方许可，产物与源文件逐字节/SHA256一致。
- [静默验收记录](p2-bookmark-search-runtime.md)分别记录后台与前台边界；本批未启动正式应用、未修改用户Application Support数据。已离线审查最终搜索布局，源码清单46文件/88子集适配/15界面出处/26上游文件不代表功能覆盖率。本批截图前缀独立，原Windows、旧图及用户.DS_Store保持。

书架bookmark scheme互联、完整树/模板、来源元数据排序、每目录参数、真实父书/子书和详细输入仍待迁。P2不封板；真机IME/弹出菜单、Windows动态对照、NAS、长期内存及完整P95独立待验。本节点自动本地提交，不推送发布。

## P2 第十二批：原历史列表结构化搜索

- 原BookHistory五属性与HistoryList逐项SearcherFilter迁入，保留书名、访问日期、真实文件大小及书签/历史标志；原直接父目录过滤先执行，无书签递归或路径Contains替代。目录与缺失大小为-1，日期/名称不读取来源。
- 独立HistorySearchViewModel管理Trim/Analyze、500ms增量、Enter/失焦确认、原历史选择/删除、草稿与错误；前后列表导航和界面共用已提交结果。原BookHistorySearchHistory与书签历史共用原集合/三文件事务，失败原地回滚，同词可重试，保存开关保留未迁字段。
- 补充22项用例，最终全量 **197通过、0失败、0跳过**；原历史单/双击、多选/空白右击、书签搜索回归继续通过。修复空确认重刷影响单击，并保留旧盘符/UNC逻辑与Mac合法反斜杠；菜单保存失败回滚、关闭取消/等待及未确认增量关闭通过。
- Engine、正式Library、正式 `.app`和本地ad-hoc严格签名五步exit 0，见[独立输出](p2-history-search-validation.json)。依赖版本及三项目方向不变，源码清单46文件/93子集适配/17界面出处/26上游文件不代表覆盖率。
- 已审查最终[离线搜索布局](p2-history-search-history-search-layout.png)，截图前缀独立；本批未启动正式应用、未注入真实键鼠、未修改用户数据，详见[静默记录](p2-history-search-runtime.md)。原Windows、旧图及用户.DS_Store保持。

四种历史模板/缩略图、可靠无效清理、按书架位置清理与完整联动仍待迁；原文件保留限制由下述第十三批接入。P2尚未封板；Windows动态对照、真实IME/焦点/菜单/触控板、NAS、长期内存及完整显示P95另验。本节点自动本地提交，不推送或发布。

## P2 第十三批：原历史文件保留限制

- 原LimitSize/LimitSpan、数量优先/严格日期TakeWhile及CreateMemento/fromLoad边界迁入；加载不写文件，保存只限制副本，运行历史/Find/导航保持完整。原数量0、默认无限、保序日期与LastBook独立恢复通过。
- 原历史设置页及两张候选表接入，自定义精确值保留；更多菜单跳转同一设置窗口。独立表现草稿不写配置，候选在三文件成功后应用，失败/取消/重试保持原配置和草稿，不重建正文。
- 补齐保存前原应用临时来源排除，启动层传入已有归档临时根；目录边界保护同前缀用户目录，不排除系统临时根。极大期限防日期溢出为明确适配，其余保存/登记/自动清理字段保留待迁。
- 新增16项，最终全量 **213通过、0失败、0跳过**；Engine、正式Library、正式 `.app` 构建0警告/0错误，本地ad-hoc严格签名通过，见[独立输出](p2-history-retention-validation.json)。TRX一致，默认目录串行。
- 已审查最终[设置页离线图](p2-history-retention-history-settings-layout.png)，失败反馈及同草稿重试分别留证；本批未启动正式应用、未注入真实键鼠或访问用户数据，见[静默记录](p2-history-retention-runtime.md)。清单46文件/94子集适配/19界面出处/26上游文件不代表覆盖率，原Windows、旧图与用户.DS_Store保持。

完整历史保存/登记/自动清理、四种模板、书架查询互联/参数、真实父书/子书及详细输入仍待迁。P2尚未封板；真机交互、Windows动态对照、NAS、长期内存及完整显示P95独立待验。本节点自动本地提交，不推送或发布。

## P2 第十四批：原每目录参数与书籍排序

- 原FolderParameter/Memento及FolderConfigCollection参数分支迁入；当前目录排序独立，全局默认保留。原随机种子补值/刷新/返回/重启、nullable默认/祖先递归规则与原普通排序命令接通，RandomBook独立排除正文当前书。
- 保留原特殊拼写Foldres.json、Folders结构、数值枚举和未知根/条目/参数/缩略配置；IsKeepFolderStatus仅影响保存副本。现有事务扩为四JSON，旧双/三文件恢复兼容，失败恢复排序/选择/种子，第四文件准备失败和中断恢复有回归。
- 新增11项，最终全量 **224通过、0失败、0跳过**；Engine、正式Library、正式.app构建及本地ad-hoc严格签名五步exit 0，见[p2-folder-parameters-validation.json](p2-folder-parameters-validation.json)。默认目录串行，源码清单46文件/99子集适配/26上游文件，不折算覆盖率。
- 已离线审查[书架布局](p2-folder-parameters-reading-layout.png)，当前目录排序及正文布局保持。未启动正式应用、未注入真实键鼠、未访问用户数据，见[静默记录](p2-folder-parameters-runtime.md)。旧验收、原Windows及用户.DS_Store保持。

书架bookmark scheme互联、真实父子书、完整输入及列表完善继续迁入；递归参数存在不代表递归内容加载已完成。P2仍在开发，真机/Windows动态对照、NAS、长期内存及完整P95另验。本节点自动本地提交后继续，不推送或发布。

## P2 第十五批：真实书籍页与父子书导航

- 迁入原三页面收集模式、目录递归/空书与shortcut防环、Folder/Archive页框、按需封面和真实父子书；ZIP逻辑目录保留实际条目ID，嵌套压缩明确留P5。
- 递归切换和父书返回按实际条目定位，失败保持旧书；逐页错误不终止双页或其余缩略图。独立卡片绘制、单/双击和主题与Engine规则分开。
- 全量 **241通过、0失败、0跳过**，新增17项；Engine、正式Library、正式.app构建及本地ad-hoc严格签名均通过，见[p2-book-hierarchy-validation.json](p2-book-hierarchy-validation.json)。修复缺失归档页提示回归后全量复测。
- 已离线查看目录卡片/横图归档封面，独立阶段截图未覆盖旧证据；未启动正式应用或修改用户数据，见[静默记录](p2-book-hierarchy-runtime.md)。清单47文件/110子集适配/26上游文件不折算功能覆盖率。

P2仍继续完整输入、阅读控制、书架书签互联与列表完善；Windows/真机交互、NAS、长期内存及完整P95独立待验。本节点自动本地提交后继续，不推送发布。

## P2 第十六批：原输入方案与鼠标组合

- 迁入原A/B/C、默认阅读方向、六对反转许可及共享参数；自定义/解绑优先，原Commands只写差分。五按钮、单双击、组合与四方向轮滚统一解析，菜单保持明确方向语义。
- 正式Avalonia额外按钮变化由PointerMoved接入；组合/右键轮滚消费后抑制普通释放，所有按钮捕获输入，移出与捕获转移取消待确认点击。多格逐步/半格有符号余量/轴与修饰隔离通过。
- 设置候选在导航锁和保存同步屏障后应用；失败原地恢复已知配置、Commands及阅读设置，重试保留草稿与对象引用；输入/外观保存不重建正文。
- 最终全量 **266通过、0失败、0跳过**，新增25项。Engine、正式Library、正式.app及本地ad-hoc严格签名见[p2-mouse-input-validation.json](p2-mouse-input-validation.json)。默认输出串行，独立阶段图已离线查看。
- 本批未启动正式应用、未注入真实键鼠或访问用户数据，见[静默记录](p2-mouse-input-runtime.md)。原Windows、旧验收图及用户.DS_Store保持。

P2继续查看器变换、页尾/锁定/帧方向、书架书签互联、历史与浮动宿主；真机输入、Windows动态对照、NAS、长期内存与完整显示P95独立待验。自动本地提交后继续，不推送或发布。

## P2 第十七批：原查看器变换与参数编辑

- 迁入原变换图/共享数据、缩放/旋转/Stretch/滚动限制参数；统一绘制/包围盒/导航器/卡片逆矩阵。默认翻页重置、每页记忆、锁定与Books继承分别验证，BaseScale沿原Props独立。
- 参数弹窗与父草稿分开，未知字段/$type/共享owner/原大数值保持；设置保存失败恢复运行ValidStretchMode。窗口跟随保留原相对适配比例，None的设备比例只做一次。
- 新增20项，最终全量 **286通过、0失败、0跳过**。Engine、正式Library、正式.app构建与本地ad-hoc严格签名见[p2-view-transform-validation.json](p2-view-transform-validation.json)。串行默认输出；首次裁剪分析错误修复后全量重跑，未跳过正式构建。
- 已离线检查[参数窗口](p2-view-transform-parameter-layout.png)和[旋转翻转卡片](p2-view-transform-transform-layout.png)。本批未启动正式应用或修改用户数据，见[静默记录](p2-view-transform-runtime.md)。原Windows、旧证据和用户.DS_Store保持。

P2继续书籍锁定/页尾/帧方向/Unload、书架书签、历史策略/模板与浮动宿主；依设备验收与完整显示性能独立待验。本增量自动本地提交后继续，不推送发布。

## P2 第十八批：原书籍控制

- 原书籍锁定、五种页尾动作及三种下一书位置策略迁入唯一BookOperation；Unload可靠清除LastBook而保留历史，释放来源后服务继续可重开。
- 弹窗回报核对书籍/代次/位置，重复页尾不重入；原帧方向与JSON设置接通，非默认动画下一增量完善。
- 新增12项，最终全量 **298通过、0失败、0跳过**；Engine、正式Library、正式ARM64.app与本地ad-hoc严格签名均通过，见[p2-book-controls-validation.json](p2-book-controls-validation.json)。两项旧界面断言修正后全量重跑，默认输出串行。
- 已离线查看[页尾对话框](p2-book-controls-page-end-layout.png)，未启动正式应用、未修改用户数据；见[静默记录](p2-book-controls-runtime.md)。原Windows、旧验收与用户.DS_Store保持。

P2继续书架书签互联、历史、列表模板、浮动宿主及输入/动画；真实设备与Windows对照独立待验。本增量自动本地提交后继续，不推送发布。

## P2 第十九批：原书架书签互联

- bookmark scheme、书签树、FocusBookmarkList与独立面板转交接入，共用原节点及Foldres参数，位置/选择独立；来源时间/大小按需读取，别名保持。
- 原StartUp列表快照、两个恢复开关与独立面板同步选项沿JSON保存；参数失败回滚，晚到元数据不取消搜索。首次有效视口修复启动前打开的原点偏移。
- 全量 **307通过、0失败、0跳过**；Engine、正式Library、正式ARM64.app及本地ad-hoc严格签名均通过，见[p2-bookshelf-bookmarks-validation.json](p2-bookshelf-bookmarks-validation.json)。默认输出串行。
- 已离线审查[两个书签列表](p2-bookshelf-bookmarks-bookshelf-bookmarks-layout.png)，未启动正式应用或访问用户数据；见[静默记录](p2-bookshelf-bookmarks-runtime.md)。原Windows、旧证据及用户.DS_Store保持。

P2继续历史、模板、浮动宿主与动画；依设备及Windows动态对照另验。本增量自动本地提交后继续，不推送发布。

## P2 第二十批：原历史登记与可靠清理

- 原主Page阈值、已有书一次切换、删除后同页抑制/真实翻页重启、设置/页尾/退出请求和强制访问日期接入。IsSaveHistory关闭只删除文件，运行历史与LastBook独立。
- 可靠来源检查和全批提交保护权限/超时/取消/离线卷/未映射路径；归档逻辑目录精确检查，晚到旧版本不删除新进度。启动/手动清理及ClearHistoryInPlace接通同一服务。
- 新增13项，最终全量 **320通过、0失败、0跳过**；Engine、正式Library、ARM64.app及本地ad-hoc严格签名均通过，见[p2-history-policy-validation.json](p2-history-policy-validation.json)。默认输出串行。
- 已离线查看[历史设置](p2-history-policy-history-settings-layout.png)，未启动正式应用或修改用户数据，见[静默记录](p2-history-policy-runtime.md)。原Windows、旧证据及用户.DS_Store保持。

P2继续四模板、浮动宿主和动画；Windows/真机/NAS/完整性能分别待验。本增量自动本地提交后继续，不推送发布。

## P2 第二十一批：原列表四模板与可见封面

- 历史、书架、书签接入原Normal/Content/Banner/Thumbnail及共享Profile，独立JSON字段和原条目身份保持。共用唯一PanelListItemView，Thumbnail真实虚拟网格只实现可见邻行，Normal不读取封面。
- 稳定路径封面复用原ArchivePageUtility和唯一BitmapFactory，原指定封面/单图/归档内部定位、版本失效、可见防抖、隐藏/回收/晚到释放及显示旧租约预算分别验证。显示设置保存不登记阅读访问，失败恢复旧模板/配置。
- 新增8项，最终全量 **328通过、0失败、0跳过**；Engine、正式Library、ARM64.app与本地ad-hoc严格签名五步通过，见[p2-list-templates-validation.json](p2-list-templates-validation.json)。默认输出串行，旧菜单占位断言更新后全量重跑。
- 已离线检查Content、Banner和万条目网格，独立前缀不覆盖旧证据；未启动正式应用、未操作用户数据，见[静默记录](p2-list-templates-runtime.md)。原Windows及用户.DS_Store保持。

P2继续浮动宿主、动画/手势反馈与资源性能收尾；Windows动态、真实设备/NAS和完整显示性能分别待验。本增量自动本地提交后继续，不推送发布。

## P2 第二十二批：原侧栏浮动宿主

- 原StandAlone/Open/OpenWindow/OpenDock/Close及Windows.Panels/WindowPlacement迁入。唯一面板内容浮动/停靠/关闭重开/跨窗拖回，位置和选择独立；非模态宿主与对话框区分。
- 主关闭先保存位置、冻结浮窗；失败恢复同一宿主/资源，成功后释放但保留打开集合供重启。原JSON未知数据保持；真实捕获/Retina/多屏独立待验。
- 新增7项，最终全量 **335通过、0失败、0跳过**；Engine、正式Library、ARM64.app和本地ad-hoc严格签名五步通过，见[p2-floating-validation.json](p2-floating-validation.json)。正式XAML生成字段命名冲突修正后完整重跑，默认输出串行。
- 已离线查看导航器浮窗；未启动正式应用或操作用户数据，见[静默记录](p2-floating-runtime.md)。旧Windows、验收图及用户.DS_Store保持。

P2继续动画/手势及资源性能收尾；Windows/真机/NAS与完整显示性能分别待验。本增量自动本地提交后继续，不推送发布。

## P2 第二十三批：原方向鼠标手势

- 原MouseSequence/Builder与U/R/D/L/C、距离/夹角规则、默认12个非空方向手势、配对及Commands.MouseGesture差分迁入。右键释放/C终止、轮滚取消、捕获丢失/切书/Escape取消及原文本箭头提示接通；表现和主题不承担规则。
- 全量 **340通过、0失败、0跳过**，Engine/正式Library/ARM64.app与本地严格签名五步通过，见[p2-direction-gestures-validation.json](p2-direction-gestures-validation.json)。离线检查设置，距离控件裁切修正后完整重跑；未启动应用/操作用户数据。
- 本地提交7c28aa5d7；真机鼠标/触控板/焦点捕获及Windows动态待验。

## P2 第二十四批：原分页/滚动动画

- 原Scroll/Fade方向/时长与静态1间距、滚动EaseOut、连续轮滚线性、Hover开关/敏感度及原优先规则迁入。逻辑目标保持唯一变换图，表现插值独立；一个退出帧复用租约、不复制像素，仍计预算，晚到/关闭清理。
- 全量 **351通过、0失败、0跳过**，五步通过，见[p2-animation-validation.json](p2-animation-validation.json)。离线检查Scroll/Fade；未启动正式应用/操作用户数据。
- 本地提交c3b94ac11；全景/幻灯片专用策略留后续，真实屏幕/Windows动态独立待验。

## P2 第二十五批：资源优化与开发收尾

- 预算内免排序与同图片/规格/版本复用显示缓冲，保持唯一工厂及租约预算。原RemoveUnlinkedHistory接通既有可靠清理，正反TogglePageMode及共享IsLoop默认/端点规则补齐；53条陈旧命令阶段更新，运行导出逐字段一致。
- 固定4K JPEG目录/ZIP软件完整帧、热租约分配、30轮切书短期资源测量保存前后原始输出；同帧30次刷新显示缓冲新建30→0，无重复解码；1024条目/1000热请求分配约288.8MB→1.34MB。测试缓存未超8MiB，工厂关闭归零。方法/限制见[p2-resources.md](../docs/p2-resources.md)，不能外推真机/长期native。
- 最终全量 **355通过、0失败、0跳过**，Engine/正式Library/ARM64.app构建及本地ad-hoc严格签名全部通过，见[p2-completion-validation.json](p2-completion-validation.json)。默认bin/obj串行，不启动/激活应用或操作用户数据。主窗口/输入设置/浮窗/样本离线检查完成。
- **P2开发范围完成，整体验收未封板。** 原235命令保留、138入口接入/97占位；P3/P4/P5及高级兼容项仍按明确清单推进。静默证据、真机/Windows/性能边界见[p2-completion-runtime.md](p2-completion-runtime.md)。本增量自动本地提交，不推送、公证或发布。

## P2 设备验收节点：Windows 动态参考

- 用户已连接 RDP。使用独立 EXE 副本及仅子进程生效的临时 Profile，采集目录/CBZ、单双页/方向/宽图/分割/首末单、跨栏水平/垂直组合、比例调整、成员浮动/停靠、右侧自动隐藏及明确打开目录后的恢复。测试副本正常关闭，原进程保留；见[参考记录](p2-windows-reference.md)。
- 实际参考包为 `ddc857b511e9f04cbc356bdabb1f748963e4f9e1-dirty`，版本/EXE SHA256已记录，不能宣称匹配固定 `c5c398d89`。两端 ZIP及24张PNG哈希核对；[34张截图清单](p2-windows-evidence.json)只提交文件名/哈希/实际格式，原图留本机，排除含私人缩略图的采集。
- Windows 仅保存条目名，重开003回到右开首半页；当前 Mac 的 SaveData 额外读写 MacPagePart。静态差异已复核，Mac同夹具复演待执行，本节点不修改产品规则、不列为一致性通过。
- CUA末段连续超时，显式重置并定向绑定后AX/截图恢复；客户端扫描码恢复动作已发送，但当前模式未能观察确认。不能将工具失联等同于RDP断开或0x104复发。此Windows节点未启动Mac正式应用、未替换Application Support；真实触控板/Retina/长期浏览待验，多屏缺环境。
- 本节点仅文档/证据清单变更，产品构建/测试不重复；验证JSON、哈希、文档链接及diff，自动本地提交，不推送发布。P2整体验收仍未封板。

## P2设备验收节点：Mac 同夹具复演与修复

- 正式Mac隔离Profile复演目录/CBZ单双页、步进、方向、宽图/分割、首末单页及切书/重启。数字菜单提示误解析和额外MacPagePart持久化两差异修复后真机确认；同书启动快照未知JSON/Props保留与反向跨书末半页另有自动回归。
- 跨栏垂直/水平组合、成员拆组、整组移动、比例及组合状态重启真机通过。Windows对应样本不全的部分不计跨端通过；CUA弹出层不能稳定观测，菜单/浮动/自动隐藏本轮Mac动态仍待验。
- 最终375/375、0失败/跳过；Engine/正式Library/ARM64.app构建和本地ad-hoc严格签名全部通过，见[p2-device-input-validation.json](p2-device-input-validation.json)。真机用372测试构建执行；审查后追加JSON未知字段保留仅由最终自动验证覆盖，不冒充再次真机运行。
- 原四JSON文件已还原，路径/大小/SHA256与快照及备份一致。36张Mac和34张允许留证的Windows截图归档本机持久目录，Git只纳清单及记录，原私人截图排除；重复Headless图和指标留本机，旧阶段证据不变，见[运行记录](p2-device-input-runtime.md)及[清单](p2-macos-device-evidence.json)。
- Retina JPEG尺寸预检不证明无损设备像素1:1；真实触控板/长期浏览未执行，多屏无环境，Windowsdirty包未证明固定源码一致。P2整体验收仍未封板，本节点自动本地提交，不推送/公证/发布。

## P2设备验收节点：资源观测与排序修复

- 正式诊断构建完成30分钟间歇式真实4K目录/CBZ浏览：356样本、88完整循环、968次核对导航，完整循环累计约546秒，不称连续30分钟翻页。缓存主图512 MiB和缩略64 MiB独立预算已回收，句柄233–234，窗口关闭同工厂像素/显示/租约/pending归零；RSS末段仍增长，自然GC补采回落，不标长期稳定性通过。
- Retina实际RenderScaling=2、800×600源图对应400×300 DIP的100%比例子集通过；截图为JPEG，无损设备像素采样待验。资源/Retina使用375项诊断构建，最终376项构建的真机升降序和隐藏/一次显示命令子集另行留证。
- 修复原Book.Pages原地排序未更新实际ListBox：原Page身份保持，排序版本驱动表现数组，普通导航不复制全书，getter及时读取已提交来源。第一次完整回归1项失败修复后，最终376/376、0失败/跳过，Engine/正式Library/ARM64.app/本地严格ad-hoc签名通过；见[p2-device-resources-validation.json](p2-device-resources-validation.json)。
- 默认诊断关闭，显式NEEVIEW_DIAGNOSTICS=1才写有限数值日志，复用同一产品窗口/缓存。两测试进程正常退出，Mac原Profile完整路径/类型/大小/哈希与快照和备份一致；Windows测试副本关闭、原实例保留。原始证据本机归档，Git只提交新清单/记录，用户.DS_Store不提交。
- 触控板：用户要求跳过，未验；多屏：无环境。hover/全焦点/弹出层、Mac浮动及完整历史/书签动态等已有限制继续保留。详见[运行记录](p2-device-resources-runtime.md)与[证据清单](p2-device-resources-evidence.json)。P2整体验收未封板，自动本地提交，不推送、公证或发布。

## P3 第二批：普通目录树与逐页缩略

- 沿原FolderTree父子/延迟/自然排序/确认及祖先同步子集适配，普通树与书架/正文选择独立；Top/Left分隔及显隐/同步保存原Bookshelf JSON，隐藏或关闭取消请求。
- PageList四模板直接传递当前书原Page，唯一BitmapFactory及64 MiB缩略预算；方向键只选择、按下/Enter确认、释放焦点，万页可见控件/离屏租约有界。
- 用户指定目录取三个子目录218页、九位置只读Headless实图采样，含实际绘制像素存在检查；原图留本机，关闭工厂归零。最终构建/自动回归/严格签名计数见[p3-navigation-validation.json](p3-navigation-validation.json)，细节见[运行记录](p3-navigation-runtime.md)。
- 最终403/403通过、0失败/跳过，Engine/正式Library/ARM64.app与严格本地ad-hoc签名五步全部通过；新增17项，原235命令保留，140个入口/95个占位。
- 本批未启动前台应用；真实Mac交互/用户验收与Windows本批动态对照待集中执行。完整树/页面组搜索、渐进索引、原帧级全景和大量图片性能仍待迁，P3未封板；自动本地提交，不推送发布。

## P3 第三批：后台检查点布局及万项索引测量

- 原阅读/来源/排序/JSON链路保持；BrowseLayout使用256项不可变检查点，补尺寸共享未变前缀，最短列精确收敛后共享尾部，最坏仍重算全部尾部。
- 几何计算及原元数据/Page创建循环后台执行，单槽有界；UI按书籍/顺序/计算代次提交并恢复最新Page/页内锚点。首次布局尚未发布的导航请求保留，关闭后晚到计算不再发布。
- 最终413/413通过，0失败/跳过，正式Library/ARM64.app与严格本地签名通过；同一三个子目录218页瀑布12位置/缩略9位置和关闭归零通过，见[p3-performance-runtime.md](p3-performance-runtime.md)。
- 万项真实本地空文件测枚举/创建/原排序；20次纯几何更新保留前后测量。完整检查点增加元数据开销，靠后补尺寸减少重复计算/分配，不宣称屏幕60Hz、首图P95或任意场景倍数提升。
- 渐进可见目录索引和原帧级全景仍未完成；本批无前台交互，Mac/Windows/Retina/长期内存及屏幕性能仍分别待验。P3未封板，自动本地提交，不推送发布。

## P3 第四批：渐进普通目录索引

非递归普通目录首批可读、已知图片先产出、128项有界批次；原Page身份/排序/Part及末端保护，失败保留已提交页。完整418项回归、Library/正式ARM64.app/严格ad-hoc签名通过，见[p3-index-validation.json](p3-index-validation.json)与[契约](../docs/p3-index.md)。递归展平/归档仍完整索引，设备/Windows验收独立；本轮继续原全景与导航。
