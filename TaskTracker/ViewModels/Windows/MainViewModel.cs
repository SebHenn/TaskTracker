using CommunityToolkit.Mvvm.ComponentModel;
using TaskTracker.Core.Storage;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TaskTracker.Messages;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;
using TaskTracker.Services;
using TaskTracker.ViewModels.Pages;
using TaskTracker.Views.Windows;

namespace TaskTracker.ViewModels.Windows
{
    public partial class MainViewModel : ObservableObject, IRecipient<ProjectSelectClickMessage>
    {
        private readonly IServiceProvider _serviceProvider;
        private INavigationService _navigationService;

        public INavigationService NavigationService
        {
            get { return _navigationService; }
            set
            {
                _navigationService = value;
                OnPropertyChanged();
            }
        }

        private IProjectsService _projectsService;
        private ILanguageService _languageService;
        private IDialogService _dialogService;

        [ObservableProperty]
        private bool _isHomeSelected = true;

        [ObservableProperty]
        private bool _isAgendaSelected = false;

        [ObservableProperty]
        private bool _isSettingsSelected = false;

        [ObservableProperty]
        private ProjectModel? _selectedProject;

        [ObservableProperty]
        private ObservableCollection<ProjectModel> _projects;

        [ObservableProperty]
        private ObservableCollection<ProjectModel> _shownProjects;

        [ObservableProperty]
        private WindowState _windowState = WindowState.Normal;

        /// <summary>
        /// Sidebar width, two-way bound to the splitter's column. Read back in
        /// App.OnExit alongside the window placement rather than saved on every drag,
        /// which would write the settings file continuously while resizing.
        /// </summary>
        [ObservableProperty]
        private double _sidebarWidth = 220;

        [RelayCommand]
        public void OnMinimize()
        {
            WindowState = WindowState.Minimized;
        }

        [RelayCommand]
        public void OnMaximize()
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }

        [RelayCommand]
        public void OnClose()
        {
            _projectsService.Flush();
            Application.Current.Shutdown();
        }


        [RelayCommand]
        private void OnNewTaskShortcut()
        {
            // Ctrl+T: add a task when a project board is open.
            if (NavigationService.CurrentView is ProjectViewModel projectViewModel)
                projectViewModel.NewTaskClickCommand.Execute(null);
        }

        [RelayCommand]
        private void OnNavigateToHome()
        {
            ClearOtherSelections(home: true);
            _serviceProvider.GetRequiredService<HomeViewModel>().Refresh();
            NavigationService.NavigateTo<HomeViewModel>();
        }

        [RelayCommand]
        private void OnNavigateToAgenda()
        {
            ClearOtherSelections(agenda: true);
            _serviceProvider.GetRequiredService<AgendaViewModel>().Refresh();
            NavigationService.NavigateTo<AgendaViewModel>();
        }

        [RelayCommand]
        private void OnNavigateToSettings()
        {
            ClearOtherSelections(settings: true);
            NavigationService.NavigateTo<SettingsViewModel>();
        }

        /// <summary>
        /// The sidebar entries are radio buttons sharing one group name, but the group is
        /// only enforced for the ones the user clicks — navigating in code has to clear
        /// the rest, or two rows stay lit. One place for it, so a page added later has a
        /// single line to change rather than four call sites to find.
        /// </summary>
        private void ClearOtherSelections(bool home = false, bool agenda = false, bool settings = false)
        {
            IsHomeSelected = home;
            IsAgendaSelected = agenda;
            IsSettingsSelected = settings;
            if (!home && !agenda && !settings)
                return;
            if (SelectedProject != null)
                SelectedProject.IsSelected = false;
        }

        [ObservableProperty]
        public bool _isShowEmpty = true;

        [ObservableProperty]
        public bool _isShowDone = false;

        [ObservableProperty]
        public bool _isShowOnlyFav = false;

        [ObservableProperty]
        public bool _isShowArchived = false;

        [ObservableProperty]
        private string _searchText = "";

        // ---- Running timer, surfaced shell-wide ----
        //
        // A running timer used to be visible only in the task detail drawer of the board
        // it belongs to, so closing the drawer or switching project left it running
        // invisibly and quietly banking hours against the wrong task.

        [ObservableProperty]
        private bool _isTimerRunning;

        [ObservableProperty]
        private string _runningTimerTitle = "";

        [ObservableProperty]
        private string _runningTimerText = "";

