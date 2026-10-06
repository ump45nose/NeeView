# P5 第十一批：原设置搜索静默验收

日期2026-10-06；起点 `b11951cf0`，固定Windows源码基线 `c5c398d89`。三个生产项目、原JSON、唯一表单保存和阅读链保持。使用隔离合成夹具及正式Headless，没有激活NeeView/Finder、修改用户图片/Profile或Windows/NAS。

## 本批通过

- 原设置Page/Section/Item文本关系及默认Searcher结构化语法迁入，解析失败不退化为全部匹配。现有九页所有命名编辑项、全部235命令和已迁参数文案均进入搜索，未迁命令/ClearType仍保持原禁用资格。
- 结果直接编辑同一个控件或ShortcutEdit。多轮搜索/清空/导航恢复同一草稿、原父级/顺序和DataContext；字体/主题/历史TwoWay值没有脱离后null覆盖。取消保持权威JSON字节；保存失败回滚运行值，结果页草稿可直接重试。
- 正式参数按钮可打开原参数编辑弹窗：弹窗取消不应用，确认进入owner共享草稿，父设置保存后正反命令共用结果。
- 原设置搜索历史仅窗口内，明确确认才追加、去重/裁剪/删除，关闭重开为空，不改History.json。坏语法保留上次有效结果；清空与500ms合并/关闭晚到均覆盖。
- 16项新增专项通过。最终全量 **1239通过、0失败、2资源跳过，总计1241**。两项挂载资源测试本轮未重跑，不继承为此次资源验收。
- Engine、正式macOS Library、默认ARM64 `.app` 和strict/deep本地ad-hoc签名通过。串行构建，始终使用默认bin/obj。

第一轮全量通过后，正式Library发现两处代码Binding裁剪检查IL2026失败；改为现有XAML绑定和独立输入DataContext后重新全量/正式构建通过。失败记录保留于仓库外first-full目录。独立只读复核未发现确认缺陷；后续变化为XAML绑定、空查询返回、辅助标签及保存中拒绝查询，均进入最终回归。

[字段结果](p5-settings-search-fields.png)和[命令结果](p5-settings-search-command.png)已检查，保持原左导航/搜索、右编辑和底部保存/取消结构。正文未参与搜索或增加解码。

[串行验证](p5-settings-search-validation.json)、[专项/复核证据](p5-settings-search-evidence.json)。全量TRX、重复旧用例截图/JSON归档 `/Users/yuwk/.codex/artifacts/neeview/p5-settings-search-20261006/final-full`；用户 `.DS_Store` 保留。提交/推送单独核对。

## 边界

完整原设置页面、实际效果/高级媒体/脚本、用户两分类分支真实导出、许可清单及正式Developer ID/公证/安装继续待迁/待验。本批覆盖当前实际表单搜索，不声明完整P5完成或Windows动态一致；本地ad-hoc签名不等于分发通过。此前P3/P4用户跳过项及AX/NAS问题保持。
