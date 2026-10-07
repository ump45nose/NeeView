using Avalonia.Controls;
namespace NeeView.MacOS.Views;
public sealed partial class SettingsWindow
{
    private readonly CheckBox _recordEnabled = new(){Name="RecordEnabled",Content="保存页面浏览记录（TSV）"};
    private readonly TextBox _recordPath = new(){Name="RecordPath",PlaceholderText="记录文件完整路径"};
    private readonly NumericUpDown _recentCount = new(){Name="RecentBookCount",Minimum=1,Maximum=int.MaxValue,Increment=1,FormatString="0"};
    private readonly NumericUpDown _autoSensitivity = new(){Name="AutoScrollSensitivity",Minimum=.01m,Maximum=100,Increment=.1m};
    private readonly CheckBox _autoStop = new(){Name="AutoScrollStop",Content="自动滚动时，其他交互停止滚动"};
    private readonly CheckBox _autoDesktopHide = new(){Name="AutoHideFullDesktop",Content="跨屏显示时自动隐藏界面"};
    private readonly ComboBox _longMode = new(){Name="LongButtonMode",ItemsSource=new[]{"无", "放大镜", "自动滚动", "重复点击"}};
    private readonly ComboBox _longMask = new(){Name="LongButtonMaskChoice",ItemsSource=new[]{"左键", "右键", "全部按钮"}};
    private readonly NumericUpDown _longTime = new(){Name="LongButtonTime",Minimum=.01m,Maximum=60,Increment=.1m};
    private readonly NumericUpDown _repeatTime = new(){Name="LongRepeatTime",Minimum=.01m,Maximum=60,Increment=.1m};
    private void FillCompletionSettings()
    {
        var history=(StackPanel)this.FindControl<ScrollViewer>("HistorySettings")!.Content!;
        foreach(var item in history.Children.OfType<TextBlock>().Where(t=>t.Text?.Contains("尚未迁移")==true).ToArray()) history.Children.Remove(item);
        _recentCount.Value=Config.Current.History.RecentBookCount;
        _recordEnabled.IsChecked=Config.Current.PageViewRecorder.IsSavePageViewRecord; _recordPath.Text=Config.Current.PageViewRecorder.PageViewRecordFilePath;
        history.Children.Add(new TextBlock{Text="最近打开书籍菜单数量"}); history.Children.Add(_recentCount); history.Children.Add(_recordEnabled); history.Children.Add(_recordPath);
        history.Children.Add(new TextBlock{Text="启用后仅追加记录停留时间；已有文件保持。父目录须已存在。",TextWrapping=Avalonia.Media.TextWrapping.Wrap});
        var input=this.FindControl<CheckBox>("GestureEnabled")!.Parent as StackPanel;
        _autoSensitivity.Value=(decimal)Math.Clamp(Config.Current.Mouse.AutoScrollSensitivity,.01,100); _autoStop.IsChecked=Config.Current.Mouse.IsStopAutoScrollUponInteraction;
        if(input is not null){input.Children.Add(new TextBlock{Text="指针自动滚动灵敏度"});input.Children.Add(_autoSensitivity);input.Children.Add(_autoStop);}
        _longMode.SelectedIndex=(int)Config.Current.Mouse.LongButtonDownMode; _longMask.SelectedIndex=(int)Config.Current.Mouse.LongButtonMask;
        _longTime.Value=(decimal)Math.Clamp(Config.Current.Mouse.LongButtonDownTime,.01,60); _repeatTime.Value=(decimal)Math.Clamp(Config.Current.Mouse.LongButtonRepeatTime,.01,60);
        if(input is not null)
        {
            input.Children.Add(new TextBlock{Text="鼠标长按模式"});input.Children.Add(_longMode);
            input.Children.Add(new TextBlock{Text="长按按钮"});input.Children.Add(_longMask);
            input.Children.Add(new TextBlock{Text="长按时间（秒）"});input.Children.Add(_longTime);
            input.Children.Add(new TextBlock{Text="重复间隔（秒）"});input.Children.Add(_repeatTime);
        }
        var auto=(StackPanel)this.FindControl<ScrollViewer>("AutoHideSettings")!.Content!;
        _autoDesktopHide.IsChecked=Config.Current.Window.IsAutoHideInFullDesktop; auto.Children.Add(_autoDesktopHide);
    }
    private void ApplyCompletionSettings()
    {
        Config.Current.History.RecentBookCount=(int)(_recentCount.Value??10);
        Config.Current.PageViewRecorder.IsSavePageViewRecord=_recordEnabled.IsChecked==true;
        Config.Current.PageViewRecorder.PageViewRecordFilePath=_recordPath.Text;
        Config.Current.Mouse.AutoScrollSensitivity=(double)(_autoSensitivity.Value??1);
        Config.Current.Mouse.IsStopAutoScrollUponInteraction=_autoStop.IsChecked==true;
        Config.Current.Mouse.LongButtonDownMode=(LongButtonDownMode)Math.Max(0,_longMode.SelectedIndex);
        Config.Current.Mouse.LongButtonMask=(LongButtonMask)Math.Max(0,_longMask.SelectedIndex);
        Config.Current.Mouse.LongButtonDownTime=(double)(_longTime.Value??1);
        Config.Current.Mouse.LongButtonRepeatTime=(double)(_repeatTime.Value??.1m);
        Config.Current.Window.IsAutoHideInFullDesktop=_autoDesktopHide.IsChecked==true;
    }
}
