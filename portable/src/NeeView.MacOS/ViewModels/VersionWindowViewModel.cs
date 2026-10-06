// Copyright (c) NeeLaboratory. 原VersionWindowViewModel的名称、版本、复制和链接表现适配。
using System.Reflection;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView.MacOS.ViewModels;

/// <summary>只呈现实际Mac构建元数据，系统动作经注入契约，不读取Profile或改变阅读。</summary>
public sealed class VersionWindowViewModel(IPlatformService platform, Func<string, CancellationToken, Task> copyText,
    Uri? license = null) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _closed = new();
    private bool _disposed, _busy;
    private string _message = "";
    private static Assembly Assembly => typeof(BookOperation).Assembly;
    public string ApplicationName => "NeeView Mac";
    public string DisplayVersion => Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetName().Version?.ToString(3) ?? "版本未知";
    public string BuildVersion => Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? DisplayVersion;
    public string VersionNote => $"{ApplicationName} {DisplayVersion}\nBuild: {BuildVersion}\nWindows baseline: c5c398d89\n{RuntimeInformation.FrameworkDescription}\n{RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})";
    public Uri LicenseUri { get; } = license ?? new Uri(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources", "LICENSE.md")));
    public Uri ProjectUri { get; } = new("https://github.com/ump45nose/NeeView");
    public Uri OriginalProjectUri { get; } = new("https://neelabo.github.io/NeeView");
    // 原检查器只发布Windows包；独立Mac尚无对应release，不提示安装Windows更新。
    public string UpdateStatus => "Mac 更新检查尚未接入";
    public bool IsCheckerEnabled => false;
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); OnPropertyChanged(nameof(CanAct)); } }
    public bool CanAct => !_disposed && !IsBusy;
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    /// <summary>原复制完整VersionNote；系统剪贴板失败可重试。</summary>
    public Task CopyVersionAsync() => RunAsync(token => copyText(VersionNote, token));
    /// <summary>原许可/项目链接；布局和主题不参与地址解析或系统启动。</summary>
    public Task OpenLicenseAsync() => RunAsync(token => platform.OpenUriAsync(LicenseUri, token));
    public Task OpenProjectAsync() => RunAsync(token => platform.OpenUriAsync(ProjectUri, token));
    public Task OpenOriginalProjectAsync() => RunAsync(token => platform.OpenUriAsync(OriginalProjectUri, token));
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (!CanAct) return;
        IsBusy = true; Message = "";
        try { _closed.Token.ThrowIfCancellationRequested(); await action(_closed.Token); }
        catch (OperationCanceledException) when (_disposed || _closed.IsCancellationRequested) { }
        catch (Exception error) { if (!_disposed) Message = "操作失败：" + error.Message; }
        finally { if (!_disposed) IsBusy = false; }
    }
    /// <summary>关闭后取消尚未提交动作，晚到结果不更新已关闭窗口。</summary>
    public void Dispose() { if (_disposed) return; _disposed = true; _closed.Cancel(); _closed.Dispose(); OnPropertyChanged(nameof(CanAct)); }
}
