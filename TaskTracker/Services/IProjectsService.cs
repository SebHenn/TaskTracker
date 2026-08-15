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

        /// <summary>False when the name is blank or already taken; the project is left alone.</summary>
        bool ChangeProjectName(ProjectModel project, string newName);

        void ChangeProjectDescription(ProjectModel project, string newDescription);

        // No AddTaskToProject/RemoveTaskFromProject: they were one-line wrappers over
        // project.Tasks with no callers, and removing a task has to go through
        // Core.Services.Trash rather than straight off the list.

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
