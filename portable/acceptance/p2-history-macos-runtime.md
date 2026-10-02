# P2 第六批正式 Mac 运行验收

日期：2026-10-03。设备 hostname 192.168.31.148、UID 501，用户解锁后执行。唯一正式应用为默认目录 src/NeeView.MacOS/bin/Debug/net10.0-macos/NeeView.MacOS.app，没有使用 Preview 或更换输出目录。

## 执行范围与版本

先正常 Command+Q 退出旧进程并查询确认，再备份用户三 JSON 到独立 artifacts/p2-history-smoke/state-before。自建样本包括 Same 下 BookA/BookB/中文漫画.cbz、Other 下 BookC、缺失归档、不同日期访问记录、独立书签及未知字段。原用户数据全程可以完整还原。

历史列表交互在 9c8597464 构建执行。发现启动恢复只传路径的缺陷后，沿原 FirstLoader/BookHub 的显式 memento 链修复。最终构建重新执行全量 **101 项测试**、Engine、正式入口 Library、正式 .app 及 ad-hoc 严格签名，均通过，见 [构建记录](p2-history-validation.json)。最终 .app 复测启动/清空/翻页/退出/重启和用户原布局恢复，没有把修复前流程冒充最终版本的完整重跑。

## 已观察结果

| 检查 | 实际观察与证据 |
|---|---|
| 默认单击 | BookA 第三页 → 单击 BookC，恢复第二页；[AX](p2-history-single-click-bookc.ax.txt) |
| 前后历史 | 后退至中文 CBZ 第四页，前进回 BookC 第二页；访问顺序及时间保持；[后退](p2-history-prev-to-zip.ax.txt)、[前进](p2-history-next-to-bookc.ax.txt) |
| 缺失来源 | 缺失 CBZ 报“来源不存在”，地址仍为 BookB，原正文保留；[截图](p2-history-missing-source-keeps-bookb.png)、[AX](p2-history-missing-source-keeps-bookb.ax.txt) |
| 日期与目录 | 今天/昨天/完整日期分组；当前目录 4/5 项，排除 Other/BookC；BookA 后退到中文 CBZ，跳过 BookC；[分组](p2-history-menu-keyboard-probe.ax.txt)、[过滤](p2-history-current-folder-filter.png)、[导航](p2-history-folder-prev-to-zip.ax.txt) |
| 关键词与文本隔离 | 搜索 Book 后只显示当前目录 BookA/BookB；搜索框 Enter/Delete 保持当前中文 CBZ 与全部历史；BookA 后退到 BookB；[输入](p2-history-search-text-isolation.ax.txt)、[导航](p2-history-search-prev-to-bookb.ax.txt) |
| 列表 Enter | Up 只改变选择，正文仍为 BookB；Enter 打开 BookA 第三页；[选择](p2-history-keyboard-row-selection.ax.txt)、[打开](p2-history-enter-restores-booka.ax.txt) |
| 面板开关 | 菜单键盘操作隐藏数量、隐藏搜索框；重复 FocusHistorySearchBox 显示并聚焦，面板不关闭；[数量](p2-history-items-count-hidden.ax.txt)、[隐藏](p2-history-search-hidden.ax.txt)、[聚焦](p2-history-focus-search-repeat.ax.txt) |
| 多选移除 | Shift+Down 选中 BookA 和中文 CBZ；右击已选 BookA 保留两项；Escape 后 Delete 移除批次，正文仍为 BookA；[右击](p2-history-right-click-keeps-multiple.ax.txt)、[移除](p2-history-removed-current-and-zip.png) |
| 清空取消 | 打开真实确认窗口，取消后列表保持，History.json 与取消前逐字节一致；[确认](p2-history-clear-confirm.png)、[取消](p2-history-clear-cancel.ax.txt) |
| 清空提交 | 面板确认及原 ClearHistory 命令清空全部记录，包括过滤外 BookC；原命令不弹确认；[面板](p2-history-clear-applied.ax.txt)、[命令](p2-history-clear-command-no-dialog.ax.txt) |
| 删除后的保存 | 同进程删除当前项后真实翻页至第三页未加入历史；清空后真实翻页至第四页，退出保存 Items 仍为空；[删除后翻页](p2-history-removed-current-real-page-turn.ax.txt)、[清空后翻页](p2-history-clear-current-real-page-turn.ax.txt) |
| 原双击设置 | BookB 第五页启动恢复；OpenWithDoubleClick=true 时，单击 BookA 只选择，双击恢复第三页；[单击](p2-history-double-mode-single-select.ax.txt)、[双击](p2-history-double-mode-double-opens.png) |
| 数据独立 | 样本 PNG/中文 CBZ 与原文件逐字节一致；书签语义一致，未知根字段/记录字段/History 设置保留；[文件验证](p2-history-runtime-state-checks.json) |

