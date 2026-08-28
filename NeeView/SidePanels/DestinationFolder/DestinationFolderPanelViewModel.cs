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
    /// 目标文件夹面板的显示状态和交互命令。
    /// </summary>
    public partial class DestinationFolderPanelViewModel : ObservableObject
    {
        private readonly DestinationMoveService _moveService;

        /// <summary>
        /// 初始化目标文件夹列表，并订阅配置、页面和移动历史变化。
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
        /// 获取面板中按配置数量显示的目标文件夹。
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
        /// 从当前配置重新构建可见的数字映射。
        /// </summary>
        public void Refresh()
        {
            Items.Clear();
            var visibleItemCount = Config.Current.Panels.DestinationFolderPanelItemCount;

            // 设置值已由配置模型限制在 1～9，数字映射始终与脚本快捷键范围一致。
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
        /// 通知面板属性和生成命令重新计算可执行状态。
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
