using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using TaskTracker.Core.Models;
using TaskTracker.ViewModels.Pages;

namespace TaskTracker.ViewModels.Windows
{
    public partial class NewProjectViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _titleString = "";

        [ObservableProperty]
        private string _nameString = "";

        [ObservableProperty]
        private string _descriptionString = "";

        [ObservableProperty]
        private string _name = "";

        [ObservableProperty]
        private string _description = "";

        [ObservableProperty]
        private bool _dialogResult = false;

        /// <summary>True when the dialog edits a task (shows due date/priority/labels).</summary>
        [ObservableProperty]
        private bool _isTaskMode = false;

        [ObservableProperty]
        private DateTime? _dueDate;

        [ObservableProperty]
        private TaskPriority _selectedPriority = TaskPriority.Medium;

        [ObservableProperty]
        private string _labelsText = "";

        public IReadOnlyList<TaskPriority> Priorities { get; } = new[] { TaskPriority.Low, TaskPriority.Medium, TaskPriority.High };

        public int WindowHeight => IsTaskMode ? 430 : 300;

        public List<string> ParseLabels() =>
            LabelsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        [RelayCommand]
        private void OnConfirm()
        {
            DialogResult = true;
        }

        [RelayCommand]
        private void OnCancel()
        {
            DialogResult = false;
        }

        public NewProjectViewModel(ProjectViewModel projectViewModel)
        {
            IsTaskMode = projectViewModel.IsCreateTask || projectViewModel.IsEditTask;
            TitleString = projectViewModel.IsEditing ? "Change current Project" : projectViewModel.IsCreateTask ? "Create new Task" : projectViewModel.IsEditTask ? "Change current Task" : "Create new Project";
            NameString = IsTaskMode ? "Task Name" : "Project Name";
            DescriptionString = IsTaskMode ? "Task Description" : "Project Description";
        }
    }
}
