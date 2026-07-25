using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TaskTracker.Helpers
{
    /// <summary>
    /// Visible when the bound count is greater than zero, or — with ConverterParameter
    /// "Empty" — when it is zero. The inverted form is what empty-state placeholders
    /// bind to, so a list and the message that replaces it share one source of truth.
    /// </summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var hasItems = value is int count && count > 0;
            var wantEmpty = string.Equals(parameter as string, "Empty", StringComparison.OrdinalIgnoreCase);
            return hasItems != wantEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
