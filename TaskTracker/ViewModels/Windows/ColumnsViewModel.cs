using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;

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

        // The rules live in Core.ColumnOperations so the MCP server enforces the same
        // ones; this dialog is just the Windows front end for them.

        [RelayCommand]
        private void OnAddColumn()
        {
            if (_project != null)
                ColumnOperations.Add(_project, "New column");
        }

        [RelayCommand]
        private void OnDeleteColumn(BoardColumn column)
        {
            if (_project != null)
                ColumnOperations.Delete(_project, column);
        }

        [RelayCommand]
        private void OnMoveUp(BoardColumn column)
        {
            if (_project != null)
                ColumnOperations.Move(_project, column, -1);
        }

        [RelayCommand]
        private void OnMoveDown(BoardColumn column)
        {
            if (_project != null)
                ColumnOperations.Move(_project, column, +1);
        }
    }
}
