# NeeView Mac 源码迁移架构

P5第三十三批：原Loupe十二字段、五命令、固定开启基点/设备舍入与局部相对捕获进入唯一查看器；普通变换、JSON和窗口布局保持。见[放大镜契约](p5-loupe.md)。

P5第三十二批：六类空间效果与原WPF Blur接入整阅读视口合成，十四类原效果均有实际后端；页框/背景/网格作用域、CPU/GPU Auto采样和原生像素租约保持。缩放滤镜及真正Loupe继续迁移，见[空间效果契约](p5-spatial-effects.md)。

P5第三十一批：Bloom、Monochrome和ColorTone按固定原后端指令/常量关系进入现有Skia颜色链；参数、层序、显示租约和View导出保持，七类空间/模糊效果继续待迁，见[颜色效果契约](p5-color-effects.md)。

P5第三十批：原主菜单帮助、搜索模板/八张动态表/CSS和两个命令接通；Engine只生成文档并管理独占临时文件，Mac只转交唯一平台打开和关闭。未迁能力仍标明，见[帮助契约](p5-help-manuals.md)。

P5第二十九批：原四类书架排序资格和四个路径/登记顺序命令接通；普通搜索路径排序、原书签注册索引、唯一Foldres.json与失败回滚保持。表现层消费同一资格表，切换来源立即刷新菜单，见[书架排序契约](p5-book-order.md)。

P5第二十八批：原效果参数/层/缓存/六分支预设、自定义尺寸/裁剪/DPI及整个页框网格接入唯一JSON和查看器；四类自有HLSL转现有Skia。草稿隔离、逆层顺序、显式失败、延迟/离屏租约与导出共享保持。十类WPF/第三方效果、resize及其余P5能力继续迁移，见[效果契约](p5-image-effects.md)。

P5 第十九批：唯一正式 ARM64 Release 开发包、实际依赖/固定原文许可、完整 Mach-O 签名及 ZIP 重定位接入；失败保留旧成品。Developer ID、公证和干净安装继续待验，见[分发契约](p5-distribution.md)。

P5 第十八批：原幻灯周期/输入重置/首周期EOS等待、页尾覆盖、自动滚动和启动选项进入唯一BookOperation与JSON；顶部原4 DIP计时条和设置草稿独立。关闭失败恢复播放及滚动；调度适配与剩余媒体边界见[幻灯契约](p5-slideshow.md)。


P5 第十七批：GIF/WebP与官方ImageIO APNG完整合成帧、原播放状态/三命令/底部媒体条和JSON设置接入唯一阅读工厂；取消首帧不缓存成功、原生来源/像素/显示统一计费。界面结构和草稿独立，详见[动图契约](p5-animated-images.md)。

P5 第十六批：ZIP AES/PKWARE、RAR4/5 与 7z 固实加密接入原 ArchiveKey 打开链。来源私有口令、真实抽取验证、逻辑路径缓存、取消及失败父链释放保持；加密 ZIP 只读，普通 ZIP 删除不变。见[压缩密码契约](p5-compressed-password.md)。


P5 第十五批：原ArchiveKey/进程AES缓存及纯输入弹窗接入唯一打开链；根/明确嵌套PDF通过系统后端解锁，后台无交互、失败父链释放、切书/关闭取消，见[密码契约](p5-pdf-password.md)。压缩密码仍待迁。

P5 第十三批：原PDF归档/页目录/三种尺寸及原JSON配置接入唯一阅读链；CoreGraphics/PDFKit官方绑定替换PDFium。正文直接输出像素，提取才惰性PNG，请求级流初始化/读/关闭串行。PDF密码交互由P5第十五批接入，压缩密码仍待迁，扩展名配置已接入，见[PDF契约](p5-pdf.md)。

P5 第十二批接入原嵌套归档 Source/Parent、条目入口和三收集模式。后端随机临时代理分离逻辑/物理路径，完整父链与借用父源分别释放；所有活动代理共享2GiB预算、最多16层。历史存在检测和封面沿同一工厂，嵌套包保持只读，前端/JSON不增加状态。见[嵌套契约](p5-nested.md)。

P5 第十一批接入原设置结构化搜索。Engine复用原Searcher和SettingItemRecord文本关系；Mac从现有九页表单建立索引，全部235命令及参数文案沿同一草稿。结果直接编辑原控件，导航/清空/关闭归还原父级与DataContext；窗口历史不写JSON。未迁设置页面仍按清单推进，见[搜索契约](p5-settings-search.md)。

P5 第十批补齐原 OpenCustomThemeFolder：Engine 在已有单槽中准备目录，仅首次创建目录时按原字节生成Sample；表现模型转交当前目录草稿，系统打开沿同一IPlatformService。已有目录不补样例、不覆盖材料，失败/取消不提交配置，关闭取消未完成打开。见[目录动作契约](p5-theme-folder.md)。

P5 第九批迁入原 FontsConfig 与 FontParameters 字号公式。AppKit 只提供消息/菜单字体度量，Mac FontPresenter 独立发布原字体资源；字体表单保存成功后才应用，失败沿原配置分支回滚。常规/菜单/树/面板分别引用角色，阅读及唯一JSON链保持；ClearType保留禁用，原38非零字号门槛保持。见[字体契约](p5-fonts.md)。

