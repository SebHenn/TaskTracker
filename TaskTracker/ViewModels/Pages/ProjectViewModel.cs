using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using TaskTracker.Core.GitHub;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;
using TaskTracker.Core.Storage;
using TaskTracker.Messages;
using TaskTracker.Services;
using TaskTracker.ViewModels.Windows;
using TaskTracker.Views.Windows;

namespace TaskTracker.ViewModels.Pages
{
    public record WeekBarItem(string Tooltip, int Count, double Height);

    public partial class ProjectViewModel : ObservableObject
    {
        private const double MaxBarHeight = 48;

        private IProjectsService _projectsService;
        private INavigationService _navigationService;
        private IServiceProvider _serviceProvider;
        private MainViewModel _mainViewModel;
        private ILanguageService _languageService;
        private ISettingsService _settingsService;
        private readonly GitHubSyncService _gitHubSyncService = new();

        private string AllLabelsFilter => _languageService.GetString("AllLabels");

        [ObservableProperty]
        private string _favImage = "/Assets/starEmpty-32.png";

        [ObservableProperty]
        private ObservableCollection<ColumnLaneViewModel> _lanes = [];

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
        private string _selectedLabelFilter = "";

        [ObservableProperty]
        private bool _hasLabels = false;

        [ObservableProperty]
        private ProjectStatsResult? _stats;

        [ObservableProperty]
        private ObservableCollection<WeekBarItem> _weekBars = [];

        [ObservableProperty]
        private string _archiveButtonText = "Archive";

