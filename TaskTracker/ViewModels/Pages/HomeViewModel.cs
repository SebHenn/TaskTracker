using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;
using TaskTracker.Messages;
using TaskTracker.Services;

namespace TaskTracker.ViewModels.Pages
{
    /// <summary>
    /// One project's last seven days, with the completed list capped for display —
    /// a productive week can complete dozens of tasks and the point of the section is
    /// the shape of the week, not an exhaustive log.
    /// </summary>
    public record ReviewRow(
        string ProjectName,
        int Completed,
        int Created,
        int Open,
        int Overdue,
        double TrackedHours,
        IReadOnlyList<string> Highlights,
        string MoreText)
    {
        public bool HasHighlights => Highlights.Count > 0;
        public bool HasMore => MoreText.Length > 0;

        /// <summary>
        /// Own flag rather than a count converter: TrackedHours is a double, and
        /// CountToVisibilityConverter only recognises ints — it would silently hide
        /// every project's tracked time.
        /// </summary>
        public bool HasTrackedTime => TrackedHours > 0;
    }

    public partial class HomeViewModel : ObservableObject
    {
        /// <summary>Completed titles listed per project before the rest become a count.</summary>
        private const int HighlightLimit = 5;

        private IProjectsService _projectsService;

        private INavigationService _navigationService;
        private readonly ILanguageService _languageService;

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

        /// <summary>
        /// The last seven days, per project. ReviewReport has been implemented and tested
        /// in Core all along but was reachable only through the MCP <c>weekly_review</c>
        /// tool, so the app itself never showed it.
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<ReviewRow> _reviewRows = [];

        [ObservableProperty]
        private int _reviewCompleted;

        [ObservableProperty]
        private int _reviewCreated;

        [ObservableProperty]
        private string _reviewRangeText = "";

        /// <summary>False when nothing at all happened, which gets a sentence instead of an empty list.</summary>
        [ObservableProperty]
        private bool _hasReview;

        public HomeViewModel(IProjectsService projectsService, INavigationService navigationService, ILanguageService languageService)
        {
            _projectsService = projectsService;
            _navigationService = navigationService;
            _languageService = languageService;

            RecentProjects = _projectsService.RecentProjects;

            Description = languageService.GetString("AppTagline");
            languageService.LanguageChanged += () =>
            {
                Description = languageService.GetString("AppTagline");
                // The review's date range and "+N more" are built from format strings,
                // so they have to be rebuilt rather than re-resolved by the binding.
                Refresh();
            };

            WeakReferenceMessenger.Default.Register<StoreReloadedMessage>(
                this, (r, m) => ((HomeViewModel)r).Refresh());
            Refresh();
        }

        /// <summary>Rebuilds everything the home page derives from the store.</summary>
        public void Refresh()
        {
            RefreshDueTasks();
            RefreshReview();
        }

        public void RefreshDueTasks()
        {
            var overview = DueTasks.Collect(_projectsService.projectModels);
            OverdueTasks = new ObservableCollection<DueTaskItem>(overview.Overdue);
            DueTodayTasks = new ObservableCollection<DueTaskItem>(overview.DueToday);
            DueThisWeekTasks = new ObservableCollection<DueTaskItem>(overview.DueThisWeek);
            HasDueTasks = !overview.IsEmpty;
        }

        private void RefreshReview()
        {
            var review = ReviewReport.Compute(_projectsService.projectModels);

            ReviewCompleted = review.TotalCompleted;
            ReviewCreated = review.TotalCreated;
            ReviewRangeText = string.Format(
                _languageService.GetString("ReviewRangeFormat"),
                review.FromUtc.ToLocalTime(), review.ToUtc.ToLocalTime());

            var moreFormat = _languageService.GetString("ReviewMoreCompleted");
            ReviewRows = new ObservableCollection<ReviewRow>(review.Projects.Select(project =>
            {
                var extra = project.CompletedTitles.Count - HighlightLimit;
                return new ReviewRow(
                    project.ProjectName,
                    project.CompletedTitles.Count,
                    project.Created,
                    project.Open,
                    project.Overdue,
                    project.TrackedHours,
                    project.CompletedTitles.Take(HighlightLimit).ToList(),
                    extra > 0 ? string.Format(moreFormat, extra) : "");
            }));

            // A week with nothing completed and nothing created is worth saying out loud;
            // an empty list on its own reads as a section that failed to load.
            HasReview = ReviewCompleted > 0 || ReviewCreated > 0;
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
