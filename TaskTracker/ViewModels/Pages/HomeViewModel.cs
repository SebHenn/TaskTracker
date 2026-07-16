using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskTracker.Messages;
using TaskTracker.Core.Models;
using TaskTracker.Services;
using TaskTracker.ViewModels.Windows;

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

        public HomeViewModel(IProjectsService projectsService, INavigationService navigationService, ILanguageService languageService)
        {
            _projectsService = projectsService;
            _navigationService = navigationService;

            RecentProjects = _projectsService.RecentProjects;

            Description = languageService.GetString("AppTagline");
            languageService.LanguageChanged += () => Description = languageService.GetString("AppTagline");
        }

        [RelayCommand]
        private void OnProjectClick(ProjectModel para)
        {
            WeakReferenceMessenger.Default.Send(new ProjectSelectClickMessage(para));
        }
    }
}
