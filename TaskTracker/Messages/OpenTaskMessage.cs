using TaskTracker.Core.Models;

namespace TaskTracker.Messages
{
    /// <summary>
    /// Open a project board and select one task on it.
    ///
    /// <see cref="ProjectSelectClickMessage"/> only gets you to the board, which is why
    /// clicking a search result or an agenda row used to drop you on a project and leave
    /// you to find the task again yourself.
    /// </summary>
    public class OpenTaskMessage
    {
        public OpenTaskMessage(ProjectModel project, TaskModel task)
        {
            Project = project;
            Task = task;
        }

        public ProjectModel Project { get; }

        public TaskModel Task { get; }
    }
}
