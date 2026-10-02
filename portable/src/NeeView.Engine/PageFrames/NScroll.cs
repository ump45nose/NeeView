// Copyright (c) NeeLaboratory. 原 NScroll 算法迁入，遵循仓库 MIT 许可。
using System;

namespace NeeView.PageFrames
{







    public class NScroll
    {
        private const double _scrollCountThreshold = 0.9;

        private PageFrameContext _context;
        private Rect _contentRect;
        private Rect _viewRect;


        /// <summary>接收原阅读上下文及内容/视口矩形，输出内容移动方向。</summary>
        public NScroll(PageFrameContext context, Rect contentRect, Rect viewRect)
        {
            _context = context;
            _contentRect = contentRect;
            _viewRect = viewRect;
        }





        /// <summary>依据翻页方向、阅读方向及原命令参数计算滚动结果。</summary>
        public ScrollResult ScrollN(int direction, IScrollNTypeParameter parameter, double endMargin)
        {
            return ScrollN(direction, _context.ReadOrder.ToSign(), parameter, endMargin);
        }






        /// <summary>依据翻页方向、阅读方向及原命令参数计算滚动结果。</summary>
        public ScrollResult ScrollN(int direction, int bookReadDirection, IScrollNTypeParameter parameter, double endMargin)
        {

            return ScrollN(direction, bookReadDirection, parameter.ScrollType, parameter.Scroll, endMargin);
        }









        /// <summary>依据翻页方向、阅读方向及原命令参数计算滚动结果。</summary>
        private ScrollResult ScrollN(int direction, int bookReadDirection, NScrollType scrollType, double rate, double endMargin)
        {
            var delta = GetNScrollDelta(direction, bookReadDirection, scrollType, rate, endMargin);
            return new ScrollResult(scrollType, delta);
        }

        /// <summary>保留原五模式分派；矩形由宿主提供，不依赖控件。</summary>
        private Vector GetNScrollDelta(int direction, int bookReadDirection, NScrollType scrollType, double rate, double endMargin)
        {
            var area = new DragArea(_viewRect, _contentRect);

            return scrollType switch
            {
                NScrollType.NType => GetNTypeScrollDelta(area, direction, bookReadDirection, rate, endMargin),
                NScrollType.ZType => GetZTypeScrollDelta(area, direction, bookReadDirection, rate, endMargin),
                NScrollType.Diagonal => GetDiagonalScrollDelta(area, direction, bookReadDirection, rate, endMargin),
                NScrollType.Horizontal => GetHorizontalScrollDelta(area, direction, bookReadDirection, rate, endMargin),
                NScrollType.Vertical => GetVerticalScrollDelta(area, direction, bookReadDirection, rate, endMargin),
                _ => throw new NotSupportedException()
            };
        }

        /// <summary>先纵向滚动，再横移并回到纵向起点。</summary>
        private Vector GetNTypeScrollDelta(DragArea area, int direction, int bookReadDirection, double rate, double endMargin)
        {
            var delta = SnapZero(GetNScrollVertical(area, direction, bookReadDirection, rate), endMargin);
            if (delta.Y == 0.0)
            {
                delta = SnapZero(GetNScrollNewLineVertical(area, direction, bookReadDirection, rate), endMargin);
            }
            return delta;
        }

        /// <summary>先横向滚动，再纵移并回到横向起点。</summary>
        private Vector GetZTypeScrollDelta(DragArea area, int direction, int bookReadDirection, double rate, double endMargin)
        {
            var delta = SnapZero(GetNScrollHorizontal(area, direction, bookReadDirection, rate), endMargin);
            if (delta.X == 0.0)
            {
                delta = SnapZero(GetNScrollNewLineHorizontal(area, direction, bookReadDirection, rate), endMargin);
            }
            return delta;
        }

        /// <summary>同时计算两轴位移，保留原终端容差。</summary>
        private static Vector GetDiagonalScrollDelta(DragArea area, int direction, int bookReadDirection, double rate, double endMargin)
        {
            var deltaX = GetNScrollHorizontal(area, direction, bookReadDirection, rate);
            var deltaY = GetNScrollVertical(area, direction, bookReadDirection, rate);
            return SnapZero(new Vector(deltaX.X, deltaY.Y), endMargin);
        }

        /// <summary>只计算阅读方向上的水平位移。</summary>
        private static Vector GetHorizontalScrollDelta(DragArea area, int direction, int bookReadDirection, double rate, double endMargin)
        {
            var deltaX = GetNScrollHorizontal(area, direction, bookReadDirection, rate);
            return SnapZero(new Vector(deltaX.X, 0.0), endMargin);
        }

        /// <summary>只计算翻页方向上的纵向位移。</summary>
        private static Vector GetVerticalScrollDelta(DragArea area, int direction, int bookReadDirection, double rate, double endMargin)
        {
            var deltaY = GetNScrollVertical(area, direction, bookReadDirection, rate);
            return SnapZero(new Vector(0.0, deltaY.Y), endMargin);
        }

        /// <summary>阅读方向与翻页方向共同决定横向符号。</summary>
        private static Vector GetNScrollHorizontal(DragArea area, int direction, int bookReadDirection, double rate)
        {
            var delta = new Vector();

            if (direction * bookReadDirection > 0)
            {
                delta.X = SnapZero(GetNScrollHorizontalToRight(area, rate));
            }
            else
            {
                delta.X = SnapZero(GetNScrollHorizontalToLeft(area, rate));
            }

            return delta;
        }

