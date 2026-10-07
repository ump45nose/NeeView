namespace NeeView.Engine.Tests;
public sealed class PrintLayoutTests
{
    [Fact] public void FourByFourMeansSixteenSheetsOfOneScene()
    { var r=PrintLayout.Create(new(new(){Columns=4,Rows=4,MarginMm=new(10,5,10,5)},1000,1000),new(960,960));
      Assert.Equal(37.7952,r.EffectiveMargin.Left,3);Assert.Equal(884.4096,r.CellWidth,3);Assert.Equal(16,r.Pages.Count);
      Assert.Equal(r.Pages[0].ContentRect.X-r.CellWidth,r.Pages[1].ContentRect.X,5);Assert.Equal(r.Pages[0].ContentRect.Y-r.CellHeight,r.Pages[4].ContentRect.Y,5); }
    [Theory][InlineData(PrintMode.RawImage)][InlineData(PrintMode.ViewStretch)][InlineData(PrintMode.View)][InlineData(PrintMode.ViewFill)]
    public void EveryModeKeepsConfiguredSheetCountAndPrinterMargins(PrintMode mode)
    {var r=PrintLayout.Create(new(new(){Mode=mode,Columns=2,Rows=3},400,600,1000,700,-200,-300),new(800,600,new(20,10,30,40)));
     Assert.Equal(6,r.Pages.Count);Assert.All(r.Pages,p=>Assert.Equal(new PrintPageRect(20,10,750,550),p.CellRect)); }
    [Fact] public void BadSizesRejectedBeforeAnyAllocation()
    {Assert.Throws<ArgumentException>(()=>PrintLayout.Create(new(new(),double.NaN,3),new(800,600)));}
}
