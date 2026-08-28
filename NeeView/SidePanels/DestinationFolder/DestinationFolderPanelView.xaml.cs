using System.Windows.Controls;

namespace NeeView
{
    /// <summary>
    /// Interaction logic for DestinationFolderPanelView.xaml.
    /// </summary>
    public partial class DestinationFolderPanelView : UserControl
    {
        private readonly DestinationFolderPanelViewModel _viewModel;

        /// <summary>
        /// Initialize the panel and bind its view model.
        /// </summary>
        public DestinationFolderPanelView()
        {
            InitializeComponent();
            _viewModel = new DestinationFolderPanelViewModel();
            Root.DataContext = _viewModel;
        }

        /// <summary>
        /// Refresh destination folders from the current configuration.
        /// </summary>
        public void Refresh()
        {
            _viewModel.Refresh();
        }

        /// <summary>
        /// Move keyboard focus into the panel.
        /// </summary>
        public void FocusAtOnce()
        {
            Focus();
        }
    }
}
