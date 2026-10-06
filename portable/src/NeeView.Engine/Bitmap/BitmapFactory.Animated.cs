namespace NeeView;

public sealed partial class BitmapFactory
{
    private readonly HashSet<AnimatedBitmapLease> _animationSources = [];
    private long _animationBytes;
    /// <summary>沿原页面/归档流打开动画；共享两解码槽并计入原主图预算，缩略图不加载动画。</summary>
    /// <returns>真实动画的来源租约；关闭开关或静态图片返回空，超限明确失败。</returns>
    public async Task<AnimatedBitmapLease?> OpenAnimationAsync(Page page, DecodeRequest request, CancellationToken token)
    {
        if (!page.IsImage || request.IsThumbnail || !Config.Current.Image.Standard.IsAnimationEnabled(page.EntryName) || decoder is not IAnimatedImageDecoder animated) return null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _decodeSlots.WaitAsync(linked.Token).ConfigureAwait(false);
        IAnimatedImageSource? source = null;
        try
        {
            await using var stream = await page.ArchiveEntry.Archive.OpenEntryAsync(page.ArchiveEntry, linked.Token).ConfigureAwait(false);
            source = await animated.OpenAnimationAsync(stream, request, Math.Min(256L * 1024 * 1024, Budget), linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested(); if (source is null) return null;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (source.Info.ResourceBytes < 0 || source.Info.ResourceBytes > Budget - _animationBytes) throw new NotSupportedException("当前动画资源超过图像缓存预算。");
                AnimatedBitmapLease? lease = null;
                lease = new(source, LoadAnimationFrameAsync, released =>
                {
                    // 帧读取真正完成后才归还原生计费，不能把取消等待当作释放完成。
                    released.Dispose(); lock (_sync) { _animationBytes -= released.Info.ResourceBytes; _animationSources.Remove(lease!); Trim(); }
                });
                _animationBytes += source.Info.ResourceBytes; _animationSources.Add(lease); source = null; Trim(); return lease;
            }
        }
        finally { source?.Dispose(); _decodeSlots.Release(); }
    }
    /// <summary>当前帧输出和实际显示缓冲沿已有退役Entry/Rent生命周期计费，没有第二像素缓存。</summary>
    private async Task<BitmapLease> LoadAnimationFrameAsync(IAnimatedImageSource source, int index, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _decodeSlots.WaitAsync(linked.Token).ConfigureAwait(false);
        DecodedImageLease? image = null;
        try
        {
            image = await source.ReadFrameAsync(index, linked.Token).ConfigureAwait(false); linked.Token.ThrowIfCancellationRequested();
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var entry = new Entry(image, false) { Cached = false }; _retired.Add(entry); image = null; return Rent(entry);
            }
        }
        finally { image?.Dispose(); _decodeSlots.Release(); }
    }
}

/// <summary>显示端拥有动画来源；仍在读取的原生资源延迟到真实完成才释放。</summary>
public sealed class AnimatedBitmapLease(IAnimatedImageSource source, Func<IAnimatedImageSource,int,CancellationToken,Task<BitmapLease>> read, Action<IAnimatedImageSource> release) : IDisposable
{
    private readonly object _gate = new();
    private int _readers;
    private bool _disposed, _released;
    public AnimatedImageInfo Info => source.Info;
    public async Task<BitmapLease> ReadFrameAsync(int index, CancellationToken token)
    {
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); _readers++; }
        try
        {
            var frame = await read(source, index, token).ConfigureAwait(false);
            bool disposed; lock (_gate) disposed = _disposed;
            if (disposed) { frame.Dispose(); throw new OperationCanceledException("动画已关闭。"); }
            return frame;
        }
        finally
        { bool cleanup; lock (_gate) { _readers--; cleanup = TakeRelease(); } if (cleanup) release(source); }
    }
    private bool TakeRelease()
    { if (!_disposed || _released || _readers != 0) return false; _released = true; return true; }
    public void Dispose()
    { bool cleanup; lock (_gate) { _disposed = true; cleanup = TakeRelease(); } if (cleanup) release(source); }
}
