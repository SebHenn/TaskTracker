using System.Collections.ObjectModel;
using TaskTracker.Core.Models;

namespace TaskTracker.Services
{
    public interface IProjectsService
    {
        ObservableCollection<ProjectModel> projectModels { get; }

        ObservableCollection<ProjectModel> RecentProjects { get; }

        void AddRecentProject(ProjectModel project);

        ProjectModel AddProject(string project, string description);

        void RemoveProject(ProjectModel project);

        void ChangeProjectName(ProjectModel project, string newName);

        void ChangeProjectDescription(ProjectModel project, string newDescription);

        void AddTaskToProject(ProjectModel project, TaskModel task);

        void RemoveTaskFromProject(ProjectModel project, TaskModel task);

        /// <summary>Flush any pending changes to disk immediately.</summary>
        void SaveNow();
    }
}
