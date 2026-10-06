# P5 第十三批：PDF 静默验收

2026-10-06；起点 `8917402f1`，固定 Windows 源码 `c5c398d89`。沿原 Archive/Page/页框与唯一 BitmapFactory/JSON 链接入官方 CoreGraphics/PDFKit。全部合成隔离夹具，未激活正式 NeeView、Finder 或 Windows，未修改用户图片/Profile/NAS。

- 原 PDF 默认及三尺寸规则、JSON差分/未知字段/回滚、原目录关联Page、根/嵌套/显式页/历史恢复通过。
- 尺寸探测无渲染；正文直接像素、缩略规格和缓存复用、实际PNG提取、取消晚到及关闭释放通过。
- 独立复核发现导出流惰性初始化与关闭竞争，已修复；两项并发回归确认只编码一次和关闭等待活动读取，规则复核未发现确认缺陷。
- PDF/嵌套/设置搜索 **57项专项通过**。最终全量 **1280通过、0失败、2资源跳过，总计1282**；本批新增18项。两项跳过不计为真机通过。
- 官方 macOS 原生后台 `.app` **6项通过**：实际CropBox/旋转/日期/两层书签，BGRA/上下方向/白纸，来源/PNG/真实提取，嵌套内部定位，加密失败保留旧书，损坏/超限/取消和40轮重复释放。没有创建窗口。
- Engine、正式 Library 检查、默认 ARM64 `.app` 和 strict/deep 本地 ad-hoc 签名通过，串行默认 bin/obj。

[正式设置](p5-pdf-settings.png)、[正式Headless查看器](p5-pdf-viewer.png)及[真实原生像素](p5-pdf-native.png)已视觉检查。Headless查看器使用合成替换渲染器，实际系统像素由独立原生测试证明；两者不能混记为桌面设备验收。新增设置已进入现有搜索及唯一保存事务。

[串行验证](p5-pdf-validation.json)及[专项/复核证据](p5-pdf-evidence.json)。全量重复截图、JSON、TRX及原生XML归档于 `/Users/yuwk/.codex/artifacts/neeview/p5-pdf-20261006/final-full`，旧证据及用户 `.DS_Store` 保留。提交/推送以Git实际结果为准。

密码交互、SupportFileTypes执行、完整原设置、效果/动图/视频/脚本、真实两分支导出及正式分发继续待迁/待验。256MiB仅是输出缓冲预算，不涵盖PDF内部全部原生工作内存；重复释放测试不等于长期内存稳定。P3/P4用户跳过项和AX/NAS问题保持，P5整体未完成。
