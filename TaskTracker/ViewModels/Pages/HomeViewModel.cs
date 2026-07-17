using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;
using TaskTracker.Messages;
using TaskTracker.Services;

namespace TaskTracker.ViewModels.Pages
{
    public partial class HomeViewModel : ObservableObject
    {
        private IProjectsService _projectsService;

        private INavigationService _navigationService;

        [ObservableProperty]
        public string _name = "TaskTracker";

        [ObservableProperty]
        public string _description = "";

        [ObservableProperty]
        public ObservableCollection<ProjectModel> _recentProjects;

        [ObservableProperty]
        private ObservableCollection<DueTaskItem> _overdueTasks = [];

        [ObservableProperty]
        private ObservableCollection<DueTaskItem> _dueTodayTasks = [];

        [ObservableProperty]
        private ObservableCollection<DueTaskItem> _dueThisWeekTasks = [];

        [ObservableProperty]
        private bool _hasDueTasks;

        public HomeViewModel(IProjectsService projectsService, INavigationService navigationService, ILanguageService languageService)
        {
            _projectsService = projectsService;
            _navigationService = navigationService;

            RecentProjects = _projectsService.RecentProjects;

            Description = languageService.GetString("AppTagline");
            languageService.LanguageChanged += () => Description = languageService.GetString("AppTagline");

            WeakReferenceMessenger.Default.Register<StoreReloadedMessage>(
                this, (r, m) => ((HomeViewModel)r).RefreshDueTasks());
            RefreshDueTasks();
        }

        public void RefreshDueTasks()
        {
            var overview = DueTasks.Collect(_projectsService.projectModels);
            OverdueTasks = new ObservableCollection<DueTaskItem>(overview.Overdue);
            DueTodayTasks = new ObservableCollection<DueTaskItem>(overview.DueToday);
            DueThisWeekTasks = new ObservableCollection<DueTaskItem>(overview.DueThisWeek);
            HasDueTasks = !overview.IsEmpty;
        }

        [RelayCommand]
        private void OnProjectClick(ProjectModel para)
        {
            WeakReferenceMessenger.Default.Send(new ProjectSelectClickMessage(para));
        }

        [RelayCommand]
        private void OnDueTaskClick(DueTaskItem item)
        {
            WeakReferenceMessenger.Default.Send(new ProjectSelectClickMessage(item.Project));
        }
    }
}
