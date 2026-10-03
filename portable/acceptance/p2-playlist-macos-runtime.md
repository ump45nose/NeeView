# P2 第八批：播放列表与全局页标记 Mac 运行验收

2026-10-03，在用户已解锁的 Apple Silicon Mac（UID 501）运行正式 `NeeView.MacOS.app`，没有 Preview 入口。最终全量 **121通过、0失败、0跳过**；Engine、正式入口Library、正式`.app`构建及本地ad-hoc严格签名分别通过，见[p2-playlist-validation.json](p2-playlist-validation.json)。没有Developer ID签名、公证、推送或发布。

## 版本与缺陷

验收采用同一源码增量的四次构建，事件明确记录版本，见[p2-playlist-runtime-events.json](p2-playlist-runtime-events.json)。初版`Multiple,Toggle`导致无修饰单击累加两项并误开首项；改为`Multiple`。随后发现集合刷新丢掉列表容器焦点，方向键落到全局前后书；恢复选中容器焦点，并在列表隧道消费Enter/Delete，解决Enter被控件提前处理。再发现前后登记已切正文但高亮仍停旧行；刷新时以Hub新当前项为准，当前项仍在批次内则保留多选。

三项均补正式XAML指针/键盘回归。初版[单击缺陷](p2-playlist-failure-toggle-selection.png)、[焦点缺陷AX](p2-playlist-failure-focus-loss.ax.txt)及[高亮滞后](p2-playlist-failure-navigation-highlight.png)保留为失败证据，不作为通过材料。原WPF前后登记先移动SelectedItem再打开的顺序保留；失效条目被选中时旧书保持可读，并不回退列表选择。

## 最终版本操作结果

| 操作 | 实际观察与证据 |
|---|---|
| 单击、方向键、Enter | 单击目录目标打开；Down仅把选择移到005而正文仍001，Enter才打开005。见[只选行](p2-playlist-sync-final-down-selects-only.ax.txt)、[Enter](p2-playlist-sync-final-enter-opens-selection.ax.txt)。归档003通过同一入口打开。 |
| 前后登记 | 临时快捷键在001/005间移动正文与高亮；归档逻辑路径定位中文003。见[下一登记](p2-playlist-sync-final-next-item.png)、[上一登记](p2-playlist-sync-final-prev-item.png)。 |
| 书内前后标记 | 当前目录005返回标记001，再到005；列表选择独立。见[前标记](p2-playlist-sync-final-prev-mark-in-book.png)、[后标记](p2-playlist-sync-final-next-mark-in-book.png)。 |
| 标记开/关 | Ctrl+M移除/重新登记当前005，正文保持；胶片条金色角标和滑条标记同步消失/出现。见[关](p2-playlist-sync-final-mark-off.png)、[开](p2-playlist-sync-final-mark-on.png)。 |
| 失效目标 | 选择归档内不存在条目，显示“指定页面已不存在：中文/不存在.png”，漫画中文003、页号和来源保持。见[旧书保持](p2-playlist-sync-final-missing-keeps-book.png)。 |
| 多选删除与恢复 | Shift+Down选择失效项与003，Delete移除两个登记，不删图；文件立即剩2项。恢复按钮回到4项且恢复按钮禁用，已落盘。见[批次选择](p2-playlist-sync-final-multiselect.ax.txt)、[删除](p2-playlist-sync-final-delete-batch.png)、[恢复](p2-playlist-sync-final-restore-batch.png)。 |
| 文件循环和组合框 | Default→Pagemark→Default循环，组合框键盘切回Default；正文保持。见[Pagemark](p2-playlist-sync-final-next-file-pagemark.png)、[循环](p2-playlist-sync-final-file-wrap-default.png)、[组合框](p2-playlist-sync-final-combo-default.png)。 |
| 新建、登记及重排 | 更多菜单进入新建对话框，创建“验收列表”，＋登记当前中文003；Default中的003上移后正文不变，实际文件顺序核验。见[新建](p2-playlist-sync-final-create-list.png)、[登记](p2-playlist-sync-final-add-current.png)、[上移](p2-playlist-sync-final-move-up.png)。 |
| 退出与重启 | 当前文件保存为短名`验收列表.nvpls`；未经修改JSON重新启动恢复该列表、一项中文003及漫画3/6，删除恢复按钮禁用。见[重启](p2-playlist-sync-final-restart-selected-list.png)。 |

更多菜单的原四模板、列表文件更名/删除、无效清理及OpenAsBook共8个禁用项在正式Headless装载中核验；原全局命令清单没有删除。格式/未知字段、取消/失败回滚、旧列表晚到拒绝、排序/分组/过滤、单双/分割目标及设置显隐有自动证据。

独立ContextMenu窗口的AX/截图捕获存在工具限制。菜单键盘探索实际进入新建对话框，因此对应事件明确记录“新建”，没有声称分组开关通过。分组/过滤开关、别名对话框、打开文件选择器、四模板的弹出菜单视觉、右击批次/空白等完整真机操作仍待真人验收。组合框AX项报告offscreen后采用键盘操作，结果已确认；虚拟容器AX排列不充当实际登记顺序，顺序取保存文件。

## 用户数据保护和恢复

临时种子只使用`portable/artifacts/p2-playlist-smoke/Playlists`，原用户默认Playlists目录原不存在，最终仍未创建。三份JSON事先独立备份到忽略目录，验收退出后各自以临时文件Flush/原子替换还原，逐字节及SHA256核验通过。原列表文件为0个，目录存在状态保持；六张PNG和漫画CBZ哈希全部保持。

Default真实删除后2项、恢复后4项，根`FutureRoot`与001条目的`FutureItem=19`保留；新列表1项，未留自己的tmp。保存文件、还原哈希及最终状态见[p2-playlist-runtime-state-checks.json](p2-playlist-runtime-state-checks.json)。

验收准备时，工具探索曾触发前一书，把漫画第三页切到目录第三页，备份包含该临时阅读状态。还原后通过正式地址栏重新打开原漫画中文003；左信息/页面列表组合、比例与右导航器恢复，临时键位与列表目录配置撤回，应用留运行，见[最终窗口](p2-playlist-final-user-restored.png)。重新正常打开会自然更新访问/阅读保存，不要求运行后的JSON仍等于还原瞬间哈希。

## 尚未证明的范围

Windows动态对照、真人IME/鼠标/触控板、Retina像素1:1/多屏、NAS、长期内存及完整首图/帧P95未在本批完成。源格式支持也不等于任意旧Profile可直接迁移；完整导入仍在P5。当前迁移清单45文件/63子集适配，数量不代表功能覆盖率。P2未封板。
