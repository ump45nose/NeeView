namespace NeeView;
public sealed partial class BookOperation
{
    private SlideShow? _slideShow;
    /// <summary>唯一窗口阅读控制持有原幻灯业务，界面计时器只回报时钟。</summary>
    public SlideShow SlideShow => _slideShow ??= new(this);
    /// <summary>原菜单忽略固定开关参数；键位允许Toggle/On/Off。</summary>
    public void ToggleSlideShow(bool fromMenu = false)
    {
        if (_disposed || _closing) return;
        var parameter = saveData.GetCommandParameter<ToggleCommandParameter>("ToggleSlideShow");
        SlideShow.SetPlaying(parameter.GetState(SlideShow.IsPlaying, fromMenu));
    }
}
