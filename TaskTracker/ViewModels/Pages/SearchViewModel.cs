using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using TaskTracker.Core.Services;
using TaskTracker.Messages;
using TaskTracker.Services;

namespace TaskTracker.ViewModels.Pages
{
    public partial class SearchViewModel : ObservableObject
    {
        private readonly IProjectsService _projectsService;

        [ObservableProperty]
        private string _query = "";

        [ObservableProperty]
        private int _resultCount;

        [ObservableProperty]
        private ObservableCollection<SearchResult> _results = new();

        public SearchViewModel(IProjectsService projectsService)
        {
            _projectsService = projectsService;
        }

        public void RunSearch(string query)
        {
            Query = query;
            // Build the list first and assign once. Clearing then adding one by one
            // raised a collection-changed event per hit, so the results list
            // re-laid-out as many times as there were matches.
            Results = new ObservableCollection<SearchResult>(
                TaskSearch.Search(_projectsService.projectModels, query));
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
