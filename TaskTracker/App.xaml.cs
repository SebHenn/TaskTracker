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
            services.AddSingleton<AgendaViewModel>();
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
            services.AddTransient<TrashWindow>(provider => new TrashWindow(
                provider.GetRequiredService<TrashViewModel>()));
            services.AddTransient<TrashViewModel>();
            services.AddSingleton<AutoSyncService>();
            services.AddSingleton<TrayService>();
            services.AddSingleton<HotkeyService>();
            services.AddTransient<QuickAddWindow>(provider => new QuickAddWindow(
                provider.GetRequiredService<QuickAddViewModel>()));
            services.AddTransient<QuickAddViewModel>();


            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IProjectsService, ProjectsService>();
            services.AddSingleton<ILanguageService, LanguageService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IStartupService, StartupService>();
            // Singletons on purpose: the factory owns the one shared HttpClient,
            // and the sync service is stateless.
            services.AddSingleton<TaskTracker.Core.GitHub.IGitHubApiFactory, TaskTracker.Core.GitHub.GitHubApiFactory>();
            services.AddSingleton<TaskTracker.Core.GitHub.GitHubSyncService>();

            services.AddSingleton<Func<Type, ObservableObject>>(serviceProvider => viewModelType => (ObservableObject)serviceProvider.GetRequiredService(viewModelType));

            // ValidateOnBuild constructs every registration up front, so a missing
            // dependency fails loudly at startup instead of the first time someone
            // navigates to the page that needed it.
            _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });

            DispatcherUnhandledException += (_, args) =>
            {
                TaskTracker.Core.Storage.AppLog.Write("unhandled", args.Exception);
                // Deliberately the native MessageBox, not the themed MessageWindow: this
                // runs after something already failed, and a themed window needs the
                // resource dictionaries and DI container that may be exactly what broke.
                MessageBox.Show(args.Exception.Message, "TaskTracker", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
        }

        private System.Threading.Mutex? _instanceMutex;

        /// <summary>
        /// False when this process lost the single-instance race and shut down before
        /// building any UI. OnExit still runs in that case, and must not resolve — and
        /// so construct — the main window purely to read settings off it.
        /// </summary>
        private bool _isPrimaryInstance;

        protected override void OnStartup(StartupEventArgs e)
        {
            _instanceMutex = new System.Threading.Mutex(true, "TaskTracker.SingleInstance", out var isFirstInstance);
            if (!isFirstInstance)
            {
                // A second instance would fight over the store's file watcher.
                // Native MessageBox again: this fires before the container is built, so
                // there is no language service and no theme to show it in yet.
                MessageBox.Show("TaskTracker is already running.", "TaskTracker",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            _isPrimaryInstance = true;

            var settings = _serviceProvider.GetRequiredService<ISettingsService>().Settings;
            var langservice = _serviceProvider.GetRequiredService<ILanguageService>();
            var themeService = _serviceProvider.GetRequiredService<IThemeService>();

            themeService.ChangeTheme(settings.Theme);

            // Repairs an entry left pointing at an old path — the app is published over
            // itself and can be moved, and a stale entry fails silently at every login.
            _serviceProvider.GetRequiredService<IStartupService>().Reconcile(settings.LaunchOnStartupEnabled);

            var window = _serviceProvider.GetRequiredService<MainWindow>();
            ApplyWindowPlacement(window, settings);

            // Shown either way, just minimised when Windows launched us: never showing it
            // would leave Application.MainWindow unset, and the tray's restore has nothing
            // to bring back.
            if (Core.Services.StartupCommand.StartsMinimized(e.Args) && window.DataContext is MainViewModel startupViewModel)
                startupViewModel.WindowState = WindowState.Minimized;
            window.Show();

            base.OnStartup(e);

            langservice.ChangeLanguage(settings.Language);

            _serviceProvider.GetRequiredService<AutoSyncService>();

            var tray = _serviceProvider.GetRequiredService<TrayService>();
            tray.Initialize();

            // The tray tooltip mirrors the shell's timer strip, so a forgotten timer is
            // visible while the window is minimised.
            var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
            mainViewModel.TimerChanged += () => tray.ShowRunningTimer(
                mainViewModel.IsTimerRunning ? mainViewModel.RunningTimerTitle : null,
                mainViewModel.RunningTimerText);
            mainViewModel.RefreshRunningTimer();

            var hotkeys = _serviceProvider.GetRequiredService<HotkeyService>();
            hotkeys.Initialize(window);
            hotkeys.HotkeyPressed += (_, _) =>
            {
                if (_serviceProvider.GetRequiredService<ISettingsService>().Settings.QuickAddHotkeyEnabled)
                    _serviceProvider.GetRequiredService<QuickAddWindow>().Show();
            };
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
            if (window.DataContext is MainViewModel mainViewModel)
            {
                // Go through the view model — the window's WindowState is bound to it.
                if (settings.WindowMaximized)
                    mainViewModel.WindowState = WindowState.Maximized;
                if (settings.SidebarWidth is > 120 and < 600)
                    mainViewModel.SidebarWidth = settings.SidebarWidth.Value;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (!_isPrimaryInstance)
            {
                // Nothing was started, so there is nothing to tear down — and the other
                // instance owns the files.
                _instanceMutex?.Dispose();
                base.OnExit(e);
                return;
            }

            _serviceProvider.GetRequiredService<HotkeyService>().Dispose();
            _serviceProvider.GetRequiredService<TrayService>().Dispose();

            var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
            var settings = settingsService.Settings;

            // Resolved from the container, not Application.MainWindow: by the time OnExit
            // runs the window has already closed and that property is null, so the whole
            // block used to be skipped and no setting was ever written — placement,
            // theme, language and sidebar width all silently failed to persist. The
            // window is a DI singleton, so this is the same instance either way.
            var window = _serviceProvider.GetRequiredService<MainWindow>();
            // A minimised window describes nothing worth restoring, and writing it back
            // would clear a saved "maximised" for anyone who starts with Windows and exits
            // without ever opening the window.
            if (window.WindowState != WindowState.Minimized)
            {
                settings.WindowMaximized = window.WindowState == WindowState.Maximized;
                if (window.WindowState == WindowState.Normal)
                {
                    settings.WindowLeft = window.Left;
                    settings.WindowTop = window.Top;
                    settings.WindowWidth = window.Width;
                    settings.WindowHeight = window.Height;
                }
            }
            if (window.DataContext is MainViewModel mainViewModel)
                settings.SidebarWidth = mainViewModel.SidebarWidth;

            settingsService.Save();

            // Before the flush, not after: a sync still in flight would otherwise land its
            // changes on the model once there is nothing left to persist them. Reached
            // through the current view so no page view model is constructed here just to
            // be torn down — and a board navigated away from cancelled on the way out.
            if (_serviceProvider.GetRequiredService<INavigationService>().CurrentView is ProjectViewModel board)
                board.CancelSync();

            var projectsService = _serviceProvider.GetRequiredService<IProjectsService>();
            // Blocking: saving in the background is fine while running, but exiting
            // before the write completes would drop it.
            projectsService.Flush();
            (projectsService as IDisposable)?.Dispose();
            _instanceMutex?.Dispose();

            base.OnExit(e);
        }
    }
}