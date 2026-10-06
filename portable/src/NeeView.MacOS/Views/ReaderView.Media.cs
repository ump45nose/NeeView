using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
namespace NeeView.MacOS.Views;

public sealed partial class ReaderView
{
    private sealed class Animation(Page page, DecodeRequest request, AnimatedBitmapLease source, System.ComponentModel.PropertyChangedEventHandler notify) : IDisposable
    {
        public Page Page => page;
        public DecodeRequest Request => request;
        public long Length { get; } = page.ArchiveEntry.Length;
        public DateTime Version { get; } = page.ArchiveEntry.LastWriteTime;
        public AnimatedBitmapLease Source => source;
        public AnimatedMediaPlayer Player { get; } = new(source.Info) { IsRepeat = Config.Current.Image.IsMediaRepeat };
        public CancellationTokenSource Cancellation { get; } = new();
        public CancellationToken Token => _token ??= Cancellation.Token;
        private CancellationToken? _token;
        public Display? Display;
        public int DisplayFrame = -1;
        public void Dispose() { Cancellation.Cancel(); Player.PropertyChanged -= notify; Player.Dispose(); Display?.Dispose(); Display = null; Source.Dispose(); Cancellation.Dispose(); }
    }
    private readonly Dictionary<Page, Animation> _animations = [];
    private readonly Dictionary<Page,(DecodeRequest Request,long Length,DateTime Version)> _animationAttempts = [];
    private readonly DispatcherTimer _mediaTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private bool _updatingMedia, _pendingMediaUpdate;
    private long _lastMediaTick;
    internal int AnimationCount => _animations.Count;
    public event EventHandler? MediaChanged;
    private Display GetMediaDisplay(Page page, Display fallback) => _animations.GetValueOrDefault(page)?.Display ?? fallback;
    /// <summary>只保留当前可见原页，静态判定按页面/规格/版本复用；浏览面板继续使用首帧。</summary>
    private void ReconcileAnimations(IReadOnlyList<Page> pages)
    {
        foreach (var page in _animations.Keys.Where(p => !pages.Contains(p) || !Config.Current.Image.Standard.IsAnimationEnabled(p.EntryName)).ToArray())
        { _animations[page].Dispose(); _animations.Remove(page); }
        foreach (var page in _animationAttempts.Keys.Where(p => !pages.Contains(p) || !Config.Current.Image.Standard.IsAnimationEnabled(p.EntryName)).ToArray()) _animationAttempts.Remove(page);
        SelectCurrentMedia();
    }
    /// <summary>沿现有工厂打开真实动画，等待/晚到/切书结果使用查看器原revision裁决。</summary>
    private async Task EnsureAnimationAsync(Page page, DecodeRequest request, int revision, CancellationToken token)
    {
        if (_factory is null || !page.IsImage || !Config.Current.Image.Standard.IsAnimationEnabled(page.EntryName)) return;
        var version = (request, page.ArchiveEntry.Length, page.ArchiveEntry.LastWriteTime);
        if (_animations.TryGetValue(page, out var existing))
        {
            if ((existing.Request, existing.Length, existing.Version) == version) { existing.Player.IsRepeat = Config.Current.Image.IsMediaRepeat; return; }
            existing.Dispose(); _animations.Remove(page); SelectCurrentMedia();
        }
        if (_animationAttempts.GetValueOrDefault(page) == version) return;
        AnimatedBitmapLease? source = null; Animation? animation = null;
        try
        {
            source = await _factory.OpenAnimationAsync(page, request, token);
            if (_disposed || revision != _revision || token.IsCancellationRequested) return;
            if (source is null) { _animationAttempts[page] = version; return; }
            animation = new(page, request, source, MediaPlayerChanged); source = null;
            animation.Player.PropertyChanged += MediaPlayerChanged;
            _ = animation.Token; // 关闭后仍在等待的帧使用已取得的token，不再访问已释放CTS.Token。
            var first = await animation.Source.ReadFrameAsync(0, token);
            if (_disposed || revision != _revision || token.IsCancellationRequested) { first.Dispose(); return; }
            // 这里转交租约，显示Bitmap先释放再归还，不使用using提前释放。
            animation.Display = CreateAnimationDisplay(page, request, first);
            // 只有真实首帧提交才缓存成功判定；取消的首帧不能阻止下一代次再次打开。
            _animationAttempts[page] = version; _pageErrors.Remove(page);
            animation.DisplayFrame = 0; animation.Player.Play(); _animations[page] = animation; animation = null;
            StartMediaTimer(); SelectCurrentMedia(); InvalidateVisual();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        { if (!_disposed && revision == _revision && !token.IsCancellationRequested) { _animationAttempts[page] = version; _pageErrors[page] = error.Message; InvalidateVisual(); MediaChanged?.Invoke(this, EventArgs.Empty); } }
        finally { animation?.Dispose(); source?.Dispose(); }
    }
    /// <summary>只复制到一个当前显示Bitmap，像素及显示缓冲仍由工厂租约计费。</summary>
    private Display CreateAnimationDisplay(Page page, DecodeRequest request, BitmapLease lease)
    {
        var pixels = lease.Image; var handle = GCHandle.Alloc(pixels.Pixels, GCHandleType.Pinned); Bitmap? bitmap = null;
        try
        {
            bitmap = new(PixelFormat.Bgra8888, AlphaFormat.Premul, handle.AddrOfPinnedObject(), new((int)pixels.Size.Width,(int)pixels.Size.Height), new(96,96), pixels.Stride);
            lease.RegisterDisplayBytes(checked((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4)); BitmapCreationCount++;
            return new(bitmap, lease, request, page.ArchiveEntry.Length, page.ArchiveEntry.LastWriteTime);
        }
        catch { bitmap?.Dispose(); lease.Dispose(); throw; }
        finally { handle.Free(); }
    }
    private void SelectCurrentMedia()
    {
        if (_operation is null) return;
        var player = _operation.Book?.CurrentPage is { } page ? _animations.GetValueOrDefault(page)?.Player : null;
        if (ReferenceEquals(player, _operation.CurrentMediaPlayer)) return;
        _operation.CurrentMediaPlayer = player; MediaChanged?.Invoke(this, EventArgs.Empty);
    }
    private void MediaPlayerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AnimatedMediaPlayer.IsPlaying) or nameof(AnimatedMediaPlayer.IsEnabled)
            && sender is AnimatedMediaPlayer { IsPlaying: true, IsEnabled: true }) StartMediaTimer();
        if (args.PropertyName is nameof(AnimatedMediaPlayer.IsPlaying) or nameof(AnimatedMediaPlayer.IsRepeat)) MediaChanged?.Invoke(this, EventArgs.Empty);
    }
    private void StartMediaTimer()
    {
        if (_mediaTimer.IsEnabled) return;
        _lastMediaTick = Stopwatch.GetTimestamp(); _mediaTimer.Tick -= MediaTimerTick; _mediaTimer.Tick += MediaTimerTick; _mediaTimer.Start();
    }
    private async void MediaTimerTick(object? sender, EventArgs args)
    {
        if (_updatingMedia) return;
        var now = Stopwatch.GetTimestamp(); var elapsed = Stopwatch.GetElapsedTime(_lastMediaTick, now); _lastMediaTick = now;
        await UpdateMediaFramesAsync(elapsed);
    }
    /// <summary>命令seek/暂停/恢复后刷新当前帧，不重新打开来源或静态解码。</summary>
    public async Task RefreshMediaAsync()
    {
        foreach (var animation in _animations.Values) animation.Player.IsRepeat = Config.Current.Image.IsMediaRepeat;
        _lastMediaTick = Stopwatch.GetTimestamp(); await UpdateMediaFramesAsync(TimeSpan.Zero);
        if (_animations.Values.Any(a=>a.Player.IsPlaying && a.Player.IsEnabled)) StartMediaTimer();
    }
    private async Task UpdateMediaFramesAsync(TimeSpan elapsed)
    {
        if (_disposed) return;
        if (_updatingMedia) { _pendingMediaUpdate = true; return; }
        _updatingMedia = true;
        try
        {
            foreach (var animation in _animations.Values.ToArray())
            {
                animation.Player.Advance(elapsed); var index = animation.Player.CurrentFrameIndex;
                if (index == animation.DisplayFrame || animation.Cancellation.IsCancellationRequested) continue;
                BitmapLease? frame = null;
                try
                {
                    frame = await animation.Source.ReadFrameAsync(index, animation.Token);
                    if (_disposed || animation.Cancellation.IsCancellationRequested || !ReferenceEquals(_animations.GetValueOrDefault(animation.Page),animation)) continue;
                    var display = CreateAnimationDisplay(animation.Page, animation.Request, frame); frame = null;
                    animation.Display?.Dispose(); animation.Display = display; animation.DisplayFrame = index; InvalidateVisual();
                }
                catch (OperationCanceledException) { }
                catch (Exception error)
                {
                    if (_disposed || animation.Token.IsCancellationRequested || !ReferenceEquals(_animations.GetValueOrDefault(animation.Page),animation)) continue;
                    animation.Player.Pause(); _pageErrors[animation.Page] = error.Message; InvalidateVisual(); MediaChanged?.Invoke(this,EventArgs.Empty);
                }
                finally { frame?.Dispose(); }
            }
            if (!_animations.Values.Any(a=>a.Player.IsPlaying && a.Player.IsEnabled)) _mediaTimer.Stop();
        }
        finally { _updatingMedia = false; }
        if (_pendingMediaUpdate) { _pendingMediaUpdate = false; await UpdateMediaFramesAsync(TimeSpan.Zero); }
    }
    private void ClearAnimations()
    {
        _mediaTimer.Stop(); _mediaTimer.Tick -= MediaTimerTick;
        foreach (var animation in _animations.Values) animation.Dispose(); _animations.Clear(); _animationAttempts.Clear();
        SelectCurrentMedia();
    }
}