P5 第八批接入原 ThemeConfig/ThemeSource/ThemeColor/ThemeProfile 与预设 JSON、BasedOn、失败回退和实际颜色应用。Engine 只输出颜色值，Mac 的 ThemePresenter 管应用资源/系统色值，主题表单独立；保存成功后才应用，启动/导入重建沿唯一链路。原窗口区域和书籍/图片工厂保持。见[主题契约](p5-theme.md)。


P5 第七批将原三个一级附属目录接入预览、独立选择和唯一Profile提交/备份/恢复，列表复用现有格式与Hub；Mac管理目录落点在确认中说明。主题/脚本只保留原材料，不装载或执行。动态恢复清单受限，旧五文件备份兼容。契约见[附属文件导入](p5-profile-assets.md)。

P5 第六批接入原 Alpha.5 的旧单效果→首层/参数缓存/默认预设纯数据升级，早期Mac保留标记可继续迁移，现代层/未来类型不被旧标记覆盖。预览/应用/差分保存仍走唯一JSON链，原材料归档；实际效果执行及编辑尚未接入。契约见[效果兼容](p5-effect-compatibility.md)。

P5 第五批接入原 DiffJsonConverter 的已迁配置/命令差分写出，保留四模板及九数字实例默认、共享 owner、未知字段和完整关闭布局。旧滚动 setter 与自动隐藏读取别名先转换再退役，原五文件事务与运行 JSON 不变。契约见[差分保存](p5-difference-settings.md)；旧效果数据由第六批更新；执行仍待迁，附属材料由第七批接入。

P5 第四批接入原独立目录4065/4209升级、旧History字典的单独转换、快速访问内嵌版本校验与缩略目标映射；按最终选项配置归一排序。旧滚动差分setter退役到兼容材料，未知字段保持。范围与生命周期见[目录兼容契约](p5-folder-compatibility.md)；当时完整原差分写出及旧效果待迁入；差分的当前范围由第五批契约更新。

P5 第三批已接入原旧设置版本规则、真实参数包装和 V0/V1/V2 侧栏布局，预览与事务应用共用升级候选。旧字体尺寸与旧效果有明确未迁入边界，见[兼容契约](p5-legacy-compatibility.md)。Engine 继续复用唯一 SaveData 五文件事务，MacApp 关闭旧阅读上下文后经唯一装配路径重建配置引用；视图只回传确认请求，没有第二状态体系。完整浮窗菜单/停靠和屏幕 P95 按用户要求[跳过](../acceptance/p34-skipped-validation.md)，AX/NAS 未解决项保留，不宣称 P3/P4 整体验收通过。P5整体见[清单](p5-completion-checklist.md)。

P0/P1 已建立工程骨架、原窗口区域和目录/图片/ZIP 阅读链路。P2 开发范围已收尾：RAR/7z、历史/书签及结构化搜索、胶片条/导航器、原分页/变换/页尾规则、常用书架/父子书导航、播放列表/页标记、键鼠/方向手势、动画、侧栏拖拽组合/自动隐藏/浮动及资源优化均进入同一产品链路。P3 开发范围也已完成：连续/瀑布、后台检查点布局、普通目录渐进索引、原帧全景、普通目录树/QuickAccess及监视、页面目录/名称和页面/书架搜索进入同一链路。完整235条命令保留；P5第二批为168个执行入口接入、67个继续占位（原ImportBackup接通），数量不代表功能覆盖率。详见[P2清单](p2-completion-checklist.md)与[P3清单](p3-completion-checklist.md)。真实设备、Windows动态及长期原生内存未完成项分别记录，不把开发完成标记为整体验收封板。Mac 独立维护；原 Windows 工程是固定行为参考，不参与 Mac 构建。

## 基线与技术栈

共同基线 `686a43362dc4b3c9f2ea014240dbba2d0e9fbcaa`；分类分支 `801eab4842b9dbfc18eae7c96006f64eb7b80c30`、`84449934c86a2e7faba9a7c7a7d4ff9dfbea2229` 的真实合并为 `c5c398d89`。两条历史保留在 `integration/neeview-baseline`，本轮在 `feature/macos-port` 实施。Windows 构建未执行；已从用户实际安装的 dirty 包采集部分[动态参考](../acceptance/p2-windows-reference.md)，未证明该包与固定基线一致，已完成目录/CBZ 阅读和部分侧栏同夹具复演；差异修复、通过项与限制见[Mac 设备记录](../acceptance/p2-device-input-runtime.md)。

C#/.NET 10、Avalonia 12.1.3、CommunityToolkit.Mvvm 8.4.2、Magick.NET Q8 14.17.2、SharpCompress 0.50.3。状态采用原 JSON，不增加 SQLite、Rust 或收费框架。NuGet 版本集中锁定，源码继续遵循仓库 MIT 许可。macOS API版本固定27.0，对应本机workload 27.0.10722，使正式Exe和Library编译检查使用同一NuGet锁图；这不改变最低macOS15要求。

## 三个生产项目

```mermaid
flowchart TB
  Mac["NeeView.MacOS\n唯一启动装配、Avalonia 视图、表现、主题、输入"] --> Engine["NeeView.Engine\n原位置、阅读规则、命令、配置、状态"]
  Mac --> Backends["NeeView.Backends\n图像、归档、AppKit 系统/输入实现"]
  Backends --> Engine
```

Engine 使用 `net10.0`，只引用原 MVVM 辅助库；不引用 WPF、Avalonia、AppKit 或具体图片/归档库。Backends、MacOS 使用 `net10.0-macos`，目标 macOS 15+、Apple Silicon。Mac 的视图和表现模型只调用 Engine 类型与契约，具体实现只在 `MacApp` 装配。

