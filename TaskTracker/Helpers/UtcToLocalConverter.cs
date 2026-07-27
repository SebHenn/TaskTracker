using System;
using System.Globalization;
using System.Windows.Data;

namespace TaskTracker.Helpers
{
    /// <summary>
    /// Formats a UTC timestamp in the viewer's local time and culture.
    ///
    /// Everything in the model is stored UTC, so binding one straight to a TextBlock
    /// shows a time that is wrong by the offset — quietly, and most obviously to whoever
    /// is furthest from Greenwich.
    /// </summary>
    public class UtcToLocalConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not DateTime utc)
                return "";
            // Kind is Unspecified on anything that has been through JSON, and
            // ToLocalTime would then treat it as already-local and shift nothing.
            var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
            return local.ToString(parameter as string ?? "g", culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
