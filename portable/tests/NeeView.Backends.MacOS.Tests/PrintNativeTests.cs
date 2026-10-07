using ImageMagick;
using NeeView.Backends;
using NeeView;
namespace NeeView.Backends.MacOS.Tests;

/// <summary>同一 AppKit 打印链无窗口保存隔离 PDF；不操作物理打印机或用户桌面。</summary>
public sealed class PrintNativeTests
{
    [Theory]
    [InlineData(PrintOrientation.Portrait)]
    [InlineData(PrintOrientation.Landscape)]
    public async Task NativePrintSavesConfiguredSheetsWithDifferentCrops(PrintOrientation orientation)
    {
        var root=Path.Combine(Path.GetTempPath(),"neeview-print-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            using var image=new MagickImage(MagickColors.Red,800,400);
            using var right=new MagickImage(MagickColors.Blue,400,400);image.Composite(right,400,0,CompositeOperator.Over);
            var content=new PrintImage(image.ToByteArray(MagickFormat.Png),800,400,800,400);
            var output=Path.Combine(root,"sheets.pdf");
            Assert.True(await new MacPrintService().SavePdfAsync(content,new(){Columns=2,Rows=1,Orientation=orientation,IsDotScale=true,HorizontalAlignment=PrintHorizontalAlignment.Left},output,TestContext.Current.CancellationToken));
            var renderer=new MacPdfRenderer();var pdf=renderer.Inspect(output,TestContext.Current.CancellationToken);Assert.Equal(2,pdf.Pages.Count);
            Assert.All(pdf.Pages,size=>Assert.Equal(orientation==PrintOrientation.Landscape,size.Width>size.Height));
            using var first=renderer.Render(output,0,new(300,300),TestContext.Current.CancellationToken);
            using var second=renderer.Render(output,1,new(300,300),TestContext.Current.CancellationToken);
            Assert.Contains(Enumerable.Range(0,first.Pixels.Length/4),i=>first.Pixels[i*4+2]>200&&first.Pixels[i*4]<30);
            Assert.Contains(Enumerable.Range(0,second.Pixels.Length/4),i=>second.Pixels[i*4]>200&&second.Pixels[i*4+2]<30);
            Assert.DoesNotContain(Enumerable.Range(0,second.Pixels.Length/4),i=>second.Pixels[i*4+2]>200&&second.Pixels[i*4]<30);
        }
        finally { Directory.Delete(root,true); }
    }
    [Fact]
    public async Task CanceledPrintNeverCreatesOutput()
    {
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        var output=Path.Combine(Path.GetTempPath(),"neeview-canceled-"+Guid.NewGuid().ToString("N")+".pdf");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new MacPrintService().SavePdfAsync(new([],1,1,1,1),new(),output,cancel.Token));
        Assert.False(File.Exists(output));
    }
    [Fact]
    public async Task PrintBackgroundCoversPaperOutsideImageBounds()
    {
        var root=Path.Combine(Path.GetTempPath(),"neeview-print-background-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            using var content=new MagickImage(MagickColors.Red,400,400);using var background=new MagickImage(MagickColors.Blue,16,16);
            var output=Path.Combine(root,"background.pdf");
            Assert.True(await new MacPrintService().SavePdfAsync(new(content.ToByteArray(MagickFormat.Png),400,400,400,400){BackgroundPng=background.ToByteArray(MagickFormat.Png)},new(){IsBackground=true},output,TestContext.Current.CancellationToken));
            using var page=new MacPdfRenderer().Render(output,0,new(300,300),TestContext.Current.CancellationToken);
            Assert.Contains(Enumerable.Range(0,page.Pixels.Length/4),i=>page.Pixels[i*4]>200&&page.Pixels[i*4+2]<30);
            Assert.Contains(Enumerable.Range(0,page.Pixels.Length/4),i=>page.Pixels[i*4+2]>200&&page.Pixels[i*4]<30);
        }
        finally {Directory.Delete(root,true);}
    }
}