独立 solution 保留在 `portable/NeeView.CrossPlatform.slnx`。旧 Core/Application/Desktop/Persistence 等重写项目、SQLite 身份体系和 Preview Host 已退役，历史成果留在 Git 和旧验收记录中。测试项目直接编译正式视图与后端源码进行 Headless 验证，没有第二个产品入口。

## 原源码与适配的区别

[源码迁移清单](source-migration.json) 记录原文件、基线 SHA256、目标和改造。位置/范围、设置按字段恢复、自然排序、页框生成等算法直接迁入。`PageFrameFactory` 保留原判断顺序，几何计算只替换实际 WPF 值类型及旋转变换。

Book/Page/Archive/BookOperation 是按阶段迁入的原关系子集适配，尚未完整迁入高级媒体、metadata/rating、脚本和高级控制；原页面/普通书架搜索已由P3接入。不能把“存在同名类型”当作整组功能已经迁完。新增替换点只有来源、像素、系统交互；不建立 WPF 模拟层、事件总线或插件框架。

原 `BookSourceFactory.ValidatePageSortMode` 对普通书籍排除播放列表注册顺序，P1 保留其回退到文件名排序的规则。自然比较器的 Win32 字符比较改为 .NET CurrentCulture；数值、全半角、日文归一逻辑保留，语言排序细节仍待 Windows 样本对照。

## 打开与显示

路径 → BookOperation → Archive/ArchiveEntry → 原设置 Mix/BookPageSort → 尺寸探测 → 原 PageFrameFactory → ReaderView 当前帧需求 → BitmapFactory → 后端解码 → 像素租约 → Avalonia Bitmap/绘制。胶片条及导航器共用同一 BitmapFactory，按可见窗口申请缩略规格。胶片条/滑条共用 PageSelector，临时选择不改变正文；200ms防抖和可见序列去重，点击/Enter或滑条释放才确认。

图片定位到所在目录中的条目。普通非递归目录支持128项有界渐进批次、已知图片先产出，原Page身份、排序/Part及末端保护保持；递归展平和ZIP/RAR/7z继续完整索引。窗口级 BookOperation 使用互斥保护导航、设置与提交；打开按代次裁决，失败保留旧书。切书先保存旧状态，替换成功后释放旧来源。分割位置使用原 PagePosition.Part，不另定义身份或锚点体系。

## P3 来源集合与导航

BookPageCollection.SourcePages是完整未过滤的原Page集合，Pages是经原Searcher和BookPageSort处理的当前正文。搜索/排序不创建新Page，正文Index重编号、EntryIndex保持；渐进批次追加SourcePages后重新筛选。空结果保留原CurrentPage/LastBook条目但无正文页框，清空恢复来源页。PageOrderVersion驱动正文表现数组，SourceVersion只在来源变化时驱动目录树；Smart名称及目录代表页由全源计算。

BookshelfFolderList管理普通递归搜索与单个活动目录根监视，普通FolderTree管理最多32个展开节点一级监视。QuickAccess树与书架共享同一JSON节点，拖放只登记引用或重排，不移动真实文件。NavigationSearchViewModel独立管理输入、确认和关闭；四类原搜索历史共用总保存开关和五JSON事务，失败原地回滚。相关页面布局、主题和表单不承担匹配、枚举或保存算法；见[p3-page-search.md](p3-page-search.md)。

## 资源与取消

来源属于 Book，流属于请求；解码像素由 BitmapFactory 缓存，显示 Bitmap 与租约由 ReaderView 所有。显示 Bitmap 先释放，再归还像素租约。视图按 revision 拒绝晚到结果；取消等待不取消其他消费者共享的解码；没有消费者时取消排队需求，原生晚到结果只清理。

主图像素和实际显示缓冲计入 512 MiB 目标预算；缩略图像素和显示缓冲另计独立 64 MiB 预算，合计不是512 MiB。等待者和显示租约保护资源；无引用资源按 LRU 回收，预算内不排序完整缓存。ReaderView 对同一图片/解码规格/来源版本复用显示缓冲；目录/归档封面和新来源仍走原请求。动画最多保留一个退出帧，复用租约、不复制像素，仍计预算。预算不等于进程 RSS 上限，临时/native 工作单独限额。解码并发 2，背景槽 1，待处理需求上限512；缩略图共用原工厂和解码槽。短期固定 JPEG/目录/ZIP 软件完整帧测量见[p2-resources.md](p2-resources.md)，不外推真机帧率或长期 native 稳定性。

正式窗口的只读 RuntimeDiagnostics 仅在 `NEEVIEW_DIAGNOSTICS=1` 时启用，记录同一 ReaderView 的实际 RenderScaling/绘制几何和同一 BitmapFactory 的锁内资源计数；不新增宿主、读取入口或内容身份。默认不执行日志I/O；启用时每5秒及显示完成写有限JSONL，路径不入日志，5 MiB/文件、最多4份，窗口关闭停计时、退订并释放。30分钟间歇式真实浏览已观察预算回收、稳定句柄及关闭后工厂归零；RSS末段仍增长，长期稳定性待复测，见[设备资源记录](../acceptance/p2-device-resources-runtime.md)。自然GC回落不能替代稳定性验收。

