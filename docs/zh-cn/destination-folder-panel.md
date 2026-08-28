# NeeView 快速分类薄 Fork

此版本只新增目标文件夹面板、移动历史与快捷分类脚本，不修改图片渲染、解码、缓存或归档读取链路。

## 原版 46.3 脚本

将 `SampleScripts/MoveToDestination1.nvjs` 至 `MoveToDestination9.nvjs` 放入 NeeView 的脚本目录。Store 版可在“选项 > 脚本 > 打开脚本文件夹”确认实际目录。

数字键 `1` 至 `9` 始终对应 Destination Folders 当前顺序的前九项。目录改名或换路径无需修改脚本；调整目录顺序会同步改变数字映射。如果数字键已被其他命令占用，请先在命令或脚本设置中解决快捷键冲突。

脚本和面板只移动直接从普通文件夹打开的真实图片。压缩包、PDF、播放列表、媒体和快捷方式页面会被拒绝。`MultiPagePolicy` 固定为 `Once`，因此每次只移动主当前图片；文件监视与页面校验流程负责继续显示下一张。

## 面板与撤销/重做

`DestinationFolderPanel` 默认出现在右侧，可拖动、浮动、隐藏。面板显示前九个 Destination Folders，单击一行即可移动当前主图片；底部可打开目标目录管理器，也可执行撤销和重做。

全局命令 `UndoDestinationMove` 和 `RedoDestinationMove` 默认快捷键分别为 `Ctrl+Z`、`Ctrl+Y`，可在普通命令设置中修改。历史仅保存在当前会话内，最多记录 300 次成功文件移动；退出后清空。取消、失败、源文件被外部移动或取消覆盖时，历史栈顶保持不变，处理问题后可以重试。

撤销时若原路径已有同名文件，会明确询问“覆盖/取消”，不会静默覆盖。若用户仍在原文件夹浏览，成功撤销后会重新加载并定位恢复的图片；若已切换到其他位置，则只恢复文件并提示。

## 独立 Profile 与迁移

x64 ZIP 使用程序目录旁的 `Profile` 文件夹，与 Store 版配置完全隔离。不要修改或替换 `C:\Program Files\WindowsApps` 下的任何文件。

如需迁移现有设置，请先在 Store 版中“导出全部设置”为 `.nvzip`，再在 fork 中导入一次。导入后两套 Profile 不再共享，后续修改互不覆盖。

## 构建与打包

在仓库根目录执行：

```powershell
.\MakePackage\BuildDestinationForkPackage.ps1
```

脚本使用官方 x64 发布配置，验证九个脚本的数字映射，复制中文说明和脚本到便携 Profile，并在 `MakePackage\Deliverables` 生成 ZIP 与 SHA-256 文件。
