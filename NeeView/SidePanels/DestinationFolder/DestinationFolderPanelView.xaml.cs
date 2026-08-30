using System.Windows.Controls;
using System.Windows.Input;

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

            // Item buttons resolve MoveCommand from the ancestor UserControl, so bind the view itself.
            DataContext = _viewModel;
        }

        /// <summary>
        /// Refresh destination folders from the current configuration.
        /// </summary>
        public void Refresh()
        {
            _viewModel.Refresh();
        }

        /// <summary>
        /// Execute the same command as the create-folder button when Enter is pressed.
        /// </summary>
        /// <param name="sender">New-folder name text box.</param>
        /// <param name="e">Keyboard event arguments.</param>
        private void NewFolderNameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;

            // Route both interactions through one command so validation and busy state stay consistent.
            if (_viewModel.CreateFolderCommand.CanExecute(null))
            {
                _viewModel.CreateFolderCommand.Execute(null);
                e.Handled = true;
            }
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
