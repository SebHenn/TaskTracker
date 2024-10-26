using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using System.Windows;

namespace TaskTracker.Services
{
    public class ThemeService : IThemeService
    {
        public void ChangeTheme(string theme)
        {
            var applicationResources = Application.Current.Resources;

            string themeDictionaryPath = theme == "dark" ? "Styles/DarkMode.xaml" : "Styles/LightMode.xaml";

            var newThemeDictionary = new ResourceDictionary
            {
                Source = new Uri(themeDictionaryPath, UriKind.Relative)
            };

            var existingThemeDictionary = applicationResources.MergedDictionaries.Where(md => md.Source.OriginalString.Equals(theme == "dark" ? "Styles/LightMode.xaml" : "Styles/DarkMode.xaml")).FirstOrDefault();

            if (existingThemeDictionary != null)
            {
                int index = applicationResources.MergedDictionaries.IndexOf(existingThemeDictionary);
                applicationResources.MergedDictionaries[index] = newThemeDictionary;
            }
            else
            {
                applicationResources.MergedDictionaries.Add(newThemeDictionary);
            }
        }
    }
}
