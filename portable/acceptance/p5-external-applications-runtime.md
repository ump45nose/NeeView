# P5 第二十五批：原外部应用静默验收

2026-10-06，固定 Windows 源码基线 c5c398d89。原三命令、应用集合/五字段、页组顺序、原路径占位符与整书策略接入唯一 BookOperation、归档与原 JSON。

- 22 项专项、1464 项全量通过，0 失败，2 项需显式资源目录的测试跳过。
- 22 项真实 macOS 后台测试通过；新增4项以合成可执行文件和 .app 核验真实 argv、工作目录、取消/错误与字面参数。
- Engine、正式 Library、默认目录 ARM64 .app 构建及 strict/deep 本地 ad-hoc 签名通过。
- 正式 Headless 集合重排、Index0选择菜单、父设置失败/重试通过；[设置截图](p5-external-applications-layout.png)已检查。

第一次原生测试仅因 /var 与系统解析后的 /private/var 比较失败；改用现有实际路径能力核对同一工作目录后完整串行复验通过。初次与最终日志、全量派生截图在 /Users/yuwk/.codex/artifacts/neeview/ 保留。

请求保持来源至准备/提交完成。随机外部材料成功或部分成功后保留至进程退出；剪贴板与外部材料共享2GiB提取预算，零字节租约与清理失败继续计费。原 {Uri}/$Uri 是编码路径，不重新解释为file URL。Windows程序路径不执行兼容层。

原235命令正常宿主195入口、接入ImportBackup后196入口/39占位，数量不代表功能覆盖率。未激活正式应用、改用户图片/Profile或真实系统剪贴板；真实第三方应用、默认关联/URL及Windows动态另验。Developer ID/公证/干净安装未执行；P5整体尚未完成，P3/P4跳过及已知问题保持。
