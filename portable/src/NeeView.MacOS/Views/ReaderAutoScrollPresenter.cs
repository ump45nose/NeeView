using Avalonia;
using Avalonia.Threading;
using System.Diagnostics;

namespace NeeView.MacOS.Views;

/// <summary>指针中心自动滚动的有界时钟；只回调 ReaderView 的既有 Pan。</summary>
internal sealed class ReaderAutoScrollPresenter : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Action<Avalonia.Vector> _pan;
    private Point _start;
    private Avalonia.Vector _velocity;
    private long _lastTick;
    public bool IsActive { get; private set; }
    public bool IsLongDownMode { get; private set; }
    public ReaderAutoScrollPresenter(Action<Avalonia.Vector> pan) { _pan = pan; _timer.Tick += Tick; }
    public void Start(Point start, bool longDownMode = false) { _start = start; _velocity = default; _lastTick = Stopwatch.GetTimestamp(); IsLongDownMode = longDownMode; IsActive = true; _timer.Start(); }
    public void Stop() { IsActive = false; IsLongDownMode = false; _velocity = default; _timer.Stop(); }
    public void ButtonPressed() { if (!IsLongDownMode) Stop(); }
    public void ButtonReleased(bool anyButtonHeld) { if (IsLongDownMode && !anyButtonHeld) Stop(); }
    public void Reset() => Stop();
    public void Move(Point point, double sensitivity)
    {
        var delta = (point - _start) * .02 * sensitivity;
        _velocity = new(Limit(delta.X, sensitivity), Limit(delta.Y, sensitivity));
    }
    private static double Limit(double value, double min) { var sign = Math.Sign(value); return Math.Abs(value) < min ? 0 : sign * Math.Min(Math.Abs(value) - min, 16); }
    private void Tick(object? sender, EventArgs e) { if (!IsActive) return; var now = Stopwatch.GetTimestamp(); var seconds = Math.Clamp((now - _lastTick) / (double)Stopwatch.Frequency, 0, .1); _lastTick = now; _pan(-_velocity * (seconds * 1000)); }
    public void Dispose() { _timer.Tick -= Tick; Stop(); }
}
