// Copyright (c) NeeLaboratory. 原 PrintModel 的系统输出替换点；官方 AppKit 绑定，MIT。
using AppKit;
using CoreGraphics;
using Foundation;
namespace NeeView.Backends;

/// <summary>系统打印对话框和纸张能力；所有系统对象由请求在主线程持有并释放。</summary>
public sealed class MacPrintService : IPrintService
{
    private static readonly SemaphoreSlim Slot = new(1);
    private static NSPrintInfo? _lastInfo;
    public Task<bool> PrintAsync(PrintImage image, PrintParameters parameters, CancellationToken token = default)
        => RunAsync(image, parameters, null, token);
    /// <summary>无面板原生回归通过同一打印operation保存PDF；不向物理打印机提交。</summary>
    internal Task<bool> SavePdfAsync(PrintImage image, PrintParameters parameters, string destination, CancellationToken token = default)
        => RunAsync(image, parameters, Path.GetFullPath(destination), token);
    private async Task<bool> RunAsync(PrintImage image, PrintParameters parameters, string? destination, CancellationToken token)
    {
        await Slot.WaitAsync(token);
        try { return await VideoMain.RunAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            using var info = new NSPrintInfo((_lastInfo ?? NSPrintInfo.SharedPrintInfo).Dictionary);
            info.Orientation=parameters.Orientation==PrintOrientation.Landscape?NSPrintingOrientation.Landscape:NSPrintingOrientation.Portrait;
            info.HorizontallyCentered=false;info.VerticallyCentered=false;
            // NSView自己按完整纸张坐标计算一次硬件/附加边距；AppKit不能再平移一次。
            info.LeftMargin=0;info.RightMargin=0;info.TopMargin=0;info.BottomMargin=0;info.ScalingFactor=1;
            // 导出的NSString常量值不等于符号名（NSPrintJobSavingURL实际为NSJobSavingURL）。
            using var saveKey = PrintConstant("NSPrintJobSavingURL");
            using var disposition = PrintConstant(destination is null ? "NSPrintSpoolJob" : "NSPrintSaveJob");
            info.Dictionary.Remove(saveKey); info.JobDisposition = disposition.ToString();
            using var outputUrl = destination is null ? null : NSUrl.FromFilename(destination);
            if (outputUrl is not null) info.Dictionary[saveKey] = outputUrl;
            using var view=new PrintedScene(image,parameters,info);
            using var operation=NSPrintOperation.FromView(view,info);
            operation.ShowsPrintPanel=destination is null;operation.ShowsProgressPanel=destination is null;
            var success=operation.RunOperation();
            if(success && destination is null){_lastInfo?.Dispose();_lastInfo=new NSPrintInfo(operation.PrintInfo.Dictionary);}
            // RunOperation取消返回false；已经提交的系统任务不伪装成取消。
            return success;
        }); }
        finally { Slot.Release(); }
    }
    private static NSString PrintConstant(string symbol)
    {
        var library=ObjCRuntime.Dlfcn.dlopen("/System/Library/Frameworks/AppKit.framework/AppKit",0);
        try { return ObjCRuntime.Dlfcn.GetStringConstant(library,symbol) ?? throw new NotSupportedException("系统打印常量不可用："+symbol); }
        finally { if(library!=IntPtr.Zero)ObjCRuntime.Dlfcn.dlclose(library); }
    }
    private sealed class PrintedScene : NSView
    {
        private readonly PrintImage _source; private readonly PrintParameters _parameters; private readonly NSPrintInfo _info;
        private readonly NSData _data; private readonly NSImage _image;
        private readonly NSData? _backgroundData; private readonly NSImage? _background;
        public PrintedScene(PrintImage source,PrintParameters parameters,NSPrintInfo info)
            :base(new CGRect(0,0,info.PaperSize.Width,info.PaperSize.Height*Math.Clamp(parameters.Rows,1,4)*Math.Clamp(parameters.Columns,1,4)))
        {_source=source;_parameters=parameters;_info=info;_data=NSData.FromArray(source.Png);_image=new NSImage(_data);
         if(parameters.IsBackground&&source.BackgroundPng is { } background){_backgroundData=NSData.FromArray(background);_background=new NSImage(_backgroundData);}}
        public override bool IsFlipped=>true;
        private NSPrintInfo Info=>NSPrintOperation.CurrentOperation?.PrintInfo??_info;
        public override CGPoint LocationOfPrintRect(CGRect rect)=>new(0,0);
        public override bool KnowsPageRange(ref NSRange range)
        {range=new NSRange(1,Math.Clamp(_parameters.Columns,1,4)*Math.Clamp(_parameters.Rows,1,4));Frame=new(0,0,Info.PaperSize.Width,Info.PaperSize.Height*range.Length);return true;}
        public override CGRect RectForPage(nint page)=>new(0,(page-1)*Info.PaperSize.Height,Info.PaperSize.Width,Info.PaperSize.Height);
        public override void DrawRect(CGRect dirtyRect)
        {
            var paper=Info.PaperSize;var area=Info.ImageablePageBounds; const double factor=96.0/72;
            var caps=new PrintSystemCapabilities((double)paper.Width*factor,(double)paper.Height*factor,new(
                (double)area.X*factor,(double)(paper.Height-area.Y-area.Height)*factor,
                (double)(paper.Width-area.X-area.Width)*factor,(double)area.Y*factor));
            var layout=PrintLayout.Create(new(_parameters,_source.Width,_source.Height,_source.ViewWidth,_source.ViewHeight,_source.X,_source.Y),caps);
            var index=Math.Clamp((int)(NSPrintOperation.CurrentOperation?.CurrentPage??1)-1,0,layout.Pages.Count-1);
            var page=layout.Pages[index]; var offset=index*(double)paper.Height;
            var context=NSGraphicsContext.CurrentContext?.CGContext??throw new IOException("系统打印绘制上下文不可用。");
            var graphics = NSGraphicsContext.CurrentContext!;
            var interpolation=graphics.ImageInterpolation;
            context.SaveState();
            try
            {
                graphics.ImageInterpolation=NSImageInterpolation.High;
                var clip=page.CellRect;
                context.ClipToRect(new(clip.X/factor,clip.Y/factor+offset,clip.Width/factor,clip.Height/factor));
                if(_background is not null)
                {
                    var columns=Math.Clamp(_parameters.Columns,1,4);var rows=Math.Clamp(_parameters.Rows,1,4);
                    _background.Draw(new CGRect((clip.X-index%columns*clip.Width)/factor,(clip.Y-index/columns*clip.Height)/factor+offset,
                        clip.Width*columns/factor,clip.Height*rows/factor),new CGRect(0,0,_background.Size.Width,_background.Size.Height),NSCompositingOperation.SourceOver,1,true,null);
                }
                graphics.ImageInterpolation=_parameters.IsDotScale ? NSImageInterpolation.None : NSImageInterpolation.High;
                var rect=page.ContentRect;
                _image.Draw(new CGRect(rect.X/factor,rect.Y/factor+offset,rect.Width/factor,rect.Height/factor),
                    new CGRect(0,0,_image.Size.Width,_image.Size.Height),NSCompositingOperation.SourceOver,1,true,null);
            }
            finally {graphics.ImageInterpolation=interpolation;context.RestoreState();}
        }
        protected override void Dispose(bool disposing)
        {if(disposing){_background?.Dispose();_backgroundData?.Dispose();_image.Dispose();_data.Dispose();}base.Dispose(disposing);}
    }
}
