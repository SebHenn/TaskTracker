using System.Collections.ObjectModel;
using TaskTracker.Core.Models;

namespace TaskTracker.ViewModels.Pages
{
    /// <summary>One board lane: a column plus the (filtered, sorted) tasks in it.</summary>
    public class ColumnLaneViewModel
    {
        public BoardColumn Column { get; }
        public ObservableCollection<TaskModel> Tasks { get; }
        public int Count => Tasks.Count;

        /// <summary>Header count like "3" or "3/2" when a WIP limit is set.</summary>
        public string CountText => Column.WipLimit.HasValue ? $"{Tasks.Count}/{Column.WipLimit}" : Tasks.Count.ToString();

        public bool IsOverWip => Column.WipLimit.HasValue && Tasks.Count > Column.WipLimit.Value;

        public ColumnLaneViewModel(BoardColumn column, IEnumerable<TaskModel> tasks)
        {
            Column = column;
            Tasks = new ObservableCollection<TaskModel>(tasks);
        }
    }
}
