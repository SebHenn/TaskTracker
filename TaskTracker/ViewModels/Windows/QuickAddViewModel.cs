using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using TaskTracker.Core.Models;
using TaskTracker.Services;

namespace TaskTracker.ViewModels.Windows
{
    public partial class QuickAddViewModel : ObservableObject
    {
        private readonly IProjectsService _projectsService;

        [ObservableProperty]
        private string _title = "";

        [ObservableProperty]
        private ProjectModel? _selectedProject;

        [ObservableProperty]
        private bool _dialogResult;

        public ObservableCollection<ProjectModel> Projects { get; }

        public QuickAddViewModel(IProjectsService projectsService)
        {
            _projectsService = projectsService;
            Projects = new ObservableCollection<ProjectModel>(
                projectsService.projectModels.Where(p => !p.IsArchived));
            SelectedProject = projectsService.RecentProjects.FirstOrDefault() ?? Projects.FirstOrDefault();
        }

        [RelayCommand]
        private void OnConfirm()
        {
            if (SelectedProject == null || string.IsNullOrWhiteSpace(Title))
            {
                DialogResult = false;
                return;
            }

            var task = new TaskModel { Title = Title.Trim(), CreatedAtUtc = DateTime.UtcNow };
            SelectedProject.Tasks.Add(task);
            if (SelectedProject.FirstColumn is { } column)
                SelectedProject.MoveTaskToColumn(task, column);
            DialogResult = true;
        }

        [RelayCommand]
        private void OnCancel()
        {
            DialogResult = false;
        }
    }
}
