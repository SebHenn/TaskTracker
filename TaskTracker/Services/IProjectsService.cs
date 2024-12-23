using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using TaskTracker.Models;

namespace TaskTracker.Services
{
    public interface IProjectsService
    {
        ObservableCollection<ProjectModel> projectModels { get; }

        ObservableCollection<ProjectModel> RecentProjects { get; }
        
        void AddRecentProject(ProjectModel project);

        void AddProject(string project, string description);

        void RemoveProject(ProjectModel project);

        void ChangeProjectName(ProjectModel oldName, string newName);

        void ChangeProjectDescription(ProjectModel project, string newDescription);

        void AddTaskToProject(ProjectModel project, TaskModel task);

        void RemoveTaskFromProject(ProjectModel project, TaskModel task);
    }
}
