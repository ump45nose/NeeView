using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;
public sealed partial class PrintWindow : Window
{
    private CancellationTokenSource? _previewCancellation;
    private Bitmap? _bitmap;
    private bool _closed;
    public Func<PrintParameters,CancellationToken,Task<PrintImage>>? PreviewAsync { get; set; }
    public Task PendingPreview { get; private set; } = Task.CompletedTask;
    public PrintWindow() { Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this); }
    public PrintWindow(PrintViewModel model):this()
    {
        DataContext=model;var p=model.Parameters;
        Set("Mode",(int)p.Mode);Set("Orientation",(int)p.Orientation);Set("Horizontal",(int)p.HorizontalAlignment);Set("Vertical",(int)p.VerticalAlignment);
        Number("Columns").Value=p.Columns;Number("Rows").Value=p.Rows;
        this.FindControl<CheckBox>("PrintBackground")!.IsChecked=p.IsBackground;this.FindControl<CheckBox>("Nearest")!.IsChecked=p.IsDotScale;
        Number("LeftMargin").Value=(decimal)p.MarginMm.Left;Number("RightMargin").Value=(decimal)p.MarginMm.Right;Number("TopMargin").Value=(decimal)p.MarginMm.Top;Number("BottomMargin").Value=(decimal)p.MarginMm.Bottom;
        Closed+=(_,_)=>{_closed=true;_previewCancellation?.Cancel();this.FindControl<Image>("Preview")!.Source=null;_bitmap?.Dispose();_bitmap=null;};
    }
    private NumericUpDown Number(string name)=>this.FindControl<NumericUpDown>(name)!;
    private void Set(string name,int index)=>this.FindControl<ComboBox>(name)!.SelectedIndex=index;
    private int Index(string name)=>Math.Max(0,this.FindControl<ComboBox>(name)!.SelectedIndex);
    internal PrintParameters GetParameters()=>new(){Mode=(PrintMode)Index("Mode"),Orientation=(PrintOrientation)Index("Orientation"),
        Columns=(int)(Number("Columns").Value??1),Rows=(int)(Number("Rows").Value??1),HorizontalAlignment=(PrintHorizontalAlignment)Index("Horizontal"),VerticalAlignment=(PrintVerticalAlignment)Index("Vertical"),
        IsBackground=this.FindControl<CheckBox>("PrintBackground")!.IsChecked==true,IsDotScale=this.FindControl<CheckBox>("Nearest")!.IsChecked==true,
        MarginMm=new((double)(Number("LeftMargin").Value??0),(double)(Number("TopMargin").Value??0),(double)(Number("RightMargin").Value??0),(double)(Number("BottomMargin").Value??0))};
    private void ConfirmClick(object? sender,RoutedEventArgs e)=>Close(GetParameters());
    private void CancelClick(object? sender,RoutedEventArgs e)=>Close(null);
    private void PreviewClick(object? sender,RoutedEventArgs e)
    { if(_closed||!PendingPreview.IsCompleted||PreviewAsync is null)return;PendingPreview=UpdatePreviewAsync(); }
    private async Task UpdatePreviewAsync()
    {
        using var cancel=new CancellationTokenSource();_previewCancellation=cancel;
        try
        {
            var image=await PreviewAsync!(GetParameters(),cancel.Token);cancel.Token.ThrowIfCancellationRequested();
            if (_closed) return;
            using var stream=new MemoryStream(image.Png,false);var bitmap=new Bitmap(stream);_bitmap?.Dispose();_bitmap=bitmap;this.FindControl<Image>("Preview")!.Source=bitmap;
        }
        catch(OperationCanceledException){}
        catch(Exception error){if (!_closed) this.FindControl<TextBlock>("Message")!.Text="预览失败："+error.Message;}
        finally{if (ReferenceEquals(_previewCancellation,cancel)) _previewCancellation=null;}
    }
}
