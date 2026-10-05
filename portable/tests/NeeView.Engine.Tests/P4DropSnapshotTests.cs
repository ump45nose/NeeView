using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>正式拖放适配的有限数据/所有权回归；不触碰真实系统剪贴板。</summary>
public sealed class P4DropSnapshotTests
{
    [AvaloniaFact]
    public void PlainTextDoesNotBecomeFileOrUrlAndQueryPathHasPriority()
    {
        using var transfer = new DataTransfer(); transfer.Add(DataTransferItem.CreateText("https://fixture.invalid/image.png"));
        var content = ContentDropSnapshot.Read(transfer); Assert.Empty(content.Files); Assert.Null(content.Content);
        transfer.Add(DataTransferItem.Create(DataFormat.CreateStringPlatformFormat(FileClipboardCodec.QueryPathsType), FileClipboardCodec.EncodeQueryPaths(["/fixture/image.png"])));
        transfer.Add(DataTransferItem.Create(DataFormat.CreateBytesPlatformFormat("public.png"), new byte[64 * 1024 * 1024 + 1]));
        Assert.Equal("/fixture/image.png", Assert.Single(ContentDropSnapshot.Read(transfer).QueryPaths));
    }
    [AvaloniaFact]
    public void StandardBrowserUrlIsPassedToBusinessReceiver()
    {
        using var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(DataFormat.CreateStringPlatformFormat("public.url"), "https://fixture.invalid/image.png"));
        var content = ContentDropSnapshot.Read(transfer); Assert.Empty(content.Files); Assert.Equal("https://fixture.invalid/image.png", Assert.Single(content.Content!.WebUrls!));
    }
    [AvaloniaFact]
    public void BitmapSnapshotBorrowsSenderResourceAndCopiesEncodedBytes()
    {
        using var f = new Fixture(); using var bitmap = new Bitmap(System.IO.Path.Combine(f.Images, "001.png")); using var transfer = new DataTransfer();
        var item = new DataTransferItem(); item.SetBitmap(bitmap); transfer.Add(item);
        var image = Assert.Single(ContentDropSnapshot.Read(transfer).Content!.Images); Assert.True(image.IsBitmap); Assert.NotEmpty(image.Bytes);
        using var encoded = new MemoryStream(); bitmap.Save(encoded, PngBitmapEncoderOptions.Default); Assert.NotEmpty(encoded.ToArray());
    }
}
