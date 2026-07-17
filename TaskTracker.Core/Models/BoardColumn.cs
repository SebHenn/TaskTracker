using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace TaskTracker.Core.Models
{
    /// <summary>
    /// A board column of a project. Order is the position within
    /// <see cref="ProjectModel.Columns"/>. Tasks in a column with
    /// <see cref="IsDoneColumn"/> count as done everywhere (stats, sync, filters).
    /// </summary>
    public partial class BoardColumn : ObservableObject
    {
        [ObservableProperty]
        private Guid _id = Guid.NewGuid();

        [ObservableProperty]
        private string _name = "";

        [ObservableProperty]
        private bool _isDoneColumn = false;
    }
}
