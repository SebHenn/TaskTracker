using Microsoft.Win32;
using System;
using System.Linq;
using System.Windows;

namespace TaskTracker.Services
{
    public class ThemeService : IThemeService, IDisposable
    {
        private const string DarkPath = "Styles/DarkMode.xaml";
        private const string LightPath = "Styles/LightMode.xaml";

        /// <summary>Follow whatever Windows is set to, and change with it.</summary>
        public const string System = "system";

        private bool _following;

        public ThemeService()
        {
            // Fires on a Windows light/dark switch, among other preference changes.
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        public void ChangeTheme(string theme)
        {
            _following = string.Equals(theme, System, StringComparison.OrdinalIgnoreCase);
            Apply(_following ? SystemTheme() : theme);
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (!_following || e.Category != UserPreferenceCategory.General)
                return;

            // Raised on a background thread; touching Application.Resources off the UI
            // thread would throw.
            Application.Current?.Dispatcher.Invoke(() => Apply(SystemTheme()));
        }

        /// <summary>
        /// Windows' app theme, from the registry. There is no managed API for it, and a
        /// missing value means the light default rather than an error.
        /// </summary>
        private static string SystemTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                // AppsUseLightTheme: 1 light, 0 dark.
                return key?.GetValue("AppsUseLightTheme") is int value && value == 0 ? "dark" : "light";
            }
            catch (Exception)
            {
                return "dark";
            }
        }

        private static void Apply(string theme)
        {
            var merged = Application.Current?.Resources.MergedDictionaries;
            if (merged == null)
                return;

            var targetPath = theme == "light" ? LightPath : DarkPath;

            var current = merged.FirstOrDefault(md =>
                md.Source != null &&
                (md.Source.OriginalString.Equals(DarkPath) || md.Source.OriginalString.Equals(LightPath)));

            if (current?.Source?.OriginalString.Equals(targetPath) == true)
                return;

            var newThemeDictionary = new ResourceDictionary { Source = new Uri(targetPath, UriKind.Relative) };

            if (current != null)
                merged[merged.IndexOf(current)] = newThemeDictionary;
            else
                merged.Add(newThemeDictionary);
        }

        public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
