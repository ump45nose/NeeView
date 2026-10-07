using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NeeView;
namespace NeeView.MacOS.Views;

/// <summary>连续/瀑布表现：只持有可见像素及滚动位置，阅读状态、来源和缓存共用原链路。</summary>
internal sealed class ReaderBrowsePresenter(ReaderView owner, BookOperation operation, BitmapFactory factory, Action displayed) : IDisposable
{
    private sealed class Display(Bitmap bitmap, BitmapLease lease, int width, int height) : IDisposable
    {
        private int _references = 1;
        public Bitmap Bitmap { get; } = bitmap;
        public BitmapLease Lease { get; } = lease;
        public int Width { get; } = width;
        public int Height { get; } = height;
        public Display Retain() { Interlocked.Increment(ref _references); return this; }
        public void Dispose() { if (Interlocked.Decrement(ref _references) != 0) return; Bitmap.Dispose(); Lease.Dispose(); }
    }
    private sealed record Demand(CancellationTokenSource Cancellation, bool Background)
    { public DecodeRequest? Request { get; set; } }
    private readonly Dictionary<Page, Display> _images = [];
    public ThemeRgba ContentColor => operation.Book?.CurrentPage is { } page && _images.TryGetValue(page, out var image)
        ? image.Lease.Image.Color : ThemeRgba.Parse("Black");
    private readonly Dictionary<Page, Demand> _pending = [];
    private readonly Dictionary<Page, string> _errors = [];
    private readonly DispatcherTimer _relayout = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer _position = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private Book? _book;
    private Page[] _pages = [];
    private long _order = -1;
    private readonly SemaphoreSlim _layoutSlot = new(1);
    private readonly Dictionary<int, NeeView.Size> _dirtySizes = [];
    private CancellationTokenSource? _layoutCancellation;
    private Task _layoutWork = Task.CompletedTask;
    private long _layoutGeneration;
    private bool _layoutPending;
    private string? _layoutError;
    private double _layoutComputeMs, _layoutPublishMs;
    private (double Width, BrowseLayoutMode Mode, double ColumnWidth, bool Rtl, double Scale)? _layoutShape;
    private double? _pendingNavigation;
    private (Page Page, double X, double Y, Point Pointer)? _zoomAnchor;
    public long LayoutPublications { get; private set; }
    public bool IsLayoutPending => _layoutPending || _relayout.IsEnabled;
    private (bool, NeeView.Size, CustomSizeAspectRatio, double, bool, bool, double, double, double, double, bool)? _geometryKey;
    private double _height;
    private static (bool, NeeView.Size, CustomSizeAspectRatio, double, bool, bool, double, double, double, double, bool) GeometryKey()
    { var c = Config.Current.ImageCustomSize; var t = Config.Current.ImageTrim; return (c.IsEnabled,c.Size,c.AspectRatio,c.ApplicabilityRate,c.IsAlignLongSide,t.IsEnabled,t.Left,t.Right,t.Top,t.Bottom,Config.Current.Image.Standard.IsAspectRatioEnabled); }
    private NeeView.Size EffectiveSize(Page page) => page.IsImage && operation.Context is { } context ? new NeeView.PageFrames.PageSizeCalculator(context,page.Content.PageDataSource).GetPageSize() : page.Content.PageDataSource.Size;
    private double _width, _columnWidth, _scale, _offset, _offsetX;
    private BrowseLayoutMode _mode;
    private bool _rightToLeft, _disposed, _initialized;
    private Page? _reported, _selection;
    private int _observedIndex = -1;
    private (Page? Page, double Fraction) _resumeAnchor;
    private int _resumePosition = -1;
    private Point? _pressed;
    private double _pressedOffset;
    private double _pressedOffsetX;
    private bool _scrollbarDrag;
    public bool Active { get; private set; }
    public BrowseLayout? Layout { get; private set; }
    public int DisplayCount => _images.Count;
    public double Offset => _offset;
    public Page? SelectedPage => _selection;
    /// <summary>Mac连续/瀑布扩展只复制显式选中且已显示的图片，滚动锚点不作为操作目标。</summary>
    internal Page? CopyImagePage => !_disposed && Active && ReferenceEquals(_book, operation.Book)
        && _selection is { IsImage: true } page && !_errors.ContainsKey(page) && _images.ContainsKey(page) ? page : null;
    /// <summary>后台编码期间保留现有显示租约，离开可见区也不能提前释放。</summary>
    internal (Bitmap Bitmap, IDisposable Retained) RetainCopyImage(Page page)
    { if (!ReferenceEquals(page, CopyImagePage)) throw new InvalidOperationException("选中图片已改变。"); var image = _images[page].Retain(); return (image.Bitmap, image); }
    /// <summary>Loupe 的原图基准仅采用当前页面几何，不重新布局或改滚动锚点。</summary>
    internal double GetOriginalScale()
    {
        var index = FindPageIndex(operation.Book?.CurrentPage);
        if (Layout is null || index < 0) return 1;
        var size = _pages[index].Content.PageDataSource.Size;
        return Layout.Items[index].Width / Math.Max(1, size.Width) * (TopLevel.GetTopLevel(owner)?.RenderScaling ?? 1);
    }
    public int PendingCount => _pending.Count + (IsLayoutPending ? 1 : 0);
    public bool HasPressedPointer => _pressed is not null;