        /// <summary>正向向下，反向向上，输出内容移动向量。</summary>
        private static Vector GetNScrollVertical(DragArea area, int direction, int bookReadDirection, double rate)
        {
            var delta = new Vector();

            if (direction > 0)
            {
                delta.Y = SnapZero(GetNScrollVerticalToBottom(area, rate));
            }
            else
            {
                delta.Y = SnapZero(GetNScrollVerticalToTop(area, rate));
            }

            return delta;
        }

        /// <summary>横向换行时先纵移，再将横轴移回阅读起点。</summary>
        private static Vector GetNScrollNewLineHorizontal(DragArea area, int direction, int bookReadDirection, double rate)
        {
            var delta = new Vector();

            var canHorizontalScroll = area.Over.Width > 0.0;
            var rateY = canHorizontalScroll ? 1.0 : rate;
            if (direction > 0)
            {
                delta.Y = SnapZero(GetNScrollVerticalToBottom(area, rateY));
            }
            else
            {
                delta.Y = SnapZero(GetNScrollVerticalToTop(area, rateY));
            }

            if (delta.Y != 0.0)
            {
                if (direction * bookReadDirection > 0)
                {
                    delta.X = SnapZero(GetNScrollHorizontalMoveToLeft(area));
                }
                else
                {
                    delta.X = SnapZero(GetNScrollHorizontalMoveToRight(area));
                }
            }

            return delta;
        }

        /// <summary>纵向换行时先横移，再将纵轴移回阅读起点。</summary>
        private static Vector GetNScrollNewLineVertical(DragArea area, int direction, int bookReadDirection, double rate)
        {
            var delta = new Vector();

            var canVerticalScroll = area.Over.Height > 0.0;
            var rateX = canVerticalScroll ? 1.0 : rate;
            if (direction * bookReadDirection > 0)
            {
                delta.X = SnapZero(GetNScrollHorizontalToRight(area, rateX));
            }
            else
            {
                delta.X = SnapZero(GetNScrollHorizontalToLeft(area, rateX));
            }

            if (delta.X != 0.0)
            {
                if (direction > 0)
                {
                    delta.Y = SnapZero(GetNScrollVerticalMoveToTop(area));
                }
                else
                {
                    delta.Y = SnapZero(GetNScrollVerticalMoveToBottom(area));
                }
            }

            return delta;
        }




        /// <summary>按原绝对值或向量长度容差归零。</summary>
        private static double SnapZero(double value, double margin = 1.0)
        {
            return -margin < value && value < margin ? 0.0 : value;
        }

        /// <summary>按原绝对值或向量长度容差归零。</summary>
        private static Vector SnapZero(Vector v, double margin)
        {
            if (v.IsZero())
            {
                return v;
            }

            if (v.LengthSquared < margin * margin)
            {
                return new Vector();
            }

            return v;
        }

        /// <summary>按原越界距离与视口比例计算步长或端点位移。</summary>
        private static double GetNScrollVerticalToTop(DragArea area, double rate)
        {
            if (area.Over.Top < 0.0)
            {
                double dy = Math.Abs(area.Over.Top);
                var n = (int)(dy / (area.ViewRect.Height * rate) + _scrollCountThreshold);
                if (n > 1)
                {
                    dy = Math.Min(dy / n, dy);
                }
                return dy;
            }
            else
            {
                return 0.0;
            }
        }

        /// <summary>按原越界距离与视口比例计算步长或端点位移。</summary>
        private static double GetNScrollVerticalToBottom(DragArea area, double rate)
        {
            if (area.Over.Bottom > 0.0)
            {
                double dy = Math.Abs(area.Over.Bottom);
                var n = (int)(dy / (area.ViewRect.Height * rate) + _scrollCountThreshold);
                if (n > 1)
                {
                    dy = Math.Min(dy / n, dy);
                }
                return -dy;
            }
            else
            {
                return 0.0;
            }
        }

        /// <summary>按原越界距离与视口比例计算步长或端点位移。</summary>
        private static double GetNScrollVerticalMoveToTop(DragArea area)
        {
            return Math.Abs(area.Over.Top);
        }

        /// <summary>按原越界距离与视口比例计算步长或端点位移。</summary>
        private static double GetNScrollVerticalMoveToBottom(DragArea area)
        {
            return -Math.Abs(area.Over.Bottom);
        }

        /// <summary>按原越界距离与视口比例计算步长或端点位移。</summary>
        private static double GetNScrollHorizontalMoveToLeft(DragArea area)
        {
            return Math.Abs(area.Over.Left);
        }

        /// <summary>按原越界距离与视口比例计算步长或端点位移。</summary>
        private static double GetNScrollHorizontalMoveToRight(DragArea area)
        {
            return -Math.Abs(area.Over.Right);
        }

        /// <summary>按原越界距离与视口比例计算步长或端点位移。</summary>
        private static double GetNScrollHorizontalToLeft(DragArea area, double rate)
        {
            if (area.Over.Left < 0.0)
            {
                double dx = Math.Abs(area.Over.Left);
                var n = (int)(dx / (area.ViewRect.Width * rate) + _scrollCountThreshold);
                if (n > 1)
                {
                    dx = Math.Min(dx / n, dx);
                }
                return dx;
            }
            else
            {
                return 0.0;
            }
        }

        /// <summary>按原越界距离与视口比例计算步长或端点位移。</summary>
        private static double GetNScrollHorizontalToRight(DragArea area, double rate)
        {
            if (area.Over.Right > 0.0)
            {
                double dx = Math.Abs(area.Over.Right);
                var n = (int)(dx / (area.ViewRect.Width * rate) + _scrollCountThreshold);
                if (n > 1)
                {
                    dx = Math.Min(dx / n, dx);
                }
                return -dx;
            }
            else
            {
                return 0.0;
            }
        }
    }
}
