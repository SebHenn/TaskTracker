using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TaskTracker.Helpers
{
    /// <summary>Hex color string → SolidColorBrush; null/invalid → Transparent.</summary>
    public class ColorStringToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hex && !string.IsNullOrWhiteSpace(hex))
            {
                try
                {
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                }
                catch (FormatException) { }
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
