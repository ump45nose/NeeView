# P2 第二十批静默运行记录

本批未启动/激活正式应用、未注入系统键鼠、未访问用户Application Support。正式历史设置/菜单/启动生命周期由Avalonia Headless与临时PNG/CBZ/JSON运行。

最终全量320通过、0失败、0跳过；Engine、正式Library、ARM64应用及本地ad-hoc严格签名五步通过，见p2-history-policy-validation.json。新增13项策略/清理回归；修正旧删除后永久抑制断言，保持独立LastBook恢复；强制日期测试基准放在切书保存完成后。

离线查看history-settings-layout：保存开关、原阈值、日期选项及保存失败草稿/重试结构正常，剩余字段在可滚动区域。来源检查覆盖确定缺失、权限/超时/取消、离线卷、归档内部目录/条目、未知Windows路径；旧快照晚到不能删除新进度。关闭历史在四JSON事务内删除文件，失败/中断恢复测试通过。

嵌套历史策略随P5后端接入；Headless不替代Windows动态、真机Retina/输入、真实NAS及长期内存/完整帧P95。P2继续模板、浮动宿主与动画。本地提交后继续，不推送发布。