文件系统后台槽 2，队列和执行等待各 15 秒超时；不能中断的系统调用仍占槽到真正结束。ZIP/RAR/7z 解压在后台持有来源互斥。非固实大条目用 DeleteOnClose 随机临时文件；固实及 7z 使用独立顺序读取实例和每来源 2 GiB LRU 磁盘缓存，关闭清理。7z 索引顺序不同于 Reader 顺序，按名称及同名序号定位。尚无跨来源总预算和崩溃遗留缓存回收；重复同名 7z 的物理顺序映射尚待专门夹具。目录和压缩包不长期持有所有图片流。

## 状态与退出

`UserSetting.json`、`History.json`、`Bookmark.json`、`Foldres.json`、`QuicAccess.json` 与原全局 `.nvpls` 是对应模块的权威数据，沿用 Path/Page/Props、差分键位和原设置枚举。未迁移配置及未知 Props 保留。Mac 用户目录为 `~/Library/Application Support/NeeView.Mac`；不修改 Windows Profile 或旧 NeeView.Portable 数据。

保存先准备五个临时文件，再保留副本和小型提交标记，原子替换各文件；失败恢复旧完整文件和历史内存状态，中断在下次启动恢复，兼容旧双/三/四文件标记。书签编辑原地回滚节点，保留选择及重试引用。阅读防抖一秒，切书和退出立即保存。关闭入口共享可等待任务，保存失败保持书籍/查看器并允许重试。非文件系统激活重建窗口时恢复最后书籍；明确打开文件优先于旧状态。该链路已在正式Mac应用中验证，见[运行记录](../acceptance/p1-macos-runtime.md)。

原 Props 无法无歧义编码 IsWide=false，Mac 增加 `MacIsSupportedWidePage` 补值；原 Props 解析算法保持。2026-10-04 同夹具复演确认早期 `MacPagePart` 导致半页恢复与 Windows/原源码不同，已收回该扩展：Find/GetLastBook 直接返回原 BookMemento，SaveAsync 不接受半页参数；普通切书/启动按原条目名恢复到阅读方向首半页，当前阅读和反向页尾仍保留原 Part 算法。旧字段不读取，更新当前记录时移除，其他未知字段保持。同书 LastBookV2 的未知嵌套字段及 Props 继续保存，不跨书传递。P2 首批接入原 BookmarkNode 字段，第五批接入原集合算法与登记编辑；完整旧版本迁移、路径映射与 .nvzip 导入在 P5，当前不能宣称任意旧 Profile 可直接使用。

## 界面与迁移目标

原 MainWindow/SidePanelFrame 的区域关系是布局基准，原 Colors/IconGeometries 是资源基准。顶部菜单/地址、左右图标栏/面板、中央查看器、底部滑条/状态和胶片条插槽已转换。九个原面板完整登记；未迁移命令保留禁用菜单，分类面板已由P4第一批接入，效果等面板保留阶段说明。用户已认可总体布局；原 LayoutPanel 关系下的跨栏重排、分割组合、成员拆组、比例/选择恢复、拖动自动隐藏锁定及单面板浮动/停靠/关闭/重开已接入。Engine 只保存布局数据，SidePanelPresenter 负责 Avalonia 控件、浮窗及拖放预览，主题可独立更改。旧 V0/V1 布局导入、高级窗口/输入细节及 Windows 动态对照仍待迁移/验证。

原 Book.Pages 仍原地排序并保持 Page 身份；排序提交递增 PageOrderVersion，表现模型按书籍引用/顺序版本发布新列表数组，使Avalonia收到排序变化。普通翻页不复制全书，getter及时读取已提交书籍，不把表现数组变成第二业务集合。名称升降序、主图/页号与选中项的真机对照见[设备资源记录](../acceptance/p2-device-resources-runtime.md)。触控板本轮按用户要求跳过，未验；多屏无环境。

滚动翻页从原 PageFrameBox/NScroll/ScrollResult 迁入五种模式、分段、终端吸附和换行停顿，普通滚轮命令到边界后才进入原帧导航。精确滚动仍走表现平移，由原 DragArea.SnapView 约束；书籍阅读方向与帧移动方向分别保留。完整分页滚动参数、鼠标组合和方向手势已接入；原帧全景 PagesAsOne/NScroll已由P3迁入，同一查看器另支持Mac连续/瀑布展示。详见 [P2 第二批契约](p2-docking-scroll.md)、[参数与变换](p2-view-transform.md)、[方向手势](p2-direction-gestures.md)。

第三批契约见 [页选择与导航历史](p2-selection-navigation.md)。原100项环形历史按页面条目和书籍打开顺序分别保存于进程内；重放成功后提交游标，跨书保留访问排序。JSON仍是唯一持久化权威。

第四批契约见 [普通书架与文件夹页导航](p2-bookshelf-navigation.md)。BookshelfFolderList 独立维护浏览位置和选择，后台枚举目录/归档元数据；重排及翻页不重扫。普通前后书采用原分组排序，成功打开后提交选择；文件夹页只按当前书页面目录组跳页。全局默认排序及分组沿用原 JSON；每目录参数、持久随机种子、真实 Folder 页及父书定位分别由第十四/十五批接入。P3已接入普通目录树展开时枚举、Mac/挂载卷根、QuickAccess引用拖放、系统图标和有界监视，以及页面可见缩略和普通非递归渐进索引；真实文件操作在P4。

