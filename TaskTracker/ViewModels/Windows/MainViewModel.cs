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

        [RelayCommand]
        private void OnNewProjectClick()
        {
            var newProjectWindow = _serviceProvider.GetRequiredService<NewProjectWindow>();

            newProjectWindow.ShowDialog();

            if (newProjectWindow.DataContext is NewProjectWindowViewModel vm && vm.DialogResult == true)
            {
                string enteredName = vm.Name;
                MessageBox.Show($"Entered name: {enteredName}");
            }
            else
            {
                MessageBox.Show("Operation canceled.");
            }
        }

        public MainViewModel(INavigationService navigationService, IServiceProvider serviceProvider)
        {
            Projects = new ObservableCollection<ProjectModel>
            {
                new ProjectModel { Name = "Project 1", Description = "Details about Project 1" },
                new ProjectModel { Name = "Project 2", Description = "Details about Project 2" }
            };

            SelectedProject = Projects[0];

            NavigationService = navigationService;

            _serviceProvider = serviceProvider;

            NavigationService.NavigateTo<HomeViewModel>();
        }
    }
}
