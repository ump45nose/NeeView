using Avalonia.Controls;
namespace NeeView.MacOS.ViewModels;
/// <summary>原信息配置的独立草稿；字段布局/资源可修改，保存使用现有配置事务。</summary>
public sealed class InformationSettingsViewModel(InformationConfig config)
{
    public InformationConfig Draft { get; } = new()
    {
        DateTimeFormatRaw = config.DateTimeFormatRaw, MapProgramFormatRaw = config.MapProgramFormatRaw,
        PropertyHeaderWidth = config.PropertyHeaderWidth,
        IsVisibleFile = config.IsVisibleFile, IsVisibleImage = config.IsVisibleImage,
        IsVisibleDescription = config.IsVisibleDescription, IsVisibleOrigin = config.IsVisibleOrigin,
        IsVisibleCamera = config.IsVisibleCamera, IsVisibleAdvancedPhoto = config.IsVisibleAdvancedPhoto,
        IsVisibleGps = config.IsVisibleGps, IsVisibleExtras = config.IsVisibleExtras
    };
    public void Validate()
    {
        _ = DateTime.Now.ToString(Draft.DateTimeFormat);
        _ = GridLength.Parse(Draft.PropertyHeaderWidth);
    }
    /// <summary>保留原raw-null及列宽JSON值；验证失败不改运行配置。</summary>
    public void Apply(InformationConfig target)
    {
        Validate();
        target.DateTimeFormatRaw = Draft.DateTimeFormatRaw; target.MapProgramFormatRaw = Draft.MapProgramFormatRaw;
        target.PropertyHeaderWidth = Draft.PropertyHeaderWidth;
        target.IsVisibleFile = Draft.IsVisibleFile; target.IsVisibleImage = Draft.IsVisibleImage;
        target.IsVisibleDescription = Draft.IsVisibleDescription; target.IsVisibleOrigin = Draft.IsVisibleOrigin;
        target.IsVisibleCamera = Draft.IsVisibleCamera; target.IsVisibleAdvancedPhoto = Draft.IsVisibleAdvancedPhoto;
        target.IsVisibleGps = Draft.IsVisibleGps; target.IsVisibleExtras = Draft.IsVisibleExtras;
    }
}
