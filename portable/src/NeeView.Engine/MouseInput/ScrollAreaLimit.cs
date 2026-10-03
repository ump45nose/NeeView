// Copyright (c) NeeLaboratory. MIT; original numeric movement limit.
namespace NeeView;
public sealed class ScrollAreaLimit(Rect contentRect, Rect viewRect)
{
    private readonly Rect _contentRect = contentRect;
    private readonly Rect _viewRect = viewRect;
        public Vector GetLimitContentMove(Vector delta)
        {
            var marginX = _contentRect.Width < _viewRect.Width ? _viewRect.Width - _contentRect.Width : 0;
            var marginY = _contentRect.Height < _viewRect.Height ? _viewRect.Height - _contentRect.Height : 0;

            if (delta.X < 0 && _contentRect.Right + delta.X < _viewRect.Right - marginX)
            {
                delta.X = Math.Min(_viewRect.Right - marginX - _contentRect.Right, 0.0);
            }
            else if (delta.X > 0 && _contentRect.Left + delta.X > _viewRect.Left + marginX)
            {
                delta.X = Math.Max(_viewRect.Left + marginX - _contentRect.Left, 0.0);
            }

            if (delta.Y < 0 && _contentRect.Bottom + delta.Y < _viewRect.Bottom - marginY)
            {
                delta.Y = Math.Min(_viewRect.Bottom - marginY - _contentRect.Bottom, 0.0);
            }
            else if (delta.Y > 0 && _contentRect.Top + delta.Y > _viewRect.Top + marginY)
            {
                delta.Y = Math.Max(_viewRect.Top + marginY - _contentRect.Top, 0.0);
            }

            return delta;
        }

}
