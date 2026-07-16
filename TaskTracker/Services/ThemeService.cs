using System;
using System.Linq;
using System.Windows;

namespace TaskTracker.Services
{
    public class ThemeService : IThemeService
    {
        private const string DarkPath = "Styles/DarkMode.xaml";
        private const string LightPath = "Styles/LightMode.xaml";

        public void ChangeTheme(string theme)
        {
            var merged = Application.Current.Resources.MergedDictionaries;
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
    }
}
