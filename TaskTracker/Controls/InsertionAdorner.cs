using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace TaskTracker.Controls
{
    /// <summary>
    /// The line drawn between cards showing where a dragged task would land.
    ///
    /// The drop index was always computed correctly; nothing ever rendered it, so the
    /// only way to find out where a card would end up was to drop it and look.
    /// </summary>
    public sealed class InsertionAdorner : Adorner
    {
        private const double Thickness = 2;
        private const double SideInset = 6;

        private readonly Brush _brush;
        private double _offset;
        private bool _isVisible;

        public InsertionAdorner(UIElement adornedElement, Brush brush) : base(adornedElement)
        {
            IsHitTestVisible = false;
            _brush = brush;
        }

        /// <param name="verticalOffset">
        /// Distance from the top of the adorned lane list, in its own coordinates.
        /// </param>
        public void ShowAt(double verticalOffset)
        {
            if (_isVisible && Math.Abs(_offset - verticalOffset) < 0.5)
                return;
            _isVisible = true;
            _offset = verticalOffset;
            InvalidateVisual();
        }

        public void Hide()
        {
            if (!_isVisible)
                return;
            _isVisible = false;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (!_isVisible || AdornedElement is not FrameworkElement lane)
                return;

            var width = lane.ActualWidth - (SideInset * 2);
            if (width <= 0)
                return;

            // Clamped so the line never draws past the ends of the visible list.
            var top = Math.Clamp(_offset - (Thickness / 2), 0, Math.Max(0, lane.ActualHeight - Thickness));
            drawingContext.DrawRoundedRectangle(_brush, null,
                new Rect(SideInset, top, width, Thickness), 1, 1);
        }
    }
}
