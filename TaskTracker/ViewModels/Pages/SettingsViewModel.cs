using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskTracker.Services;

namespace TaskTracker.ViewModels.Pages
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ILanguageService _languageService;
        private CultureInfo _selectedLanguage;

        public SettingsViewModel(ILanguageService languageService)
        {
            _languageService = languageService;
            AvailableLanguages = _languageService.AvailableLanguages;
            SelectedLanguage = _languageService.AvailableLanguages.First(c => c.Name == Thread.CurrentThread.CurrentUICulture.Name);
        }

        public List<CultureInfo> AvailableLanguages { get; }

        public CultureInfo SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                if (_selectedLanguage != value)
                {
                    _selectedLanguage = value;
                    OnPropertyChanged();
                    _languageService.ChangeLanguage(value.Name);
                }
            }
        }
    }
}
