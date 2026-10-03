// Copyright (c) NeeLaboratory. 转换原 ArchivePageControl 的封面、叠页和 FileCard 区域。
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NeeView;
namespace NeeView.MacOS.Views;

/// <summary>独立书籍卡片表现；颜色来自主题，尺寸来自原页框，业务与封面选择不在此处。</summary>
public static class ArchivePageRenderer
{
    private static readonly StreamGeometry FolderIcon = StreamGeometry.Parse("M0,0 L14,0 14,10 16,12 16,18 0,18 Z M14,10 L13,12 13,18");
    /// <summary>原上部3/4封面区，底部保留文件信息；小尺寸按原紧凑余量显示。</summary>
    public static Avalonia.Rect CoverArea(Avalonia.Rect card)
    {
        var margin = Math.Min(card.Height < 300 ? 5 : 20, Math.Min(card.Width, card.Height) / 4);
        return new(card.X + margin, card.Y + margin, Math.Max(1, card.Width - margin * 2), Math.Max(1, card.Height * .75 - margin * 2));
    }
    /// <summary>等比包含解码封面，不把横向封面拉伸成原480×640页框。</summary>
    public static Avalonia.Rect Fit(Avalonia.Rect area, Avalonia.Size size)
    {
        var scale = Math.Min(area.Width / size.Width, area.Height / size.Height);
        return new(area.Center.X - size.Width * scale / 2, area.Center.Y - size.Height * scale / 2, size.Width * scale, size.Height * scale);
    }
    /// <summary>绘制原叠页封面与文件名/类型/大小，空书有正常卡片而非永久加载。</summary>
    public static void Draw(Control owner, DrawingContext context, Page page, Avalonia.Rect card, Bitmap? cover, string status)
    {
        var background = Brush(owner, "ArchivePage.Background", Brushes.WhiteSmoke);
        var foreground = Brush(owner, "ArchivePage.Foreground", Brushes.Black);
        var border = Brush(owner, "ArchivePage.CoverBorder", Brushes.LightGray);
        context.FillRectangle(background, card);
        var area = CoverArea(card);
        var image = Fit(area.Deflate(Math.Min(8, Math.Min(area.Width, area.Height) / 4)), cover?.Size ?? new Avalonia.Size(256, 320));
        context.FillRectangle(border, image.Translate(new Avalonia.Vector(6, 6)));
        context.FillRectangle(background, image); context.DrawRectangle(null, new Pen(border, 2), image);
        if (cover is not null) context.DrawImage(cover, image.Deflate(Math.Min(2, Math.Min(image.Width, image.Height) / 4)));
        else
        {
            // 原fic_folder轮廓；空封面与加载状态共享卡片，不制造解码资源。
            var icon = Fit(image.Deflate(Math.Min(image.Width, image.Height) / 4), new Avalonia.Size(16, 18));
            using var transform = context.PushTransform(Matrix.CreateScale(icon.Width / 16, icon.Height / 18) * Matrix.CreateTranslation(icon.X, icon.Y));
            context.DrawGeometry(null, new Pen(border, 1), FolderIcon);
        }
        using var clip = context.PushClip(card);
        var info = new Point(card.X + 12, card.Y + card.Height * .77);
        Text(page.EntryName, 14, info);
        Text((page.PageType == PageType.Folder ? "文件夹" : "压缩包") + " · 双击封面打开", 11, info + new Avalonia.Vector(0, 25));
        if (status.Length > 0 && cover is null) Text(status, 11, info + new Avalonia.Vector(0, 46));
        void Text(string value, double size, Point point) => context.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("sans-serif"), size, foreground), point);
    }
    /// <summary>主题替换不修改引擎或正文绘制控制。</summary>
    private static IBrush Brush(Control owner, string key, IBrush fallback) => owner.TryFindResource(key, out var value) && value is IBrush brush ? brush : fallback;
}