## 发现与修复：启动快照不能只传路径

修复前，移除当前 BookA 并退出，LastBookV2 仍保存第三页，但重启只传路径，因 History 已无该书而落到首图。自动启动是新访问，会再次登记；进程内抑制不等同跨进程禁止访问。

原 NeeView/MainWindow/FirstLoader.cs:126 传完整 BookMemento，NeeView/BookHub/BookHub.cs:489 优先使用显式快照。Mac 的恢复入口改为 BookOperation.RestoreLastAsync，在同一加载链优先使用原快照的页位置、设置和排序种子；普通打开/历史导航仍走字段恢复策略。SaveData 共用 Path/Page/Props 解析，LastBookV2 补存已有 MacPagePart/MacIsSupportedWidePage 扩展，没有新增状态体系。

两项正式 XAML 回归覆盖缺失/过期历史、Default 恢复策略与显式快照优先、当前项删除后保存/再加载。最终真机使用空历史和相反的默认页模式：

1. 启动正确恢复 004.png、单页、从左向右；[AX](p2-history-fixed-startup-without-history.ax.txt)。
2. ClearHistory 后用实际方向键翻至 003.png 并退出；History.Items 为空，LastBookV2 保留该页、方向及 Mac 补值；[AX](p2-history-fixed-clear-page-three.ax.txt)、[文件验证](p2-history-runtime-state-checks.json)。
3. 不改退出后的 JSON 再启动，正确显示 003.png、单页、从左向右；[截图](p2-history-fixed-restart-after-clear.png)、[AX](p2-history-fixed-restart-after-clear.ax.txt)。新访问仍可登记历史，不把删除抑制保存成黑名单。

原 Windows 当前项删除/退出登记细节没有完整动态对照。本批维持已批准的 Mac 契约“删除后同进程保存不复活”；启动快照优先由原源码证实，不能扩大为所有原历史行为完全一致。

## 用户数据还原

首次验收和修复复测分别创建不可覆盖的独立备份。退出后原子恢复三 JSON，每份逐字节及 SHA256 一致，见 [还原验证](p2-history-runtime-state-checks.json)。第二份备份来自首次还原后的正常打开/退出，不用旧批次数据覆盖用户状态；重启后的正常访问保存与验收临时写入分开判断。

临时快捷键和 History 设置已撤回。最终应用恢复原中文 CBZ 中文/003.png、左栏信息/页面列表组合及比例、右栏导航器，并留运行；[最终截图](p2-history-final-user-restored.png)、[AX](p2-history-final-user-restored.ax.txt)。用户 .DS_Store、旧图片、旧备份和 Windows 源码未改。

## 工具与未验边界

UI 全程使用 cua_repl，Mac 文件/进程核验使用本地环境插件。独立 ContextMenu 窗口未被工具主窗口 AX/截图捕获，开关和清空通过键盘操作及实际结果验证。四样式/无效清理的禁用状态由正式 XAML Headless 验证，未取得正式弹出菜单逐项视觉证据。右击其他行切换选择已观察；空列表菜单没有可执行动作；非空列表空白处清除多选/禁用的精确指针检查仍只有 Headless 证据。

粘贴曾超时但已生效，观察后未重复粘贴；启动防抖使 AX 索引过期时重新读取。AX 滑条 setValue 只改临时选择，不作为翻页通过，正文翻页证据来自实际方向键。

真人鼠标/触控板/中文 IME、Retina 像素 1:1、多屏、NAS、长期原生内存、完整显示 P95、Windows 动态对照及用户新增交互验收仍待执行。P2 未封板，未推送/发布；Developer ID、公证和干净安装未执行。
