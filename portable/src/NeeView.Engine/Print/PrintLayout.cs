// Copyright (c) NeeLaboratory. 原 PrintModel.CreateVisualElement/CreatePageCollection 的纯几何，MIT。
namespace NeeView;
public static class PrintLayout
{
    /// <summary>原毫米→96DPI换算与列/行铺纸；每张纸裁剪同一整场景，不按图像面积猜页数。</summary>
    public static PrintLayoutResult Create(PrintRequest request,PrintSystemCapabilities capabilities)
    {
        var p=request.Parameters; var columns=Math.Clamp(p.Columns,1,4);var rows=Math.Clamp(p.Rows,1,4);
        if (!double.IsFinite(request.ContentWidth)||!double.IsFinite(request.ContentHeight)||request.ContentWidth<=0||request.ContentHeight<=0
            || !double.IsFinite(capabilities.PrintableWidth)||!double.IsFinite(capabilities.PrintableHeight)||capabilities.PrintableWidth<=0||capabilities.PrintableHeight<=0)
            throw new ArgumentException("打印尺寸无效。");
        static double Margin(double hardware,double mm) => double.IsFinite(mm)&&double.IsFinite(hardware) ? Math.Max(0,hardware+mm*.039370*96) : throw new ArgumentException("打印边距无效。");
        var h=capabilities.HardwareMargin;var m=new PrintMargin(Margin(h.Left,p.MarginMm.Left),Margin(h.Top,p.MarginMm.Top),Margin(h.Right,p.MarginMm.Right),Margin(h.Bottom,p.MarginMm.Bottom));
        var w=Math.Max(1,capabilities.PrintableWidth-m.Left-m.Right);var height=Math.Max(1,capabilities.PrintableHeight-m.Top-m.Bottom);
        var canvasW=w*columns;var canvasH=height*rows;
        var clip=new PrintPageRect(request.ContentX,request.ContentY,request.ContentWidth,request.ContentHeight);
        if(p.Mode is PrintMode.View or PrintMode.ViewFill)
        {
            var vw=request.ViewWidth>0?request.ViewWidth:request.ContentWidth;var vh=request.ViewHeight>0?request.ViewHeight:request.ContentHeight;
            var cw=vw;var ch=vh;double ox=0,oy=0;
            if(p.Mode==PrintMode.ViewFill)
            {
                if(vw/vh>canvasW/canvasH) {ch=vw/(canvasW/canvasH);oy=(ch-vh)*((int)p.VerticalAlignment-1)*.5;}
                else {cw=vh*(canvasW/canvasH);ox=(cw-vw)*((int)p.HorizontalAlignment-1)*.5;}
            }
            clip=new(-cw/2-ox,-ch/2-oy,cw,ch);
        }
        var scale=Math.Min(canvasW/clip.Width,canvasH/clip.Height);
        var x=(canvasW-clip.Width*scale)*(int)p.HorizontalAlignment*.5+(request.ContentX-clip.X)*scale;
        var y=(canvasH-clip.Height*scale)*(int)p.VerticalAlignment*.5+(request.ContentY-clip.Y)*scale;
        var pages=new List<PrintLayoutPage>(columns*rows);
        for(int row=0;row<rows;row++)for(int col=0;col<columns;col++)
            pages.Add(new(pages.Count,new(m.Left+x-col*w,m.Top+y-row*height,request.ContentWidth*scale,request.ContentHeight*scale),new(m.Left,m.Top,w,height)));
        return new(m,w,height,pages);
    }
}