第五批契约见 [书签集合操作](p2-bookmark-operations.md)。BookmarkCollection只操作原JSON节点；SaveData串行保存与失败原地回滚，Mac视图独立管理对话框/选择/拖动。登记命令保留打开编辑界面的含义，Mac异步编辑窗替换原Popup宿主；书签目录/搜索和书架联动由后续批次接入，高级树排布/修复等继续占位。

第六批契约见 [历史列表导航与管理](p2-history-list.md)。沿用原 KeepHistoryOrder/SkipSamePlace 和过滤后前后语义，当前记录移除后同进程位置保存不重新登记；启动/重开按原 FirstLoader 显式传入完整 LastBook 快照，不依赖历史记录存在，成功恢复仍开始新访问。History 四开关保存到原 JSON，原搜索语法、四模板和可靠无效清理均已接入。各批正式运行和静默回归分别留证。

第七批契约见 [底部页号与滑条设置](p2-slider-input.md)。独立 SliderTextBox 表现控件保留一起始转换、Enter/失焦及 Escape 提交，原始索引进入唯一 BookOperation.JumpAsync，不走双页滑块对齐；来源身份在原互斥中再次核对。滑条显示、SliderIndexLayout、厚度、透明度及滚轮写回原 JSON，纯外观保存不重建正文。主题资源修复15 DIP薄滑条裁切。原46.3页标记属于全局播放列表/Pagemark.nvpls，后续随原链路迁入，不新增每本书标记体系；当时完整自动隐藏/全局显隐为禁用占位，现由第九批接通。107项测试、正式构建与本地签名及真机重启/数据还原分别留证。

[前端边界](frontend-boundaries.md)、[行为对照](behavior-baseline.md)、[完整命令表](command-migration.md)、[布局表](layout-migration.md)、[模块设计](modules/M01.md) 和 [阶段证据](../acceptance/stages.md) 是后续开发契约。P2/P3开发完成与整体验收分开；P4分类和基础文件操作开发范围已接入，集中设备验收待项独立记录；P5兼容/高级内容/发布仍是后续目标，未继承旧重写方案的“通过”。

优化只按测量热点独立修改并回归。代码删除必须说明 Windows 专属、不可达、重复或被替换的原因。构建串行、使用默认输出；不得通过 Preview 或改输出目录绕过 Xcode。本机Xcode27.0已满足构建要求；开发Host明确使用ad-hoc签名和JIT权限，最终.app在默认输出目录，RID子目录的.app只是SDK中间产物。构建及本地签名校验写入p1-validation.json；真机运行单独留证。编译、自动测试、运行、Windows 对照、用户验收、提交和发布分别报告。

第八批契约见 [原播放列表与全局页标记](p2-playlist.md)。PlaylistHub沿原Default首项、真实文件自然顺序和选择关系，保留v1/v2、未知字段及别名省略规则；Mac编辑采用即时可等待的原子保存和原地回滚。BookPlaylist/BookPageMarker映射当前全局列表，书内标记和过滤/分组后的跨书列表导航独立，归档逻辑目标共用唯一加载链。主图片按原SelectedRange索引升序确定，未确认的PageSelector不改变登记对象。PlaylistView与表现模型独立，标记回报只更新绘制/菜单；未迁模板/文件管理/修复保持占位；PlaylistArchive来源由P4第七批接入。提交前指纹检查不提供跨进程互斥保证，完整监视后续迁入。最终121项测试、正式构建/本地签名及真机导航、编辑、列表重启和数据还原分别留证；源码迁移清单持续随阶段更新，数量不代表覆盖率，P2整体验收未封板。

第九批契约见 [原窗口自动隐藏与显示控制](p2-autohide.md)。AutoHide/Window/MenuBar及原Panels/Slider字段沿用原JSON；早期Mac别名只读取，保存收归原字段。AutoHidePresenter独立管理五区的延迟、真实焦点/弹出层/捕获和一次显示锁，40ms背景计时不扫描页面。自动隐藏区域覆盖正文，弹出/收起不改变视口；原滑条与胶片条宿主联动及侧栏内容边角余量保留。已迁窗口显示命令接入，原生精确手势按实际控件命中排除覆盖层。常用输入与侧栏浮动/位置保存后续已接入；FullDesktop、主视图浮动及高级窗口细节保留占位，多屏/真实焦点待验。构建、自动回归、正式运行及用户验收分别记录。

第十批契约见[书签列表目录导航与排序](p2-bookmark-navigation.md)。BookmarkFolderList 共用原 BookmarkCollection/BookmarkNode；独立 BookmarkListViewModel/BookmarkListView 显示位置和有序子项，既有编辑树保留。书签事务专属回报避免阅读保存重复排序，编辑和加载继续进入原 SaveData/BookOperation。结构化搜索、来源元数据排序与书架 bookmark scheme 互联后续已接入；完整树布局继续占位。本批默认静默，正式窗口运行另行验收。

第十一批契约见[原书签结构化搜索](p2-bookmark-search.md)。原 NeeLaboratory.IO.Search 以固定 gitlink 源码内嵌 Engine，仍只有三个生产项目；未迁入无用队列或诊断宿主。单次快照查询及原字符串历史复用唯一节点/JSON；日期大小按需经来源接口读取，正则250ms单项超时是明确的运行预算改造。表现模型取消/重筛与原阅读控制独立；搜索框和历史菜单可单独调整。完整库测试、正式XAML、构建/本地签名及真机各自留证，本批默认静默。

