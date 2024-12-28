using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private MainViewModel _mainViewModel;

        [ObservableProperty]
        private string _favImage = "/Assets/starEmpty-32.png";

        [RelayCommand]
        public void OnFavProject()
        {
            CurrentProject.IsFavourite = !CurrentProject.IsFavourite;
            SetImage();
            _mainViewModel.ResortProjects();
        }

        private void SetImage()
        {
            if (CurrentProject.IsFavourite)
                FavImage = "/Assets/starFull-32.png";
            else
                FavImage = "/Assets/starEmpty-32.png";
        }

        [ObservableProperty]
        private ObservableCollection<TaskModel> _notDoneTasks = [];

        [ObservableProperty]
        private ObservableCollection<TaskModel> _doneTasks = [];

        [ObservableProperty]
        private bool _isEditing = false;

        [ObservableProperty]
        private bool _isCreateTask = false;

        [ObservableProperty]
        private bool _isEditTask = false;

        [ObservableProperty]
        private ProjectModel _currentProject;

        public ProjectViewModel(MainViewModel mainViewModel, IProjectsService projectsService, INavigationService navigationService, IServiceProvider serviceProvider)
        {
            _mainViewModel = mainViewModel;
            _projectsService = projectsService;
            _navigationService = navigationService;
            _serviceProvider = serviceProvider;

            CurrentProject = mainViewModel.SelectedProject;
            mainViewModel.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(mainViewModel.SelectedProject))
                {
                    CurrentProject = mainViewModel.SelectedProject;

                    CategorizeTasks();
                    SetImage();
                }
            };
            CategorizeTasks();
            SetImage();
        }

        private void CategorizeTasks()
        {
            NotDoneTasks = new ObservableCollection<TaskModel>(CurrentProject.Tasks.Where(task => !task.IsDone));
            DoneTasks = new ObservableCollection<TaskModel>(CurrentProject.Tasks.Where(task => task.IsDone));
        }

        [RelayCommand]
        private void OnChangeProject()
        {
            IsEditing = true;

            var newProjectWindow = _serviceProvider.GetRequiredService<NewProjectWindow>();

            ((NewProjectViewModel)newProjectWindow.DataContext).Name = CurrentProject.Name;
            ((NewProjectViewModel)newProjectWindow.DataContext).Description = CurrentProject.Description;

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
            _mainViewModel.IsHomeSelected = true;
            _mainViewModel.ResortProjects();
            _navigationService.NavigateTo<HomeViewModel>();
        }

        [RelayCommand]
        private void OnNewTaskClick()
        {
            IsCreateTask = true;

            TaskModel task = new TaskModel();

            var newProjectWindow = _serviceProvider.GetRequiredService<NewProjectWindow>();

            newProjectWindow.ShowDialog();

            if (newProjectWindow.DataContext is NewProjectViewModel vm && vm.DialogResult == true)
            {
                string enteredName = vm.Name;
                string enteredDescription = vm.Description;
                task.Title = enteredName;
                task.Description = enteredDescription;
                task.IsDone = false;
                CurrentProject.Tasks.Add(task);
                CategorizeTasks();
            }

            IsCreateTask = false;
        }

        [RelayCommand]
        private void OnMarkAsDone(TaskModel task)
        {
            task.IsDone = true;
            CategorizeTasks();
        }

        [RelayCommand]
        private void OnMarkAsInProgress(TaskModel task)
        {
            task.IsDone = false;
            CategorizeTasks();
        }

        [RelayCommand]
        private void OnEditTask(TaskModel task)
        {
            IsEditTask = true;

            var newProjectWindow = _serviceProvider.GetRequiredService<NewProjectWindow>();

            if (newProjectWindow.DataContext is NewProjectViewModel vm)
            {
                vm.Name = task.Title;
                vm.Description = task.Description;
            }

            newProjectWindow.ShowDialog();

            if (newProjectWindow.DataContext is NewProjectViewModel resultVm && resultVm.DialogResult == true)
            {
                task.Title = resultVm.Name;
                task.Description = resultVm.Description;
                CategorizeTasks();
            }

            IsEditTask = false;
        }

        [RelayCommand]
        private void OnDeleteTask(TaskModel task)
        {
            CurrentProject.Tasks.Remove(task);
            CategorizeTasks();
        }
    }
}
