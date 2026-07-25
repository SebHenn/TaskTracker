using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TaskTracker.Helpers
{
    /// <summary>
    /// Bridges a stored sidebar width and a <see cref="ColumnDefinition.Width"/>.
    ///
    /// Two-way on purpose: dragging the GridSplitter assigns the column's Width
    /// property, and a two-way binding turns that assignment into a push back to the
    /// view model — which is what lets the position be remembered without the view
    /// having to report it.
    /// </summary>
    public class SidebarWidthConverter : IValueConverter
    {
        private const double Fallback = 220;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return new GridLength(value is double width && width > 0 ? width : Fallback, GridUnitType.Pixel);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // A star or auto length has no pixel width to store; only a dragged
            // (absolute) column does.
            return value is GridLength length && length.IsAbsolute ? length.Value : Fallback;
        }
    }
}
