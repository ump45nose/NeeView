# P2 Windows 动态验收：连接排查记录

日期：2026-10-04。用户明确允许开始前台验收，并表示目标 Windows 已打开和聚焦 NeeView。本记录只覆盖 Mac 远程桌面客户端与传输入口；没有进入目标桌面，不能视为 NeeView 动态验收。

## 已观察结果

| 层级 | 证据 | 状态 |
|---|---|---|
| Windows 传输入口 | 指定目标的 TCP 3389 可达；22 超时 | RDP 入口可达，SSH 不可用 |
| RDP 服务协议 | 发送标准 X.224/RDP 协商请求，收到 19 字节 Connection Confirm；Negotiation Response type=2、selectedProtocol=2（HYBRID/NLA） | 服务能够响应 RDP 协商；未进行 TLS/凭据认证 |
| Mac 客户端 | Windows App 11.4.3 / build 3115；新建专用连接，无网关、按需询问凭据，关闭全屏启动及窗口调整时改变分辨率 | 配置已保存；没有保存密码 |
| 连接尝试 | 已核对保存地址；客户端正常重启后重试；通过正常粘贴重新输入地址及显式 3389 端口后重试 | 仍提示 0x104，尚未出现凭据界面 |
| 系统本地网络界面 | 本地网络列表显示 17 项，没有 Windows App 条目 | 未观察到该应用的关闭开关或实际授权提示；不能断言权限被拒绝 |
| Windows 当前应用 | 尚未读取远程桌面、安装包版本或 Profile | 原版基线、阅读、菜单和侧栏均未验 |

0x104 原文包含“无法连接到远程电脑，因为找不到该电脑”及检查本地网络访问的提示。这是通用客户端提示；现有证据没有给出底层 socket/TCC 原因。RDP 协商成功也不证明登录或桌面会话成功。

## 独立的客户端崩溃

首次配置只包含生成样本的共享目录时，通过 AX.setValue 编辑目录显示名称，Windows App 意外退出；随后基础连接不启用文件夹共享，仍发生上述 0x104。

本机 DiagnosticReports 的 2026-10-04 10:59:54 +0800 记录显示 EXC_CRASH / SIGABRT、swift_dynamicCastFailure、EditBookmarkViewController.controlTextDidChange(_:) 及 NSAccessibility 设置属性栈。该证据支持“客户端编辑 UI 的类型转换崩溃”，不支持把崩溃判为 RDP 连接错误的原因。未发送 Microsoft 故障报告，未读取客户端凭据数据库。

## 当前边界与下一步

- 操作限于 Mac 的 Windows App 和系统权限界面的只读查看。没有启动 Mac NeeView，没有进入 Windows 桌面，没有发送远程应用输入，没有读取或修改 NeeView 的现有书籍/配置/历史/书签。
- 共享目录配置没有保存成功；最终专用连接不共享 Mac 文件夹、不保存密码。密码尚未用于认证。
- 等待用户通过普通操作手动打开专用连接，确认是否出现权限提示、凭据界面或同样错误。新的权限提示按实际内容处理，不通过修改 TCC、签名或安全配置绕过。
- 若普通操作能够连接，再核对参考程序版本、实际 DPI/桌面、用户现有实例和数据隔离后执行[共用验收用例](../docs/p2-device-acceptance.md)。如仍不能连接，先解决客户端入口；动态用例保持未执行。

资料核查：[微软 Windows App 概述](https://learn.microsoft.com/en-us/windows-app/overview)、[微软 macOS 客户端说明](https://learn.microsoft.com/en-us/windows-server/remote/remote-desktop-services/clients/remote-desktop-mac)、[Apple 本地网络权限说明](https://support.apple.com/guide/mac-help/control-access-local-network-mchla4f49138/mac)。这些页面不提供当前 0x104 的具体根因，也不证明列表缺少条目等于权限关闭。

本增量没有产品源码变化，不重复产品构建/自动测试；仅提交排查记录，不推送或发布。P2 整体验收仍未封板。
