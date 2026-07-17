using System;
using System.Globalization;
using System.Windows.Data;

namespace TaskTracker.Helpers
{
    /// <summary>TextBox ↔ int?: empty or invalid text means null.</summary>
    public class NullableIntConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value?.ToString() ?? "";

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => int.TryParse(value?.ToString(), out var parsed) && parsed > 0 ? parsed : null;
    }
}
