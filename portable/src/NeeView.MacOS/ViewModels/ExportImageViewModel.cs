using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView.MacOS.ViewModels;

/// <summary>原导出参数的独立表单草稿与命名预览；不读取图片/文件，不保存配置。</summary>
public sealed class ExportImageViewModel : ObservableObject
{
    private readonly IExportPageSource _source;
    private string _error = "";
    public ExportImageViewModel(IExportImageParameter source, IExportPageSource pages, bool book)
    {
        Draft = book ? new ExportBookParameter(source, (source as ExportBookParameter)?.BookType ?? ExportBookType.Zip) : new ExportImageParameter(source);
        if (book && Draft.OverwriteMode == ExportImageOverwriteMode.Confirm) Draft.OverwriteMode = ExportImageOverwriteMode.Disallow;
        _source = pages; IsBook = book;
        Draft.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(PreviewName)); OnPropertyChanged(nameof(IsView)); };
    }
    public ExportImageParameter Draft { get; }
    public bool IsBook { get; }
    public ExportBookType BookType { get => (Draft as ExportBookParameter)?.BookType ?? ExportBookType.Zip; set { if (Draft is ExportBookParameter book) { book.BookType = value; OnPropertyChanged(); } } }
    public bool IsView => Draft.Mode == ExportImageMode.View;
    public ExportImageMode[] Modes => Enum.GetValues<ExportImageMode>();
    public BitmapImageFormat[] Formats => Enum.GetValues<BitmapImageFormat>();
    public ExportBookType[] BookTypes => Enum.GetValues<ExportBookType>();
    public ExportImageOverwriteMode[] OverwriteModes => IsBook ? [ExportImageOverwriteMode.AddNumber, ExportImageOverwriteMode.Disallow] : Enum.GetValues<ExportImageOverwriteMode>();
    public string Error { get => _error; set => SetProperty(ref _error, value); }
    public string PreviewName
    {
        get { try { return new DefaultExportImageFileNamePolicy(Draft).CreateFileName(_source, 1); } catch (Exception ex) { return ex.Message; } }
    }
}