    /// <summary>保持原Page锚点，尺寸补齐/排序/窗口变化后补偿滚动；普通回报不重算全书。</summary>
    /// <returns>当前后台布局及UI需求刷新完成；被新请求取代时直接退出。</returns>
    public async Task RefreshAsync()
    {
        if (_disposed) return;
        if (!_initialized)
        {
            _initialized = true;
            _relayout.Tick += async (_, _) => { _relayout.Stop(); await ScheduleLayoutAsync(CaptureAnchor(), full: false); };
            _position.Tick += async (_, _) => { _position.Stop(); await ReportPositionAsync(); };
        }
        var book = operation.Book;
        bool wasActive = Active;
        Active = operation.BrowseMode is BrowseLayoutMode.Continuous or BrowseLayoutMode.Masonry;
        if (!Active)
        {
            if (wasActive) { _resumeAnchor = CaptureAnchor(); _resumePosition = operation.Position.Index; }
            ReleaseDemand(); Layout = null; return;
        }
        bool newBook = !ReferenceEquals(book, _book);
        var anchor = newBook ? (Page: book?.CurrentPage, Fraction: 0d) : CaptureAnchor();
        bool rebuild = _geometryKey != GeometryKey() || (_height != owner.Bounds.Height && Config.Current.ImageCustomSize.AspectRatio is CustomSizeAspectRatio.View or CustomSizeAspectRatio.HalfView) || newBook || _order != book?.PageOrderVersion || _mode != operation.BrowseMode
            || _width != owner.Bounds.Width || _columnWidth != Config.Current.Book.MacGalleryColumnWidth
            || _scale != Config.Current.Book.MacContinuousScale
            || _rightToLeft != (book?.Setting.BookReadOrder == PageReadOrder.RightToLeft);
        bool newOrder = _order != book?.PageOrderVersion;
        if (newBook || newOrder || _mode != operation.BrowseMode) { ReleaseDemand(); Layout = null; }
        if (newBook) { _zoomAnchor = null; _selection = null; _reported = null; _observedIndex = -1; }
        if (!wasActive)
        {
            anchor = !newBook && _resumePosition == operation.Position.Index && _resumeAnchor.Page is not null ? _resumeAnchor : (book?.CurrentPage, 0);
            _reported = null; _observedIndex = -1; rebuild = true;
        }
        _book = book;
        if (rebuild)
        {
            if (newBook || _order != book?.PageOrderVersion) _pages = book?.Pages.ToArray() ?? [];
            _order = book?.PageOrderVersion ?? -1; _mode = operation.BrowseMode;
            _layoutWork = ScheduleLayoutAsync(anchor, full: true);
        }
        var generation = _layoutGeneration;
        await _layoutWork;
        if (_disposed || !Active || generation != _layoutGeneration || !ReferenceEquals(book, _book)) return;
        _offset = Math.Clamp(_offset, 0, MaximumOffset);
        if (_observedIndex != operation.Position.Index && book is not null && !ReferenceEquals(book.CurrentPage, _reported))
            BringIntoView(operation.Position.Index);
        _observedIndex = operation.Position.Index;
        UpdateDemand(); owner.InvalidateVisual();
    }

