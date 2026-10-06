# P5 第二十三批：原默认阅读设置与文件权限

## 职责、依赖与出处

固定基线 c5c398d89 的 SetDefaultPageSetting、BookSettingConfigExtensions.CopyTo/ToBookMemento、Book.Setting_PropertyChanged、PageFrameBoxContext 设置订阅与 TogglePermitFile。Engine 保留原十二字段及唯一阅读/JSON链；MainWindow仅转交菜单上下文和更新能力/勾选。不新增状态模型、文件内核或生产项目。

## 契约与业务规则

SetDefaultPageSetting 非加载时把默认十二字段复制到当前设置，包括非序列化的Page字段；普通变化保持Config.BookSetting与Book.Setting对象和来源引用，排序后按原Page重建页框。实际字段变化按原PageFrameBoxContext调用RequestSaveBookMemento(false)，允许更新移除的历史；相同值不改变历史登记条件。原Page不是永久数字页码，也不在默认复制时重新跳首图。

递归字段变化对应原DirtyBook重收集：独立默认BookMemento携带当前路径/条目/随机种子和搜索，走唯一OpenCoreAsync。新来源准备成功后才替换旧书；失败保留旧设置/来源并报告可重试错误。更新的打开或退出使旧重收集失效，晚到任务不能覆盖新书；被抢占不显示旧来源失败。

TogglePermitFile修改原System.IsFileWriteAccessEnabled。菜单取反；键盘/手势读取原On/Off/Toggle参数。它是应用全局开关，始终保留入口，不代替文件系统实际权限。开关成功后菜单重新查询删除/改名/分类等真实目标资格，不导航、不刷新正文；归档ZIP独立权限保持。

## 状态、生命周期与错误

两命令进入原导航锁及唯一SaveData事务，取消未提交防抖并等待在途写入。保存失败恢复设置、随机种子、位置/页框，以及完整历史计数/登记/移除抑制。权限保存失败恢复原开关，未知JSON字段与差分默认保持。普通复制无书也可保存默认；加载或已关闭上下文拒绝操作。

## 测试、验收与扩展

11项专项覆盖十二字段/引用、两方向与单双页、排序定位、无书保存、递归失败/重试、新打开抢占、相同值/实际变化历史、保存失败恢复及菜单/快捷键/删除资格/正文不刷新。固定源码对照包含原设置事件订阅，不能只比较命令Execute单行。

正式Headless使用隔离图片及Profile，不启动正式窗口、不操作用户图片。构建、全量和签名结果见[静默验收](../acceptance/p5-default-settings-runtime.md)。原系统文件权限、Windows动态及P5其余能力继续独立验收。
