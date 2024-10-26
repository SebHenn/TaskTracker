using CommunityToolkit.Mvvm.ComponentModel;
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
            services.AddTransient<NewProjectWindow>(provider => new NewProjectWindow(
                provider.GetRequiredService<NewProjectViewModel>()));
            services.AddTransient<NewProjectViewModel>();


            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IProjectsService, ProjectsService>();
            services.AddSingleton<ILanguageService, LanguageService>();

            services.AddSingleton<Func<Type, ObservableObject>>(serviceProvider => viewModelType => (ObservableObject)serviceProvider.GetRequiredService(viewModelType));

            _serviceProvider = services.BuildServiceProvider();

        }

        protected override void OnStartup(StartupEventArgs e)
        {
            var langservice = _serviceProvider.GetService<ILanguageService>();
            var window = _serviceProvider.GetRequiredService<MainWindow>();
            window.Show();
            
            base.OnStartup(e);

            langservice.ChangeLanguage("en");
        }
    }
}