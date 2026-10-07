# P5 第四十批：剩余开发收尾与静默验收

2026-10-07。打印、长按/自动滚动、FullDesktop/LastState、停留记录器、脚本item、最近书籍菜单与设置动作已进入唯一 Engine / Backends / MacOS 链。全部235个原命令保留，正式宿主装配有232个执行入口、3个明确禁用占位；入口数不代表完整功能覆盖率。

## 已完成校验

- 原算法及正式XAML全量：1831项，1819通过、0失败，12项需显式资源样本的用例跳过。跳过项包括挂载图片的浏览/缩略/滤镜、效果及导出，不将跳过视为通过。
- 官方macOS后台：38/38通过，含真实AppKit打印双页PDF、横竖方向、不同裁剪、整纸背景和取消；PDF/动图/视频等既有原生回归同时通过。不弹系统打印面板、不操作物理打印机。
- Engine及正式ARM64应用构建：0警告、0错误；默认最终.app的strict/deep本地ad-hoc签名通过。原生测试宿主构建有一个既有SDK apphost PublishFolderType元数据警告，38项后台执行实际通过；不将其混入产品构建状态。
- Python打包脚本：14/14通过，见[原始输出](p5-completion-package-tests.json)。
- 来源/依赖边界：三个生产项目；129个保留源码、528个部分适配及26个内嵌上游文件的出处检查通过。计数不是功能覆盖率。
- 本轮没有启动正式应用、激活窗口、发送真实键鼠、改写用户图片或用户Profile。

原始分步骤输出见[p5-completion-validation.json](p5-completion-validation.json)；当前实际命令登记见[p5-completion-configured-commands.json](p5-completion-configured-commands.json)。普通测试宿主导出少装配ImportBackup，因此为231入口，不能替代正式宿主232项证据。

## 回归问题与修复

- 未显示窗口关闭时无条件读取Screens导致后端未初始化；现在只在进入FullDesktop时取得/订阅，退出及关闭仅退订已有引用。
- FullDesktop已接入后的旧自动隐藏用例仍断言该命令不可用；更新过时能力断言，几何/导航断言保持。
- 导出关闭检查间歇剩余2400/10584字节；诊断确认显示租约为0、一个路径封面等待者尚未退出，而不是透明像素或退出动画泄漏。窗口现在先关闭显示消费者，再等待同一工厂已退休资源归零；不强制清零、不增加延时或弱化原ByteCount断言。新增持有显示租约时异步关闭不得提前释放的回归；原导出三组合连续三轮9次通过。
- 脚本书架中的书签改名遗漏取消令牌；在入口检查取消并传给原SaveData事务，新增节点及落盘名称均不变的用例。
- 小视口原图打印受原尺寸标记提前清除和普通UI刷新取消专用捕获两项因素影响；标记现在保持到整个捕获finally，首元素动画探测/复核与实际源图保持同一规格。输出持有原导航锁期间，普通UI刷新不能取消显式输出需求。增加小视口仍输出400×600原图及并发普通刷新不打断捕获的断言；[修前专项证据](p5-completion-print-capture-failure.json)保留。
- 打印常量通过官方AppKit导出取得真实NSString值，NSPrintJobSavingURL符号对应NSJobSavingURL，不能直接拼符号名。当前使用真实NSPrintOperation分页保存PDF；不使用只生成一页的PdfFromView替代。

最初编译/回归失败记录分别保留为[p5-completion-initial-validation.json](p5-completion-initial-validation.json)、[四项失败](p5-completion-regression-failures.json)与[关闭时序失败](p5-completion-close-failure.json)。最终65项打印/记录器/输入/窗口/脚本/设置/导出专项通过，1项实图导出用例跳过；随后补强普通UI刷新不能打断打印的断言，由最终全量复跑确认。打印表单800×600及300参数区、预览真实像素、草稿隔离和关闭取消通过；[Headless布局](p5-completion-print-layout.png)只作界面证据，不替代系统打印面板。

## 开发包与提交

已验证源码提交为`5f6e6e2c7d5f814a3154295ec7f6cd950830e554`。随后由package_macos.py在默认Release bin/obj生成0.1.0自包含ARM64开发包，包内dependencies.json记录同一源码提交。18个Mach-O的ARM64及strict签名、ZIP完整性、随机解包重定位及签名复核通过；25个实际依赖均带许可材料，无仅元数据许可项。原始结果见[本批打包记录](p5-completion-distribution.json)。

- 开发包：[NeeView.zip](../artifacts/NeeView.zip)，70,902,942字节。
- SHA256：`b1f82c952dc83a07d97bab5fec90feffdef760de3075828bef17b169fad94586`。
- 签名为ad-hoc development；没有Developer ID或公证，不代表Gatekeeper、交互安装或实际硬件打印通过。没有安装到用户Applications。
- Release自包含发布有既有SDK提示：命令行RuntimeIdentifier覆盖项目RuntimeIdentifiers；发布退出0。此前正式Debug构建仍为0警告/0错误，二者分开记录。
- 原报告worktree_dirty及tracked_source_dirty为true，来自既有/测试重新生成的验收截图与记录。本批生产源码、测试、脚本及设计文档在打包前已提交；不清理无关材料来改变标记。校验记录随本批提交，推送以远端SHA核对结果为准。

## 仍独立保留的边界

CutFile/CutBook按用户决定禁用；TouchEmulate属于WPF调试模拟，保留说明。原分卷归档、内部目录提取原TODO、Mac系统更新器及无合理对应的Windows专属能力仍在兼容清单；本批不将它们宣称为已迁移。打印硬件/系统面板、真实用户两分支Profile导出、Windows完整动态一致性、混合缩放多屏及相关真机操作仍需独立验收。

用户跳过的完整浮窗菜单/停靠与屏幕P95保持跳过。AX长期引用增长仍未解决，NAS原生不可及时取消仍部分修复；见[已知问题](../docs/known-issues.md)。Developer ID、公证、Gatekeeper和干净系统安装尚未执行。开发收尾、设备验收和正式发布分别判断。

[设计与公开契约](../docs/p5-completion.md)。
