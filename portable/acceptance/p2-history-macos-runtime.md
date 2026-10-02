# P2 第六批正式 Mac 运行验收状态

日期：2026-10-03。实际设备 hostname 192.168.31.148、UID 501，Mac 执行器在线。本批构建和测试见 p2-history-validation.json，正式应用为默认输出目录 src/NeeView.MacOS/bin/Debug/net10.0-macos/NeeView.MacOS.app。

## 已执行与阻挡

本批尚未执行正式应用 UI 操作或重启保存验收。两次通过 cua_repl.getApp 读取该正式应用均返回：`The Mac is locked and automatic unlock could not unlock it. Ask the user to unlock the Mac manually before continuing.` 已异步请求用户解锁，未尝试改变锁屏或安全设置。

Mac 进程查询仍显示 PID 31618 的上批正式应用在运行。本批新的 .app 已在同一默认目录构建，但不能将磁盘成品当作当前运行进程已更新；没有退出或重启该进程。用户原 UserSetting.json、History.json、Bookmark.json 未为本轮真机验收备份、替换或编辑。自动测试只在自建临时目录操作数据。

Headless 直接编译正式 XAML/表现代码，覆盖菜单占位、分组/开关、确认取消、多选、文本隔离、单/双击与右击。p2-history-history-layout.png 是 Headless 渲染，不能替代正式应用运行或 Windows 动态对照。

## 解锁后接续清单

1. 正常 Command+Q 退出旧进程并查询确认；再备份三 JSON 到独立 p2-history-smoke/state-before，保留用户数据。
2. 使用自建目录/中文 CBZ 历史样本（不同访问日期及独立书签），启动最终正式 .app。
3. 验证历史默认单击、原双击设置、Enter 与搜索文本隔离；前后按当前目录/关键词筛选序列，访问时间/顺序保持；缺失来源失败后旧书可读。
4. 更多菜单日期分组、目录过滤、项目数/搜索框显示；未实现的四样式/无效清理为禁用占位。验证右击已选成员保留多选、空白禁用操作。
5. 仅在临时样本上移除/清空，验证取消不提交、源文件/书签独立，当前项翻页/退出不会重新加入。正常退出并重启验证配置、删除和位置。
6. 正常退出后原子还原三 JSON 并核验字节/SHA256，再启动恢复原中文第三页及组合侧栏；独立保存 AX、截图和运行证据。

以上为待执行检查，不是已通过结果。真人鼠标/触控板/IME、Retina 动态行为、NAS、长期原生内存、完整显示 P95 和 Windows 动态对照仍分别待验。Developer ID 签名、公证、安装和发布未执行。
