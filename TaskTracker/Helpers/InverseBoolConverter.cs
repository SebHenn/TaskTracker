using System;
using System.Globalization;
using System.Windows.Data;

namespace TaskTracker.Helpers
{
    /// <summary>
    /// Negates a bool, for the "enabled unless" cases where a trigger would be heavier
    /// than the thing it controls.
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is not bool flag || !flag;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is not bool flag || !flag;
    }
}