        [ObservableProperty]
        private bool _isSyncing = false;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDetailOpen))]
        private TaskModel? _selectedTask;

        public bool IsDetailOpen => SelectedTask != null;

        /// <summary>Comma-separated labels of the selected task; parsed back on set.</summary>
        public string SelectedTaskLabelsText
        {
            get => SelectedTask == null ? "" : string.Join(", ", SelectedTask.Labels);
            set
            {
                if (SelectedTask == null) return;
                SelectedTask.Labels.Clear();
                foreach (var label in LabelParser.Parse(value))
                    SelectedTask.Labels.Add(label);
                OnPropertyChanged();
            }
        }

        [ObservableProperty]
        private string _newSubTaskText = "";

        public IReadOnlyList<TaskPriority> Priorities { get; } = new[] { TaskPriority.Low, TaskPriority.Medium, TaskPriority.High };

        [ObservableProperty]
        private bool _isGitHubLinked = false;

        [ObservableProperty]
        private string _lastSyncedText = "";

        public ProjectViewModel(MainViewModel mainViewModel, IProjectsService projectsService, INavigationService navigationService, IServiceProvider serviceProvider, ILanguageService languageService, ISettingsService settingsService)
        {
            _mainViewModel = mainViewModel;
            _projectsService = projectsService;
            _navigationService = navigationService;
            _serviceProvider = serviceProvider;
            _languageService = languageService;
            _settingsService = settingsService;
            _selectedLabelFilter = AllLabelsFilter;
            languageService.LanguageChanged += RefreshFromProject;

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
            ArchiveButtonText = _languageService.GetString(CurrentProject?.IsArchived == true ? "Unarchive" : "Archive");
            RefreshGitHubState();
        }

        private void RefreshGitHubState()
        {
            IsGitHubLinked = CurrentProject?.IsGitHubLinked == true;
            LastSyncedText = !IsGitHubLinked ? ""
                : CurrentProject!.LastSyncedAtUtc == null
                    ? _languageService.GetString("NeverSynced")
                    : $"{_languageService.GetString("LastSynced")}: {CurrentProject.LastSyncedAtUtc.Value.ToLocalTime():g}";
        }

        [RelayCommand]
        private void OnLinkGitHub()
        {
            if (CurrentProject == null) return;

            var window = _serviceProvider.GetRequiredService<LinkGitHubWindow>();
            if (window.DataContext is LinkGitHubViewModel vm)
            {
                vm.Owner = CurrentProject.GitHubOwner ?? "";
                vm.Repo = CurrentProject.GitHubRepo ?? "";
            }

            window.ShowDialog();

            if (window.DataContext is LinkGitHubViewModel resultVm && resultVm.DialogResult)
            {
                var owner = resultVm.Owner.Trim();
                var repo = resultVm.Repo.Trim();
                CurrentProject.GitHubOwner = string.IsNullOrWhiteSpace(owner) ? null : owner;
                CurrentProject.GitHubRepo = string.IsNullOrWhiteSpace(repo) ? null : repo;
                if (!CurrentProject.IsGitHubLinked)
                    CurrentProject.LastSyncedAtUtc = null;
                RefreshGitHubState();
            }
        }

        [RelayCommand]
        private async Task OnSyncGitHub()
        {
            if (CurrentProject == null || !CurrentProject.IsGitHubLinked || IsSyncing)
                return;

            var settings = _settingsService.Settings;
            var token = TokenProtector.Unprotect(settings.GitHubTokenProtected, settings.GitHubTokenIsPlaintext);
            if (string.IsNullOrEmpty(token))
            {
                MessageBox.Show(_languageService.GetString("NoTokenConfigured"));
                return;
            }

            IsSyncing = true;
            try
            {
                // Await on the UI thread: model mutations happen in dispatcher
                // continuations, HTTP calls run off-thread in between.
                await _gitHubSyncService.SyncAsync(CurrentProject, new GitHubApi(token));
                CategorizeTasks();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{_languageService.GetString("SyncFailed")}: {ex.Message}");
            }
            finally
            {
                IsSyncing = false;
                RefreshGitHubState();
            }
        }

        partial void OnSelectedLabelFilterChanged(string value) => CategorizeTasks();

        private void CategorizeTasks()
        {
            if (CurrentProject == null)
            {
                Lanes = [];
                AvailableLabels = [];
                HasLabels = false;
                Stats = null;
                WeekBars = [];
                return;
            }

            ProjectStore.NormalizeColumns(CurrentProject);

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

            Lanes = new ObservableCollection<ColumnLaneViewModel>(
                CurrentProject.Columns.Select(column =>
                    new ColumnLaneViewModel(column, sorted.Where(t => CurrentProject.ColumnOf(t)?.Id == column.Id))));

            RefreshStats();
        }

        [RelayCommand]
        public void OnOpenTaskDetail(TaskModel task)
        {
            SelectedTask = task;
            NewSubTaskText = "";
            OnPropertyChanged(nameof(SelectedTaskLabelsText));
        }

        [RelayCommand]
        private void OnCloseTaskDetail()
        {
            SelectedTask = null;
            // Title/priority/due/label edits can affect sorting and filters.
            CategorizeTasks();
        }

        [RelayCommand]
        private void OnAddSubTask()
        {
            if (SelectedTask == null || string.IsNullOrWhiteSpace(NewSubTaskText))
                return;
            SelectedTask.SubTasks.Add(new SubTaskModel { Title = NewSubTaskText.Trim() });
            NewSubTaskText = "";
        }

        [RelayCommand]
        private void OnDeleteSubTask(SubTaskModel subTask)
        {
            SelectedTask?.SubTasks.Remove(subTask);
        }

        /// <summary>Called from the view's drop handler.</summary>
        public void MoveTask(Guid taskId, ColumnLaneViewModel targetLane)
        {
            if (CurrentProject == null) return;
            var task = CurrentProject.Tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null || task.ColumnId == targetLane.Column.Id) return;
            CurrentProject.MoveTaskToColumn(task, targetLane.Column);
            CategorizeTasks();
        }

        [RelayCommand]
        private void OnEditColumns()
        {
            if (CurrentProject == null) return;
            var window = _serviceProvider.GetRequiredService<ColumnsWindow>();
            if (window.DataContext is ColumnsViewModel vm)
                vm.SetProject(CurrentProject);
            window.ShowDialog();
            ProjectStore.NormalizeColumns(CurrentProject);
            CategorizeTasks();
        }

        private void RefreshStats()
        {
            if (CurrentProject == null)
                return;
            var stats = ProjectStats.Compute(CurrentProject);
            Stats = stats;
            var max = Math.Max(1, stats.DonePerWeek.Max(w => w.Count));
            var weekOf = _languageService.GetString("WeekOf");
            WeekBars = new ObservableCollection<WeekBarItem>(stats.DonePerWeek.Select(w =>
                new WeekBarItem($"{weekOf} {w.WeekStart:d}", w.Count, w.Count == 0 ? 2 : MaxBarHeight * w.Count / max)));
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
                ArchiveButtonText = _languageService.GetString("Archive");
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
            var confirmed = MessageBox.Show(
                string.Format(_languageService.GetString("DeleteProjectConfirm"), CurrentProject.Name),
                "TaskTracker", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmed != MessageBoxResult.Yes)
                return;
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
            if (CurrentProject?.FirstDoneColumn is { } column)
                CurrentProject.MoveTaskToColumn(task, column);
            CategorizeTasks();
        }

        [RelayCommand]
        private void OnMarkAsInProgress(TaskModel task)
        {
            if (CurrentProject?.FirstColumn is { } column)
                CurrentProject.MoveTaskToColumn(task, column);
            CategorizeTasks();
        }

        [RelayCommand]
        private void OnEditTask(TaskModel task)
        {
            // Editing happens in the detail panel now.
            OnOpenTaskDetail(task);
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsUndoVisible))]
        private TaskModel? _lastDeletedTask;

        private ProjectModel? _lastDeletedFrom;
        private System.Windows.Threading.DispatcherTimer? _undoTimer;

        public bool IsUndoVisible => LastDeletedTask != null;

        [ObservableProperty]
        private string _undoText = "";

        [RelayCommand]
        private void OnDeleteTask(TaskModel task)
        {
            if (CurrentProject == null) return;
            if (SelectedTask == task)
                SelectedTask = null;
            CurrentProject.Tasks.Remove(task);

            // Offer undo for a few seconds instead of a confirmation dialog.
            LastDeletedTask = task;
            _lastDeletedFrom = CurrentProject;
            UndoText = string.Format(_languageService.GetString("TaskDeletedUndo"), task.Title);
            _undoTimer ??= CreateUndoTimer();
            _undoTimer.Stop();
            _undoTimer.Start();

            CategorizeTasks();
        }

        private System.Windows.Threading.DispatcherTimer CreateUndoTimer()
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            timer.Tick += (_, _) => { timer.Stop(); LastDeletedTask = null; _lastDeletedFrom = null; };
            return timer;
        }

        [RelayCommand]
        private void OnUndoDelete()
        {
            _undoTimer?.Stop();
            if (LastDeletedTask != null && _lastDeletedFrom != null)
            {
                _lastDeletedFrom.Tasks.Add(LastDeletedTask);
                if (_lastDeletedFrom == CurrentProject)
                    CategorizeTasks();
            }
            LastDeletedTask = null;
            _lastDeletedFrom = null;
        }
    }
}
