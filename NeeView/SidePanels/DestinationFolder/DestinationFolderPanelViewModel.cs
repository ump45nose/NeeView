using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeeLaboratory.ComponentModel;
using NeeView.Properties;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NeeView
{
    /// <summary>
    /// Display state and interaction commands for the destination-folder panel.
    /// </summary>
    public partial class DestinationFolderPanelViewModel : ObservableObject
    {
        private readonly DestinationMoveService _moveService;
        private bool _isFolderOperationBusy;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CreateFolderCommand))]
        private string _newFolderName = "";

        /// <summary>
        /// Initialize destination folders and observe configuration, page, and move-history changes.
        /// </summary>
        public DestinationFolderPanelViewModel()
        {
            _moveService = DestinationMoveService.Current;
            Items = new ObservableCollection<DestinationFolderPanelItem>();

            Config.Current.System.SubscribePropertyChanged(nameof(SystemConfig.DestinationFolderCollection),
                (s, e) => Refresh());
            PageFrameBoxPresenter.Current.ViewPageChanged += (s, e) => UpdateCommandStates();
            BookOperation.Current.BookChanged += (s, e) => UpdateCommandStates();
            _moveService.StateChanged += (s, e) => UpdateCommandStates();

            Refresh();
        }

        /// <summary>
        /// Get every configured destination folder shown by the panel.
        /// </summary>
        public ObservableCollection<DestinationFolderPanelItem> Items { get; }

        /// <summary>
        /// Get whether the panel contains a clickable destination.
        /// </summary>
        public bool HasItems => Items.Count > 0;

        /// <summary>
        /// Get whether a destination move is in progress.
        /// </summary>
        public bool IsBusy => _moveService.IsBusy;

        /// <summary>
        /// Rebuild the complete destination-folder list from current configuration.
        /// </summary>
        public void Refresh()
        {
            Items.Clear();

            // The panel has no item limit; the first nine remain aligned with numeric shortcuts.
            foreach (var item in Config.Current.System.DestinationFolderCollection
                .Select((folder, index) => new DestinationFolderPanelItem(index + 1, folder)))
            {
                Items.Add(item);
            }

            OnPropertyChanged(nameof(HasItems));
            UpdateCommandStates();
        }

        /// <summary>
        /// Determine whether the destination can receive the current main image.
        /// </summary>
        /// <param name="item">Destination panel item.</param>
        /// <returns>True when the main image is movable and the service is idle.</returns>
        private bool CanMove(DestinationFolderPanelItem? item)
        {
            return item is not null
                && !_moveService.IsBusy
                && BookOperation.Current.Control.CanMoveToFolder(item.Folder, MultiPagePolicy.Once);
        }

        /// <summary>
        /// Move the current main image to the selected destination folder.
        /// </summary>
        /// <param name="item">Destination panel item.</param>
        [RelayCommand(CanExecute = nameof(CanMove))]
        private void Move(DestinationFolderPanelItem? item)
        {
            if (item is null) return;

            BookOperation.Current.Control.MoveToFolder(item.Folder, MultiPagePolicy.Once);
        }

        /// <summary>
        /// Determine whether a successful move can be undone.
        /// </summary>
        /// <returns>True when undo is available.</returns>
        private bool CanUndo()
        {
            return _moveService.CanUndo;
        }

        /// <summary>
        /// Undo the most recent successful move.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanUndo))]
        private async Task Undo()
        {
            await _moveService.UndoAsync(CancellationToken.None);
        }

        /// <summary>
        /// Determine whether an undone move can be redone.
        /// </summary>
        /// <returns>True when redo is available.</returns>
        private bool CanRedo()
        {
            return _moveService.CanRedo;
        }

        /// <summary>
        /// Redo the most recently undone move.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanRedo))]
        private async Task Redo()
        {
            await _moveService.RedoAsync(CancellationToken.None);
        }

        /// <summary>
        /// Open the existing destination-folder management dialog.
        /// </summary>
        [RelayCommand]
        private void Manage()
        {
            DestinationFolderDialog.ShowDialog(MainViewComponent.Current.GetWindow());
        }

        /// <summary>
        /// Determine whether destinations can be refreshed from the current image folder.
        /// </summary>
        /// <returns>True when a regular file-system image is current and no folder operation is running.</returns>
        private bool CanRefreshFromCurrentFolder()
        {
            return !_isFolderOperationBusy && TryGetCurrentImageDirectory(out _);
        }

        /// <summary>
        /// Replace destinations with the immediate child folders of the current image folder.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanRefreshFromCurrentFolder))]
        private async Task RefreshFromCurrentFolder()
        {
            if (!TryGetCurrentImageDirectory(out var currentDirectory)) return;

            SetFolderOperationBusy(true);
            try
            {
                await RefreshDestinationFoldersAsync(currentDirectory);
            }
            catch (Exception ex)
            {
                ShowFolderOperationError(ex, "DestinationFolderPanel.RefreshFailed");
            }
            finally
            {
                SetFolderOperationBusy(false);
            }
        }

        /// <summary>
        /// Determine whether a direct child folder can be created from the current input.
        /// </summary>
        /// <returns>True when the name and current image folder are valid and no folder operation is running.</returns>
        private bool CanCreateFolder()
        {
            return !_isFolderOperationBusy
                && !string.IsNullOrWhiteSpace(NewFolderName)
                && TryGetCurrentImageDirectory(out _);
        }

        /// <summary>
        /// Create a direct child folder, refresh destinations, and move the current image into it.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanCreateFolder))]
        private async Task CreateFolder()
        {
            if (!TryGetCurrentImageDirectory(out var currentDirectory)) return;
            if (!TryCreateChildDirectoryPath(currentDirectory, NewFolderName, out var destinationPath))
            {
                ToastService.Current.Show(new Toast(
                    TextResources.GetString("DestinationFolderPanel.InvalidFolderName"),
                    TextResources.GetString("DestinationFolderPanel.CreateFailed"),
                    ToastIcon.Warning));
                return;
            }

            SetFolderOperationBusy(true);
            try
            {
                // Run file-system work in the background so mapped network folders do not block the panel.
                await Task.Run(() => Directory.CreateDirectory(destinationPath));
                NewFolderName = "";
                await RefreshDestinationFoldersAsync(currentDirectory);

                // Reuse the existing move command to retain next-page behavior and session undo history.
                var destinationFolder = new DestinationFolder(Path.GetFileName(destinationPath), destinationPath);
                if (BookOperation.Current.Control.CanMoveToFolder(destinationFolder, MultiPagePolicy.Once))
                {
                    BookOperation.Current.Control.MoveToFolder(destinationFolder, MultiPagePolicy.Once);
                }
            }
            catch (Exception ex)
            {
                ShowFolderOperationError(ex, "DestinationFolderPanel.CreateFailed");
            }
            finally
            {
                SetFolderOperationBusy(false);
            }
        }

        /// <summary>
        /// Get the real file-system directory containing the current main image.
        /// </summary>
        /// <param name="directory">Receives the containing directory on success.</param>
        /// <returns>True when the current page is a regular file-system image in an existing directory.</returns>
        private static bool TryGetCurrentImageDirectory(out string directory)
        {
            var page = BookOperation.Current.Book?.CurrentPage;
            if (page?.ArchiveEntry.Archive is not FolderArchive
                || !page.ArchiveEntry.IsFileSystem
                || page.ArchiveEntry.IsShortcut)
            {
                directory = "";
                return false;
            }

            directory = Path.GetDirectoryName(page.TargetPath) ?? "";
            return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory);
        }

        /// <summary>
        /// Enumerate immediate child folders and replace the global destination-folder configuration.
        /// </summary>
        /// <param name="currentDirectory">Directory containing the current main image.</param>
        private static async Task RefreshDestinationFoldersAsync(string currentDirectory)
        {
            var folders = await Task.Run(() => EnumerateDestinationFolders(currentDirectory));

            // Replace the collection reference so panels, commands, and numeric shortcuts refresh together.
            Config.Current.System.DestinationFolderCollection = new DestinationFolderCollection(folders);
        }

        /// <summary>
        /// Enumerate a directory non-recursively and produce stably sorted destination folders.
        /// </summary>
        /// <param name="currentDirectory">Parent directory to read.</param>
        /// <returns>Immediate child folders sorted by name.</returns>
        private static List<DestinationFolder> EnumerateDestinationFolders(string currentDirectory)
        {
            return Directory.EnumerateDirectories(currentDirectory, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
                .Select(path => new DestinationFolder(Path.GetFileName(path), path))
                .ToList();
        }

        /// <summary>
        /// Validate the input and create a path constrained to a direct child of the image directory.
        /// </summary>
        /// <param name="parentDirectory">Directory containing the current image.</param>
        /// <param name="input">Folder name entered by the user.</param>
        /// <param name="destinationPath">Receives the full path when validation succeeds.</param>
        /// <returns>True when the input is safe to use as a direct child folder name.</returns>
        private static bool TryCreateChildDirectoryPath(string parentDirectory, string input, out string destinationPath)
        {
            var name = input.Trim();
            destinationPath = "";
            if (string.IsNullOrEmpty(name)
                || name is "." or ".."
                || name.EndsWith(".", StringComparison.Ordinal)
                || FileIO.ContainsInvalidFileNameChars(name)
                || IsReservedWindowsName(name))
            {
                return false;
            }

            try
            {
                var fullParentPath = Path.GetFullPath(parentDirectory);
                var fullDestinationPath = Path.GetFullPath(Path.Combine(fullParentPath, name));
                var destinationParent = Path.GetDirectoryName(fullDestinationPath);

                // Recheck the normalized parent so the input cannot escape the current image directory.
                if (destinationParent is null
                    || !string.Equals(
                        destinationParent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        fullParentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                destinationPath = fullDestinationPath;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }

        /// <summary>
        /// Determine whether the name is a reserved Windows device name.
        /// </summary>
        /// <param name="name">Folder name to inspect.</param>
        /// <returns>True when the name cannot represent a normal directory.</returns>
        private static bool IsReservedWindowsName(string name)
        {
            var stem = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
            return stem is "CON" or "PRN" or "AUX" or "NUL"
                || (stem.Length == 4
                    && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                    && stem[3] is >= '1' and <= '9');
        }

        /// <summary>
        /// Update folder-operation busy state and reevaluate related commands.
        /// </summary>
        /// <param name="isBusy">Whether a folder operation is running.</param>
        private void SetFolderOperationBusy(bool isBusy)
        {
            _isFolderOperationBusy = isBusy;
            RefreshFromCurrentFolderCommand.NotifyCanExecuteChanged();
            CreateFolderCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Show a folder-operation error with a localized caption.
        /// </summary>
        /// <param name="exception">File-system exception.</param>
        /// <param name="captionResourceKey">Resource key for the error caption.</param>
        private static void ShowFolderOperationError(Exception exception, string captionResourceKey)
        {
            ToastService.Current.Show(new Toast(
                exception.Message,
                TextResources.GetString(captionResourceKey),
                ToastIcon.Error));
        }

        /// <summary>
        /// Notify panel properties and generated commands to recalculate state.
        /// </summary>
        private void UpdateCommandStates()
        {
            OnPropertyChanged(nameof(IsBusy));
            MoveCommand.NotifyCanExecuteChanged();
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
            RefreshFromCurrentFolderCommand.NotifyCanExecuteChanged();
            CreateFolderCommand.NotifyCanExecuteChanged();
        }
    }
}
