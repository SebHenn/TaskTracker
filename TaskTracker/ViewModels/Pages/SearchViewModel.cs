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

        public ObservableCollection<SearchResult> Results { get; } = new();

        public SearchViewModel(IProjectsService projectsService)
        {
            _projectsService = projectsService;
        }

        public void RunSearch(string query)
        {
            Query = query;
            Results.Clear();
            foreach (var result in TaskSearch.Search(_projectsService.projectModels, query))
                Results.Add(result);
            ResultCount = Results.Count;
        }

        [RelayCommand]
        private void OnResultClick(SearchResult result)
        {
            WeakReferenceMessenger.Default.Send(new ProjectSelectClickMessage(result.Project));
        }
    }
}
