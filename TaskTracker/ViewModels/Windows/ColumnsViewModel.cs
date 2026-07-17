using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using TaskTracker.Core.Models;

namespace TaskTracker.ViewModels.Windows
{
    public partial class ColumnsViewModel : ObservableObject
    {
        private ProjectModel? _project;

        public ObservableCollection<BoardColumn> Columns { get; private set; } = new();

        public void SetProject(ProjectModel project)
        {
            _project = project;
            Columns = project.Columns;
            OnPropertyChanged(nameof(Columns));
        }

        [RelayCommand]
        private void OnAddColumn()
        {
            Columns.Add(new BoardColumn { Name = "New column" });
        }

        [RelayCommand]
        private void OnDeleteColumn(BoardColumn column)
        {
            if (_project == null || Columns.Count <= 1)
                return;
            // Keep at least one done column on the board.
            if (column.IsDoneColumn && Columns.Count(c => c.IsDoneColumn) == 1)
                return;

            Columns.Remove(column);

            // Rehome the deleted column's tasks.
            var fallback = _project.FirstColumn ?? Columns[0];
            foreach (var task in _project.Tasks.Where(t => t.ColumnId == column.Id).ToList())
                _project.MoveTaskToColumn(task, fallback);
        }

        [RelayCommand]
        private void OnMoveUp(BoardColumn column)
        {
            var index = Columns.IndexOf(column);
            if (index > 0)
                Columns.Move(index, index - 1);
        }

        [RelayCommand]
        private void OnMoveDown(BoardColumn column)
        {
            var index = Columns.IndexOf(column);
            if (index >= 0 && index < Columns.Count - 1)
                Columns.Move(index, index + 1);
        }
    }
}
