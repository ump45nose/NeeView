# P2 第二十一批静默验收

正式源码/主题在Headless装载，临时PNG/中文ZIP与独立JSON夹具验证，不启动或激活正式NeeView，不注入系统键鼠，不读取用户Application Support。虚拟网格样本一万条目只实例化可见邻行，Normal不读封面，离屏/祖先隐藏/切模板释放显示，远端定位与方向键保持原条目。

Content/Banner及网格离线图用于模板结构/绘制核验。构建/自动回归/本地签名见p2-list-templates-validation.json；不代表真机触控板/IME/Retina/Finder/NAS或Windows动态原版一致性，长期内存及完整P95也未在此批宣称通过。

最终五步验证通过，全量328通过、0失败、0跳过；Engine、正式Library和ARM64.app构建及本地ad-hoc严格签名分别通过。旧历史菜单断言由禁用占位更新为四模板真实启用后重新运行完整验证，未更换默认输出目录。

源码复核发现的“相对归档封面应以父目录展开”未采纳：原FolderConfig.GetThumbnailFullPath明确Path.Combine(bookPath,target)，新增归档内部指定封面夹具保持原行为。P2继续浮动宿主、动画/手势反馈及资源性能审查，本批不封板、不推送、不公证或发布。
