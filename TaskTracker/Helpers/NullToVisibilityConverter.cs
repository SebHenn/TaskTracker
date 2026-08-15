using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TaskTracker.Helpers
{
    /// <summary>
    /// Visible when the bound value is null (e.g. "not yet linked" actions).
    ///
    /// Pass <c>ConverterParameter="False"</c> to invert it — visible when the value is
    /// *not* null, which is how a nullable timestamp like TimerStartedAtUtc drives a
    /// "running" badge. Same parameter convention as BoolToVisibilityConverter: the
    /// parameter is the state that shows.
    /// </summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var visibleWhenNull = parameter is not string text || !bool.TryParse(text, out var wanted) || wanted;
            return value == null == visibleWhenNull ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
