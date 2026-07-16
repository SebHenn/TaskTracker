using CommunityToolkit.Mvvm.ComponentModel;
using TaskTracker.Core.Storage;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TaskTracker.Messages;
using TaskTracker.Core.Models;
using TaskTracker.Services;
using TaskTracker.ViewModels.Pages;
using TaskTracker.Views.Windows;

namespace TaskTracker.ViewModels.Windows
{
    public partial class MainViewModel : ObservableObject, IRecipient<ProjectSelectClickMessage>
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
        private bool _isHomeSelected = true;

        [ObservableProperty]
        private bool _isSettingsSelected = false;

        [ObservableProperty]
        private ProjectModel? _selectedProject;

        [ObservableProperty]
        private ObservableCollection<ProjectModel> _projects;

        [ObservableProperty]
        private ObservableCollection<ProjectModel> _shownProjects;

        [ObservableProperty]
        private WindowState _windowState = WindowState.Normal;

        [RelayCommand]
        public void OnMinimize()
        {
            WindowState = WindowState.Minimized;
        }

        [RelayCommand]
        public void OnMaximize()
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }

        [RelayCommand]
        public void OnClose()
        {
            _projectsService.SaveNow();
            Application.Current.Shutdown();
        }


        [RelayCommand]
        private void OnNavigateToHome()
        {
            NavigationService.NavigateTo<HomeViewModel>();
        }

        [RelayCommand]
        private void OnNavigateToSettings()
        {
            NavigationService.NavigateTo<SettingsViewModel>();
        }

        [ObservableProperty]
        public bool _isShowEmpty = true;

        [ObservableProperty]
        public bool _isShowDone = false;

        [ObservableProperty]
        public bool _isShowOnlyFav = false;

        [ObservableProperty]
        public bool _isShowArchived = false;

        [ObservableProperty]
        private string _searchText = "";

        partial void OnSearchTextChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (NavigationService.CurrentView is SearchViewModel)
                {
                    IsHomeSelected = true;
                    NavigationService.NavigateTo<HomeViewModel>();
                }
                return;
            }

            var searchViewModel = _serviceProvider.GetRequiredService<SearchViewModel>();
            searchViewModel.RunSearch(value.Trim());
            if (NavigationService.CurrentView is not SearchViewModel)
            {
                IsHomeSelected = false;
                IsSettingsSelected = false;
                if (SelectedProject != null)
                    SelectedProject.IsSelected = false;
                NavigationService.NavigateTo<SearchViewModel>();
            }
        }

        [RelayCommand]
        private void OnSortClick()
        {
            var sortProjectWindow = _serviceProvider.GetRequiredService<SortProjectWindow>();
            
            sortProjectWindow.ShowDialog();

            if(sortProjectWindow.DataContext is SortProjectViewModel vm && vm.DialogResult == true)
            {
                IsShowEmpty = vm.IsShowEmpty;
                IsShowDone = vm.IsShowDone;
                IsShowOnlyFav = vm.IsShowOnlyFav;
                IsShowArchived = vm.IsShowArchived;
                ResortProjects();
            }
        }

        public void ResortProjects()
        {
            var filteredProjects = Projects.Where(p =>
            {
                if (p.IsArchived && !IsShowArchived)
                    return false;

                if(IsShowOnlyFav && !p.IsFavourite)
                    return false;

                if (IsShowEmpty && !p.Tasks.Any())
                    return true;
                if (IsShowDone && p.Tasks.Any() && p.Tasks.All(t => t.IsDone))
                    return true;
                if (!IsShowDone && p.Tasks.Any() && p.Tasks.All(t => t.IsDone))
                    return false;
                if (!IsShowEmpty && !p.Tasks.Any())
                    return false;

                return true;
            });

            ShownProjects = new ObservableCollection<ProjectModel>(filteredProjects);
        }

        [RelayCommand]
        private void OnNavigateToProject(Guid projectId)
        {
            if(IsHomeSelected)
            {
                IsHomeSelected = false;
            }
            else if(IsSettingsSelected)
            {
                IsSettingsSelected = false;
            }
            else if (SelectedProject != null)
            {
                SelectedProject.IsSelected = false;
            }

            var selectedProject = _projectsService.projectModels.FirstOrDefault(x => x.Id == projectId);
            if (selectedProject != null)
            {
                selectedProject.IsSelected = true;
                SelectedProject = selectedProject;

                _projectsService.AddRecentProject(selectedProject);
            }

            NavigationService.NavigateTo<ProjectViewModel>();
        }

        [RelayCommand]
        private void OnNewProjectClick()
        {
            var newProjectWindow = _serviceProvider.GetRequiredService<NewProjectWindow>();

            newProjectWindow.ShowDialog();

            if (newProjectWindow.DataContext is NewProjectViewModel vm && vm.DialogResult == true)
            {
                if (!string.IsNullOrWhiteSpace(vm.Name) && !Projects.Any(x => x.Name == vm.Name))
                {
                    var created = _projectsService.AddProject(vm.Name, vm.Description);
                    OnNavigateToProject(created.Id);
                    return;
                }
                MessageBox.Show("Invalid project name or already exists");
            }

            ResortProjects();
        }

        public void Receive(ProjectSelectClickMessage message)
        {
            OnNavigateToProject(message.Value.Id);
        }

        public void Receive(StoreReloadedMessage message)
        {
            // The store was replaced from disk; our object references may be stale.
            SelectedProject = SelectedProject != null
                ? Projects.FirstOrDefault(p => p.Id == SelectedProject.Id)
                : null;
            if (SelectedProject == null && !IsSettingsSelected)
            {
                IsHomeSelected = true;
                NavigationService.NavigateTo<HomeViewModel>();
            }
            else if (SelectedProject != null && !IsHomeSelected && !IsSettingsSelected)
            {
                SelectedProject.IsSelected = true;
            }
            ResortProjects();
        }

        public MainViewModel(INavigationService navigationService, IServiceProvider serviceProvider, IProjectsService projectsService)
        {
            _projectsService = projectsService;

            WeakReferenceMessenger.Default.Register<ProjectSelectClickMessage>(this);
            WeakReferenceMessenger.Default.Register<StoreReloadedMessage>(this, (r, m) => ((MainViewModel)r).Receive(m));

            Projects = _projectsService.projectModels;
            ShownProjects = Projects;
            ResortProjects();

            if(projectsService.projectModels.Count > 0)
                SelectedProject = projectsService.projectModels[0];

            NavigationService = navigationService;

            _serviceProvider = serviceProvider;

            NavigationService.NavigateTo<HomeViewModel>();
        }
    }
}