第十二批契约见[原历史列表结构化搜索](p2-history-search.md)。原BookHistory五属性及逐项SearcherFilter在后台快照执行，访问时间不替换为文件修改时间；日期/名称/布尔属性不读取来源。大小沿现有来源接口，缺失/目录为-1。HistorySearchViewModel独立管理500ms输入、确认历史与关闭，GetViewItems同时服务面板和前后导航，JSON仍为唯一权威。

第十三批契约见[原历史文件保留限制](p2-history-retention.md)。原Limit与CreateMemento/fromLoad边界迁入，保存事务只裁剪写出副本；LastBook及表达式历史独立。设置候选锁内提交，成功后才改运行配置；现有归档临时根由启动层注入，保存前排除应用临时来源，普通系统临时目录保留。历史保存/登记/可靠无效清理由第二十批接入，原 RemoveUnlinkedHistory 命令在收尾接通既有清理流程。

第十四批契约见[原每目录参数](p2-folder-parameters.md)。FolderParameter/FolderConfigCollection迁入当前普通书架，目录排序与种子按路径保存，不再改全局默认。原特殊拼写Foldres.json接入同一四文件事务，旧双/三文件标记兼容；关闭保留仅影响写出副本，未知参数/缩略字段保持。原普通排序命令、切换与RandomBook分别接通，不创建平行配置或第二阅读内核。

第十五批契约见[真实书籍页与父子书](p2-book-hierarchy.md)。原三收集模式/目录递归/空书籍页、原480×640非图像页框及按需封面接入唯一链；ZIP逻辑目录保留物理ID且书架不误用DirectoryInfo。真实父子导航和递归切换按条目定位，失败保留旧书；Mac合法反斜杠保持。独立卡片绘制和主题不承担封面选择，逐页错误不终止同帧/胶片条。嵌套压缩仍明确留P5。

第十六批契约见[原输入方案与鼠标组合](p2-mouse-input.md)。原A/B/C/默认方向/参数共享/运行反转进入Engine，鼠标事件和统一解析在Mac表现层；普通滚轮全部依绑定。表单保存通过原导航锁，失败原地回滚，成功仅阅读字段变化重建正文。后续原方向序列、动画与 Hover/连续轮滚沿同一边界接入。

第十七批契约见[原查看器变换与参数编辑](p2-view-transform.md)。原变换图/参数/滚动约束归Engine，ReaderTransformPresenter提供唯一绘制与命中矩阵；共享/每页/跨书保持及BaseScale独立。参数表单草稿和主题与业务分开，未知字段与原数值保留。

第十八批见[p2-book-controls.md](p2-book-controls.md)。原锁定、五种页尾/下一书位置策略和可复用Unload归BookOperation；弹窗只回报选择，原循环页框/JSON保持。

第十九批见[p2-bookshelf-bookmarks.md](p2-bookshelf-bookmarks.md)。原书架bookmark scheme、共享节点独立列表、每目录参数/元数据排序及StartUp列表恢复接通；无第二书签树/状态体系。

第二十批接入原历史登记策略与可靠清理，继续使用唯一BookMementoControl/SaveData及四JSON事务；表现仅编辑草稿与转交命令，见[契约](p2-history-policy.md)。

第二十一批迁入原列表四模板/共享Profile、稳定路径可见封面及真正虚拟缩略网格；仍共用唯一来源/BitmapFactory和原三列表JSON，见[p2-list-templates.md](p2-list-templates.md)。

第二十二批迁入原Windows.Panels/WindowPlacement及浮动/停靠/关闭/重开；唯一内容与输入在表现端适配，主退出失败恢复同一宿主，见[p2-floating.md](p2-floating.md)。

第二十三批接入原方向序列、235命令的MouseGesture默认元数据及原差分配对，表现层处理捕获/提示，见[p2-direction-gestures.md](p2-direction-gestures.md)。

第二十四批接入原Scroll/Fade方向/时长与Hover/连续滚轮，单个退出帧共用现有显示租约，插值仅归表现，见[p2-animation.md](p2-animation.md)。

第二十五批完成预算内免排序和同规格显示缓冲复用、原无效历史清理入口、正反向单双页切换及共享循环参数，固定夹具测量与 P2 收尾证据见[p2-resources.md](p2-resources.md)。没有增加第二工厂、状态模型或长期 Preview 路线。

P3第一批接入[连续/瀑布速览](p3-browse.md)：唯一ReaderView与原Book/Page/BitmapFactory/JSON共享，布局/可见资源由独立表现辅助管理。原分页不改为新内核；纵向逐图是Mac扩展，原帧全景由第五批接入；目录树/逐页缩略、渐进索引与后台布局已由后续批次补齐。当前P3开发完成及验收边界见收尾清单。

P3第二批契约见[p3-navigation.md](p3-navigation.md)：Bookshelf拥有唯一普通FolderTree，节点通过既有ListFoldersAsync展开时读取，树选择/确认独立于正文。页面四模板复用共享表现，原Page直接进入唯一BitmapFactory；不增加后端服务、身份模型或状态存储。普通树/QuickAccess及原帧全景由后续批次接入；当前144个入口/91个占位，后台实图与正式Mac动态验收分别留证。

P3第三批契约见[p3-performance.md](p3-performance.md)：连续/瀑布几何改为256项不可变检查点快照，尺寸补齐共享未变前缀、从最早变化段重算；完整几何和尺寸更新在单槽后台执行，UI按书籍/顺序/代次提交并保留原Page及最新滚动锚点。最短列尾部最坏仍O(n)，完整几何仍全算。原元数据收集/过滤/Page创建后台执行及逐项取消，该批次万项完整元数据测量不等于渐进打开；普通非递归目录随后由第四批改为分批提交，递归及归档仍完整索引。没有新增生产项目、来源、阅读内核或状态体系。

