# macOS 应用图标与开发包

日期：2026-10-07。分支 `feature/macos-port`，产品提交 `48fa6fb13dcee589ccfd1765aaf73511473af74e`。契约见[分发设计](../docs/p5-distribution.md)，匿名回执见[图标证据](p5-app-icon-evidence.json)。

## 修改

复用固定基线中的原 `AppList.targetsize-256.svg`，保留红色书籍图标与原 MIT 版权。用系统 sips/iconutil 从矢量直接生成十个标准/Retina PNG 表示并封装 `AppIcon.icns`，最大 1024 像素；没有新增运行时依赖。正式项目将 ICNS 装入 `Contents/Resources`，`CFBundleIconFile` 登记供系统读取。

第一次增量构建虽已复制图标，SDK 仍复用旧的编译 Info.plist，因此未算图标登记通过。现通过公开 `PartialAppManifest` 输入追踪原清单，重新构建后正式输出中的登记和资源均匹配；不换 bin/obj，不在打包时修改清单绕过构建。

## 验证

- 打包回归 20/20 通过，覆盖原许可/原生运行/替换恢复及新增图标登记、缺失、损坏和高分辨率资源。
- 默认目录串行 Release 构建通过，0 错误；保留既有命令行 RID 覆盖警告。完整自包含发布也通过。
- 系统 iconutil 解包和 AppKit 图像解码确认十个表示，16、32、64、128、256、512、1024 像素均存在；透明角及不透明正文成立。
- `NSWorkspace.icon(forFile:)` 在后台查询实际成品，系统返回原 NeeView 红色图标，已检查导出图像；未启动或聚焦应用。该证据不替代 Finder/Dock 前台交互验收。
- 成品及 ZIP 解包重定位后的图标指纹、全部 ARM64 Mach-O、strict/deep 签名、正式 exe 实际移动/日志释放均通过。

## 成品与边界

`portable/artifacts/NeeView.app`、`NeeView.zip`、`NeeView.report.json` 对应上述产品提交。ZIP SHA256：`a3713dde3cc1b4d1f16a812cd1d45c7903ace337b5f534479be2aa98e924e6d4`。图标 SHA256：`3e51ef114e9cb289a47e06d41a61f347edc774450a5d98afa85fc22da81e4620`。

自包含 ARM64，ad-hoc 开发签名；Developer ID、公证、Gatekeeper 和正式干净安装继续按此前决定延期。本批只涉及图标资源、构建清单与分发校验，原业务/设备验收结论及排除项见[最终收尾](p5-final-closure.md)。源码与脚本相对包提交无修改；原有验收截图/JSON 保持，不把全仓库脏标记改写为 clean。