    /// <summary>捕获当前可见序列首项及页内相对偏移，重排后继续使用原Page身份。</summary>
    private (Page? Page, double Fraction) CaptureAnchor()
    {
        if (Layout is null) return (null, 0);
        int index = Layout.Query(_offset, _offset + Math.Max(1, owner.Bounds.Height)).FirstOrDefault(-1);
        return index < 0 || index >= _pages.Length ? (null, 0) : (_pages[index], (_offset - Layout.Items[index].Y) / Layout.Items[index].Height);
    }
    /// <summary>后台单槽构造不可变快照；结构变化全算，尺寸补齐从检查点重算。</summary>
    /// <param name="anchor">旧几何失效时保存的原Page及页内相对位置。</param>
    /// <param name="full">书籍/顺序/几何设置变化必须全算，尺寸补齐复用旧检查点。</param>
    /// <returns>计算及UI发布完成；过期结果不发布，计算异常保留可用旧布局。</returns>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "正式Mac项目LinkMode=None；后台布局仅克隆固定原ImageCustomSize/ImageTrim配置，不启用AOT裁剪。")]
    private Task ScheduleLayoutAsync((Page? Page, double Fraction) anchor, bool full)
    {
        if (_disposed || !Active) return Task.CompletedTask;
        _layoutCancellation?.Cancel();
        var cancellation = new CancellationTokenSource(); _layoutCancellation = cancellation;
        var generation = ++_layoutGeneration; _layoutPending = true;
        _width = owner.Bounds.Width; _height = owner.Bounds.Height; _geometryKey = GeometryKey(); _columnWidth = Config.Current.Book.MacGalleryColumnWidth;
        _scale = Config.Current.Book.MacContinuousScale;
        _rightToLeft = _book?.Setting.BookReadOrder == PageReadOrder.RightToLeft;
        var pages = _pages; var book = _book; var order = _order;
        double width = Math.Max(32, _width - 12), columnWidth = _columnWidth, scale = _scale;
        var mode = _mode; bool rtl = _rightToLeft;
        var shape = (width, mode, columnWidth, rtl, scale);
        // 尺寸补齐可能赶上窗口/列宽全算；不能用旧几何检查点替换新几何请求。
        var basis = full || _layoutShape != shape ? null : Layout;
        var changes = new Dictionary<int, NeeView.Size>(_dirtySizes);
        // 仅调度时捕获几何副本；后台计算不能跟随用户继续编辑的运行参数。
        var geometry = new Config { ImageCustomSize = System.Text.Json.JsonSerializer.Deserialize<ImageCustomSizeConfig>(System.Text.Json.JsonSerializer.Serialize(Config.Current.ImageCustomSize))!, ImageTrim = System.Text.Json.JsonSerializer.Deserialize<ImageTrimConfig>(System.Text.Json.JsonSerializer.Serialize(Config.Current.ImageTrim))! };
        var geometryContext = new NeeView.PageFrames.PageFrameContext(new(),geometry) { CanvasSize = new(_width,_height) };
        geometry.Image.Standard.IsAspectRatioEnabled = Config.Current.Image.Standard.IsAspectRatioEnabled;
        NeeView.Size Adjust(int index, NeeView.Size source) => pages[index].IsImage ? new NeeView.PageFrames.PageSizeCalculator(geometryContext,
            new PageDataSource(source) { AspectSize = pages[index].Content.PageDataSource.AspectSize }).GetPageSize() : source;
        _layoutWork = CalculateAsync(); return _layoutWork;

        async Task CalculateAsync()
        {
            bool entered = false; var token = cancellation.Token;
            try
            {
                await _layoutSlot.WaitAsync(token); entered = true;
                var result = await Task.Run(() =>
                {
                    var clock = Stopwatch.StartNew();
                    BrowseLayout next;
                    if (basis is not null) next = basis.WithSizes(changes.ToDictionary(x => x.Key, x => Adjust(x.Key,x.Value)), token);
                    else
                    {
                        // PageDataSource为不可变记录；后台读取引用，不访问控件或扫描来源。
                        var sizes = new NeeView.Size[pages.Length];
                        for (int i = 0; i < sizes.Length; i++) { if ((i & 255) == 0) token.ThrowIfCancellationRequested(); sizes[i] = Adjust(i,pages[i].Content.PageDataSource.Size); }
                        next = new(sizes, width, mode, columnWidth, rtl, scale, token);
                    }
                    return (Layout: next, Milliseconds: clock.Elapsed.TotalMilliseconds);
                }, token);
                if (_disposed || !Active || token.IsCancellationRequested || generation != _layoutGeneration
                    || !ReferenceEquals(book, operation.Book) || order != book?.PageOrderVersion) return;
                // 用户可以在计算期间继续滚动；发布时捕获最新旧布局锚点，避免退回请求时的位置。
                var latest = Layout is null ? anchor : CaptureAnchor();
                double centerX = (_offsetX + _width / 2) / Math.Max(1, Layout?.Width ?? _width);
                var publish = Stopwatch.StartNew();
                Layout = result.Layout; _layoutShape = shape; _layoutComputeMs = result.Milliseconds; _layoutError = null;
                _offsetX = Math.Clamp(centerX * Layout.Width - _width / 2, 0, Math.Max(0, Layout.Width - _width));
                int index = FindPageIndex(latest.Page);
                if (index >= 0) _offset = Layout.Items[index].Y + latest.Fraction * Layout.Items[index].Height;
                if (_zoomAnchor is { } zoom)
                {
                    _zoomAnchor = null; int zoomIndex = FindPageIndex(zoom.Page);
                    if (zoomIndex >= 0)
                    {
                        var rect = Layout.Items[zoomIndex];
                        _offset = rect.Y + rect.Height * zoom.Y - zoom.Pointer.Y;
                        _offsetX = Math.Clamp(rect.X + rect.Width * zoom.X - zoom.Pointer.X, 0, Math.Max(0, Layout.Width - _width));
                    }
                }
                _offset = Math.Clamp(_offset, 0, MaximumOffset);
                foreach (var pair in changes) if (_dirtySizes.TryGetValue(pair.Key, out var value) && value == pair.Value) _dirtySizes.Remove(pair.Key);
                if (_pendingNavigation is { } target)
                {
                    _pendingNavigation = null;
                    // 初次布局尚未发布时导航器也可使用；避免Refresh随后把位置拉回原页。
                    _observedIndex = operation.Position.Index;
                    Scroll(target * MaximumOffset - _offset);
                }
                LayoutPublications++; UpdateDemand(); owner.InvalidateVisual(); _layoutPublishMs = publish.Elapsed.TotalMilliseconds;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (!_disposed && generation == _layoutGeneration) { _layoutError = "布局计算失败"; owner.InvalidateVisual(); }
                System.Diagnostics.Trace.WriteLine("Browse layout: " + ex.GetType().Name);
            }
            finally
            {
                if (entered) _layoutSlot.Release();
                if (ReferenceEquals(_layoutCancellation, cancellation)) { _layoutCancellation = null; _layoutPending = false; }
                cancellation.Dispose();
            }
        }
    }
    /// <summary>已提交的原Page.Index提供常数时间身份校验，旧顺序对象不能误命中新书。</summary>
    /// <param name="page">原内容锚点。</param>
    /// <returns>当前快照索引；身份失效返回-1。</returns>
    private int FindPageIndex(Page? page) => page is not null && page.Index >= 0 && page.Index < _pages.Length
        && ReferenceEquals(_pages[page.Index], page) ? page.Index : -1;
    private double MaximumOffset => Math.Max(0, (Layout?.Height ?? 0) - owner.Bounds.Height);
    /// <summary>原导航改变页面时，只有该项离开视口才滚动，不重置已可见项的偏移。</summary>
    /// <param name="index">当前排序后的原页面索引。</param>
    private void BringIntoView(int index)
    {
        if (Layout is null || index < 0 || index >= Layout.Items.Count) return;
        var rect = Layout.Items[index];
        if (rect.Bottom <= _offset || rect.Y >= _offset + owner.Bounds.Height)
            _offset = Math.Clamp(rect.Y - 8, 0, MaximumOffset);
    }

