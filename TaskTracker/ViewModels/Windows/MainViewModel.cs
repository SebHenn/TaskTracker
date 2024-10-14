using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.ViewModels.Pages;
using TaskTracker.Views.Windows;

namespace TaskTracker.ViewModels.Windows
{
    public partial class MainViewModel : ObservableObject
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

        [ObservableProperty]
        private ProjectModel _selectedProject;

        [ObservableProperty]
        private ObservableCollection<ProjectModel> _projects;

        [RelayCommand]
        private void OnNavigateToHome()
        {
            NavigationService.NavigateTo<HomeViewModel>();
        }

        [RelayCommand]
        private void OnNavigateToProject(string para)
        {
            SelectedProject = _projectsService.projectModels.FirstOrDefault(x => x.Name == para);
            NavigationService.NavigateTo<ProjectViewModel>();
        }

        [RelayCommand]
        private void OnNewProjectClick()
        {
            var newProjectWindow = _serviceProvider.GetRequiredService<NewProjectWindow>();

            newProjectWindow.ShowDialog();

            if (newProjectWindow.DataContext is NewProjectViewModel vm && vm.DialogResult == true)
            {
                string enteredName = vm.Name;
                string enteredDescription = vm.Description;
                _projectsService.AddProject(enteredName, enteredDescription);
                OnNavigateToProject(enteredName);
            }
        }

        public MainViewModel(INavigationService navigationService, IServiceProvider serviceProvider, IProjectsService projectsService)
        {
            _projectsService = projectsService;

            Projects = _projectsService.projectModels;

            if(projectsService.projectModels.Count > 0)
                SelectedProject = projectsService.projectModels[0];

            NavigationService = navigationService;

            _serviceProvider = serviceProvider;

            NavigationService.NavigateTo<HomeViewModel>();
        }
    }
}