        private readonly System.Windows.Threading.DispatcherTimer _timerTick =
            new() { Interval = TimeSpan.FromSeconds(1) };

        /// <summary>Recomputes the shell timer strip from whatever the store currently says.</summary>
        public void RefreshRunningTimer()
        {
            var running = _projectsService.projectModels
                .SelectMany(p => p.Tasks)
                .FirstOrDefault(t => t.TimerStartedAtUtc != null);

            IsTimerRunning = running != null;
            RunningTimerTitle = running?.Title ?? "";
            RunningTimerText = running == null ? "" : TimeTracking.Format(TimeTracking.TotalSeconds(running));

            if (IsTimerRunning)
                _timerTick.Start();
            else
                _timerTick.Stop();

            TimerChanged?.Invoke();
        }

        /// <summary>Raised whenever the running-timer state changes, for the tray tooltip.</summary>
        public event Action? TimerChanged;

        [RelayCommand]
        private void OnStopRunningTimer()
        {
            var running = _projectsService.projectModels
                .SelectMany(p => p.Tasks)
                .FirstOrDefault(t => t.TimerStartedAtUtc != null);
            if (running != null)
                TimeTracking.Stop(running);
            RefreshRunningTimer();
        }

        /// <summary>
        /// Typing fires this per keystroke, and each search walks every task in every
        /// project. Debouncing collapses a burst of typing into one scan once the
        /// user pauses.
        /// </summary>
        private readonly System.Windows.Threading.DispatcherTimer _searchDebounce =
            new() { Interval = TimeSpan.FromMilliseconds(200) };

        partial void OnSearchTextChanged(string value)
        {
            _searchDebounce.Stop();

            if (string.IsNullOrWhiteSpace(value))
            {
                // Clearing the box has nothing to compute, so leave immediately
                // rather than making the user wait out the debounce.
                if (NavigationService.CurrentView is SearchViewModel)
                {
                    ClearOtherSelections(home: true);
                    NavigationService.NavigateTo<HomeViewModel>();
                }
                return;
            }

            _searchDebounce.Start();
        }

        private void RunPendingSearch()
        {
            _searchDebounce.Stop();

            var query = SearchText.Trim();
            if (query.Length == 0)
                return;

            var searchViewModel = _serviceProvider.GetRequiredService<SearchViewModel>();
            searchViewModel.RunSearch(query);
            if (NavigationService.CurrentView is not SearchViewModel)
            {
                // Search has no sidebar entry of its own, so every other one clears.
                IsHomeSelected = false;
                IsAgendaSelected = false;
                IsSettingsSelected = false;
                if (SelectedProject != null)
                    SelectedProject.IsSelected = false;
                NavigationService.NavigateTo<SearchViewModel>();
            }
        }

        [RelayCommand]
        private void OnSortClick()
        {
            var sortProjectWindow = _serviceProvider.GetRequiredService<SortProjectWindow>();

            sortProjectWindow.ShowDialog();

            if (sortProjectWindow.DataContext is SortProjectViewModel vm && vm.DialogResult == true)
            {
                IsShowEmpty = vm.IsShowEmpty;
                IsShowDone = vm.IsShowDone;
                IsShowOnlyFav = vm.IsShowOnlyFav;
                IsShowArchived = vm.IsShowArchived;
                ResortProjects();
            }
        }

        public void ResortProjects()
        {
            var filteredProjects = Projects.Where(p =>
            {
                if (p.IsArchived && !IsShowArchived)
                    return false;

                if (IsShowOnlyFav && !p.IsFavourite)
                    return false;

                if (IsShowEmpty && !p.Tasks.Any())
                    return true;
                if (IsShowDone && p.Tasks.Any() && p.Tasks.All(t => t.IsDone))
                    return true;
                if (!IsShowDone && p.Tasks.Any() && p.Tasks.All(t => t.IsDone))
                    return false;
                if (!IsShowEmpty && !p.Tasks.Any())
                    return false;

                return true;
            });

            ShownProjects = new ObservableCollection<ProjectModel>(filteredProjects);
        }

