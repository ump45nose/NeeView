# P5 第七批：附属文件导入静默验收

日期：2026-10-06。基于 `83ff66174`，固定原基线 `c5c398d89`；三个生产项目和唯一 JSON/列表/窗口链保持。全部数据为隔离合成 Profile，没有激活真实 NeeView 应用、操作用户图片、Windows 或 NAS。

## 交付与自动回归

- 原 `Playlists/*.nvpls`、`Themes/*.json`、`Scripts/*.nvjs` 的目录/ZIP 字节快照、独立选择、同名覆盖及备份恢复已接入。材料只读来源；主题/脚本不装载或执行。
- 播放列表复用原唯一格式解析器和 Hub，已知路径映射保持未知/内部相对定位。Windows Default/Pagemark 别名恢复原固定名，普通大写 `.NVPLS` 可见。BOM 文件保持原字节，预览与实际 Hub 加载都兼容原 ReadAllText 语义。列表选中时目录配置与文件共同事务，确认说明 Mac Profile 管理落点。
- 新增 34 项回归全部通过：来源/两种分隔符、v1/v2/alpha、选择/快照、Hub、覆盖/原字节/缺失、清单/hash、部分提交、中断重启/重试、重建失败、路径/链接/别名、体积/取消及正式 Headless 绑定。
- 首轮专项 94 项通过；扩展导入/差分/重命名专项 350 项通过；最后附属/列表专项 71 项通过。各集合有重叠，不累加成覆盖率。
- 最终全量 **1146 通过、0 失败、2 资源用例跳过，总计 1148**。源码边界为 3 生产项目、72 原文件、291 局部适配、26 原库文件指纹；数量不代表功能覆盖率。

全量回归暴露新增校验误拒绝整书重命名内部 `.book-rename-pending.json`，相关失败导致测试等待未完成；该次运行已中断并归档。修正后保留两个原内部标记的准备写入，附属导入/恢复清单仍拒绝这些标记；重命名专项与最终全量通过。损坏清单错误统一为 InvalidDataException，缺少 Format 的坏列表不阻止单独选择脚本材料。没有将中断运行算为通过。

## 构建与界面

Engine、正式 macOS Library 编译、默认目录 ARM64 `.app` 构建及 `codesign --verify --deep --strict` 全部通过，构建无警告/错误，沿默认 bin/obj 串行执行。

正式 XAML Headless 检查三类默认关闭、双向选择、确认快照及能力文字；[附属报告截图](p5-profile-assets-import-layout.png)已检查，项目/能力/确认按钮无裁切。保留原文件报告默认页，不改阅读窗口区域或业务规则。

Headless 不等于系统选择器/真实焦点/Retina/用户实际导出验证；开发 ad-hoc 签名不等于正式 Developer ID 分发。脚本材料导入不注册事件、命令或 watcher，主题材料不自动应用。

## 证据与后续

[串行验证](p5-profile-assets-validation.json)、[用例与边界](p5-profile-assets-evidence.json)。完整 TRX、首轮失败、中断记录、native sample 与重复截图位于 `/Users/yuwk/.codex/artifacts/neeview/p5-profile-assets-20261006`；旧阶段重复生成证据归档后精确恢复，用户 `.DS_Store` 保留。

本批校验后自动本地提交并推送 `feature/macos-port`，账号 `ump45nose`；提交/推送结果另由 Git 校验，不写成发布通过。

P5整体未完成：真实两分支导出、完整设置、高级格式/实际效果/主题/脚本、依赖许可分发清单、Developer ID、公证及干净安装继续待验/待迁。P3/P4已跳过项不重启；AX/NAS原生open既有问题不因本批关闭。
