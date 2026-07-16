using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;

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
