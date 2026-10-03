using System.Diagnostics;
using Avalonia;
using NeeView;
namespace NeeView.MacOS.Views;

/// <summary>替换 WPF 点/透明度动画；仅插值表现数据，不拥有页面、图像或阅读控制。</summary>
internal sealed class ReaderMotionPresenter(Func<double>? clock = null)
{
    private readonly Func<double> _clock = clock ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
    private sealed record PanTrack(Avalonia.Vector From, Avalonia.Vector To, double Start, double Duration, bool Linear);
    private sealed record PageTrack(Avalonia.Vector Offset, PageMoveType Type, double Start, double Duration);
    private PanTrack? _pan;
    private PageTrack? _page;
    public bool IsPanActive => _pan is { } p && Progress(p.Start, p.Duration) < 1;
    public bool IsPageActive => _page is { } p && Progress(p.Start, p.Duration) < 1;
    public bool IsActive => IsPanActive || IsPageActive;
    /// <summary>逻辑目标保持原变换图，绘制用当前插值；重复命令从当前画面继续。</summary>
    public void BeginPan(Avalonia.Vector from, Avalonia.Vector to, TimeSpan duration, bool linear = false)
    { _pan = duration > TimeSpan.Zero && from != to ? new(from, to, _clock(), duration.TotalSeconds, linear) : null; }
    public Avalonia.Vector GetPanOffset()
    {
        if (_pan is not { } p) return default;
        double t = Progress(p.Start, p.Duration); if (!p.Linear) t = 1 - (1-t)*(1-t);
        return (p.From-p.To)*(1-t);
    }
    /// <summary>一次分页最多保留一个退出快照；零时长立即，不进入Fade。</summary>
    public void BeginPage(Avalonia.Vector offset, PageMoveType type, TimeSpan duration)
    { _page = duration > TimeSpan.Zero ? new(offset,type,_clock(),duration.TotalSeconds) : null; }
    public (Avalonia.Vector Incoming, Avalonia.Vector Outgoing, double IncomingOpacity, double OutgoingOpacity) GetPageState()
    {
        if (_page is not { } p) return (default,default,1,0);
        double t=Progress(p.Start,p.Duration);
        if(p.Type==PageMoveType.Fade) return (default,default,t,1-t);
        t=1-(1-t)*(1-t); return (p.Offset*(1-t),-p.Offset*t,1,1);
    }
    /// <summary>原CancelScroll保留当前显示点，返回需写回变换图的偏移。</summary>
    public Avalonia.Vector CancelPan() { var offset=GetPanOffset(); _pan=null; return offset; }
    public void CancelPage() => _page=null;
    public void Clear() { _pan=null; _page=null; }
    private double Progress(double start,double duration) => Math.Clamp((_clock()-start)/duration,0,1);
}
