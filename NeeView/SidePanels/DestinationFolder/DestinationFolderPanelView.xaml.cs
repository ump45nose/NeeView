using System.Windows.Controls;
using System.Windows.Input;

namespace NeeView
{
    /// <summary>
    /// DestinationFolderPanelView.xaml 的交互逻辑。
    /// </summary>
    public partial class DestinationFolderPanelView : UserControl
    {
        private readonly DestinationFolderPanelViewModel _viewModel;

        /// <summary>
        /// 初始化面板并绑定目标文件夹视图模型。
        /// </summary>
        public DestinationFolderPanelView()
        {
            InitializeComponent();
            _viewModel = new DestinationFolderPanelViewModel();

            // 按钮从祖先 UserControl 读取 MoveCommand，因此必须在视图本身设置 DataContext。
            DataContext = _viewModel;
        }

        /// <summary>
        /// 从当前配置刷新目标文件夹列表。
        /// </summary>
        public void Refresh()
        {
            _viewModel.Refresh();
        }

        /// <summary>
        /// 在新文件夹输入框按下回车时执行与“新增文件夹”按钮相同的命令。
        /// </summary>
        /// <param name="sender">新文件夹名称输入框</param>
        /// <param name="e">键盘事件参数</param>
        private void NewFolderNameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;

            // 统一通过 ViewModel 命令执行，确保按钮和回车共用校验与忙碌状态。
            if (_viewModel.CreateFolderCommand.CanExecute(null))
            {
                _viewModel.CreateFolderCommand.Execute(null);
                e.Handled = true;
            }
        }

        /// <summary>
        /// 将键盘焦点移入面板。
        /// </summary>
        public void FocusAtOnce()
        {
            Focus();
        }
    }
}
