# P2 第三批：页选择、胶片条与导航历史

日期：2026-10-02。承接原侧栏组合和 NScroll，本节点沿用原 PageSelector / FilmStrip / PageSlider / HistoryLimitedCollection，不增加第二套阅读或状态模型。P2 尚未整体完成。

## 职责与依赖

Engine 保存共享临时选择、索引/双页校正及两种进程内历史。BookOperation 仍是唯一正文定位和加载入口。Mac 的 ThumbnailView 负责显示、焦点、可见请求和详情；MainWindow 负责确认及可等待输入。XAML/主题可独立调整。

## 契约

| 入口 | 语义 |
|---|---|
| PageSelector.SetSelectedIndex | 修改临时条目，不改正文 PageFrame |
| PageSelector.Synchronize | 正文变化后同步原显示范围最小页索引 |
| FilmStrip.MoveSelectedIndex | 按视觉顺序移动；滚轮传方向反转，继续遵循原条目方向 |
| FilmStrip.RequestThumbnail | 原方向映射、`count + margin * 2 - 1` 余量及中心优先 |
| FilmStrip.GetFixedSliderIndex | 原双页静态对齐、首尾优先和同步步长 |
| BookOperation.MoveSizeAsync | 原最小显示索引 + delta，先归一周期再对齐；未到端点时校正到首尾，已显示端点时终止 |
| BookOperation.NavigateHistoryAsync | 页面路径/条目或打开顺序重放；成功后提交历史游标 |
| SaveData.GetMoveSizeParameter | 两方向共读 PrevSizePage.Parameter.Size，默认10、范围0–1000 |
| SaveData.SetCommandParameter | 合并已知参数，保留未知字段及差分快捷键 |

## 业务规则

- 原默认胶片条滚轮只移动选择；无修饰键左击或 Enter 才提交正文。MovePage 按原帧步进，CommandDependent 按原命令绑定执行。左右键在胶片条作用域移动，上下隔离，Escape 恢复正文选择；不抢文本框或菜单输入。
- 胶片条与滑条共用选择。开启胶片条且 IsSliderLinkedFilmStrip=true 时拖动预览、释放确认；未联动则定位正文。方向来自 SliderDirection，SyncBookReadDirection 跟随书籍方向。临时选择只通知滑条和编号，不发正文刷新。
- IsSelectedCenter 允许首尾留白，不钳制到内容端点。可见项及有限余量使用共享工厂；200ms 防抖、同序列和规格去重。页号、尺寸、方向、联动、滚轮动作及共享步长在独立设置页编辑。ImageWidth 保留原最低32及无 setter 上限。
- 当前正文范围浅色框、临时选择蓝框分别绘制。悬停详情显示条目、页码、真实尺寸及字节；未知尺寸走 Engine 探测，界面不读文件。它不是主图即时预览。打开新书、关闭详情、隐藏和离开时取消旧详情。
- 页面历史以书籍路径及真实显示最小页条目名记录；打开顺序历史独立于 History.json 的访问时间排序。容量各100，进程退出不持久化；后退后普通导航截断前进分支。
- 跨书历史走原打开链，并保留已存访问顺序/时间和终页位置。加载失败、条目丢失或被新打开取代时保持当前书和历史游标。这是为可等待加载做的必要适配；原同步实现先移游标的方式没有照搬。
- JumpPage 采用一起始输入，越界由定位校正；没有参数时打开可等待对话框。原默认跳转菜单未列出的指定页及四个历史命令追加到原跳转组，原节点/禁用占位和顺序保留。
- 刷新新页面列表后才恢复当前 Page 选择，避免 TwoWay 的清空回报覆盖选中项；不增加第二个列表位置模型。

## 状态、资源与错误

来源归 Book，页面租约归显示控件。切书请求带代次，缩略需求带 revision；已排队/原生晚到结果只释放。重绑控件先取消旧需求、解除订阅和释放 Bitmap/租约。重复可见需求共用未完成任务；规格变更释放旧显示资源。历史操作串行，正文提交仍由既有操作锁保护，失败不推进游标。关闭失败保留窗口供重试。

## 验证及范围

[64项自动测试和正式构建](../acceptance/p2-selection-validation.json) 包含原49项回归，以及方向/默认/请求排序、环形容量和分支、静态双页与终点分割、滑条同步、跨书历史、丢失条目/来源失败重试、并发取代、未知 JSON 保留、实际滚轮三模式、作用域确认及 Thumb 拖动。正式运行单列 [真机记录](../acceptance/p2-selection-macos-runtime.md)。源码出处见 source-migration.json。

本批不宣称完整 FilmStrip/PageSlider 迁入：播放列表标记、全局自动隐藏模式、Windows 触摸边界反馈仅保留配置，未提供对应行为；页标记、直接页号文本框、历史列表菜单、完整鼠标组合和触控板惯性仍待迁移。指定页目前用对话框，不把它称作原页号文本框。常用兄弟书/子书导航及完整书签编辑是后续 P2 节点；当前索引没有真实 Folder 页，不用近似命令冒充子书导航。循环页未开放。

Windows 动态对照、真实设备/IME、NAS、长期内存及显示完成 P95 仍独立验收，性能目标没有转成达成结果。
