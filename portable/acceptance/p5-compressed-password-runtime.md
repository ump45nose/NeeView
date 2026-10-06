# P5 第十六批：压缩密码静默验收

2026-10-06；起点 `7ae9677a8`，固定 Windows 源码基线 `c5c398d89`。八类公开/合成密码夹具只操作隔离副本，不使用用户凭据、图片或 Profile，不激活正式窗口。

- 根和嵌套实际 ZIP AES/PKWARE、RAR4/5 内容/加密头及 7z LZMA/LZMA2 AES：错误→正确、实际图片读取、反向/重复读取、缓存、逻辑定位、失败代理清理及原包哈希通过。
- 取消/迟到输入保持旧书、普通坏包不弹密码、加密 ZIP 禁止删除通过。正式拥有弹窗实际重试后定位 `jpg/test.jpg`，沿原 ReaderView 绘制；主线程检查[图片截图](p5-compressed-password-reader.png)。首张目录卡片不能作为图片显示证明，已修正用例。
- 新增20项；最终 **1318通过、0失败、2资源跳过，总计1320**。51相关专项通过，截图加强后单项复验通过。**10原生后台测试通过**。
- Engine、正式 Library、默认目录 ARM64 `.app` 及 strict/deep 本地 ad-hoc 签名通过，产品构建0警告；不是 Developer ID/公证/安装验收。

初次测试暴露 SharpCompress 加密 RAR 的 Volume 查询顺序问题：RAR4 正确口令仍失败、RAR5 空索引，曾导致挂起；停止本轮测试主机、归档失败证据，以条目索引先行修复。真实根八项、嵌套及正式窗口专项、最终全量均通过。外部探针只作为故障定位，不能代替正式验收。证据/TRX/XML及重复材料在 `/Users/yuwk/.codex/artifacts/neeview/p5-compressed-password-20261006`。

缓存成功点为索引及首个非空加密条目真实验证，不是全包或画面完成。RAR4/7z 的数据损坏与错密码可能不可区分，使用歧义提示；普通坏包不伪装密码失败。7z公开CRC=0、加密头专门夹具及同包多口令未验，多卷仍不支持。加密ZIP只读，普通ZIP删除不变。

[契约](../docs/p5-compressed-password.md)、[步骤](p5-compressed-password-validation.json)、[证据](p5-compressed-password-evidence.json)。效果/动图/视频/脚本、完整原设置、真实导出及正式分发继续推进；P3/P4用户跳过项和AX/NAS问题保持，P5整体未完成。
