# P2 开发收尾与静默验证

日期：2026-10-04。分支feature/macos-port；本地SDK /Users/yuwk/.local/share/neeview-dotnet/dotnet。固定Windows基线c5c398d89保持，GitHub账号ump45nose已核对。

## 交付状态

**P2开发范围完成；整体验收未封板。** 完成漫画阅读/归档、历史/书签、胶片条/导航、原输入配置/鼠标方向与组合、变换/动画、原侧栏组合/自动隐藏/浮动及资源收尾。原235条命令均保留，138个执行入口、97个能力占位，不以入口数量计算功能覆盖率。后续大量图片/连续/瀑布流、分类、完整导入/高级内容及发布不属于本轮。

## 最终串行验证

```sh
python3 portable/scripts/validate.py \
  --dotnet /Users/yuwk/.local/share/neeview-dotnet/dotnet \
  --macos-source --macos --phase p2-completion
```

| 检查 | 当前结果 |
|---|---|
| 源码出处/三个生产项目/Engine及前端依赖边界 | 通过，60个源码迁入登记/158个子集适配/26个固定上游依赖源码；数量不是覆盖率 |
| Engine构建 | 通过，0警告/错误 |
| 原规则/来源/持久化/资源/正式XAML Headless | 355通过、0失败、0跳过 |
| 正式入口Library编译检查 | 通过，0警告/错误；不是运行验收 |
| 正式ARM64.app构建 | 通过，0警告/错误 |
| codesign --verify --deep --strict | 本地ad-hoc签名通过；不是Developer ID/公证 |
| 原命令运行导出与manifest | 235条逐字段一致；138接入/97占位 |
| 正式Mac前台运行/Windows动态/用户新增交互 | 本轮未执行 |
| 推送/远端CI/Developer ID/公证/分发安装 | 未执行 |

原始输出：[五步验证](p2-completion-validation.json)、[命令导出](p2-completion-commands.json)、[缓存微测量](p2-completion-cache.json)、[完整软件帧与短期内存](p2-completion-render.json)。正式构建产物为portable/src/NeeView.MacOS/bin/Debug/net10.0-macos/NeeView.MacOS.app；RID子目录.app是SDK中间产物。

## 视觉与资源证据

离线检查[正式主窗口](p2-completion-window-layout.png)、[输入设置](p2-completion-input-settings-layout.png)、[导航器浮窗](p2-completion-floating-layout.png)及[固定4K样本](p2-completion-fixed-dataset-layout.png)。原顶部八组/地址栏、左右图标和面板、中央查看器、底部滑条/状态区域保持；方向设置字段没有裁切。截图由正式视图与主题的Headless软件渲染生成，不是Windows像素对照或真机交互。

资源优化前后、样本方法及限制见[资源设计记录](../docs/p2-resources.md)。主图缓存按8MiB测试预算保持，工厂Dispose归零；同帧30次刷新不重复创建显示缓冲/解码。软件完整帧P95和短期测试进程RSS不当作Retina/屏幕P95或长期native验收。

## 静默边界与待验

遵循[验证流程](../docs/validation-workflow.md)：本轮不启动/激活正式NeeView、不发系统键鼠/拖放/全屏、不读取或写入用户Application Support。全部来源/JSON/图像夹具由测试在临时目录生成并清理，默认bin/obj串行使用；旧截图、Windows源码和用户portable/.DS_Store保留。用户切换应用不影响这类后台验证。

早期P1/P2正式Mac运行材料继续独立保留，不能用作本轮手势/浮窗/动画已通过的证据。真实鼠标/触控板/IME/焦点/捕获/Retina/多屏、Finder双击/拖放/定位、NAS及异常来源、长期native与屏幕性能、Windows动态和用户新增交互仍待集中验收。静默验证不将这些项目标记通过。

本批自动本地提交；提交号以Git日志为准，没有推送或发布。P2开发收尾后，P3按独立阶段继续设计与实施，不在本轮自动扩展范围。
