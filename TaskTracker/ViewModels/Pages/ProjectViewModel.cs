using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;
using TaskTracker.Messages;
using TaskTracker.Services;
using TaskTracker.ViewModels.Windows;
using TaskTracker.Views.Windows;

namespace TaskTracker.ViewModels.Pages
{
    public record WeekBarItem(string Tooltip, int Count, double Height);

    public partial class ProjectViewModel : ObservableObject
    {
        public const string AllLabelsFilter = "All";
        private const double MaxBarHeight = 48;

        private IProjectsService _projectsService;
        private INavigationService _navigationService;
        private IServiceProvider _serviceProvider;
        private MainViewModel _mainViewModel;

        [ObservableProperty]
        private string _favImage = "/Assets/starEmpty-32.png";

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
        private ProjectModel? _currentProject;

        [ObservableProperty]
        private ObservableCollection<string> _availableLabels = [];

        [ObservableProperty]
        private string _selectedLabelFilter = AllLabelsFilter;

        [ObservableProperty]
        private bool _hasLabels = false;

        [ObservableProperty]
        private ProjectStatsResult? _stats;

        [ObservableProperty]
        private ObservableCollection<WeekBarItem> _weekBars = [];

        [ObservableProperty]
        private string _archiveButtonText = "Archive";

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
                    RefreshFromProject();
                }
            };
            WeakReferenceMessenger.Default.Register<StoreReloadedMessage>(
                this, (r, m) =>
                {
                    // MainViewModel re-resolves SelectedProject first (registration order),
                    // but re-read it here in case the PropertyChanged value was identical.
                    var vm = (ProjectViewModel)r;
                    vm.CurrentProject = vm._mainViewModel.SelectedProject;
                    vm.RefreshFromProject();
                });
            RefreshFromProject();
        }

        private void RefreshFromProject()
        {
            SelectedLabelFilter = AllLabelsFilter;
            CategorizeTasks();
            SetImage();
            ArchiveButtonText = CurrentProject?.IsArchived == true ? "Unarchive" : "Archive";
        }

        partial void OnSelectedLabelFilterChanged(string value) => CategorizeTasks();

        private void CategorizeTasks()
        {
            if (CurrentProject == null)
            {
                NotDoneTasks = [];
                DoneTasks = [];
                AvailableLabels = [];
                HasLabels = false;
                Stats = null;
                WeekBars = [];
                return;
            }

            var labels = CurrentProject.Tasks
                .SelectMany(t => t.Labels)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
                .ToList();
            HasLabels = labels.Count > 0;
            AvailableLabels = new ObservableCollection<string>(labels.Prepend(AllLabelsFilter));
            if (!AvailableLabels.Contains(SelectedLabelFilter))
            {
                SelectedLabelFilter = AllLabelsFilter; // triggers one clean re-categorize
                return;
            }

            var filtered = CurrentProject.Tasks.AsEnumerable();
            if (SelectedLabelFilter != AllLabelsFilter)
                filtered = filtered.Where(t => t.Labels.Contains(SelectedLabelFilter, StringComparer.OrdinalIgnoreCase));

            var sorted = filtered
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
                .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            NotDoneTasks = new ObservableCollection<TaskModel>(sorted.Where(task => !task.IsDone));
            DoneTasks = new ObservableCollection<TaskModel>(sorted.Where(task => task.IsDone));

            RefreshStats();
        }

        private void RefreshStats()
        {
            if (CurrentProject == null)
                return;
            var stats = ProjectStats.Compute(CurrentProject);
            Stats = stats;
            var max = Math.Max(1, stats.DonePerWeek.Max(w => w.Count));
            WeekBars = new ObservableCollection<WeekBarItem>(stats.DonePerWeek.Select(w =>
                new WeekBarItem($"Week of {w.WeekStart:d}", w.Count, w.Count == 0 ? 2 : MaxBarHeight * w.Count / max)));
        }

        [RelayCommand]
        public void OnFavProject()
        {
            if (CurrentProject == null) return;
            CurrentProject.IsFavourite = !CurrentProject.IsFavourite;
            SetImage();
            _mainViewModel.ResortProjects();
        }

        private void SetImage()
        {
            if (CurrentProject != null && CurrentProject.IsFavourite)
                FavImage = "/Assets/starFull-32.png";
            else
                FavImage = "/Assets/starEmpty-32.png";
        }

        [RelayCommand]
        private void OnArchiveProject()
        {
            if (CurrentProject == null) return;
            if (CurrentProject.IsArchived)
            {
                CurrentProject.IsArchived = false;
                CurrentProject.ArchivedAtUtc = null;
                ArchiveButtonText = "Archive";
                _mainViewModel.ResortProjects();
            }
            else
            {
                CurrentProject.IsArchived = true;
                CurrentProject.ArchivedAtUtc = DateTime.UtcNow;
                _mainViewModel.IsHomeSelected = true;
                _mainViewModel.ResortProjects();
                _navigationService.NavigateTo<HomeViewModel>();
            }
        }

        [RelayCommand]
        private void OnChangeProject()
        {
            if (CurrentProject == null) return;
            IsEditing = true;

            var newProjectWindow = _serviceProvider.GetRequiredService<NewProjectWindow>();

            ((NewProjectViewModel)newProjectWindow.DataContext).Name = CurrentProject.Name;
            ((NewProjectViewModel)newProjectWindow.DataContext).Description = CurrentProject.Description;

            newProjectWindow.ShowDialog();

            if (newProjectWindow.DataContext is NewProjectViewModel vm && vm.DialogResult == true)
            {
                _projectsService.ChangeProjectName(CurrentProject, vm.Name);
                _projectsService.ChangeProjectDescription(CurrentProject, vm.Description);
            }

            IsEditing = false;
        }

        [RelayCommand]
        private void OnDeleteProject()
        {
            if (CurrentProject == null) return;
            _projectsService.RemoveProject(CurrentProject);
            _mainViewModel.IsHomeSelected = true;
            _mainViewModel.ResortProjects();
            _navigationService.NavigateTo<HomeViewModel>();
        }

        [RelayCommand]
        private void OnNewTaskClick()
        {
            if (CurrentProject == null) return;
            IsCreateTask = true;

            var newProjectWindow = _serviceProvider.GetRequiredService<NewProjectWindow>();

            newProjectWindow.ShowDialog();

            if (newProjectWindow.DataContext is NewProjectViewModel vm && vm.DialogResult == true)
            {
                var task = new TaskModel
                {
                    Title = vm.Name,
                    Description = vm.Description,
                    IsDone = false,
                    DueDate = vm.DueDate,
                    Priority = vm.SelectedPriority,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                foreach (var label in vm.ParseLabels())
                    task.Labels.Add(label);
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
                vm.DueDate = task.DueDate;
                vm.SelectedPriority = task.Priority;
                vm.LabelsText = string.Join(", ", task.Labels);
            }

            newProjectWindow.ShowDialog();

            if (newProjectWindow.DataContext is NewProjectViewModel resultVm && resultVm.DialogResult == true)
            {
                task.Title = resultVm.Name;
                task.Description = resultVm.Description;
                task.DueDate = resultVm.DueDate;
                task.Priority = resultVm.SelectedPriority;
                task.Labels.Clear();
                foreach (var label in resultVm.ParseLabels())
                    task.Labels.Add(label);
                CategorizeTasks();
            }

            IsEditTask = false;
        }

        [RelayCommand]
        private void OnDeleteTask(TaskModel task)
        {
            if (CurrentProject == null) return;
            CurrentProject.Tasks.Remove(task);
            CategorizeTasks();
        }
    }
}
