namespace NeeView
{
    /// <summary>
    /// 目标文件夹面板中的一条数字映射。
    /// </summary>
    /// <param name="Number">显示和快捷键使用的序号</param>
    /// <param name="Folder">当前配置中的目标文件夹</param>
    public sealed record DestinationFolderPanelItem(int Number, DestinationFolder Folder)
    {
        /// <summary>
        /// 获取用于按钮显示的名称。
        /// </summary>
        public string Name => Folder.Name;

        /// <summary>
        /// 获取用于提示的完整路径。
        /// </summary>
        public string Path => Folder.Path;
    }
}
