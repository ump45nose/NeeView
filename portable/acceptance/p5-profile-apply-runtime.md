# P5 第二批静默验收：导入选择、实际应用与恢复

2026-10-06，固定源码 `c5c398d89`/build 4340，当前 `feature/macos-port`。本批在唯一三项目中完成原五文件选择、差分默认、备份、实际应用/失败恢复和正式窗口重建；契约见 [p5-profile-apply.md](../docs/p5-profile-apply.md)。所有写入均为隔离合成 Profile，不修改真实用户状态、导入来源或用户图片。

## 实际结果

| 验证 | 结果 |
|---|---|
| Profile 导入专项 | 65/65 通过 |
| 全量自动测试 | 905 通过、2 跳过，总计907，无失败 |
| 三项目/平台依赖/原源码指纹边界 | 通过，71直接迁入/249局部适配；数量不表示覆盖率 |
| Engine 构建 | 通过，0编译警告/错误 |
| 正式 macOS Library 源码编译 | 通过，0编译警告/错误 |
| 默认目录 ARM64 `.app` 构建 | 通过，0编译警告/错误 |
| `.app` strict/deep 本地 ad-hoc 签名 | 通过；不是 Developer ID/公证 |
| 原 ImportBackup 与完整登记 | 正式 XAML Headless 装配读取器/回调后启用；235实例、168入口/67占位 |
| 真机/Windows/真实两分支导出 | 未执行 |

两项资源测试因未提供其显式资源环境变量而跳过：BrowseResourceTests.MountedResourceSubfoldersRenderAndScrollWithBoundedDisplay、PageThumbnailResourceTests.ResourceSamplesShowThumbnailsScrollAndCloseWithBoundedResources。本批不重复 P3/P4 用户已要求跳过的设备范围，不继承静默测试为设备通过。

## 验证边界

- 原默认选择、取消所有项、实际复选框双向绑定和确认取消通过；取消不关闭预览、不写入目标。
- 已迁设置差分缺省恢复原默认、来源命令整体reset、未知原有/来源配置与参数保留，Control仍为Control。原字符串枚举及WindowPlacement、早期Mac数值配置读入通过。
- 未选文件保持精确字节；选中缺失文件不删除现有文件。独立Foldres/QuicAccess优先，旧History.Folders/Bookmark.QuickAccess后备通过。
- 44至46.3历史/书签旧Books/Page/Props、原UNC共享根/去重/文件夹日期升级通过；Page仍为条目名，尾部大小写和书签顺序保留。旧活动Books转入兼容扩展，重新保存不覆盖新进度。
- 未支持设置/未来build可预览，应用拒绝；取消该项后可导入受支持集合。坏Config/Props/已知参数在关闭前阻止，Config.Current不变。
- 关闭保存失败、关闭前/后取消、准备和部分提交失败、重建失败回滚并重开旧配置通过。持久备份校验后恢复原字节和原缺失状态。
- 失败新窗口未释放时禁止回滚/二次重开，仍保留备份；损坏备份恢复失败保留材料并报告两个错误。中断恢复缺少事务副本时marker不删除，补齐材料后再次Load恢复通过。
- 正式MainWindow关闭/新实例重建、原命令启用、再次正常保存与关闭通过；生命周期委托保持UI线程。候选复制、JSON合并/校验和哈希在后台完成，界面不写文件。

初次专项的报告前缀、复选框文案和“未知旧字段应删除”测试断言已按真实保留语义纠正。验证脚本的单行phase列表触发Python3.9编码报错，改为分行后正常运行；没有更换bin/obj输出。首次全量904/2，补齐marker失败重试和装配后的命令证据后，最终905/2。最终五个验证步骤退出码均为0。

Headless 使用正式窗口/控件源码及隔离读取/平台替身，未启动或激活桌面NeeView。真实系统选择器、Finder/Dock/退出事件竞态和故意制造的MacApp构造失败没有真机故障注入；相关宿主路径经代码核对及正式编译，不冒充设备通过。

## 证据与后续

[完整验证](p5-profile-apply-validation.json)、[摘要](p5-profile-apply-evidence.json)、[原命令状态](p5-profile-apply-commands.json)、[导入布局](p5-profile-apply-layout.png)。最终TRX、首次/最终验证及其他全量回归输出在 `/Users/yuwk/.codex/artifacts/neeview/p5-profile-apply-20261006`；仓库只新增本批合成导入图片，不覆盖历史阶段证据，原未跟踪 `.DS_Store` 保留。

完整旧UserSettingValidator、旧布局V0/V1、高级/附属数据及真实导出仍待后续；本批不宣称整个P5完成。P3/P4完整浮窗菜单/停靠与屏幕P95仍按用户要求跳过，MAC-AX-001和MAC-NAS-004未关闭。自动本地提交与推送单独报告；未签名公证或发布。
