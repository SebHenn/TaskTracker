using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace TaskTracker.Services
{
    public class LanguageService : ILanguageService
    {
        private readonly ResourceManager _resourceManager = new ResourceManager("TaskTracker.Resources.Lang", typeof(LanguageService).Assembly);
        private const string LanguageDictionaryKey = "LanguageDictionary";

        public event Action LanguageChanged;

        public List<CultureInfo> AvailableLanguages { get; } = new List<CultureInfo>
        {
            new CultureInfo("en"),
            new CultureInfo("de"),
        };

        public void ChangeLanguage(string cultureCode)
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(cultureCode);

            var existingLanguageDictionary = Application.Current.Resources.MergedDictionaries
                .OfType<ResourceDictionary>()
                .FirstOrDefault(d => d.Contains(LanguageDictionaryKey));

            if (existingLanguageDictionary != null)
            {
                Application.Current.Resources.MergedDictionaries.Remove(existingLanguageDictionary);
            }

            var languageDictionary = new ResourceDictionary();
            var resourceSet = _resourceManager.GetResourceSet(CultureInfo.CurrentUICulture, true, true);

            foreach (DictionaryEntry entry in resourceSet)
            {
                languageDictionary[entry.Key] = entry.Value;
            }

            languageDictionary[LanguageDictionaryKey] = true;
            Application.Current.Resources.MergedDictionaries.Add(languageDictionary);

            LanguageChanged?.Invoke();
        }

        public string GetString(string key)
        {
            return _resourceManager.GetString(key, Thread.CurrentThread.CurrentUICulture) ?? $"!{key}!";
        }
    }
}
