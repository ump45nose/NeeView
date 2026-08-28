namespace NeeView
{
    /// <summary>
    /// Numbered destination-folder panel item.
    /// </summary>
    /// <param name="Number">Number used for display and shortcuts.</param>
    /// <param name="Folder">Destination folder from the current configuration.</param>
    public sealed record DestinationFolderPanelItem(int Number, DestinationFolder Folder)
    {
        /// <summary>
        /// Get the display name.
        /// </summary>
        public string Name => Folder.Name;

        /// <summary>
        /// Get the full path used by the tooltip.
        /// </summary>
        public string Path => Folder.Path;
    }
}
