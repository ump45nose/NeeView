# P5：原图片元数据、评级搜索与信息面板

## 职责、依赖及出处

Engine迁入原BitmapMetadataKey/Accessor/Database、完整Exif值类型、分数、FormatValue、InformationKey/Group和InformationConfig。PageMetadataTools保留原标准字段优先、评分和格式规则；以同一Page的纯PagePictureInfo替换WPF PictureInfo。Backends按原MetadataExtractor 2.9.3读取完整IFD0/SubIFD/GPS/XMP映射，补充原WIC扩展标签枚举的替换实现；不复制读取业务到视图。

原文件及固定SHA256见source-migration.json。没有新增生产项目、内容身份、状态数据库或第二来源。

## 契约及业务规则

IArchiveFactory.ReadImageMetadataAsync接收原ArchiveEntry，返回方向校正尺寸、DPI、原格式深度信息、原元数据字典及实际后端名。Page.LoadPictureInfoAsync惰性合并同页请求；非图片不读取，失败/取消不缓存。PDF继续使用原阅读探测尺寸，不将虚拟页作为普通EXIF文件。

IFD0 Rating优先XMP Rating，没有评分为0；原ExifRating.ToInteger与五星显示分别使用。原全部映射分支/日期/分数/GPS/枚举别名保留。标准InformationKey优先额外标签；额外键删除空格并使用固定小写，归一化碰撞保留首项，其他原标签仍通过限定名保留。/title、/subject、/tags、/comments、/rating及/p.meta.[key]进入既有Searcher和同一来源页集合；后台读取失败/取消不提交过滤集合。

信息配置沿原Information分支、八组默认显隐、原日期/地图raw-null字段及差分JSON。PropertyHeaderWidth保留原GridLength字符串（128/Auto/星号）；Avalonia表现解释单位，Engine不引用框架。未知字段合并保留，设置失败沿现有事务回滚。

## 状态及资源生命周期

流属于一次真实后台请求，来源仍属于书籍。读取独立两槽，32MiB实际读取字节预算；可定位流直接读取，跳过像素不复制整张图片。等待十五秒超时/取消后，晚到工作仍占槽并关闭流；元数据不解码像素。纯信息进程缓存最多256条及16MiB估算值，只持有纯结果；Page以弱引用访问，不把整个书籍或来源带入缓存。该估算不是进程RSS或第三方解析器工作内存的硬上限。

信息表现有独立选择/代次/取消源，隐藏或换页取消旧需求，旧结果不能更新新选择。缩略图使用现有可见ListCoverImage/BitmapFactory，不解码全部页。关闭等待已确认的配置/系统动作后释放书籍，晚到加载不发布。

正式退出显式销毁信息面板的封面控件，等待其已开始的加载并归还显示租约，再销毁图像工厂。原排序入口在导航锁内等待后台筛选，避免评级/元数据搜索阻塞UI。历史展示行按原路径原地更新访问时间和页名，后台阅读保存保持选择、焦点与封面身份。

## 界面、错误及测试

原顶部当前页缩略选择、下方属性分组与更多菜单保留在原信息插槽；字段只读文本隔离阅读/数字输入，列宽拖动保存到同一配置。文件夹与地图动作进入同一平台契约。XAML、独立FileInformationViewModel及主题可调整，不改变字段/搜索算法。设置窗口新增信息页并进入原结构化设置搜索和草稿事务。

损坏/不支持元数据保留可探测尺寸并显示警告；读预算、权限、超时、取消、来源关闭分别报告。标准信息与扩展标签不写回用户图片。

隔离真实JPEG/PNG/CBZ验证EXIF与XMP优先级、方向、标签、评分搜索及同Page位置、原JSON/未知字段/失败回滚、损坏后重试、取消晚到/新选择、正式XAML和设置草稿。Windows动态字段/真实Finder或地图、NAS断线和长期资源另验。Headless不是这些设备项目的通过证据。

## 扩展点

脚本Information accessor复用PageMetadataTools.GetValueStringMap；格式后端可替换而不改Page/搜索/信息配置。完整PDF元信息、原信息面板上下文文件管理的全部宿主动作继续按原能力清单核对，不把本批字段接通称完整P5完成。
