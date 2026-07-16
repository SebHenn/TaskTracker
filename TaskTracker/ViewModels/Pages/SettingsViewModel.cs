using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using TaskTracker.Core.GitHub;
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
            _autoSyncEnabled = _settingsService.Settings.AutoSyncEnabled;
            RefreshTokenStatus();
            _languageService.LanguageChanged += RefreshTokenStatus;
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

        /// <summary>Set from the view's PasswordBox (PasswordBox does not support binding).</summary>
        public string PendingToken { private get; set; } = "";

        [ObservableProperty]
        private string _tokenStatusText = "";

        [ObservableProperty]
        private bool _showPlaintextWarning;

        [ObservableProperty]
        private bool _autoSyncEnabled;

        partial void OnAutoSyncEnabledChanged(bool value)
        {
            _settingsService.Settings.AutoSyncEnabled = value;
            _settingsService.Save();
        }

        [RelayCommand]
        private void OnSaveToken()
        {
            var token = PendingToken.Trim();
            if (token.Length == 0)
                return;
            var (protectedValue, isPlaintext) = TokenProtector.Protect(token);
            _settingsService.Settings.GitHubTokenProtected = protectedValue;
            _settingsService.Settings.GitHubTokenIsPlaintext = isPlaintext;
            _settingsService.Save();
            PendingToken = "";
            RefreshTokenStatus();
        }

        [RelayCommand]
        private void OnClearToken()
        {
            _settingsService.Settings.GitHubTokenProtected = null;
            _settingsService.Settings.GitHubTokenIsPlaintext = false;
            _settingsService.Save();
            RefreshTokenStatus();
        }

        private void RefreshTokenStatus()
        {
            var hasToken = !string.IsNullOrEmpty(_settingsService.Settings.GitHubTokenProtected);
            TokenStatusText = _languageService.GetString(hasToken ? "TokenSaved" : "TokenNotSaved");
            ShowPlaintextWarning = !OperatingSystem.IsWindows() || (hasToken && _settingsService.Settings.GitHubTokenIsPlaintext);
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
