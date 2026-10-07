namespace NeeView;

/// <summary>独立Mac发布的检查结果；检查版本和打开页面不包含安装行为。</summary>
public enum ApplicationReleaseStatus { NoRelease, NoMacPackage, Current, Available }

/// <summary>经过来源与平台校验的版本信息，界面不解析远端JSON或猜测下载地址。</summary>
public sealed record ApplicationRelease(ApplicationReleaseStatus Status, string? Version = null, Uri? Page = null, Uri? Download = null);

/// <summary>替换原VersionChecker的网络入口；由启动层注入，取消关闭后不得发布旧结果。</summary>
public interface IApplicationReleaseService
{
    /// <param name="currentVersion">当前Mac产品版本，不使用Windows基线版本。</param>
    /// <param name="token">窗口关闭或用户取消检查。</param>
    /// <returns>真实发布状态；网络/格式失败通过异常报告。</returns>
    Task<ApplicationRelease> CheckAsync(string currentVersion, CancellationToken token);
}
