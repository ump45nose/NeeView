using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeeLaboratory.ComponentModel;
using NeeView.Properties;
using System;
using System.Collections.ObjectModel;
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

        /// <summary>
        /// Initialize destination folders and observe configuration, page, and move-history changes.
        /// </summary>
        public DestinationFolderPanelViewModel()
        {
            _moveService = DestinationMoveService.Current;
            Items = new ObservableCollection<DestinationFolderPanelItem>();

            Config.Current.System.SubscribePropertyChanged(nameof(SystemConfig.DestinationFolderCollection),
                (s, e) => Refresh());
            Config.Current.Panels.SubscribePropertyChanged(nameof(PanelsConfig.DestinationFolderPanelItemCount),
                (s, e) => Refresh());
            PageFrameBoxPresenter.Current.ViewPageChanged += (s, e) => UpdateCommandStates();
            BookOperation.Current.BookChanged += (s, e) => UpdateCommandStates();
            _moveService.StateChanged += (s, e) => UpdateCommandStates();

            Refresh();
        }

        /// <summary>
        /// Get destination folders limited by the configured visible count.
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
        /// Rebuild the visible numbered mapping from current configuration.
        /// </summary>
        public void Refresh()
        {
            Items.Clear();
            var visibleItemCount = Config.Current.Panels.DestinationFolderPanelItemCount;

            // The panel can show more folders while the first nine remain aligned with numeric shortcuts.
            foreach (var item in Config.Current.System.DestinationFolderCollection
                .Take(visibleItemCount)
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
        /// Notify panel properties and generated commands to recalculate state.
        /// </summary>
        private void UpdateCommandStates()
        {
            OnPropertyChanged(nameof(IsBusy));
            MoveCommand.NotifyCanExecuteChanged();
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        }
    }
}
