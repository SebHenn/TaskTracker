using CommunityToolkit.Mvvm.ComponentModel;
using TaskTracker.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Threading;
using TaskTracker.Services;
using TaskTracker.ViewModels.Pages;
using TaskTracker.ViewModels.Windows;
using TaskTracker.Views.Pages;
using TaskTracker.Views.Windows;

namespace TaskTracker
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly ServiceProvider _serviceProvider;

        public App()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton<MainWindow>(provider => new MainWindow
            {
                DataContext = provider.GetRequiredService<MainViewModel>()
            });
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<HomeViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<ProjectViewModel>();
            services.AddSingleton<SearchViewModel>();
            services.AddTransient<NewProjectWindow>(provider => new NewProjectWindow(
                provider.GetRequiredService<NewProjectViewModel>()));
            services.AddTransient<SortProjectWindow>(provider => new SortProjectWindow(
                provider.GetRequiredService<SortProjectViewModel>()));
            services.AddTransient<NewProjectViewModel>();
            services.AddTransient<SortProjectViewModel>();
            services.AddTransient<LinkGitHubWindow>(provider => new LinkGitHubWindow(
                provider.GetRequiredService<LinkGitHubViewModel>()));
            services.AddTransient<LinkGitHubViewModel>();
            services.AddTransient<ColumnsWindow>(provider => new ColumnsWindow(
                provider.GetRequiredService<ColumnsViewModel>()));
            services.AddTransient<ColumnsViewModel>();
            services.AddSingleton<AutoSyncService>();
            services.AddSingleton<TrayService>();


            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IProjectsService, ProjectsService>();
            services.AddSingleton<ILanguageService, LanguageService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<ISettingsService, SettingsService>();

            services.AddSingleton<Func<Type, ObservableObject>>(serviceProvider => viewModelType => (ObservableObject)serviceProvider.GetRequiredService(viewModelType));

            _serviceProvider = services.BuildServiceProvider();

        }

        private System.Threading.Mutex? _instanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            _instanceMutex = new System.Threading.Mutex(true, "TaskTracker.SingleInstance", out var isFirstInstance);
            if (!isFirstInstance)
            {
                // A second instance would fight over the store's file watcher.
                MessageBox.Show("TaskTracker is already running.", "TaskTracker",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            var settings = _serviceProvider.GetRequiredService<ISettingsService>().Settings;
            var langservice = _serviceProvider.GetRequiredService<ILanguageService>();
            var themeService = _serviceProvider.GetRequiredService<IThemeService>();

            themeService.ChangeTheme(settings.Theme);

            var window = _serviceProvider.GetRequiredService<MainWindow>();
            ApplyWindowPlacement(window, settings);
            window.Show();

            base.OnStartup(e);

            langservice.ChangeLanguage(settings.Language);

            _serviceProvider.GetRequiredService<AutoSyncService>();
            _serviceProvider.GetRequiredService<TrayService>().Initialize();
        }

        private static void ApplyWindowPlacement(MainWindow window, TaskTracker.Core.Storage.AppSettings settings)
        {
            if (settings.WindowWidth is > 200 && settings.WindowHeight is > 200)
            {
                window.Width = settings.WindowWidth.Value;
                window.Height = settings.WindowHeight.Value;
            }
            if (settings.WindowLeft.HasValue && settings.WindowTop.HasValue &&
                settings.WindowLeft.Value >= SystemParameters.VirtualScreenLeft &&
                settings.WindowTop.Value >= SystemParameters.VirtualScreenTop &&
                settings.WindowLeft.Value < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
                settings.WindowTop.Value < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = settings.WindowLeft.Value;
                window.Top = settings.WindowTop.Value;
            }
            // Go through the view model — the window's WindowState is bound to it.
            if (settings.WindowMaximized && window.DataContext is MainViewModel mainViewModel)
                mainViewModel.WindowState = WindowState.Maximized;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _serviceProvider.GetRequiredService<TrayService>().Dispose();

            if (MainWindow != null)
            {
                var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
                var settings = settingsService.Settings;
                settings.WindowMaximized = MainWindow.WindowState == WindowState.Maximized;
                if (MainWindow.WindowState == WindowState.Normal)
                {
                    settings.WindowLeft = MainWindow.Left;
                    settings.WindowTop = MainWindow.Top;
                    settings.WindowWidth = MainWindow.Width;
                    settings.WindowHeight = MainWindow.Height;
                }
                settingsService.Save();
            }

            var projectsService = _serviceProvider.GetRequiredService<IProjectsService>();
            projectsService.SaveNow();
            (projectsService as IDisposable)?.Dispose();
            _instanceMutex?.Dispose();

            base.OnExit(e);
        }
    }
}