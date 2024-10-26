using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaskTracker.Services
{
    public interface ILanguageService
    {
        void ChangeLanguage(string cultureCode);
        public List<CultureInfo> AvailableLanguages { get; }
        string GetString(string key);
        event Action LanguageChanged;
    }
}
