using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TaskTracker.Helpers
{
    /// <summary>Visible when the bound count is greater than zero.</summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
