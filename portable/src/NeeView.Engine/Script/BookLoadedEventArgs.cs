namespace NeeView;
/// <summary>原脚本BookLoaded通知；重命名资格独立保留。</summary>
public sealed record BookLoadedEventArgs(Book Book, bool Renamed);
