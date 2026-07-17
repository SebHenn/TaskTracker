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

        /// <summary>Last time IsDone flipped; used by GitHub sync for last-write-wins conflicts.</summary>
        [ObservableProperty]
        private DateTime? _stateChangedUtc;

        /// <summary>Number of the linked GitHub issue, if this task is synced.</summary>
        [ObservableProperty]
        private int? _gitHubIssueNumber;

        /// <summary>Issue state ("open"/"closed") observed at the last sync; the 3-way merge base.</summary>
        [ObservableProperty]
        private string? _lastSyncedIssueState;

        [ObservableProperty]
        private ObservableCollection<SubTaskModel> _subTasks = new();

        /// <summary>Checklist progress like "2/5"; empty when there are no subtasks.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string SubTaskProgress => SubTasks.Count == 0 ? "" : $"{SubTasks.Count(s => s.IsDone)}/{SubTasks.Count}";

        public TaskModel()
        {
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