        [RelayCommand]
        private void OnNavigateToProject(Guid projectId)
        {
            // Every non-project entry clears; the previously selected project clears too,
            // since the new one lights up below.
            IsHomeSelected = false;
            IsAgendaSelected = false;
            IsSettingsSelected = false;
            if (SelectedProject != null)
                SelectedProject.IsSelected = false;

            var selectedProject = _projectsService.projectModels.FirstOrDefault(x => x.Id == projectId);
            if (selectedProject != null)
            {
                selectedProject.IsSelected = true;
                SelectedProject = selectedProject;

                _projectsService.AddRecentProject(selectedProject);
            }

            NavigationService.NavigateTo<ProjectViewModel>();
        }

        [RelayCommand]
        private void OnNewProjectClick()
        {
            var newProjectWindow = _serviceProvider.GetRequiredService<NewProjectWindow>();

            newProjectWindow.ShowDialog();

            if (newProjectWindow.DataContext is NewProjectViewModel vm && vm.DialogResult == true)
            {
                if (ProjectNaming.IsAvailable(_projectsService.projectModels, vm.Name))
                {
                    var created = _projectsService.AddProject(vm.Name.Trim(), vm.Description);
                    created.Color = string.IsNullOrEmpty(vm.SelectedColor) ? null : vm.SelectedColor;
                    OnNavigateToProject(created.Id);
                    return;
                }
                _dialogService.Error(_languageService.GetString("InvalidProjectName"));
            }

            ResortProjects();
        }

        public void Receive(ProjectSelectClickMessage message)
        {
            OnNavigateToProject(message.Value.Id);
        }

        /// <summary>
        /// Open a board and select one task on it, for the agenda and search results.
        ///
        /// Navigation and selection happen here in order rather than as two independent
        /// message handlers: ProjectViewModel follows SelectedProject, so it only knows
        /// which board it is showing *after* the navigation below, and a handler racing
        /// this one would silently do nothing whenever it ran first.
        /// </summary>
        public void Receive(OpenTaskMessage message)
        {
            OnNavigateToProject(message.Project.Id);

            var projectViewModel = _serviceProvider.GetRequiredService<ProjectViewModel>();
            if (projectViewModel.CurrentProject?.Id == message.Project.Id)
                projectViewModel.OnOpenTaskDetail(message.Task);
        }

        public void Receive(StoreReloadedMessage message)
        {
            // The store was replaced from disk; our object references may be stale.
            SelectedProject = SelectedProject != null
                ? Projects.FirstOrDefault(p => p.Id == SelectedProject.Id)
                : null;
            var onAPageOfItsOwn = IsSettingsSelected || IsAgendaSelected || IsHomeSelected;
            if (SelectedProject == null && !IsSettingsSelected && !IsAgendaSelected)
            {
                // The open project is gone from the reloaded store; Home is the only page
                // guaranteed to still have something to show.
                ClearOtherSelections(home: true);
                NavigationService.NavigateTo<HomeViewModel>();
            }
            else if (SelectedProject != null && !onAPageOfItsOwn)
            {
                SelectedProject.IsSelected = true;
            }
            ResortProjects();
        }

        public MainViewModel(INavigationService navigationService, IServiceProvider serviceProvider, IProjectsService projectsService, ILanguageService languageService, IDialogService dialogService)
        {
            // Assigned to the field, not through the property: the setter's
            // OnPropertyChanged is pointless before anything is bound, and going through
            // it hides the assignment from nullable flow analysis.
            _navigationService = navigationService;
            _projectsService = projectsService;
            _languageService = languageService;
            _dialogService = dialogService;

            _searchDebounce.Tick += (_, _) => RunPendingSearch();

            // Only refreshes the elapsed text; starting and stopping go through
            // RefreshRunningTimer, which is also what starts and stops this tick.
            _timerTick.Tick += (_, _) => RefreshRunningTimer();

            WeakReferenceMessenger.Default.Register<ProjectSelectClickMessage>(this);
            WeakReferenceMessenger.Default.Register<StoreReloadedMessage>(this, (r, m) => ((MainViewModel)r).Receive(m));
            WeakReferenceMessenger.Default.Register<OpenTaskMessage>(this, (r, m) => ((MainViewModel)r).Receive(m));

            Projects = _projectsService.projectModels;
            ShownProjects = Projects;
            ResortProjects();

            if (projectsService.projectModels.Count > 0)
                SelectedProject = projectsService.projectModels[0];

            _serviceProvider = serviceProvider;

            // A timer left running when the app was last closed is still running now.
            RefreshRunningTimer();

            NavigationService.NavigateTo<HomeViewModel>();
        }
    }
}