    /// <summary>可见区优先、邻近预取其次；离开窗口立即释放显示租约，不按条目数增长。</summary>
    private void UpdateDemand()
    {
        if (!Active || _disposed || Layout is null || _book is null || owner.Bounds.Width <= 0 || owner.Bounds.Height <= 0) return;
        var visible = owner.LoupeVisibleRect;
        double margin = visible.Height * .35;
        var indices = Layout.Query(_offset + visible.Y - margin, _offset + visible.Bottom + margin)
            .OrderBy(i => Layout.Items[i].Bottom <= _offset || Layout.Items[i].Y >= _offset + owner.Bounds.Height)
            .ThenBy(i => Math.Abs(Layout.Items[i].Y + Layout.Items[i].Height / 2 - _offset - owner.Bounds.Height / 2)).Take(128).ToArray();
        var desired = indices.Select(i => _pages[i]).ToHashSet();
        foreach (var page in _images.Keys.Except(desired).ToArray()) { _images[page].Dispose(); _images.Remove(page); }
        foreach (var page in _pending.Keys.Except(desired).ToArray()) { _pending[page].Cancellation.Cancel(); _pending.Remove(page); }
        foreach (var page in _errors.Keys.Except(desired).ToArray()) _errors.Remove(page);
        foreach (int index in indices)
        {
            var page = _pages[index]; var specification = GetRequest(index);
            TrackSize(index, page);
            if (_images.TryGetValue(page, out var image) && image.Width == specification.TargetWidth && image.Height == specification.TargetHeight) continue;
            if (_pending.TryGetValue(page, out var pending))
            {
                if (pending.Request == specification) continue;
                pending.Cancellation.Cancel(); _pending.Remove(page);
            }
            if (_errors.ContainsKey(page)) continue;
            var rect = Layout.Items[index]; var demand = new Demand(new(), rect.Bottom <= _offset || rect.Y >= _offset + owner.Bounds.Height) { Request = specification }; _pending.Add(page, demand);
            _ = LoadAsync(_book, page, demand);
        }
    }
    /// <summary>补齐可能来自取消后探测或其他缩略消费者；与发布快照比较而非只看HasSize。</summary>
    /// <param name="index">已核对的当前页面快照索引。</param>
    /// <param name="page">可见原页面，尺寸变化进入80ms合并窗口。</param>
    private void TrackSize(int index, Page page)
    {
        if (Layout is null || Layout.GetPageSize(index) == EffectiveSize(page)) return;
        _dirtySizes[index] = page.Content.PageDataSource.Size;
        if (!_relayout.IsEnabled) _relayout.Start();
    }
    /// <summary>按实际设备比例和显示尺寸计算解码规格，瀑布使用缩略预算。</summary>
    /// <param name="index">当前布局中的页面索引。</param>
    /// <returns>按64像素取整、有上限的尺寸请求，不改变页面原始尺寸。</returns>
    private DecodeRequest GetRequest(int index)
    {
        var rect = Layout!.Items[index]; double scale = (TopLevel.GetTopLevel(owner)?.RenderScaling ?? 1) * owner.LoupeFixedScale;
        var size = _pages[index].Content.PageDataSource.Size;
        if (Config.Current.ImageDotKeep.IsImageDotKeep(new(rect.Width * scale, rect.Height * scale), size))
            return new((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), _mode == BrowseLayoutMode.Masonry,
                _mode == BrowseLayoutMode.Masonry ? null : Config.Current.ImageResizeFilter.CreateParameters());
        var trim = Config.Current.ImageTrim;
        var display = new NeeView.Size(rect.Width * scale / (trim.IsEnabled ? 1-trim.Left-trim.Right : 1),
            rect.Height * scale / (trim.IsEnabled ? 1-trim.Top-trim.Bottom : 1));
        return ReaderImageRenderer.CreateRequest(size, display, 64, _mode == BrowseLayoutMode.Masonry ? 2048 : 32768, _mode == BrowseLayoutMode.Masonry);
    }
    /// <summary>探测、共享解码及显示缓冲依次接入；过期结果只释放资源，不更新新书。</summary>
    /// <param name="book">申请需求时的原书籍，切书后不再提交。</param>
    /// <param name="page">需求窗口内的原页面。</param>
    /// <param name="demand">本次优先级及独立等待取消；方法结束时释放取消源。</param>
    private async Task LoadAsync(Book book, Page page, Demand demand)
    {
        var token = demand.Cancellation.Token;
        try
        {
            await operation.EnsurePageInfoAsync(book, page, token);
            token.ThrowIfCancellationRequested();
            if (_disposed || !Active || !ReferenceEquals(_book, book)) return;
            int index = page.Index; if (index < 0 || index >= _pages.Length || !ReferenceEquals(_pages[index], page)) return;
            TrackSize(index, page);
            var request = GetRequest(index); demand.Request = request;
            var lease = await factory.GetAsync(page, request, token, demand.Background);
            if (_disposed || !Active || token.IsCancellationRequested || !ReferenceEquals(_book, book)) { lease.Dispose(); return; }
            Bitmap? bitmap = null; var pixels = lease.Image; var handle = GCHandle.Alloc(pixels.Pixels, GCHandleType.Pinned);
            try
            {
                bitmap = new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, handle.AddrOfPinnedObject(), new PixelSize((int)pixels.Size.Width, (int)pixels.Size.Height), new Avalonia.Vector(96, 96), pixels.Stride);
                lease.RegisterDisplayBytes(checked((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4));
            }
            catch { bitmap?.Dispose(); lease.Dispose(); throw; }
            finally { handle.Free(); }
            if (_images.Remove(page, out var previous)) previous.Dispose();
            _images[page] = new(bitmap, lease, request.TargetWidth, request.TargetHeight); _errors.Remove(page);
            owner.InvalidateVisual(); displayed();
        }
        catch (OperationCanceledException) { }
        catch (EmptyArchivePageException) { if (!_disposed && Active && !token.IsCancellationRequested && ReferenceEquals(_book, book)) _errors[page] = ""; }
        catch (Exception ex) { if (!_disposed && Active && !token.IsCancellationRequested && ReferenceEquals(_book, book)) { _errors[page] = ex.Message; owner.InvalidateVisual(); } }
        finally
        {
            if (_pending.TryGetValue(page, out var current) && ReferenceEquals(current, demand)) _pending.Remove(page);
            demand.Cancellation.Dispose();
            if (!_disposed && Active && !token.IsCancellationRequested && ReferenceEquals(_book, book)) UpdateDemand();
        }
    }

