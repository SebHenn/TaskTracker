using System.Globalization;
using System.Windows.Data;

namespace TaskTracker.Helpers
{
    /// <summary>
    /// Width for the board's lane strip: the viewport, or the space the lanes need at
    /// their minimum width, whichever is larger.
    ///
    /// The lanes sit in a <c>UniformGrid</c>, which divides whatever width it is given
    /// by the number of lanes with no floor — so on a narrow window, or a board with
    /// several columns, every lane is squeezed until the cards inside are unreadable.
    /// Sizing the strip here instead lets it grow past the viewport and scroll, and the
    /// width stays explicit rather than coming from the ScrollViewer's infinite measure,
    /// which would let a long task title decide how wide a lane is.
    ///
    /// Feeding a ScrollViewer's ViewportWidth back into the width of its own content
    /// looks like a measure loop, and would be one if a scrollbar appearing could change
    /// the viewport width. It cannot here: exceeding the viewport shows the *horizontal*
    /// bar, which costs height, and the vertical bar is disabled outright, so nothing
    /// ever narrows the viewport in response. Re-enabling vertical scrolling on
    /// BoardScroll would reintroduce that edge.
    /// </summary>
    public class BoardWidthConverter : IMultiValueConverter
    {
        /// <summary>Narrowest a lane may get before the board starts scrolling instead.</summary>
        public const double MinLaneWidth = 240;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] is not double viewport || values[1] is not int lanes)
            {
                return double.NaN;
            }

            // Before the first arrange the viewport is 0; sizing to the lanes' minimum
            // then would briefly show a scrollbar on a board that fits.
            if (double.IsNaN(viewport) || viewport <= 0 || lanes <= 0)
            {
                return double.NaN;
            }

            return Math.Max(viewport, lanes * MinLaneWidth);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