P3第四/五批已接入[渐进目录索引](p3-index.md)和[原帧全景](p3-panorama.md)，早期批次的待迁说明按该契约更新；普通目录高级项由第六批QuickAccess/监视和第七批页面目录/搜索补齐，静默验收不等同设备封板。

P4第一批见[p4-destination-folders.md](p4-destination-folders.md)：原双区目标面板/无限集合/九数字及可配置Index、Once主图移动复制与进程共享UndoRedo接入。系统能力经IFileOperationBackend替换；恢复使用随机文件记录/覆盖副本与SHA256，不增加数据库或第二内核。原导航锁串行协调真实落点和原SourcePages，晚取消按提交点完成必要状态；Mac布局/表现独立。P3剩余真机、Windows动态和长期性能按用户要求并入P4集中验收。

P4第二批见[p4-multipage.md](p4-multipage.md)：沿原CurrentPages/CollectPages迁入普通目录多页分类和固定CopyToFolderAs；批次独占原忙碌锁，实际成功项逐项入栈，晚取消继续原索引协调，一次重建页框。固定复制与数字面板模式独立；归档实体化仍待后续；基础删除/重命名/实体剪贴板已由第三至第五批接入，三项目/JSON/前端边界不变。

P4第三批见[p4-delete.md](p4-delete.md)：原DeleteFile单主页/写权限/默认确认迁入；系统废纸篓成功后才协调原来源/搜索/页框，失败保持页面，删除不入分类历史。像素按原Page失效、晚到清理与显示租约归还共用原工厂；空搜索保留全源锚点但不登记为可见页历史。设置/确认表现独立，归档内/链接/列表多选删除和其余基础文件操作正按P4清单分批接入。

P4第四批见[p4-rename.md](p4-rename.md)：沿原RenameBook/FileIO/RestoreBook迁入目录与根归档实体改名，保留写权限、编号/扩展名确认、失败重试及原重新加载规则。原JSON/书签/QuickAccess/目录参数/封面/播放列表与环形历史只联动明确路径，未知字段保留；小型marker和现有事务支持部分失败/重启恢复，不增加身份数据库。准备阶段关闭可取消，已授权实体等待真实结果，新打开请求优先；前端只采集/转交。实体文件剪贴板已由第五批迁入；归档实体化与其他文件范围仍待迁，设备验收独立。

P4第五批见[p4-clipboard.md](p4-clipboard.md)：原CopyFile页组三策略、CopyBook实体书籍、TextCopyPolicy与Paste加载语义迁入；NSPasteboard标准文件URL和有限私有QueryPath替换Windows剪贴板。Copy不需要源写权限，不改文件、不入移动历史；Paste复用原打开链。取消排队准备、等待已提交原生结果、关闭失败可重试；用户决定CutFile/CutBook继续禁用，多文件临时播放列表、归档实体化和位图等继续待迁。三项目/原JSON/前端边界保持。

P4第六批见[p4-realization.md](p4-realization.md)：原ArchivePolicy四值/默认SendExtractFile、LimitedRealization、保序去重和QueryPath分离接入同一归档读取链。请求级独立临时文件替换FileProxy/TempFile；成功剪贴板输出由进程持有，切书/关窗保留，下次成功复制或退出清理。固定复制在原移动服务忙碌锁中完成整组准备，复用覆盖/完整性协议、不进历史；默认数字分类仍限真实普通图片。三项目/原JSON/前端边界保持，目录提取/链接和其他文件范围继续迁移。

P4第七批见[p4-multi-paste.md](p4-multi-paste.md)：原BookHubTools多项临时列表、PlaylistArchive/Entry真实来源代理接入唯一打开链；保留接收顺序/重复/混合项及原收集/排序。全局Hub不变，临时列表进程持有、正常退出清理，运行历史保留、写出过滤、启动跳过。Finder/拖入/启动与Paste只转交同一Engine入口；Windows路径转换、其他Paste内容及书籍文件菜单/其他删除继续迁移。

P4第八批见[p4-delete-book.md](p4-delete-book.md)：原整书真实根目录/文件删除、独立确认及GetNextItem邻项规则接入；系统废纸篓成功后清启动目标、刷新书架并按原开关打开邻项，历史/书签保留。确认/准备关闭可取消，实体授权后等待真实结果，新打开请求优先；失败重开原memento/搜索/锁定，JSON保存失败在唯一事务重试。明确修正原取消/失败仍可能下一书的流程，前端只采集选择及编辑原字段。其他书籍文件菜单、其他删除范围及Paste内容继续迁移，P4未完成。

整书删除的路径保护经同一来源替换点解析系统realpath，保留实际大小写及系统链接语义，避免Profile/临时目录别名绕过；仅实体操作调用，不为阅读/排序增加扫描或原生资源持有。

P4第九批见[p4-book-transfer.md](p4-book-transfer.md)：原整书目标菜单/Index、固定复制/移动与FileIO来源释放/BookMementoRenameRecursive成功路径联动接入。目录及根文件共用FileOperationBackend后台槽、指纹/暂存/覆盖备份和恢复日志，不增加第二文件内核。复制保留当前阅读，移动成功保持卸载且不自动跳邻/目标，失败按原memento恢复；原JSON marker扩展移动指纹，模糊落点保留。前端仅呈现整书覆盖范围并转交；逻辑归档目录提取/链接及完整Finder元数据后续验收/迁移。

