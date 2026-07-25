using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;

namespace TaskTracker.ViewModels.Pages
{
    /// <summary>One board lane: a column plus the (filtered, sorted) tasks in it.</summary>
    public class ColumnLaneViewModel : ObservableObject
    {
        public BoardColumn Column { get; }
        public ObservableCollection<TaskModel> Tasks { get; } = new();
        public int Count => Tasks.Count;

        /// <summary>Header count like "3" or "3/2" when a WIP limit is set.</summary>
        public string CountText => Column.WipLimit.HasValue ? $"{Tasks.Count}/{Column.WipLimit}" : Tasks.Count.ToString();

        public bool IsOverWip => Column.WipLimit.HasValue && Tasks.Count > Column.WipLimit.Value;

        private bool _isDropTarget;

        /// <summary>
        /// True while a dragged card is over this lane. Drag state rather than board
        /// state, so it is neither persisted nor change-tracked — this view model is
        /// rebuilt from the projection and never written to the store.
        /// </summary>
        public bool IsDropTarget
        {
            get => _isDropTarget;
            set => SetProperty(ref _isDropTarget, value);
        }

        public ColumnLaneViewModel(BoardColumn column, IEnumerable<TaskModel> tasks)
        {
            Column = column;
            Sync(tasks);
        }

        /// <summary>
        /// Brings <see cref="Tasks"/> in line with a fresh projection by editing the
        /// existing collection rather than replacing it, so the lane's ListBox keeps
        /// its scroll position and reuses containers instead of rebuilding.
        /// Unchanged positions raise no collection event at all.
        /// </summary>
        public void Sync(IEnumerable<TaskModel> tasks)
        {
            var target = tasks as IList<TaskModel> ?? tasks.ToList();

            while (Tasks.Count > target.Count)
                Tasks.RemoveAt(Tasks.Count - 1);

            for (var i = 0; i < target.Count; i++)
            {
                if (i >= Tasks.Count)
                    Tasks.Add(target[i]);
                else if (!ReferenceEquals(Tasks[i], target[i]))
                    Tasks[i] = target[i];
            }

            // Derived from Tasks.Count, so they need a nudge once the edits settle.
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(IsOverWip));
        }
    }
}
