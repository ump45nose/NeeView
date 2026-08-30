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
    /// 目标文件夹面板的显示状态和交互命令。
    /// </summary>
    public partial class DestinationFolderPanelViewModel : ObservableObject
    {
        private readonly DestinationMoveService _moveService;
        private bool _isFolderOperationBusy;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CreateFolderCommand))]
        private string _newFolderName = "";

        /// <summary>
        /// 初始化目标文件夹列表，并订阅配置、页面和移动历史变化。
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
        /// 获取面板中显示的全部目标文件夹。
        /// </summary>
        public ObservableCollection<DestinationFolderPanelItem> Items { get; }

        /// <summary>
        /// 获取面板是否包含可点击的目标文件夹。
        /// </summary>
        public bool HasItems => Items.Count > 0;

        /// <summary>
        /// 获取当前是否正在执行目标文件夹移动。
        /// </summary>
        public bool IsBusy => _moveService.IsBusy;

        /// <summary>
        /// 从当前配置重新构建完整的目标文件夹列表。
        /// </summary>
        public void Refresh()
        {
            Items.Clear();

            // 面板不限制数量；前九项仍与原生命令和脚本的数字快捷键保持一致。
            foreach (var item in Config.Current.System.DestinationFolderCollection
                .Select((folder, index) => new DestinationFolderPanelItem(index + 1, folder)))
            {
                Items.Add(item);
            }

            OnPropertyChanged(nameof(HasItems));
            UpdateCommandStates();
        }

        /// <summary>
        /// 检查指定目标文件夹是否可以接收当前主图片。
        /// </summary>
        /// <param name="item">面板目标项</param>
        /// <returns>当前主图片可移动且服务空闲时返回 true</returns>
        private bool CanMove(DestinationFolderPanelItem? item)
        {
            return item is not null
                && !_moveService.IsBusy
                && BookOperation.Current.Control.CanMoveToFolder(item.Folder, MultiPagePolicy.Once);
        }

        /// <summary>
        /// 将当前主图片移动到点击的目标文件夹。
        /// </summary>
        /// <param name="item">面板目标项</param>
        [RelayCommand(CanExecute = nameof(CanMove))]
        private void Move(DestinationFolderPanelItem? item)
        {
            if (item is null) return;

            BookOperation.Current.Control.MoveToFolder(item.Folder, MultiPagePolicy.Once);
        }

        /// <summary>
        /// 检查是否存在可撤销的成功移动。
        /// </summary>
        /// <returns>可以撤销时返回 true</returns>
        private bool CanUndo()
        {
            return _moveService.CanUndo;
        }

        /// <summary>
        /// 撤销最近一次成功移动。
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanUndo))]
        private async Task Undo()
        {
            await _moveService.UndoAsync(CancellationToken.None);
        }

        /// <summary>
        /// 检查是否存在可重做的成功撤销。
        /// </summary>
        /// <returns>可以重做时返回 true</returns>
        private bool CanRedo()
        {
            return _moveService.CanRedo;
        }

        /// <summary>
        /// 重做最近一次成功撤销。
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanRedo))]
        private async Task Redo()
        {
            await _moveService.RedoAsync(CancellationToken.None);
        }

        /// <summary>
        /// 打开现有的目标文件夹管理对话框。
        /// </summary>
        [RelayCommand]
        private void Manage()
        {
            DestinationFolderDialog.ShowDialog(MainViewComponent.Current.GetWindow());
        }

        /// <summary>
        /// 检查是否可以从当前主图片所在文件夹刷新目标文件夹列表。
        /// </summary>
        /// <returns>存在普通文件系统图片且没有其他目录操作时返回 true</returns>
        private bool CanRefreshFromCurrentFolder()
        {
            return !_isFolderOperationBusy && TryGetCurrentImageDirectory(out _);
        }

        /// <summary>
        /// 将当前主图片所在文件夹的直接子文件夹设为目标文件夹。
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
        /// 检查输入的名称是否可以在当前图片文件夹中创建直接子文件夹。
        /// </summary>
        /// <returns>名称有效、当前图片可定位且没有其他目录操作时返回 true</returns>
        private bool CanCreateFolder()
        {
            return !_isFolderOperationBusy
                && !string.IsNullOrWhiteSpace(NewFolderName)
                && TryGetCurrentImageDirectory(out _);
        }

        /// <summary>
        /// 在当前主图片所在文件夹创建直接子文件夹，刷新列表并将当前图片移入其中。
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
                // 文件系统操作放到后台执行，避免网络映射目录阻塞面板界面。
                await Task.Run(() => Directory.CreateDirectory(destinationPath));
                NewFolderName = "";
                await RefreshDestinationFoldersAsync(currentDirectory);

                // 复用现有移动命令，使自动分类继续获得下一页行为和会话级撤销记录。
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
        /// 获取当前主图片所在的真实文件系统目录。
        /// </summary>
        /// <param name="directory">成功时返回图片所在目录</param>
        /// <returns>当前页是普通文件系统图片且目录存在时返回 true</returns>
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
        /// 枚举指定目录的直接子文件夹，并用结果替换全局目标文件夹配置。
        /// </summary>
        /// <param name="currentDirectory">当前主图片所在目录</param>
        private static async Task RefreshDestinationFoldersAsync(string currentDirectory)
        {
            var folders = await Task.Run(() => EnumerateDestinationFolders(currentDirectory));

            // 替换整个集合以触发配置通知，让面板、命令和数字快捷键同步刷新。
            Config.Current.System.DestinationFolderCollection = new DestinationFolderCollection(folders);
        }

        /// <summary>
        /// 非递归枚举目录，并生成稳定排序的目标文件夹对象。
        /// </summary>
        /// <param name="currentDirectory">要读取的父目录</param>
        /// <returns>按文件夹名称排序的直接子文件夹列表</returns>
        private static List<DestinationFolder> EnumerateDestinationFolders(string currentDirectory)
        {
            return Directory.EnumerateDirectories(currentDirectory, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
                .Select(path => new DestinationFolder(Path.GetFileName(path), path))
                .ToList();
        }

        /// <summary>
        /// 验证输入名称并生成受限于当前图片目录的直接子目录路径。
        /// </summary>
        /// <param name="parentDirectory">当前图片所在目录</param>
        /// <param name="input">用户输入的文件夹名称</param>
        /// <param name="destinationPath">验证成功时返回完整目标路径</param>
        /// <returns>输入可安全用作直接子文件夹名称时返回 true</returns>
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

                // 规范化后再次核对父目录，确保输入不能越出当前图片目录。
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
        /// 检查名称是否为 Windows 保留设备名。
        /// </summary>
        /// <param name="name">待检查的文件夹名称</param>
        /// <returns>名称不能用于普通目录时返回 true</returns>
        private static bool IsReservedWindowsName(string name)
        {
            var stem = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
            return stem is "CON" or "PRN" or "AUX" or "NUL"
                || (stem.Length == 4
                    && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                    && stem[3] is >= '1' and <= '9');
        }

        /// <summary>
        /// 更新目录操作忙碌状态并重新计算相关按钮可用性。
        /// </summary>
        /// <param name="isBusy">是否正在执行目录操作</param>
        private void SetFolderOperationBusy(bool isBusy)
        {
            _isFolderOperationBusy = isBusy;
            RefreshFromCurrentFolderCommand.NotifyCanExecuteChanged();
            CreateFolderCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// 以本地化标题显示目录操作错误。
        /// </summary>
        /// <param name="exception">文件系统异常</param>
        /// <param name="captionResourceKey">错误标题资源键</param>
        private static void ShowFolderOperationError(Exception exception, string captionResourceKey)
        {
            ToastService.Current.Show(new Toast(
                exception.Message,
                TextResources.GetString(captionResourceKey),
                ToastIcon.Error));
        }

        /// <summary>
        /// 通知面板属性和生成命令重新计算可执行状态。
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
