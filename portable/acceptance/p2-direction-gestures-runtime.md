# P2 方向手势静默验收

- 专项：DirectionGestureTests 与 MouseInputTests，30通过，0失败/跳过。
- Headless使用正式窗口/查看器和真实来源/解码，覆盖右键方向释放、左键C终端、未知序列不退化右击、Escape与组合滚轮取消、真实导航及设置失败回滚。
- 全量自动回归340通过，0失败/跳过；构建、全量回归和本地签名见 p2-direction-gestures-validation.json。
- 离线设置截图：p2-direction-gestures-input-settings-layout.png。
- 未启动/激活正式应用、未发送系统键鼠、未访问用户Application Support。
- 原Windows动态、真机捕获/IME/触控板与用户验收待执行；未推送、公证或发布。
