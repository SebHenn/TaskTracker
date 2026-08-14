using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;
using TaskTracker.Messages;
using TaskTracker.Services;

namespace TaskTracker.ViewModels.Pages
{
    public partial class SearchViewModel : ObservableObject
    {
        private readonly IProjectsService _projectsService;
        private readonly ILanguageService _languageService;

        [ObservableProperty]
        private string _query = "";

        [ObservableProperty]
        private int _resultCount;

        [ObservableProperty]
        private ObservableCollection<SearchResult> _results = new();

        /// <summary>
        /// Narrowing controls, matching the board's. Reused rather than reinvented so
        /// "overdue" means the same thing on both, and so there is one place the buckets
        /// are defined.
        /// </summary>
        [ObservableProperty]
        private bool _includeDone = true;

        [ObservableProperty]
        private bool _includeArchived;

        [ObservableProperty]
        private ObservableCollection<DueFilterOption> _dueFilters = [];

        [ObservableProperty]
        private ObservableCollection<PriorityOption> _priorityFilters = [];

        [ObservableProperty]
        private DueFilterOption? _selectedDueFilter;

        [ObservableProperty]
        private PriorityOption? _selectedPriorityFilter;

        public SearchViewModel(IProjectsService projectsService, ILanguageService languageService)
        {
            _projectsService = projectsService;
            _languageService = languageService;

            BuildFilterOptions();
            // The option text is localized, so the lists are rebuilt on a language change
            // rather than re-resolved by the bindings.
            _languageService.LanguageChanged += () =>
            {
                BuildFilterOptions();
                RunSearch(Query);
            };
        }

        private void BuildFilterOptions()
        {
            var previousDue = SelectedDueFilter?.Value ?? DueFilter.Any;
            var previousPriority = SelectedPriorityFilter?.Value;

            DueFilters =
            [
                new(DueFilter.Any, _languageService.GetString("AnyDueDate")),
                new(DueFilter.Overdue, _languageService.GetString("OverdueSection")),
                new(DueFilter.DueToday, _languageService.GetString("DueTodaySection")),
                new(DueFilter.DueThisWeek, _languageService.GetString("DueThisWeekSection")),
                new(DueFilter.NoDueDate, _languageService.GetString("NoDueDate")),
            ];

            PriorityFilters = [new(null, _languageService.GetString("AnyPriority"))];
            foreach (var priority in Enum.GetValues<TaskPriority>())
                PriorityFilters.Add(new(priority, priority.ToString()));

            _isSettingFilters = true;
            try
            {
                SelectedDueFilter = DueFilters.First(option => option.Value == previousDue);
                SelectedPriorityFilter = PriorityFilters.First(option => option.Value == previousPriority);
            }
            finally
            {
                _isSettingFilters = false;
            }
        }

        /// <summary>Set while several filters are assigned at once, so the search runs once.</summary>
        private bool _isSettingFilters;

        partial void OnSelectedDueFilterChanged(DueFilterOption? value) => RerunUnlessBatched();

        partial void OnSelectedPriorityFilterChanged(PriorityOption? value) => RerunUnlessBatched();

        partial void OnIncludeDoneChanged(bool value) => RerunUnlessBatched();

        partial void OnIncludeArchivedChanged(bool value) => RerunUnlessBatched();

        private void RerunUnlessBatched()
        {
            if (!_isSettingFilters)
                RunSearch(Query);
        }

        public void RunSearch(string query)
        {
            Query = query;
            if (string.IsNullOrWhiteSpace(query))
            {
                Results = [];
                ResultCount = 0;
                return;
            }

            var board = new BoardFilter(
                null,
                SelectedDueFilter?.Value ?? DueFilter.Any,
                SelectedPriorityFilter?.Value);
            var today = DateTime.Today;

            // Build the list first and assign once. Clearing then adding one by one
            // raised a collection-changed event per hit, so the results list
            // re-laid-out as many times as there were matches.
            Results = new ObservableCollection<SearchResult>(
                TaskSearch.Search(_projectsService.projectModels, query, null, IncludeDone, IncludeArchived)
                    .Where(result => board.Matches(result.Task, today)));
            ResultCount = Results.Count;
        }

        [RelayCommand]
        private void OnResultClick(SearchResult result)
        {
            // Opens the board with the matched task selected. It used to send only
            // ProjectSelectClickMessage, which dropped you on the project and left you to
            // find by eye the task you had just searched for.
            WeakReferenceMessenger.Default.Send(new OpenTaskMessage(result.Project, result.Task));
        }
    }
}
