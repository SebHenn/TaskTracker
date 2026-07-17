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

        public ColumnLaneViewModel(BoardColumn column, IEnumerable<TaskModel> tasks)
        {
            Column = column;
            Tasks = new ObservableCollection<TaskModel>(tasks);
        }
    }
}
