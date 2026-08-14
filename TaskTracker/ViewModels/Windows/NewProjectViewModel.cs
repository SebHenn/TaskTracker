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

        /// <summary>Preset project colors; empty string = none.</summary>
        public IReadOnlyList<string> ColorOptions { get; } = new[]
        {
            "", "#E53935", "#FB8C00", "#FDD835", "#43A047", "#00ACC1", "#1E88E5", "#8E24AA", "#6D4C41",
        };

        [ObservableProperty]
        private string _selectedColor = "";

        public int WindowHeight => IsTaskMode ? 430 : 300;

        public List<string> ParseLabels() => Core.Services.LabelParser.Parse(LabelsText);

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

        public NewProjectViewModel(ProjectViewModel projectViewModel, Services.ILanguageService languageService)
        {
            // No edit-task mode: editing a task moved into the board's detail drawer, so
            // this dialog only ever creates one.
            IsTaskMode = projectViewModel.IsCreateTask;
            TitleString = languageService.GetString(
                projectViewModel.IsEditing ? "ChangeCurrentProject"
                : projectViewModel.IsCreateTask ? "CreateNewTask"
                : "CreateNewProject");
            NameString = languageService.GetString(IsTaskMode ? "TaskName" : "ProjectName");
            DescriptionString = languageService.GetString(IsTaskMode ? "TaskDescription" : "ProjectDescription");
        }
    }
}
