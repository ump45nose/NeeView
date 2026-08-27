using System.Windows.Controls;

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
            Root.DataContext = _viewModel;
        }

        /// <summary>
        /// 从当前配置刷新目标文件夹列表。
        /// </summary>
        public void Refresh()
        {
            _viewModel.Refresh();
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
