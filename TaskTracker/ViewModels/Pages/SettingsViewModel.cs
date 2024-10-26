using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        private readonly IThemeService _themeService;
        private CultureInfo _selectedLanguage;

        public SettingsViewModel(ILanguageService languageService, IThemeService themeService)
        {
            _languageService = languageService;
            _themeService = themeService;
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

        [ObservableProperty]
        private bool _isDark = true;

        [RelayCommand]
        public void OnThemeCheck()
        {
            _themeService.ChangeTheme(IsDark ? "dark" : "light");
        }
    }
}
