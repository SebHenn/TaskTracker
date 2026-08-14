using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace TaskTracker.Core.Models
{
    public partial class TaskModel : ObservableObject
    {
        [ObservableProperty]
        private string _title = "";

        [ObservableProperty]
        private string _description = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsOverdue))]
        private bool _isDone = false;

        [ObservableProperty]
        private Guid _id = Guid.NewGuid();

        /// <summary>Board column this task sits in; normalized on load. Move via ProjectModel.MoveTaskToColumn.</summary>
        [ObservableProperty]
        private Guid? _columnId;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsOverdue))]
        private DateTime? _dueDate;

        [ObservableProperty]
        private TaskPriority _priority = TaskPriority.Medium;

        [ObservableProperty]
        private ObservableCollection<string> _labels = new();

        [ObservableProperty]
        private DateTime? _createdAtUtc;

        [ObservableProperty]
        private DateTime? _completedAtUtc;

        /// <summary>
        /// Last time IsDone flipped, in either direction — an audit timestamp, unlike
        /// <see cref="CompletedAtUtc"/> which is cleared on reopening.
        ///
        /// It used to claim GitHub sync read it for last-write-wins conflicts. It never
        /// did: sync resolves state with a three-way merge against
        /// <see cref="LastSyncedIssueState"/>, which is strictly better than comparing
        /// clocks across two machines. Kept because it is real data already in save files.
        /// </summary>
        [ObservableProperty]
        private DateTime? _stateChangedUtc;

        /// <summary>Number of the linked GitHub issue, if this task is synced.</summary>
        [ObservableProperty]
        private int? _gitHubIssueNumber;

        /// <summary>Issue state ("open"/"closed") observed at the last sync; the 3-way merge base.</summary>
        [ObservableProperty]
        private string? _lastSyncedIssueState;

        /// <summary>
        /// Set when a linked issue disappeared from the repository (deleted or transferred)
        /// and sync unlinked the task. It keeps the export pass from filing the very issue
        /// that was just deleted, over and over, on every following sync. Pushing the task
        /// by hand clears it.
        /// </summary>
        [ObservableProperty]
        private bool _gitHubIssueVanished;

        [ObservableProperty]
        private ObservableCollection<SubTaskModel> _subTasks = new();

        /// <summary>Explicit position within the column; null = auto-sorted (priority/due).</summary>
        [ObservableProperty]
        private double? _sortOrder;

        /// <summary>"none", "daily", "weekly", or "monthly". Completing a recurring task spawns the next occurrence.</summary>
        [ObservableProperty]
        private string _recurrence = RecurrenceRules.None;

        [ObservableProperty]
        private int _recurrenceInterval = 1;

        /// <summary>Accumulated tracked time in seconds (excluding a currently running timer).</summary>
        [ObservableProperty]
        private double _trackedSeconds;

        /// <summary>Set while a work timer is running on this task.</summary>
        [ObservableProperty]
        private DateTime? _timerStartedAtUtc;

        /// <summary>Timestamped notes; see ActivityEntry.</summary>
        [ObservableProperty]
        private ObservableCollection<ActivityEntry> _activity = new();

        /// <summary>Issue title observed at the last sync; 3-way base for pushing local renames.</summary>
        [ObservableProperty]
        private string? _lastSyncedTitle;

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsRecurring => Recurrence != RecurrenceRules.None;

        /// <summary>Checklist progress like "2/5"; empty when there are no subtasks.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string SubTaskProgress => SubTasks.Count == 0 ? "" : $"{SubTasks.Count(s => s.IsDone)}/{SubTasks.Count}";

        public TaskModel()
        {
            // Stamped here so no creation path can forget it — ReviewReport counts tasks
            // created in the last week and silently misses any with a null. Tasks loaded
            // from a file that predates the field get this cleared again on load; see
            // ProjectStore.ClearBackfilledCreatedAt.
            _createdAtUtc = DateTime.UtcNow;
            HookSubTasks(_subTasks);
        }

        partial void OnSubTasksChanged(ObservableCollection<SubTaskModel> value) => HookSubTasks(value);

        private void HookSubTasks(ObservableCollection<SubTaskModel> subTasks)
        {
            subTasks.CollectionChanged += (_, e) =>
            {
                foreach (var item in e.NewItems?.OfType<SubTaskModel>() ?? Enumerable.Empty<SubTaskModel>())
                    HookSubTask(item);
                OnPropertyChanged(nameof(SubTaskProgress));
            };
            foreach (var item in subTasks)
                HookSubTask(item);
        }

        private void HookSubTask(SubTaskModel subTask)
        {
            subTask.PropertyChanged -= OnSubTaskPropertyChanged;
            subTask.PropertyChanged += OnSubTaskPropertyChanged;
        }

        private void OnSubTaskPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SubTaskModel.IsDone))
                OnPropertyChanged(nameof(SubTaskProgress));
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsOverdue => !IsDone && DueDate.HasValue && DueDate.Value.Date < DateTime.Today;

        partial void OnIsDoneChanged(bool value)
        {
            // Central timestamping so UI, GitHub sync, and the MCP server all
            // record completion/state-change times without duplicating logic.
            StateChangedUtc = DateTime.UtcNow;
            CompletedAtUtc = value ? DateTime.UtcNow : null;
        }
    }
}
