# P5 第四批：目录与快速访问静默验收

2026-10-06；分支 `feature/macos-port`，固定 Windows 参考 `c5c398d89`、build4340。[契约](../docs/p5-folder-compatibility.md)、[完整构建与测试输出](p5-folder-compatibility-validation.json)、[合成目录映射及保存重载证据](p5-folder-compatibility-evidence.json)。

## 实际结果

| 层面 | 结果 |
|---|---|
| 全量自动回归 | 1004通过，0失败，2项需显式图片资源的用例跳过，共1006 |
| 导入专项 | 164通过，其中本批新增37；最终全量中加入原浮窗12/停靠11，共187相关用例通过 |
| 正式构建 | Engine、正式Library编译、默认ARM64 `.app` 构建通过，串行 `-m:1`、锁定依赖 |
| 签名 | strict/deep本地ad-hoc校验通过；Developer ID、公证、安装和发布未执行 |
| 源码/依赖边界 | 三个生产项目、71原源码/260局部适配/26原库指纹通过；原Windows文件无改动 |
| 真机/Windows | 没有启动或激活桌面应用，没有执行动态对照 |
| 用户数据 | 隔离合成Profile写入临时目录后清理；用户图片/Profile/NAS未操作 |

主命令：`python3 portable/scripts/validate.py --dotnet /Users/yuwk/.local/share/neeview-dotnet/dotnet --phase p5-folder-compatibility --macos-source --macos`。所有构建保持默认bin/obj，无输出目录冲突或改目录。

完整TRX、全量测试生成的其他模块截图及JSON留在 `/Users/yuwk/.codex/artifacts/neeview/p5-folder-compatibility-20261006`；仓库仅交付本批目录候选、验证及契约/验收记录。用户未跟踪 `.DS_Store` 保留。

## 行为对照与故障处理

直接核对原 `FolderConfigCollectionValidator` 和 `FolderConfigCollection.Restore(Dictionary)` 后明确：独立文件在4065/4209执行两个版本分支；旧History字典只归一排序，不执行独立文件递归validator。因此旧字典明确false必须保持。默认配置来自最终选择的磁盘候选，不借用Config.Current；普通、书签及播放列表默认分别覆盖。

首次专项编译发现params参数目标类型new语法及xUnit断言规则问题，修正测试调用与断言；随后164项中3项失败，原因是新增缩略目标映射将未知非字符串元数据当作路径。已限定映射原字符串目标，未知非字符串继续保留；最终164专项与1004全量均通过，没有放宽失败断言。

独立复核提出内嵌QuickAccess未来版本是否被校验。主代理直接核对 `ProfileImportRequest` 构造器：它对每个选项的有效后备文档调用对应BlockReason；5项未知/未来/null格式回归全部通过，该疑点无需额外修改。真实改动是保留内嵌Format，禁止旧后备逻辑直接覆写成46.3。

ScrollPage旧setter在递归Merge后再次覆盖新参数的缺陷，通过真实保存/重载回归确认并修复；旧材料保存到命令扩展，不参与执行，未知参数/快捷键继续保持。

## 未完成范围

本批独立目录数组格式46.0–46.3，旧目录字典通过已支持History后备恢复；QuickAccess原树格式44–46.3。实际两分支导出尚未验收，不能将合成格式回归外推为任意旧Profile兼容。

完整原差分序列化、旧效果层/预设、附属主题/播放列表/脚本、完整设置及高级格式继续待迁入。P5整体未完成。本轮未操作Windows、真机菜单或Finder，未进行正式发布。P3/P4已跳过项不重启；AX长期资源和NAS原生阻塞保持未关闭。

经验证自动本地提交并推送当前分支；实际Git结果在交付回复单独报告。
