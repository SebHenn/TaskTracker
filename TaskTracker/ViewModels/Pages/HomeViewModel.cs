using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.ViewModels.Windows;

namespace TaskTracker.ViewModels.Pages
{
    public partial class HomeViewModel : ObservableObject
    {
        private IProjectsService _projectsService;

        private INavigationService _navigationService;

        [ObservableProperty]
        public string _name = "Tasktracker";

        [ObservableProperty]
        public string _description = "Keep track of multiple projects by adding Tasks to them";

        [ObservableProperty]
        public ObservableCollection<ProjectModel> _recentProjects;

        public HomeViewModel(IProjectsService projectsService,INavigationService navigationService) 
        {
            _projectsService = projectsService;
            _navigationService = navigationService;

            RecentProjects = _projectsService.RecentProjects;
        }

        [RelayCommand]
        private void OnProjectClick(ProjectModel para)
        {
            var selectedProject = _projectsService.projectModels.FirstOrDefault(x => x.Name == para.Name);
            if (selectedProject != null)
            {
                selectedProject.IsSelected = true;

                _projectsService.AddRecentProject(selectedProject);
            }

            _navigationService.NavigateTo<ProjectViewModel>();
        }
    }
}
