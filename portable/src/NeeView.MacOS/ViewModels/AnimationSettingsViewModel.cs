using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView.MacOS.ViewModels;
/// <summary>原动图和媒体步长草稿；取消不触碰Config，保存由原事务应用。</summary>
public sealed class AnimationSettingsViewModel : ObservableObject
{
    private bool _gif,_png,_webp,_repeat,_aspect;private decimal _seconds;
    public bool AspectRatio {get=>_aspect;set=>SetProperty(ref _aspect,value);}
    public bool Gif {get=>_gif;set=>SetProperty(ref _gif,value);}
    public bool Png {get=>_png;set=>SetProperty(ref _png,value);}
    public bool Webp {get=>_webp;set=>SetProperty(ref _webp,value);}
    public bool Repeat {get=>_repeat;set=>SetProperty(ref _repeat,value);}
    public decimal PageSeconds {get=>_seconds;set=>SetProperty(ref _seconds,value);}
    public decimal SecondsMinimum {get;}
    public decimal SecondsMaximum {get;}
    public AnimationSettingsViewModel(ImageConfig image,MediaArchiveConfig media)
    {
        _gif=image.Standard.IsAnimatedGifEnabled;_png=image.Standard.IsAnimatedPngEnabled;_webp=image.Standard.IsAnimatedWebpEnabled;_repeat=image.IsMediaRepeat;
        _aspect=image.Standard.IsAspectRatioEnabled;
        _seconds=(decimal)media.PageSeconds;SecondsMinimum=Math.Min(0,_seconds);SecondsMaximum=Math.Max(3600,_seconds);
    }
    /// <summary>原JSON分支提交点，样式不参与动画判定或播放规则。</summary>
    public void Apply(ImageConfig image,MediaArchiveConfig media)
    {image.Standard.IsAspectRatioEnabled=AspectRatio;image.Standard.IsAnimatedGifEnabled=Gif;image.Standard.IsAnimatedPngEnabled=Png;image.Standard.IsAnimatedWebpEnabled=Webp;image.IsMediaRepeat=Repeat;media.PageSeconds=(double)PageSeconds;}
}
