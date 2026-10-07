namespace NeeView;
public record class ViewPageAccessor : PageAccessor
{
    public ViewPageAccessor(ScriptAccessContext context, Page source) : base(context, source) { }
    public double Width => Context.Read(() => Source.Content.PageDataSource.Size.Width);
    public double Height => Context.Read(() => Source.Content.PageDataSource.Size.Height);
    public MediaPlayerAccessor? Player => Context.Read(() => Context.MediaPlayer(Source) is { } player ? new MediaPlayerAccessor(Context, player) : null);
    public PageAccessor GetPageAccessor() => new(Context, Source);
}
