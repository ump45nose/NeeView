using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

/// <summary>媒体条只处理焦点/输入反馈，通过现有业务和查看器的刷新入口执行动作。</summary>
public sealed partial class MediaControlView : UserControl, IDisposable
{
    private readonly MediaControlViewModel _model = new();
    private BookOperation? _operation;
    private Func<Task>? _refresh;
    private bool _editing, _volumeEditing, _closing, _disposed;
    private Task _action = Task.CompletedTask;
    private Task _seekAction = Task.CompletedTask;
    private bool _seeking;
    private (IMediaPlayer Player, double Position)? _pendingSeek;
    private (IMediaPlayer Player,bool Muted,double Volume)? _pendingAudio;
    private Task _audioAction=Task.CompletedTask;
    private bool _savingAudio;
    public event EventHandler<string>? Failed;
    public MediaControlView()
    {
        AvaloniaXamlLoader.Load(this); DataContext = _model;
        var slider = this.FindControl<Slider>("MediaPosition")!;
        slider.AddHandler(PointerPressedEvent, (_, _) => _editing = true, RoutingStrategies.Tunnel);
        slider.AddHandler(PointerReleasedEvent, (_, _) => _editing = false, RoutingStrategies.Bubble, handledEventsToo: true);
        slider.AddHandler(KeyDownEvent, Position_KeyDown, RoutingStrategies.Tunnel);
        slider.AddHandler(PointerWheelChangedEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel);
        slider.PointerCaptureLost+=(_,_)=>_editing=false;
        var volume=this.FindControl<Slider>("MediaVolume")!;
        volume.AddHandler(PointerPressedEvent,(_,_)=>_volumeEditing=true,RoutingStrategies.Tunnel);
        volume.AddHandler(PointerReleasedEvent,(_,_)=>_volumeEditing=false,RoutingStrategies.Bubble,handledEventsToo:true);
        volume.PointerCaptureLost+=(_,_)=>_volumeEditing=false;
        volume.AddHandler(KeyDownEvent,Volume_KeyDown,RoutingStrategies.Tunnel);
        volume.AddHandler(PointerWheelChangedEvent,(_,e)=>e.Handled=true,RoutingStrategies.Tunnel);
    }
    /// <summary>宿主借用唯一业务和刷新入口；不创建播放器或读取来源。</summary>
    public void Attach(BookOperation operation, Func<Task> refresh) { _operation = operation; _refresh = refresh; UpdatePlayer(); }
    public void UpdatePlayer() { if (!_disposed) _model.Attach(_operation?.CurrentMediaPlayer); }
    private async Task RunAsync(Func<Task> action)
    {
        if (_closing || _disposed || !_action.IsCompleted) return;
        _action = ExecuteAsync(action); await _action;
    }
    private async Task ExecuteAsync(Func<Task> action)
    {
        try { await action(); if (_refresh is not null) await _refresh(); }
        catch (Exception error) { Failed?.Invoke(this, error.Message); }
        finally { UpdatePlayer(); }
    }
    private async void Play_Click(object? sender, RoutedEventArgs e) => await RunAsync(() => { _operation?.ToggleMediaPlay(); return Task.CompletedTask; });
    private async void Repeat_Click(object? sender, RoutedEventArgs e)
    { if (_operation is not null && _model.Player is { } player) await RunAsync(() => _operation.SetMediaRepeatAsync(!player.IsRepeat)); }
    private async void Mute_Click(object? sender,RoutedEventArgs e)
    { if(_model.Player is {HasAudio:true} player)await SetAudioAsync(!(_pendingAudio?.Muted??player.IsMuted),_pendingAudio?.Volume??player.Volume); }
    private async void Volume_Changed(object? sender,RangeBaseValueChangedEventArgs e)
    { if(_volumeEditing&&!_model.IsRefreshing&&_model.Player is {HasAudio:true} player)await SetAudioAsync(_pendingAudio?.Muted??player.IsMuted,e.NewValue); }
    private async void Rate_Changed(object? sender,SelectionChangedEventArgs e)
    { if(!_closing&&!_disposed&&!_model.IsRefreshing&&sender is ComboBox {SelectedItem:double rate}&&_model.Player is {RateEnabled:true} player&&rate!=player.Rate)await ExecuteAsync(()=>{player.Rate=rate;return Task.CompletedTask;}); }
    /// <summary>音量拖动串行保留最后值，自动回显不保存；切换播放器后拒绝旧需求。</summary>
    public Task SetAudioAsync(bool muted,double volume)
    {
        if(_closing||_disposed||_operation is null||_model.Player is not {HasAudio:true,IsDisposed:false} player||!double.IsFinite(volume))return Task.CompletedTask;
        _pendingAudio=(player,muted,Math.Clamp(volume,0,1));
        if(!_savingAudio)_audioAction=ApplyAudioAsync();return _audioAction;
    }
    private async Task ApplyAudioAsync()
    {
        _savingAudio=true;
        try
        {
            while(_pendingAudio is {} request)
            {
                _pendingAudio=null;
                if(!ReferenceEquals(request.Player,_model.Player)||request.Player.IsDisposed||_operation is null)continue;
                await _operation.SetMediaAudioAsync(request.Player,request.Muted,request.Volume);
                if(_refresh is not null)await _refresh();
            }
        }
        catch(Exception error){_pendingAudio=null;Failed?.Invoke(this,error.Message);}
        finally{_savingAudio=false;UpdatePlayer();}
    }
    private async void Volume_KeyDown(object? sender,KeyEventArgs e)
    {
        if(_model.Player is not {HasAudio:true} player||e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End))return;
        e.Handled=true;var volume=_pendingAudio?.Volume??player.Volume;
        await SetAudioAsync(_pendingAudio?.Muted??player.IsMuted,e.Key switch{Key.Home=>0,Key.End=>1,Key.Left or Key.Down=>volume-.05,_=>volume+.05});
    }
    /// <summary>绑定的自动回显不触发seek；只接受当前指针编辑中的变化。</summary>
    private async void Position_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    { if (_editing && e.NewValue != _model.Position) await SeekAsync(e.NewValue); }
    /// <summary>拖动保留最新需求，不在旧帧输出期间丢掉释放前的最终位置。</summary>
    public Task SeekAsync(double position)
    {
        if (_closing || _disposed || _model.Player is not { } player || !double.IsFinite(position)) return Task.CompletedTask;
        _pendingSeek = (player, position);
        if (!_seeking) _seekAction = ApplySeekAsync();
        return _seekAction;
    }
    private async Task ApplySeekAsync()
    {
        _seeking = true;
        try
        {
            while (_pendingSeek is { } request)
            {
                _pendingSeek = null;
                if (!ReferenceEquals(request.Player, _model.Player) || request.Player.IsDisposed) continue;
                request.Player.Position = request.Position;
                if (_refresh is not null) await _refresh();
            }
        }
        catch (Exception error) { Failed?.Invoke(this,error.Message); }
        finally { _seeking = false; UpdatePlayer(); }
    }
    private async void Position_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right or Key.Home or Key.End)) return;
        e.Handled = true;
        var step = _model.Player is AnimatedMediaPlayer animation ? Math.Max(.01,1.0/Math.Max(1,animation.Info.FrameCount-1)) : .01;
        await SeekAsync(e.Key switch { Key.Home => 0, Key.End => 1, Key.Left => _model.Position - step, _ => _model.Position + step });
    }
    private void Time_Pressed(object? sender, PointerPressedEventArgs e) { _model.ToggleTimeFormat(); e.Handled = true; }
    /// <summary>退出先等候已提交循环配置的事务，失败保存仍允许窗口重试。</summary>
    public async Task PrepareCloseAsync() { _closing = true; IsEnabled = false; await _action; await _seekAction; await _audioAction; }
    public void CancelClose() { if (!_disposed) { _closing = false; IsEnabled = true; } }
    public void Dispose() { if (_disposed) return; _disposed = true; _model.Dispose(); _operation = null; _refresh = null; }
}