    /// <summary>仅绘制可见索引，图片/命中/滚动共享同一几何；主题资源独立于业务。</summary>
    /// <param name="context">由唯一ReaderView提供的当前帧绘制上下文。</param>
    public void Render(DrawingContext context)
    {
        if (Layout is null || _pages.Length == 0) { Text(context, _layoutError ?? (IsLayoutPending ? "正在计算布局…" : "这个来源没有可浏览的页面"), new(24, 24)); return; }
        bool effected = ImageEffectRenderer.DrawScene(context, new Avalonia.Rect(owner.Bounds.Size), RecordScene);
        using var clip = context.PushClip(new Avalonia.Rect(owner.Bounds.Size));
        using (context.PushTransform(owner.LoupeMatrix))
        {
        foreach (int index in Layout.Query(_offset + owner.LoupeVisibleRect.Y, _offset + owner.LoupeVisibleRect.Bottom))
        {
            var item = Layout.Items[index]; var page = _pages[index];
            var target = new Avalonia.Rect(item.X - _offsetX, item.Y - _offset, item.Width, item.Height);
            if (effected) { /* 已合成整视口；选择框及滚动UI保留在效果外层。 */ }
            else if (page.PageType.IsFolder()||page.IsVideo) ArchivePageRenderer.Draw(owner, context, page, target, _images.GetValueOrDefault(page)?.Bitmap, _errors.GetValueOrDefault(page) ?? (_pending.ContainsKey(page) ? "正在加载…" : ""));
            else if (_images.TryGetValue(page, out var image))
            {
                var trim = Config.Current.ImageTrim; var size = image.Bitmap.PixelSize;
                var source = trim.IsEnabled ? new Avalonia.Rect(size.Width*trim.Left,size.Height*trim.Top,size.Width*(1-trim.Left-trim.Right),size.Height*(1-trim.Top-trim.Bottom)) : new Avalonia.Rect(size.ToSize(1));
                ReaderImageRenderer.Draw(owner,context,image.Bitmap,source,target,owner.LoupeMatrix,retain:image.Retain,pixels:image.Lease.Image);
                ImageGridRenderer.Draw(context,target,ImageGridTarget.Image);
            }
            else { context.FillRectangle(Brush("Gallery.Placeholder", Brushes.DimGray), target); Text(context, _errors.ContainsKey(page) ? "读取失败" : "正在加载…", target.TopLeft + new Avalonia.Vector(8, 8)); }
            if (ReferenceEquals(page, _selection)) context.DrawRectangle(new Pen(Brush("Gallery.Selection", Brushes.DodgerBlue), 3), target.Deflate(1.5));
        }
        }
        if (MaximumOffset > 0)
        {
            double thumb = Math.Max(32, owner.Bounds.Height * owner.Bounds.Height / Layout.Height);
            context.FillRectangle(Brush("Gallery.Scrollbar", Brushes.Gray), new Avalonia.Rect(owner.Bounds.Width - 8, _offset / MaximumOffset * (owner.Bounds.Height - thumb), 5, thumb), 2);
        }
        Text(context, $"{(_mode == BrowseLayoutMode.Masonry ? "瀑布流" : "连续阅读")} · {Layout.ColumnCount}列 · {_pages.Length}项", new(12, 10));
    }
    /// <summary>扩展浏览模式沿相同整视口效果；可见查询、像素与预算仍使用现有表现和工厂。</summary>
    private void RecordScene(SkiaSharp.SKCanvas canvas)
    {
        if (Layout is null) return;
        using var saved = new SkiaSharp.SKAutoCanvasRestore(canvas, true);
        canvas.Concat(ReaderEffectScene.Matrix(owner.LoupeMatrix));
        foreach (int index in Layout.Query(_offset + owner.LoupeVisibleRect.Y, _offset + owner.LoupeVisibleRect.Bottom))
        {
            var item = Layout.Items[index]; var page = _pages[index];
            var target = new Avalonia.Rect(item.X - _offsetX, item.Y - _offset, item.Width, item.Height);
            var image = _images.GetValueOrDefault(page);
            if (page.PageType.IsFolder()||page.IsVideo) ReaderEffectScene.Card(owner, canvas, page, target, image?.Lease.Image, image is null ? null : image.Retain,
                _errors.GetValueOrDefault(page) ?? (_pending.ContainsKey(page) ? "正在加载…" : ""), Matrix.Identity);
            else if (image is not null)
            {
                var trim = Config.Current.ImageTrim; var size = image.Bitmap.PixelSize;
                var source = trim.IsEnabled ? new Avalonia.Rect(size.Width * trim.Left, size.Height * trim.Top, size.Width * (1 - trim.Left - trim.Right), size.Height * (1 - trim.Top - trim.Bottom)) : new Avalonia.Rect(size.ToSize(1));
                ReaderEffectScene.PageBackground(canvas, target);
                ReaderEffectScene.Image(canvas, image.Lease.Image, source, target, image.Retain, ReaderImageRenderer.Interpolation(owner, source, target, owner.LoupeMatrix));
                ReaderEffectScene.Grid(canvas, target);
            }
            else
            {
                var brush = Brush("Gallery.Placeholder", Brushes.DimGray) as ISolidColorBrush;
                var color = brush?.Color ?? Colors.DimGray;
                ReaderEffectScene.Fill(canvas, target, new(color.R, color.G, color.B, color.A));
                ReaderEffectScene.Text(canvas, _errors.ContainsKey(page) ? "读取失败" : "正在加载…", target.TopLeft + new Avalonia.Vector(8, 8));
            }
        }
    }
    private IBrush Brush(string name, IBrush fallback) => owner.TryFindResource(name, out var value) && value is IBrush brush ? brush : fallback;
    private void Text(DrawingContext context, string text, Point point) => context.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("sans-serif"), 12, CanvasBackgroundPresenter.Foreground(ContentColor)), point);

    /// <summary>滚动立即更新需求，延迟回报阅读锚点；不把滚动锚点当作显式操作选择。</summary>
    /// <param name="delta">纵向DIP增量，正值向下。</param>
    /// <param name="horizontal">横向DIP增量，正值向右；超出布局边界时限幅。</param>
    public void Scroll(double delta, double horizontal = 0)
    {
        if (Layout is null || _disposed) return;
        _offset = Math.Clamp(_offset + delta, 0, MaximumOffset);
        _offsetX = Math.Clamp(_offsetX + horizontal, 0, Math.Max(0, Layout.Width - owner.Bounds.Width)); UpdateDemand(); owner.InvalidateVisual();
        _position.Stop(); _position.Start();
    }
    /// <summary>防抖结束后回报同一原Page锚点，旧书及关闭后的结果由Engine再次拒绝。</summary>
    private async Task ReportPositionAsync()
    {
        if (!Active || _disposed || _book is null) return;
        var anchor = CaptureAnchor(); if (anchor.Page is null) return;
        _reported = anchor.Page;
        try { await operation.ReportBrowsePositionAsync(_book, anchor.Page); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Browse position: " + ex.GetType().Name); }
    }
    /// <summary>缩放调整连续比例或瀑布列宽，不改变原分页变换图。</summary>
    /// <param name="factor">有限正缩放倍数，Engine负责限幅及保存。</param>
    /// <returns>Engine缩放提交完成，后续Refresh按内容锚点重排。</returns>
    public Task ZoomAsync(double factor, Point? pointer = null)
    {
        var center = pointer ?? new Point(owner.Bounds.Width / 2, owner.Bounds.Height / 2);
        int index = Layout?.HitTest(center.X + _offsetX, center.Y + _offset) ?? -1;
        if (index >= 0 && index < _pages.Length && Layout is not null)
        {
            var rect = Layout.Items[index];
            _zoomAnchor = (_pages[index], (center.X + _offsetX - rect.X) / rect.Width, (center.Y + _offset - rect.Y) / rect.Height, center);
        }
        return operation.ScaleBrowseColumnsAsync(factor);
    }
    /// <summary>把导航器相对纵向位置转换成滚动，实际Page锚点随后回报原BookOperation。</summary>
    /// <param name="point">Y为0至1的全书纵向比例，本增量忽略X。</param>
    public void Navigate(Point point)
    {
        if (_disposed || !Active || !double.IsFinite(point.Y)) return;
        var target = Math.Clamp(point.Y, 0, 1);
        if (IsLayoutPending || Layout is null) _pendingNavigation = target;
        if (Layout is not null) Scroll(target * MaximumOffset - _offset);
    }
    /// <summary>普通滚轮直接浏览；带修饰键时交还宿主原命令路由。</summary>
    /// <param name="e">框架滚轮事件。</param>
    /// <returns>本次事件是否已被浏览滚动消费。</returns>
    public bool Wheel(PointerWheelEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.None) Scroll(-e.Delta.Y * 100, -e.Delta.X * 100);
        else return false;
        return true;
    }
    /// <summary>点击显式选择，双击原图片进入分页；书籍封面双击沿原子书加载。</summary>
    /// <param name="e">宿主已确认的无修饰左键按下事件。</param>
    public void Press(PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(owner).Properties.IsLeftButtonPressed)
        {
            owner.Focus(); _pressed = e.GetPosition(owner); _pressedOffset = _offset; _pressedOffsetX = _offsetX;
            _scrollbarDrag = _pressed.Value.X >= owner.Bounds.Width - 14;
            e.Pointer.Capture(owner); e.Handled = true;
            if (_scrollbarDrag) ScrollFromThumb(_pressed.Value.Y);
        }
    }
    /// <summary>按下期间执行内容拖动或滚动条定位，不触发翻页和文件选择。</summary>
    /// <param name="e">在同一宿主捕获内的指针移动事件。</param>
    public void Move(PointerEventArgs e)
    {
        if (_pressed is not { } start) return;
        if (_scrollbarDrag) ScrollFromThumb(e.GetPosition(owner).Y);
        else Scroll(_pressedOffset + start.Y - e.GetPosition(owner).Y - _offset, _pressedOffsetX + start.X - e.GetPosition(owner).X - _offsetX);
        e.Handled = true;
    }
    /// <summary>释放捕获；未拖动的点击命中原Page，双击才切分页或打开子书。</summary>
    /// <param name="e">与按下匹配的左键释放事件。</param>
    /// <param name="child">由宿主提供的原子书打开入口，不由表现层读取来源。</param>
    /// <returns>原页面定位及必要的模式切换完成，切书后的旧点击不再执行。</returns>
    public async Task ReleaseAsync(PointerReleasedEventArgs e, Action<Page> child)
    {
        var point = e.GetPosition(owner); var start = _pressed; var scrollbarDrag = _scrollbarDrag;
        _pressed = null; e.Pointer.Capture(null);
        if (start is null || scrollbarDrag || new Avalonia.Vector(point.X - start.Value.X, point.Y - start.Value.Y).Length > 4 || Layout is null || _book is null) return;
        int index = Layout.HitTest(point.X + _offsetX, point.Y + _offset); if (index < 0) return;
        var book = _book; var selected = _pages[index]; var doubleClick = _doubleClick;
        _selection = selected; _reported = selected;
        await operation.JumpAsync(index, expectedBook: book); owner.InvalidateVisual();
        await operation.SelectFileActionPageAsync(book, selected);
        if (_disposed || !ReferenceEquals(operation.Book, book) || !ReferenceEquals(_selection, selected)) return;
        if (e.InitialPressMouseButton == MouseButton.Left && doubleClick)
        {
            if (selected.PageType.IsFolder()) child(selected);
            else await operation.SetBrowseModeAsync(BrowseLayoutMode.Paged);
        }
        e.Handled = true;
    }
    private bool _doubleClick;
    /// <summary>沿用框架点击计数，本次按下是否进入双击流程由释放时确认。</summary>
    public void SetClickCount(int count) => _doubleClick = count >= 2;
    /// <summary>捕获丢失、组合输入或模式切换取消旧按下状态，不提交选择。</summary>
    public void CaptureLost() { _pressed = null; _scrollbarDrag = false; }
    /// <summary>将滚动条指针Y转换为全书偏移，仍进入统一滚动和位置回报链。</summary>
    private void ScrollFromThumb(double y) => Scroll(Math.Clamp(y / Math.Max(1, owner.Bounds.Height), 0, 1) * MaximumOffset - _offset);
    /// <summary>正式诊断只记录数值，不包含真实目录/图片名称。</summary>
    /// <param name="writer">宿主持有的JSON输出器，本方法不关闭或写盘。</param>
    public void WriteDiagnostics(Utf8JsonWriter writer)
    {
        writer.WriteStartObject(); writer.WriteString("Layout", _mode.ToString()); writer.WriteNumber("RenderScaling", TopLevel.GetTopLevel(owner)?.RenderScaling ?? 1);
        writer.WriteNumber("PageCount", _pages.Length); writer.WriteNumber("Columns", Layout?.ColumnCount ?? 0);
        writer.WriteNumber("Offset", _offset); writer.WriteNumber("ContentHeight", Layout?.Height ?? 0);
        writer.WriteNumber("DisplayCount", _images.Count); writer.WriteNumber("PendingCount", _pending.Count);
        writer.WriteBoolean("LayoutPending", IsLayoutPending); writer.WriteNumber("LayoutPublications", LayoutPublications);
        writer.WriteNumber("LayoutComputeMs", _layoutComputeMs); writer.WriteNumber("LayoutPublishMs", _layoutPublishMs);
        writer.WriteNumber("RecomputedItems", Layout?.RecomputedItemCount ?? 0); writer.WriteNumber("ReusedBlocks", Layout?.ReusedBlockCount ?? 0);
        writer.WriteNumber("ErrorCount", _errors.Count); writer.WriteNumber("PageIndex", operation.Position.Index); writer.WriteEndObject();
    }
    /// <summary>停止位置/重排计时、取消等待并释放显示；晚到任务自行归还原生租约。</summary>
    private void ReleaseDemand()
    {
        CaptureLost();
        _relayout.Stop(); _position.Stop();
        ++_layoutGeneration; _layoutCancellation?.Cancel(); _layoutPending = false; _layoutWork = Task.CompletedTask; _dirtySizes.Clear(); _pendingNavigation = null;
        foreach (var pending in _pending.Values) pending.Cancellation.Cancel(); _pending.Clear();
        foreach (var image in _images.Values) image.Dispose(); _images.Clear(); _errors.Clear();
    }
    /// <summary>宿主关闭时幂等释放浏览需求和页面引用，不关闭共享Book或BitmapFactory。</summary>
    public void Dispose() { if (_disposed) return; _disposed = true; Active = false; ReleaseDemand(); _pages = []; _book = null; Layout = null; }
}
