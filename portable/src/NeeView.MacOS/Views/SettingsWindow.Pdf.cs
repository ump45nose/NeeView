using Avalonia.Controls;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;
public sealed partial class SettingsWindow
{
    private PdfSettingsViewModel? _pdfSettings;
    private void FillPdf()
    {
        _pdfSettings = new(Config.Current.Archive.Pdf, Config.Current.Performance);
        this.FindControl<ScrollViewer>("ArchiveSettings")!.DataContext = _pdfSettings;
    }
}
