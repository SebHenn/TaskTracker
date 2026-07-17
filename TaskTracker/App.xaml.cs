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


            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IProjectsService, ProjectsService>();
            services.AddSingleton<ILanguageService, LanguageService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<ISettingsService, SettingsService>();

            services.AddSingleton<Func<Type, ObservableObject>>(serviceProvider => viewModelType => (ObservableObject)serviceProvider.GetRequiredService(viewModelType));

            _serviceProvider = services.BuildServiceProvider();

        }

        protected override void OnStartup(StartupEventArgs e)
        {
            var settings = _serviceProvider.GetRequiredService<ISettingsService>().Settings;
            var langservice = _serviceProvider.GetRequiredService<ILanguageService>();
            var themeService = _serviceProvider.GetRequiredService<IThemeService>();

            themeService.ChangeTheme(settings.Theme);

            var window = _serviceProvider.GetRequiredService<MainWindow>();
            window.Show();

            base.OnStartup(e);

            langservice.ChangeLanguage(settings.Language);

            _serviceProvider.GetRequiredService<AutoSyncService>();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            var projectsService = _serviceProvider.GetRequiredService<IProjectsService>();
            projectsService.SaveNow();
            (projectsService as IDisposable)?.Dispose();

            base.OnExit(e);
        }
    }
}