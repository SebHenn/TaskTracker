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

    /// <summary>A due-date filter choice with its localized label.</summary>
    public record DueFilterOption(DueFilter Value, string Text);

    /// <summary>
    /// A priority filter choice; a null <see cref="Value"/> is the "any priority" entry.
    /// </summary>
    public record PriorityOption(TaskPriority? Value, string Text);

    public partial class ProjectViewModel : ObservableObject
    {
        private const double MaxBarHeight = 48;

        private IProjectsService _projectsService;
        private INavigationService _navigationService;
        private IServiceProvider _serviceProvider;
        private MainViewModel _mainViewModel;
        private ILanguageService _languageService;
        private ISettingsService _settingsService;
        private readonly GitHubSyncService _gitHubSyncService;
        private readonly IGitHubApiFactory _gitHubApiFactory;
        private readonly IDialogService _dialogService;

        private string AllLabelsFilter => _languageService.GetString("AllLabels");

        [ObservableProperty]
        private ObservableCollection<ColumnLaneViewModel> _lanes = [];

        /// <summary>
        /// False only when the project has no tasks at all — which is a different thing
        /// from a column being empty, and wants different wording.
        /// </summary>
        [ObservableProperty]
        private bool _hasAnyTasks;

        /// <summary>
        /// Compact cards: same information, less height each. A view preference, so it
        /// is toggled on the board and remembered in settings.
        /// </summary>
        [ObservableProperty]
        private bool _isCompact;

        [RelayCommand]
        private void OnToggleDensity()
        {
            IsCompact = !IsCompact;
            _settingsService.Settings.CompactCards = IsCompact;
            _settingsService.Save();
        }

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
        private ObservableCollection<DueFilterOption> _dueFilters = [];

        [ObservableProperty]
        private ObservableCollection<PriorityOption> _priorityFilters = [];

        [ObservableProperty]
        private DueFilterOption? _selectedDueFilter;

        [ObservableProperty]
        private PriorityOption? _selectedPriorityFilter;

        /// <summary>
        /// True when the board is showing a subset. Drives the clear affordance and,
        /// with <see cref="HasAnyTasks"/>, tells "this project is empty" apart from
        /// "your filters match nothing" — two situations that want different wording.
        /// </summary>
        [ObservableProperty]
        private bool _isFilterActive;

        /// <summary>False when the filters leave every lane empty on a non-empty project.</summary>
        [ObservableProperty]
        private bool _hasVisibleTasks = true;

        /// <summary>
        /// Set while more than one filter is being assigned at once. Every filter setter
        /// re-projects the whole board, so without this a reset would build it three
        /// times and throw the first two away.
        /// </summary>
        private bool _isSettingFilters;

        /// <summary>
        /// Rebuilds the filter dropdowns. The entries carry localized text, so they are
        /// regenerated on a language change rather than re-resolved by a binding.
        /// </summary>
        private void BuildFilterOptions()
        {
            var previousDue = SelectedDueFilter?.Value ?? DueFilter.Any;
            var previousPriority = SelectedPriorityFilter?.Value;

            DueFilters =
            [
                new(DueFilter.Any, _languageService.GetString("AnyDueDate")),
                new(DueFilter.Overdue, _languageService.GetString("OverdueSection")),
                new(DueFilter.DueToday, _languageService.GetString("DueTodaySection")),
                new(DueFilter.DueThisWeek, _languageService.GetString("DueThisWeekSection")),
                new(DueFilter.NoDueDate, _languageService.GetString("NoDueDate")),
            ];

            // Priority values show their enum names, the same as the detail drawer's
            // picker — localizing them here only would make the two disagree.
            PriorityFilters = [new(null, _languageService.GetString("AnyPriority"))];
            foreach (var priority in Priorities)
                PriorityFilters.Add(new(priority, priority.ToString()));

            // Reselected by value, because the option objects are new instances and the
            // old selection would no longer match anything in the list — leaving the
            // box blank while the board stayed filtered.
            SetFilters(() =>
            {
                SelectedDueFilter = DueFilters.First(option => option.Value == previousDue);
                SelectedPriorityFilter = PriorityFilters.First(option => option.Value == previousPriority);
            });
        }

        private void SetFilters(Action assign)
        {
            _isSettingFilters = true;
            try
            {
                assign();
            }
            finally
            {
                _isSettingFilters = false;
            }
        }

        partial void OnSelectedDueFilterChanged(DueFilterOption? value) => ReprojectUnlessBatched();

        partial void OnSelectedPriorityFilterChanged(PriorityOption? value) => ReprojectUnlessBatched();

        private void ReprojectUnlessBatched()
        {
            if (!_isSettingFilters)
                CategorizeTasks();
        }

        /// <summary>Puts all three filters back to "everything", re-projecting once.</summary>
        private void ResetFilterSelections() => SetFilters(() =>
        {
            SelectedLabelFilter = AllLabelsFilter;
            SelectedDueFilter = DueFilters.First(option => option.Value == DueFilter.Any);
            SelectedPriorityFilter = PriorityFilters.First(option => option.Value == null);
        });

        [RelayCommand]
        private void OnClearFilters()
        {
            ResetFilterSelections();
            CategorizeTasks();
        }

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

        [ObservableProperty]
        private string _newActivityText = "";

        [ObservableProperty]
        private string _trackedTimeText = "";

        [ObservableProperty]
        private bool _isTimerRunning;

        private System.Windows.Threading.DispatcherTimer? _trackedTimeRefresh;

        [RelayCommand]
        private void OnToggleTimer()
        {
            if (SelectedTask == null) return;
            if (SelectedTask.TimerStartedAtUtc != null)
            {
                TimeTracking.Stop(SelectedTask);
            }
            else
            {
                // Single active timer across the whole store; the rule lives in Core so
                // the MCP server's start_timer enforces it too.
                TimeTracking.StartExclusive(_projectsService.projectModels, SelectedTask);
            }
            RefreshTimerState();
        }

        private void RefreshTimerState()
        {
            IsTimerRunning = SelectedTask?.TimerStartedAtUtc != null;
            TrackedTimeText = SelectedTask == null ? "" : TimeTracking.Format(TimeTracking.TotalSeconds(SelectedTask));

            if (IsTimerRunning && _trackedTimeRefresh == null)
            {
                _trackedTimeRefresh = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _trackedTimeRefresh.Tick += (_, _) =>
                {
                    if (SelectedTask == null || SelectedTask.TimerStartedAtUtc == null)
                    {
                        _trackedTimeRefresh!.Stop();
                        _trackedTimeRefresh = null;
                        return;
                    }
                    TrackedTimeText = TimeTracking.Format(TimeTracking.TotalSeconds(SelectedTask));
                };
                _trackedTimeRefresh.Start();
            }
        }

        [RelayCommand]
        private void OnAddActivity()
        {
            if (SelectedTask == null || string.IsNullOrWhiteSpace(NewActivityText))
                return;
            SelectedTask.Activity.Insert(0, new ActivityEntry { Text = NewActivityText.Trim() });
            NewActivityText = "";
        }

        [RelayCommand]
        private void OnDeleteActivity(ActivityEntry entry)
        {
            SelectedTask?.Activity.Remove(entry);
        }

        public IReadOnlyList<TaskPriority> Priorities { get; } = new[] { TaskPriority.Low, TaskPriority.Medium, TaskPriority.High };

        public IReadOnlyList<string> RecurrenceOptions { get; } = RecurrenceRules.All;

        [ObservableProperty]
        private bool _isGitHubLinked = false;

        [ObservableProperty]
        private string _lastSyncedText = "";

        /// <summary>
        /// True while the sync line is reporting a failure rather than a timestamp, so
        /// the board can style it as an error instead of quietly showing stale text.
        /// </summary>
        [ObservableProperty]
        private bool _hasSyncError;

        /// <summary>
        /// Cancels the in-flight sync. Switching projects mid-sync used to let the old
        /// project's response land and re-categorize a board that is no longer shown.
        /// </summary>
        private System.Threading.CancellationTokenSource? _syncCts;

        // Cancel only — the sync's own finally block clears and disposes the source.
        // Nulling it here instead would make that guard fail and leave IsSyncing stuck on.
        public void CancelSync() => _syncCts?.Cancel();

        public ProjectViewModel(MainViewModel mainViewModel, IProjectsService projectsService, INavigationService navigationService, IServiceProvider serviceProvider, ILanguageService languageService, ISettingsService settingsService, GitHubSyncService gitHubSyncService, IGitHubApiFactory gitHubApiFactory, IDialogService dialogService)
        {
            _mainViewModel = mainViewModel;
            _projectsService = projectsService;
            _navigationService = navigationService;
            _serviceProvider = serviceProvider;
            _languageService = languageService;
            _settingsService = settingsService;
            _gitHubSyncService = gitHubSyncService;
            _gitHubApiFactory = gitHubApiFactory;
            _dialogService = dialogService;
            _selectedLabelFilter = AllLabelsFilter;
            _isCompact = settingsService.Settings.CompactCards;
            BuildFilterOptions();
            languageService.LanguageChanged += () =>
            {
                BuildFilterOptions();
                RefreshFromProject();
            };

            CurrentProject = mainViewModel.SelectedProject;
            mainViewModel.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(mainViewModel.SelectedProject))
                {
                    CancelSync();
                    CurrentProject = mainViewModel.SelectedProject;
                    RefreshFromProject();
                }
            };
            WeakReferenceMessenger.Default.Register<AutoSyncFailedMessage>(
                this, (r, m) =>
                {
                    // Only the affected board reacts; a failure on some other project's
                    // background sync is not this board's business.
                    var vm = (ProjectViewModel)r;
                    if (vm.CurrentProject?.Id != m.ProjectId)
                        return;
                    vm.HasSyncError = true;
                    vm.LastSyncedText = $"{vm._languageService.GetString("SyncFailed")}: {m.Reason}";
                });
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
            // Filters are per-visit, not per-project: carrying "overdue only" onto a
            // different board would make it look half empty for no visible reason.
            ResetFilterSelections();
            CategorizeTasks();
            ArchiveButtonText = _languageService.GetString(CurrentProject?.IsArchived == true ? "Unarchive" : "Archive");
            RefreshGitHubState();
        }

        private void RefreshGitHubState()
        {
            // Any successful refresh of this line clears the error styling with it.
            HasSyncError = false;
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
                _dialogService.Error(_languageService.GetString("NoTokenConfigured"));
                return;
            }

            var cts = new System.Threading.CancellationTokenSource();
            _syncCts = cts;

            IsSyncing = true;
            try
            {
                // Await on the UI thread: model mutations happen in dispatcher
                // continuations, HTTP calls run off-thread in between.
                var result = await _gitHubSyncService.SyncAsync(CurrentProject, _gitHubApiFactory.Create(token), cts.Token);
                CategorizeTasks();

                // Export failures do not throw — the sync keeps the issues it did file.
                // Without this the board would report a clean sync while local tasks
                // silently never reached the repository.
                if (result.ExportError is { } exportError)
                {
                    HasSyncError = true;
                    _dialogService.Error($"{_languageService.GetString("SyncFailed")}: {exportError}");
                }
            }
            catch (OperationCanceledException)
            {
                // The user moved on. Not a failure, and the board they left has already
                // been refreshed by the project switch.
                return;
            }
            catch (Exception ex)
            {
                HasSyncError = true;
                _dialogService.Error($"{_languageService.GetString("SyncFailed")}: {ex.Message}");
            }
            finally
            {
                // Guarded: a newer sync may already own the field, and clearing
                // IsSyncing or the status line on its behalf would be wrong.
                if (ReferenceEquals(_syncCts, cts))
                {
                    _syncCts = null;
                    IsSyncing = false;
                    RefreshGitHubState();
                }
                cts.Dispose();
            }
        }

        partial void OnSelectedLabelFilterChanged(string value) => ReprojectUnlessBatched();

        private void CategorizeTasks()
        {
            if (CurrentProject == null)
            {
                Lanes.Clear();
                AvailableLabels = [];
                HasLabels = false;
                HasAnyTasks = false;
                HasVisibleTasks = true;
                Stats = null;
                WeekBars = [];
                return;
            }

            // Counted before filtering: a filter that matches nothing is not the
            // same as a project with nothing in it.
            HasAnyTasks = CurrentProject.Tasks.Count > 0;

            // Enforce the column invariants (mutating) before projecting (pure).
            ProjectStore.NormalizeColumns(CurrentProject);

            // "All labels" is a localized display string; Core takes null for "no filter".
            var filter = new BoardFilter(
                SelectedLabelFilter == AllLabelsFilter ? null : SelectedLabelFilter,
                SelectedDueFilter?.Value ?? DueFilter.Any,
                SelectedPriorityFilter?.Value);
            IsFilterActive = filter.IsActive;
            var board = BoardProjection.Compute(CurrentProject, filter);

            HasLabels = board.Labels.Count > 0;
            AvailableLabels = new ObservableCollection<string>(board.Labels.Prepend(AllLabelsFilter));
            if (!AvailableLabels.Contains(SelectedLabelFilter))
            {
                SelectedLabelFilter = AllLabelsFilter; // triggers one clean re-categorize
                return;
            }

            SyncLanes(board.Lanes);
            HasVisibleTasks = board.Lanes.Any(lane => lane.Tasks.Count > 0);

            RefreshStats();
        }

        /// <summary>
        /// Reconciles the existing lane view models against a fresh projection,
        /// keyed by column, instead of replacing the collection.
        ///
        /// The old code rebuilt every lane on every edit, filter change, sync and
        /// drop, which tore down and recreated the entire board visual tree — and
        /// threw away each column's scroll position with it.
        /// </summary>
        private void SyncLanes(IReadOnlyList<BoardLane> projected)
        {
            // Retire lanes whose column no longer exists.
            var live = projected.Select(lane => lane.Column.Id).ToHashSet();
            for (var i = Lanes.Count - 1; i >= 0; i--)
            {
                if (!live.Contains(Lanes[i].Column.Id))
                    Lanes.RemoveAt(i);
            }

            for (var i = 0; i < projected.Count; i++)
            {
                var lane = projected[i];
                var existing = IndexOfLane(lane.Column.Id);
                if (existing < 0)
                {
                    Lanes.Insert(i, new ColumnLaneViewModel(lane.Column, lane.Tasks));
                    continue;
                }
                if (existing != i)
                    Lanes.Move(existing, i);
                Lanes[i].Sync(lane.Tasks);
            }
        }

        private int IndexOfLane(Guid columnId)
        {
            for (var i = 0; i < Lanes.Count; i++)
            {
                if (Lanes[i].Column.Id == columnId)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Cards selected together for a bulk action, in the order the lane shows them.
        ///
        /// Held on the board rather than per lane because only one lane can carry a
        /// multi-selection at a time — the view clears the others — so "3 selected" is
        /// never ambiguous about which three.
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasBulkSelection))]
        [NotifyPropertyChangedFor(nameof(SelectionCount))]
        private ObservableCollection<TaskModel> _selectedTasks = [];

        public int SelectionCount => SelectedTasks.Count;

        /// <summary>
        /// A single selected card is just the current card; the bulk bar appears from two,
        /// where clicking each one in turn starts to cost something.
        /// </summary>
        public bool HasBulkSelection => SelectedTasks.Count > 1;

        /// <summary>Set by the view when it clears lanes, so it can undo its own work.</summary>
        public event Action? SelectionCleared;

        [RelayCommand]
        private void OnClearSelection() => SelectionCleared?.Invoke();

        [RelayCommand]
        private void OnMoveSelectedToColumn(BoardColumn column)
        {
            if (CurrentProject == null || SelectedTasks.Count == 0)
                return;
            BulkOperations.MoveAll(CurrentProject, SelectedTasks, column);
            SelectionCleared?.Invoke();
            CategorizeTasks();
        }

        [RelayCommand]
        private void OnDeleteSelected()
        {
            if (CurrentProject == null || SelectedTasks.Count == 0)
                return;

            // Recoverable, like a single delete — so no prompt, and the undo bar offers
            // the whole batch back at once.
            var count = SelectedTasks.Count;
            LastDeleted = new ObservableCollection<TrashedTask>(
                BulkOperations.DeleteAll(CurrentProject, SelectedTasks));
            _lastDeletedFrom = CurrentProject;
            UndoText = string.Format(_languageService.GetString("TasksDeletedUndo"), count);
            StartUndoWindow();

            SelectionCleared?.Invoke();
            CategorizeTasks();
        }

        [RelayCommand]
        public void OnOpenTaskDetail(TaskModel task)
        {
            SelectedTask = task;
            NewSubTaskText = "";
            NewActivityText = "";
            OnPropertyChanged(nameof(SelectedTaskLabelsText));
            RefreshTimerState();
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

        /// <summary>
        /// Called from the view's drop handler. Inserts the task at the given
        /// visual position and renumbers the target lane's explicit order.
        /// </summary>
        public void MoveTask(Guid taskId, ColumnLaneViewModel targetLane, int insertIndex = int.MaxValue)
        {
            if (CurrentProject == null) return;
            var task = CurrentProject.Tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null) return;

            BoardDrop.PlaceInLane(targetLane.Tasks, task, insertIndex);
            CurrentProject.MoveTaskToColumn(task, targetLane.Column);
            CategorizeTasks();
        }

        /// <summary>
        /// Moves a task one column left or right — the keyboard equivalent of dragging
        /// it. Returns false at the ends of the board so the caller can leave focus
        /// alone rather than pretending something happened.
        /// </summary>
        public bool MoveTaskByColumn(TaskModel task, int direction)
        {
            if (CurrentProject == null)
                return false;

            // Column order in the model is lane order on screen, so the model is the
            // source of truth here rather than the view's lane collection.
            var columns = CurrentProject.Columns;
            var current = CurrentProject.ColumnOf(task);
            var target = (current == null ? -1 : columns.IndexOf(current)) + direction;
            if (target < 0 || target >= columns.Count)
                return false;

            var lane = Lanes.FirstOrDefault(l => l.Column.Id == columns[target].Id);
            if (lane == null)
                return false;

            // Routed through MoveTask so it lands via MoveTaskToColumn: IsDone is derived
            // from the target column and a recurring task spawns its next occurrence.
            // Assigning ColumnId directly would skip both.
            MoveTask(task.Id, lane, int.MaxValue);
            return true;
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
            // The view binds the star straight to IsFavourite, so there is no image
            // path to keep in step here any more.
            CurrentProject.IsFavourite = !CurrentProject.IsFavourite;
            _mainViewModel.ResortProjects();
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
            ((NewProjectViewModel)newProjectWindow.DataContext).SelectedColor = CurrentProject.Color ?? "";

            newProjectWindow.ShowDialog();

            if (newProjectWindow.DataContext is NewProjectViewModel vm && vm.DialogResult == true)
            {
                // All-or-nothing: a rejected name leaves the description and colour alone
                // too, so the dialog's contents and the project stay in agreement.
                if (_projectsService.ChangeProjectName(CurrentProject, vm.Name))
                {
                    _projectsService.ChangeProjectDescription(CurrentProject, vm.Description);
                    CurrentProject.Color = string.IsNullOrEmpty(vm.SelectedColor) ? null : vm.SelectedColor;
                }
                else
                {
                    _dialogService.Error(_languageService.GetString("InvalidProjectName"));
                }
            }

            IsEditing = false;
        }

        [RelayCommand]
        private void OnDeleteProject()
        {
            if (CurrentProject == null) return;
            if (!_dialogService.Confirm(
                    string.Format(_languageService.GetString("DeleteProjectConfirm"), CurrentProject.Name)))
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
            if (CurrentProject != null)
                TaskCompletion.SetDone(CurrentProject, task, true);
            CategorizeTasks();
        }

        [RelayCommand]
        private void OnMarkAsInProgress(TaskModel task)
        {
            if (CurrentProject != null)
                TaskCompletion.SetDone(CurrentProject, task, false);
            CategorizeTasks();
        }

        [RelayCommand]
        private void OnEditTask(TaskModel task)
        {
            // Editing happens in the detail panel now.
            OnOpenTaskDetail(task);
        }

        [RelayCommand]
        private async Task OnPushTaskToGitHub(TaskModel task)
        {
            if (CurrentProject == null || !CurrentProject.IsGitHubLinked || task.GitHubIssueNumber.HasValue)
                return;

            var settings = _settingsService.Settings;
            var token = TokenProtector.Unprotect(settings.GitHubTokenProtected, settings.GitHubTokenIsPlaintext);
            if (string.IsNullOrEmpty(token))
            {
                _dialogService.Error(_languageService.GetString("NoTokenConfigured"));
                return;
            }

            try
            {
                await _gitHubSyncService.PushTaskAsync(CurrentProject, task, _gitHubApiFactory.Create(token));
            }
            catch (Exception ex)
            {
                _dialogService.Error($"{_languageService.GetString("SyncFailed")}: {ex.Message}");
            }
        }

        /// <summary>
        /// The most recent deletion, for the undo bar — one task or a whole bulk batch.
        /// The bar is only the fast path back; the entries stay recoverable from the
        /// trash window long after it goes.
        ///
        /// Always assigned a fresh collection, never mutated in place, because that is
        /// what makes the dependent notifications fire.
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsUndoVisible))]
        private ObservableCollection<TrashedTask> _lastDeleted = [];

        private ProjectModel? _lastDeletedFrom;
        private System.Windows.Threading.DispatcherTimer? _undoTimer;

        public bool IsUndoVisible => LastDeleted.Count > 0;

        [ObservableProperty]
        private string _undoText = "";

        [RelayCommand]
        private void OnDeleteTask(TaskModel task)
        {
            if (CurrentProject == null) return;
            if (SelectedTask == task)
                SelectedTask = null;

            // Into the trash, not out of existence: the bar below is the fast way back,
            // and the trash window is the one that still works tomorrow.
            LastDeleted = [Trash.Delete(CurrentProject, task)];
            _lastDeletedFrom = CurrentProject;
            UndoText = string.Format(_languageService.GetString("TaskDeletedUndo"), task.Title);
            StartUndoWindow();

            CategorizeTasks();
        }

        private void StartUndoWindow()
        {
            _undoTimer ??= CreateUndoTimer();
            _undoTimer.Stop();
            _undoTimer.Start();
        }

        private System.Windows.Threading.DispatcherTimer CreateUndoTimer()
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            // Only the bar goes away; the entries stay in the trash.
            timer.Tick += (_, _) => { timer.Stop(); LastDeleted = []; _lastDeletedFrom = null; };
            return timer;
        }

        [RelayCommand]
        private void OnUndoDelete()
        {
            _undoTimer?.Stop();
            if (_lastDeletedFrom != null)
            {
                foreach (var entry in LastDeleted)
                    Trash.Restore(_lastDeletedFrom, entry);
                if (_lastDeletedFrom == CurrentProject)
                    CategorizeTasks();
            }
            LastDeleted = [];
            _lastDeletedFrom = null;
        }

        [RelayCommand]
        private void OnOpenTrash()
        {
            if (CurrentProject == null) return;
            var window = _serviceProvider.GetRequiredService<TrashWindow>();
            if (window.DataContext is TrashViewModel vm)
                vm.SetProject(CurrentProject);
            window.ShowDialog();

            // A restore from in there put tasks back on the board, and the undo bar may
            // now be pointing at entries that are no longer in the trash.
            LastDeleted = [];
            _lastDeletedFrom = null;
            CategorizeTasks();
        }
    }
}
