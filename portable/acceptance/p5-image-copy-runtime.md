# P5 第二十六批：原图像复制静默验收

2026-10-06，固定 Windows 源码基线 c5c398d89。原 CopyImage 首元素语义与完整解码图像源进入唯一 ReaderView/BitmapFactory，系统适配写入 public.png。

- 12 项专项、1475 项全量通过，0 失败，2 项需显式资源目录的测试跳过。
- 26 项真实 macOS 后台测试通过，新增3项命名 NSPasteboard 图像测试，不污染用户 GeneralPasteboard。
- Engine、正式 Library、默认目录 ARM64 .app 构建与 strict/deep 本地 ad-hoc 签名通过。
- 目录/ZIP、双页方向、分割与透明、旋转背景隔离、失败重试、单槽、切书/关闭、动画禁用、瀑布显式选择和真实菜单/快捷键通过。

原生首轮外部应用探针在文件刚创建时读取到空内容；改为写临时文件后原子发布，并验证等待完成，完整原生复验通过。全量 Engine 未重复运行；失败日志、原生复验和派生截图保留在 /Users/yuwk/.codex/artifacts/neeview/。

正常宿主196入口，接入 ImportBackup 后197入口/38占位；数量不代表功能覆盖率。复制不应用裁剪/变换/背景，保持现有解码尺寸与透明 alpha，不宣称强制原分辨率。后台编码保留显示租约；ClearContents 为系统提交点，清空/写入同一主线程回调完成；关闭等待真实任务。

未激活正式应用或修改用户图片/Profile；真实通用剪贴板跨应用粘贴及 Windows 动态另验，Developer ID/公证/干净安装未执行。P5整体未完成。
