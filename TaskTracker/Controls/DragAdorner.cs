using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace TaskTracker.Controls
{
    /// <summary>
    /// The card that follows the cursor while a task is being dragged.
    ///
    /// WPF's OLE drag gives no visual of the thing being dragged — only a cursor
    /// changes, which is why dragging felt broken. This paints a snapshot of the card
    /// in the adorner layer and the view drives its position from the live cursor.
    /// </summary>
    public sealed class DragAdorner : Adorner
    {
        private const double LiftScale = 1.03;
        private const double CarryOpacity = 0.9;
        private const double RejectOpacity = 0.5;

        private static readonly Duration SettleDuration = new(TimeSpan.FromMilliseconds(160));

        private readonly Image _ghost;
        private readonly TranslateTransform _offset = new();
        private readonly VisualCollection _children;

        public DragAdorner(UIElement adornedElement, FrameworkElement card) : base(adornedElement)
        {
            _ghost = new Image
            {
                Source = Snapshot(card),
                Width = Math.Max(1, card.ActualWidth),
                Height = Math.Max(1, card.ActualHeight),
                Opacity = CarryOpacity,
                IsHitTestVisible = false,
                // A shadow stronger than the card's own reads as "lifted off the board".
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black },
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            _ghost.RenderTransform = new TransformGroup
            {
                Children = { new ScaleTransform(LiftScale, LiftScale), _offset },
            };

            _children = new VisualCollection(this) { _ghost };

            // The ghost sits directly under the pointer, so anything hit-testable here
            // would shadow the lane beneath it and no drop would ever be detected.
            // Set last: it force-inherits down to the children, and doing that before
            // _children exists walks VisualChildrenCount into a null reference.
            IsHitTestVisible = false;
        }

        /// <summary>Top-left of the ghost, in the adorned element's coordinates.</summary>
        public void SetPosition(Point position)
        {
            _offset.X = position.X;
            _offset.Y = position.Y;
        }

        /// <summary>
        /// Fades the ghost when the pointer is somewhere the card cannot land, so the
        /// drag says "this will go back" without resorting to a no-drop cursor.
        /// </summary>
        public void SetOverDropTarget(bool isOverTarget)
        {
            _ghost.Opacity = isOverTarget ? CarryOpacity : RejectOpacity;
        }

        /// <summary>
        /// Glides the ghost to <paramref name="position"/> and fades it out — the snap
        /// into a lane on a successful drop, and the trip home on a rejected one.
        /// </summary>
        public void SettleAt(Point position, Action onFinished)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            _offset.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(_offset.X, position.X, SettleDuration) { EasingFunction = ease });
            _offset.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(_offset.Y, position.Y, SettleDuration) { EasingFunction = ease });

            var fade = new DoubleAnimation(_ghost.Opacity, 0, SettleDuration) { EasingFunction = ease };
            fade.Completed += (_, _) => onFinished();
            _ghost.BeginAnimation(OpacityProperty, fade);
        }

        /// <summary>Fades out where it stands, for when there is no slot to snap to.</summary>
        public void FadeOut(Action onFinished)
        {
            var fade = new DoubleAnimation(_ghost.Opacity, 0, SettleDuration);
            fade.Completed += (_, _) => onFinished();
            _ghost.BeginAnimation(OpacityProperty, fade);
        }

        /// <summary>
        /// A still picture of the card, taken once at drag start.
        ///
        /// A live <see cref="VisualBrush"/> of the card would be simpler, but the lanes
        /// re-project during the drop and recycle their containers — the ghost would
        /// morph into whichever task landed in that container mid-animation.
        /// </summary>
        private static ImageSource Snapshot(FrameworkElement card)
        {
            var width = Math.Max(1, card.ActualWidth);
            var height = Math.Max(1, card.ActualHeight);
            var dpi = VisualTreeHelper.GetDpi(card);

            // Drawn through a VisualBrush rather than rendering the card directly:
            // RenderTargetBitmap.Render honours the element's own layout offset inside
            // the lane, which comes out as a shifted, half-clipped card.
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(new VisualBrush(card) { Stretch = Stretch.None }, null,
                    new Rect(0, 0, width, height));
            }

            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(width * dpi.DpiScaleX),
                (int)Math.Ceiling(height * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        protected override int VisualChildrenCount => _children.Count;

        protected override Visual GetVisualChild(int index) => _children[index];

        protected override Size MeasureOverride(Size constraint)
        {
            _ghost.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return _ghost.DesiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _ghost.Arrange(new Rect(0, 0, _ghost.Width, _ghost.Height));
            return finalSize;
        }
    }
}