P4第十批见[p4-directory-copy.md](p4-directory-copy.md)：原普通目录直传、内部归档目录四策略及混合页组保序/去重接入，复用第九批整树后端与原忙碌锁，不新增传输内核。当前目录内落点沿唯一打开链刷新索引并保留阅读状态；目录覆盖确认/取消可由切书或关闭取消。内部目录提取经固定基线核验为原版TODO，保留跳过并明确提示，不列为迁移丢失。三项目/原JSON/前端边界保持。

P4第十一批见[p4-logical-book-copy.md](p4-logical-book-copy.md)：Book.Path对应条目由唯一来源提供，显式图片定位仍复制整书，包内目录保留真实归档归属。CopyBook与目标目录复制分别沿原四策略/LimitedRealization，复用既有剪贴板资源与整书后端；移动仅真实根实体，逻辑复制不更新原定位、不改变分类栈。没有新来源、传输内核、界面入口或状态模型。

P4开发收尾契约见[p4-completion.md](p4-completion.md)：原三类删除/显式多选、图片/HTML/URL接收与失败回退、Mac符号链接及Finder别名接入；Archive.Zip独立写权限默认关闭，永久删除始终确认。仍只有三个项目、原JSON和唯一阅读/文件链路；设备封板另验。原目录树任意选中对象的完整文件管理不是本阶段当前书/当前页范围。

P3/P4集中设备验收沿原边界修复Finder文件引用URL解析、最终Retina设备边界和列表Delete焦点分派，没有增加生产项目或状态模型。静止设备1:1对齐归ReaderTransformPresenter，Finder引用解析归MacFileClipboard，实际删除仍归原BookOperation。长期资源测试失败；静态AX查询确认第三方macOS无障碍回调数组保留，未用禁用无障碍或维护框架分支规避。详见[最新设备记录](../acceptance/p34-device-runtime.md)，原Profile已完整恢复，阶段未封板。

后续缺陷修复仍沿同一表现边界：浮窗分别使用桌面坐标与绘制缩放；原标题来自原页框的PagePart；交互popup按实际展开状态及主/浮窗OpenedPopups隔离全局命令，控件保留自身导航。文件操作只读准备在现有有界I/O槽中超时/取消，写事务继续等待真实结果及恢复协议。真实SMB原生打开阻塞与AX回调引用保留仍未解决，详见[修复及回归记录](../acceptance/p34-fixes-runtime.md)。没有新增状态模型、传输内核或框架分支。

NAS恢复继续使用同一随机文件日志：严格属性查询区分缺失与访问失败，回滚/清理先核验两端父目录，失联不能移除日志。普通文件在尚未安装且两端保持原样时，启动恢复可逐字节验证并清理原件前缀副本；其他变更仍保留材料。目录部分树及同机外部进程在指纹复核与删除之间的替换不作新增原子性保证。诊断开关沿用NEEVIEW_DIAGNOSTICS，只报告文件流打开阶段及原mode/access/share，不记录路径。实际断线/重新挂载/启动恢复及进程中断样本见本轮记录；原生open旧阻塞未重现，不因此宣称已修复。Avalonia官方修复PR已关闭未合并，不为AX问题引入长期Native构建维护。

P5第二十批：原备份/保存/重载命令进入唯一Profile；仅UserSetting原地恢复，来源DirtyBook变化才重收集，前端布局/主题/字体独立恢复。动态预算、旧备份保持、晚到选择器/新打开边界见[契约](p5-profile-commands.md)。

P5第二十一批：原随机跳页/来源资格排序、地址栏/滑条菜单与快捷键语义、窗口状态与独立设置目录进入唯一命令链。正文与Chrome通知分离；关闭取消/等待系统目录动作。见[契约](p5-original-commands.md)。

P5第二十二批：原六种画布背景、五自定义刷、透明页底色/HSV棋盘及双轴nearest进入唯一Config/BitmapFactory；背景变化仅重绘。源首像素RGB与预乘显示分离，显示租约/UI发布和独立草稿沿原边界，见[契约](p5-background.md)。

P5第二十三批：原十二字段默认复制、实际变化历史订阅、递归DirtyBook重收集及全局文件权限接入；新打开优先，失败恢复设置/位置/历史资格，权限不刷新正文。见[契约](p5-default-settings.md)。

P5第二十四批：原版本窗口布局/图标、实际Mac构建版本、复制/许可/项目链接及macOS关于菜单接入；窗口与阅读独立，Windows更新检查保留Mac待接入区域。见[契约](p5-version-window.md)。

P5第二十五批：原外部应用命令/集合与独立设置草稿接入；Engine捕获页组及策略，唯一平台字面提交，随机材料进程留存/2GiB共用预算；布局与启动解耦，见[外部应用契约](p5-external-applications.md)。

P5第二十六批：原CopyImage首图像源、完整PNG及系统图像剪贴板接入；现有显示租约后台编码，切书/关闭拒绝旧结果，三项目/原JSON保持，见[契约](p5-image-copy.md)。

P5第二十七批：原三导出命令、Config.Book参数、命名及整书页框前进沿唯一BookOperation/Archive接入；ReaderView离屏复用真实绘制并按原尺寸请求像素，JSON/三项目保持，见[图像导出契约](p5-image-export.md)。
