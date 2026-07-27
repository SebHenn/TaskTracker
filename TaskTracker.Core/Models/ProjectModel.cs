using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace TaskTracker.Core.Models
{
    public partial class ProjectModel : ObservableObject
    {
        [ObservableProperty]
        private string _name = "";

        [ObservableProperty]
        private string _description = "";

        [ObservableProperty]
        private ObservableCollection<TaskModel> _tasks = new ObservableCollection<TaskModel>();

        /// <summary>Board columns in display order. Normalized on load: never empty, always at least one done column.</summary>
        [ObservableProperty]
        private ObservableCollection<BoardColumn> _columns = new ObservableCollection<BoardColumn>();

        /// <summary>
        /// Deleted tasks, newest first, pending retention. Go through
        /// <see cref="Services.Trash"/> rather than mutating this directly — it owns
        /// ordering, the retention clock and the size cap.
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<TrashedTask> _trash = new ObservableCollection<TrashedTask>();

        [ObservableProperty]
        [property: JsonIgnore]
        private bool _isSelected = false;

        [ObservableProperty]
        private bool _isFavourite = false;

        [ObservableProperty]
        private Guid _id = Guid.NewGuid();

        /// <summary>Owner of the linked GitHub repository, or null when not linked.</summary>
        [ObservableProperty]
        private string? _gitHubOwner;

        /// <summary>Name of the linked GitHub repository, or null when not linked.</summary>
        [ObservableProperty]
        private string? _gitHubRepo;

        [ObservableProperty]
        private DateTime? _lastSyncedAtUtc;

        [ObservableProperty]
        private bool _isArchived = false;

        [ObservableProperty]
        private DateTime? _archivedAtUtc;

        /// <summary>Accent color hex (from the preset palette), or null for none.</summary>
        [ObservableProperty]
        private string? _color;

        [JsonIgnore]
        public bool IsGitHubLinked => !string.IsNullOrWhiteSpace(GitHubOwner) && !string.IsNullOrWhiteSpace(GitHubRepo);

        [JsonIgnore]
        public BoardColumn? FirstColumn => Columns.FirstOrDefault(c => !c.IsDoneColumn) ?? Columns.FirstOrDefault();

        [JsonIgnore]
        public BoardColumn? FirstDoneColumn => Columns.FirstOrDefault(c => c.IsDoneColumn);

        /// <summary>
        /// The single place task placement happens: sets the column and derives
        /// IsDone from it, so timestamps/sync/stats stay coherent. Completing a
        /// recurring task spawns its next occurrence.
        /// </summary>
        public void MoveTaskToColumn(TaskModel task, BoardColumn column)
        {
            var wasDone = task.IsDone;
            task.ColumnId = column.Id;
            task.IsDone = column.IsDoneColumn;
            if (!wasDone && task.IsDone)
                Services.Recurrence.SpawnNextIfRecurring(this, task);
        }

        /// <summary>Resolves a task's column, falling back by done-state for unassigned/stale ids.</summary>
        public BoardColumn? ColumnOf(TaskModel task)
        {
            var column = task.ColumnId.HasValue ? Columns.FirstOrDefault(c => c.Id == task.ColumnId.Value) : null;
            return column ?? (task.IsDone ? FirstDoneColumn : FirstColumn);
        }
    }
}
