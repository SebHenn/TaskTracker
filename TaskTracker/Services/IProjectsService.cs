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

        /// <summary>
        /// Snapshot the current state now and write it in the background. Returns as
        /// soon as the snapshot is taken, so the UI thread never waits on the store
        /// lock or on file I/O.
        /// </summary>
        void SaveNow();

        /// <summary>
        /// Same, but blocks until the bytes are on disk. For shutdown, where
        /// returning early would lose the write.
        /// </summary>
        void Flush();
    }
}
