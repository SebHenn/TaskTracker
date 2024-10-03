using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using TaskTracker.Models;
using TaskTracker.Services;

namespace TaskTracker.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
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

        public ObservableCollection<ProjectModel> Projects { get; set; }
        private ProjectModel _selectedProject;

        public ProjectModel SelectedProject
        {
            get => _selectedProject;
            set
            {
                _selectedProject = value;
                OnPropertyChanged();
            }
        }

        [RelayCommand]
        private void OnNavigateToHome()
        {
            NavigationService.NavigateTo<HomeViewModel>();
        }

        [RelayCommand]
        private void OnNavigateToProject(string para)
        {
            SelectedProject = Projects.FirstOrDefault(x => x.Name == para);
            NavigationService.NavigateTo<ProjectViewModel>();
        }

        public MainViewModel(INavigationService navigationService)
        {
            Projects = new ObservableCollection<ProjectModel>
            {
                new ProjectModel { Name = "Project 1", Description = "Details about Project 1" },
                new ProjectModel { Name = "Project 2", Description = "Details about Project 2" }
            };

            SelectedProject = Projects[0];

            NavigationService = navigationService;

            NavigationService.NavigateTo<HomeViewModel>();
        }
    }
}
