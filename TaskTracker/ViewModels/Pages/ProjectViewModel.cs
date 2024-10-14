using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.ViewModels.Windows;
using TaskTracker.Views.Windows;

namespace TaskTracker.ViewModels.Pages
{
    public partial class ProjectViewModel : ObservableObject
    {
        private IProjectsService _projectsService;
        private INavigationService _navigationService;
        private IServiceProvider _serviceProvider;

        [ObservableProperty]
        private bool _isEditing = false;

        [ObservableProperty]
        private ProjectModel _currentProject;

        public ProjectViewModel(MainViewModel mainViewModel, IProjectsService projectsService, INavigationService navigationService, IServiceProvider serviceProvider)
        {
            _projectsService = projectsService;
            _navigationService = navigationService;
            _serviceProvider = serviceProvider;

            CurrentProject = mainViewModel.SelectedProject;
            mainViewModel.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(mainViewModel.SelectedProject))
                {
                    CurrentProject = mainViewModel.SelectedProject;
                }
            };
        }

        [RelayCommand]
        private void OnChangeProject()
        {
            IsEditing = true;

            var newProjectWindow = _serviceProvider.GetRequiredService<NewProjectWindow>();

            newProjectWindow.ShowDialog();

            if (newProjectWindow.DataContext is NewProjectViewModel vm && vm.DialogResult == true)
            {
                string enteredName = vm.Name;
                string enteredDescription = vm.Description;
                _projectsService.ChangeProjectName(CurrentProject, enteredName);
                _projectsService.ChangeProjectDescription(CurrentProject, enteredDescription);
            }

            IsEditing = false;
        }

        [RelayCommand]
        private void OnDeleteProject()
        {
            _projectsService.RemoveProject(CurrentProject);
            _navigationService.NavigateTo<HomeViewModel>();
        }
    }
}
