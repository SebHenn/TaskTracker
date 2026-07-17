using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TaskTracker.Helpers
{
    /// <summary>Visible when the bound value is null (e.g. "not yet linked" actions).</summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value == null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
