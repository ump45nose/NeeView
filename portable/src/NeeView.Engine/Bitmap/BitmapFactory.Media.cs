namespace NeeView;
public sealed partial class BitmapFactory
{
    /// <summary>当前视频帧沿同一退役Entry及显示租约计费；不建立第二像素缓存。</summary>
    /// <param name="pixels">调用方转交的唯一像素所有者。</param><returns>释放显示后归还的原工厂租约。</returns>
    public BitmapLease RentMediaFrame(DecodedImageLease pixels)
    {
        lock(_sync)
        {
            if(_disposed){pixels.Dispose();throw new ObjectDisposedException(nameof(BitmapFactory));}
            var entry=new Entry(pixels,false){Cached=false};_retired.Add(entry);return Rent(entry);
        }
    }
}
