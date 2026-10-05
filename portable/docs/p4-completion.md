# P4 收尾：原删除、图片接收与 Mac 链接

## 职责、依赖与出处

保留固定基线 `c5c398d89` 的 BookPageActionControl/PageFileIO、Archive.CanDelete/DeleteAsync、ZipArchive、PlaylistArchive 与 ContentDropReceiver 调用关系。Engine定义来源删除及有限数据接收契约，原BookOperation协调，Backends替换系统/解码/文件能力，Mac视图只取快照和确认。不新增项目、阅读内核、数据库或通用事务框架。

## 删除契约与业务规则

- 主页 DeleteFile 只删除当前主页面；瀑布要求显式选择。页面列表的 Delete/右键删除使用显式多选，不扩大主菜单范围。
- 普通目录项进入系统废纸篓，包含目录/非图片/空目录；目录内链接目标保留。部分失败返回实际成功项，只协调这些页面。
- 播放列表删除原登记项，不删除引用目标；相同路径的不同别名仍独立。保留未知字段，确认前后检测外部编辑，原子保存。
- ZIP/CBZ可删除条目及目录子项，保留原条目ID与幸存Page。Archive.Zip.IsFileWriteAccessEnabled沿原配置，默认false，并受System.IsFileWriteAccessEnabled约束。无论全局确认开关如何，归档条目始终提示永久删除。RAR/7z只读。
- ZIP在同目录流式重建，以80KiB缓冲避免Update模式载入整包；保存归档/条目注释、时间与外部属性。提交前校验索引和完整文件指纹、检查取消，再原子替换并重映射物理ID。
- 混合删除类型拒绝；来源/页面/代次/位置和实际元数据确认后再次核对。已授权操作由关闭流程等待真实结果；未确认可取消。成功后更新唯一SourcePages、页框、缓存及原JSON，保存失败可重试。

## 粘贴与拖放契约

FileClipboardContent可携带有限ContentDropData字节/HTML/URL，没有原生对象或控件。QueryPath优先，普通文件地址优先；浏览器来源按内联图片、已提供本机路径的文件副本、HTTP(S)图片、位图尝试。一般读取/下载/编码失败清理该阶段，再按原逻辑回退；用户取消和预算超限立即停止。Paste和Drop进入同一原OpenCore，代次检查防止晚到覆盖新书。

NSPasteboard使用标准png/tiff/jpeg/html/url；原生对象只在主线程创建/释放，ChangeCount不一致拒绝快照。Avalonia快照借用发送者Bitmap并复制编码字节，不Dispose发送者资源。普通文本不会被当作文件或URL。

每批最多16个材料，单项64MiB、批次128MiB、进程512MiB，HTML1MiB，网络30秒。位图先检查像素预算，沿原Bgr32语义关闭alpha并保存PNG；内联/下载保留真实编码。失败/取消清理批次，成功资源由进程持有供历史导航，关窗/切书不清理，正常退出释放。

## Mac链接与系统边界

符号链接复制保留LinkTarget文字，移动/重命名/废纸篓操作链接本身；树内链接不跟随。原指纹/覆盖副本/journal复用，链接使用L:指纹，旧普通文件日志仍拒绝链接替换。覆盖必须确认，UndoRedo核验源和恢复副本，外部变化保留材料/栈顶；目录链接用Directory.Move做目录项改名，File.Move不能处理该类型。根链接保护解析父目录，Profile/卷根保护仍生效。

Finder别名通过Foundation确认类型并解析目标，WithoutUI/WithoutMounting，无弹窗/自动挂载，循环最多8层。打开别名后的当前书是实际目标；复制/删除别名文件时不把解析目标作为文件操作对象。

Windows.lnk及FileContents/FileGroupDescriptorW是Windows专属协议，不在Mac模拟COM。Mac提供标准文件/URL/图片即可接收；只有未实体化file promise而无标准替代数据时明确提示，专门NSFilePromiseReceiver为后续系统扩展，不冒充已支持。CutFile/CutBook保持用户决定的禁用占位；移动继续使用分类/移至文件夹。归档内部目录递归提取是原版TODO；完整目录树文件管理、P5旧数据导入与高级内容不扩大进本批。

## 生命周期、错误与测试

来源归Book；读取流按请求释放；成功接收材料归进程；页面/缩略显示租约仍归唯一BitmapFactory与查看器。没有新状态权威。错误区分缺失/权限/外部变化/不支持/预算/取消，实际文件成功与JSON失败分别回报。

P4CompletionTests覆盖部分删除、列表重复别名/未知字段/外部改动、ZIP确认/配置/目录/幸存数据、图片alpha/失败回退/取消、关闭授权边界。P4LinkTests覆盖链接分类覆盖UndoRedo、根目录链接复制/移动/改名、指纹变化/中断/树清理；P4DropSnapshotTests装载正式Avalonia适配，验证文本、QueryPath、URL及借用Bitmap。原分类/整书/复制/删除/配置/Headless测试一并回归。实际结果和设备待项见[本轮记录](../acceptance/p4-completion-runtime.md)。

扩展继续使用原Archive、BookOperation、平台替换点，公开契约变化同步文档与调用方；性能优化必须测量与行为回归。
