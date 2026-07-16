using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using TaskTracker.Services;

namespace TaskTracker.ViewModels.Pages
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ILanguageService _languageService;
        private readonly IThemeService _themeService;
        private readonly ISettingsService _settingsService;
        private CultureInfo _selectedLanguage;

        public SettingsViewModel(ILanguageService languageService, IThemeService themeService, ISettingsService settingsService)
        {
            _languageService = languageService;
            _themeService = themeService;
            _settingsService = settingsService;
            AvailableLanguages = _languageService.AvailableLanguages;
            _selectedLanguage = AvailableLanguages.FirstOrDefault(c => c.Name == _settingsService.Settings.Language)
                                ?? AvailableLanguages.First();
            _isDark = _settingsService.Settings.Theme != "light";
        }

        public List<CultureInfo> AvailableLanguages { get; }

        public string DataFolder => _settingsService.SettingsFolder;

        public CultureInfo SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                if (_selectedLanguage != value && value != null)
                {
                    _selectedLanguage = value;
                    OnPropertyChanged();
                    _languageService.ChangeLanguage(value.Name);
                    _settingsService.Settings.Language = value.Name;
                    _settingsService.Save();
                }
            }
        }

        [ObservableProperty]
        private bool _isDark;

        partial void OnIsDarkChanged(bool value)
        {
            _themeService.ChangeTheme(value ? "dark" : "light");
            _settingsService.Settings.Theme = value ? "dark" : "light";
            _settingsService.Save();
        }

        [RelayCommand]
        private void OnOpenDataFolder()
        {
            try
            {
                Process.Start(new ProcessStartInfo(DataFolder) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // Non-critical; the path is shown next to the button anyway.
            }
        }
    }
}
