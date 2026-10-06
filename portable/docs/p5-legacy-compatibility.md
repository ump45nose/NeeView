# P5 第三批：原旧设置与三代侧栏布局

本文件保留第三批交付范围；旧目录与快速访问的最新范围及差分编辑修复见[第四批契约](p5-folder-compatibility.md)。

## 职责、依赖与出处

Engine 在既有 Profile 导入链路上适配固定 `c5c398d89` 的 `UserSettingValidator`、`TitleStringValidator`、参数 converter 和 `LayoutDockPanelContent.Restore`。不建立第二套配置模型，原 Windows 文件保持只读；逐文件 SHA256 见 [源码清单](source-migration.json)。视图、后端和生产项目数量不变。

`LegacyUserSettingUpgrade` 只处理独立 JSON 候选，`LayoutPanelCompatibility` 负责三代分组/面板名转换。普通 `Config.Panels.Layout` 读取也恢复旧布局，不要求先打开导入窗口。既有 `LayoutPanelManager` 继续拥有组、选择、比例和浮动状态。

## 契约与生命周期

只读来源 → 解析并检查版本 → 原升级规则 → 路径映射 → 命令/兼容报告 → 独立确认请求 → 原五文件验证/备份/事务 → 唯一窗口重建。升级在后台候选上完成，不写来源、不切换 `Config.Current`。预览展示升级后的命令和导出目录；版本摘要保留来源版本。

支持候选保存为 `46.3.0`；旧设置来源版本记入 `MacImportedSourceFormat`。验证和应用再次调用同一升级入口，已升级候选不重复执行旧版本分支。正常保存继续合并原未知字段。被替换的旧参数、冲突命令和拖动动作存入 `MacImportedLegacy*` 扩展，不再参与执行。

## 已核对规则

| 原版本条件 | 当前适配 |
|---|---|
| `<39` | FontName、语言、文件名排序 → Entry、Pagemark 命令/菜单/面板改名 |
| `<40` | 中文语言名、横向滚轮初始化、F12 冲突检查、MoveScale 参数 |
| `40 ≤ v <40.3` | 旧 dummy page 开关复制到首末页开关 |
| `<40.5` | 40 前反向模式命令重置；40 后仅存在且参数为空时关闭循环 |
| `<41`、`<43.0.3661` | AutoScroll/Cut 默认键位去冲突注册；Cut 仍按用户决定禁用 |
| `<42.0.6` | **实际 CopyFile 参数类型**的旧复制策略迁到 System；不按同名字段猜类型 |
| `<45.0.3973` | nullable 面板计数开关分发到四原分支 |
| `≤46.0.4065` | 文件管理/外部程序/地图/标题占位符，typed 导出参数退役 |
| `≤46.0.4134` | FilmStrip 自动隐藏与旧命令/上下文菜单改名 |
| `≤46.0.4176` | centered drag 键位按原目标默认继承；当前 Snap 归为 LockUntilResized |
| `≤46.0.4209` | FullDesktop 自动隐藏、缩略模板叠加、FullDesktop 默认键位冲突 |

命令名采用原 `Name:Number` 语法；已有新名优先。原参数 `Type/Value`（含原拼写错误）规范为 `$type`，保留未知正文/包装材料；已迁命令的错误类型在旧窗口关闭前拒绝。原当前字符串枚举与早期 Mac 数值保持兼容，Control 不改为 Command。

固定基线 `ViewConfig.IsLimitMove/IsMoveLockStart` 的 setter 为空，getter 根据当前 MovementConstraint 返回；原 validator 实际不能从旧布尔 JSON 恢复约束。Mac 保持该行为，不按字段名字重建另一套规则。Snap 经原 getter 组合变为 LockUntilResized。

面板显隐与 View 拼写别名转到已迁字段；同时存在明确新字段时新值优先，旧字段移入分支的 `MacImportedLegacyFields`，避免后续保存被旧别名覆盖。这是 Mac 合并保存所需的明确适配。

## 三代布局

V2 非空优先；V2 空时用 V1；仍空才用 V0。V1 保留方向，V0 固定纵向。原字段与未知 V1 元数据保留，V2 为运行布局。未知成员过滤、空组丢弃，未找到 SelectedItem 保持关闭；缺失原面板补回默认栏。未知 V2 成员不触发旧布局回退，损坏当前字段不由旧布局静默替换。

Pagemark 改名涵盖 Panels 字典、三代组、SelectedItem、Windows.Panels。GridLength、WindowPlacement、未知浮窗和 AlternativePanelSource 保留。普通保存被过滤的未知组材料放入 `MacImportedUnrecognizedLayout`，不影响实际停靠。

Mac JSON 合并保存显式写 `SelectedItem:null` 以清除先前选择；原版差分序列化会省略 null。保留这一已明确的格式区别，避免“关闭 → 保存 → 重启”重新打开栏。

## 范围、错误与待迁入项

| 文件 | 可实际应用范围 |
|---|---|
| UserSetting | 38–46.3；38 的非零 FontSize/FolderTreeFontSize 缺少 Windows MessageFontSize 时只预览 |
| History / Bookmark | 44–46.3，沿上一批明确 Books/UNC/日期规则 |
| 独立 Foldres / QuicAccess | 46.1–46.3；未随设置放宽旧目录递归 validator 的范围 |

未来 build >4340、非零第四段、错误 Format/类型及未核对版本保持只预览。旧字体尺寸原值保留；不能猜测 Windows 字体基准。旧 ImageEffect 参数/缓存保留并记录原版本，**效果层/效果预设转换及执行尚未接入**。字体显示、外部程序、Windows 插件、自定义拖动和自定义菜单的能力仍以迁移清单为准。字段升级不意味着这些能力可以执行。

独立目录旧递归继承升级、38 前设置、原完整差分序列化、附属主题/播放列表/脚本导入继续后续批次；脚本不执行。旧 History.Folders/Bookmark.QuickAccess 后备继续原独立文件优先策略，但不能作为完整旧目录/快速访问升级的证明。

JSON 错误、错误参数类型和损坏布局在写入前报告；源数据与运行配置保持。提交后的备份/失败恢复沿 [第二批事务](p5-profile-apply.md)，不新增恢复体系。

## 测试与扩展

新增回归覆盖精确 `<`/`≤` 版本边界、原默认与 null、键位冲突、numbered 改名、真实多态包装、参数退役、重复升级、布局优先级/方向/浮窗/隐藏/未知字段、预览验证应用和普通保存往返。全部写入隔离合成 Profile；后台 Headless 不激活桌面应用，也不操作用户图片或 Profile。

[本批实际验证](../acceptance/p5-legacy-compatibility-runtime.md)分别记录构建、测试、本地签名和未执行验收。真实两分支导出、设备/Windows动态、Developer ID、公证与安装继续待验；不继承之前测试作为通过证据。

后续逐项补齐原未迁入 validator/效果和完整设置。公开契约仍是既有 ProfileImport/SaveData/Config/LayoutPanelManager，不新增插件平台或第二业务状态。
