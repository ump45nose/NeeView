using System.Diagnostics.CodeAnalysis;
namespace NeeView;

/// <summary>已迁入原命令的唯一参数类型表，导入校验与界面草稿共用。</summary>
public static class CommandParameterTypes
{
    /// <summary>将配对命令解析为唯一参数拥有者，并返回已迁入的原参数类型。</summary>
    /// <param name="command">稳定的原命令名。</param><returns>未迁入/无参数命令返回 null。</returns>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public static Type? Get(string command) => DefaultInputScheme.GetParameterOwner(command) switch
    {
        "ViewScaleUp" or "ViewBaseScaleUp" => typeof(ViewScaleCommandParameter),
        "CopyToFolderAs" => typeof(CopyToFolderAsCommandParameter),
        "CopyFile" => typeof(CopyFileCommandParameter),
        "ExportBackup" => typeof(ExportBackupCommandParameter),
        var name when name == "MoveToFolderAs" || name.StartsWith("MoveToDestinationFolder", StringComparison.Ordinal) => typeof(MoveToFolderAsCommandParameter),
        "ViewRotateLeft" => typeof(ViewRotateCommandParameter),
        "ViewScrollUp" => typeof(ViewScrollCommandParameter),
        "ViewPresetScroll" => typeof(ViewPresetScrollCommandParameter),
        "ViewScrollNTypeUp" => typeof(ViewScrollNTypeCommandParameter),
        "PrevScrollPage" => typeof(ScrollPageCommandParameter),
        "PrevSizePage" => typeof(MoveSizePageCommandParameter),
        "ToggleStretchMode" => typeof(ToggleStretchModeCommandParameter),
        "TogglePageMode" => typeof(TogglePageModeCommandParameter),
        "SetStretchModeUniform" => typeof(StretchModeCommandParameter),
        "ToggleNearestNeighbor" or "ToggleVisibleAddressBar" or "ToggleVisiblePageSlider" or "ToggleViewFlipHorizontal" or "ToggleViewFlipVertical" or "TogglePlaylistItem" or "ToggleBookLock" or "ToggleSlideShow" => typeof(ToggleCommandParameter),
        "PrevPlaylistItemInBook" => typeof(MovePlaylistItemInBookCommandParameter),
        "PrevMediaPosition" or "NextMediaPosition" => typeof(MoveMediaPositionCommandParameter),
        "PrevPage" or "PrevOnePage" or "FirstPage" or "PrevFolderPage" => typeof(ReversibleCommandParameter),
        _ => null
    };
}
