using TaskTracker.Core.Models;
using TaskTracker.Core.Storage;

namespace TaskTracker.Core.Services
{
    /// <summary>
    /// The single place a task's done-state is set.
    ///
    /// Completion has to run through <see cref="ProjectModel.MoveTaskToColumn"/> or
    /// recurrence never fires and the column stops agreeing with IsDone. Three callers
    /// (the board, the MCP server, GitHub sync) each resolved the target column
    /// themselves and quietly fell back to <c>task.IsDone = value</c> when it came back
    /// null — which silently dropped the next occurrence of every recurring task
    /// completed outside the board. This exists so that fallback has one implementation
    /// and it is a repair rather than a bypass.
    /// </summary>
    public static class TaskCompletion
    {
        /// <summary>
        /// Marks a task done or not-done by moving it to the project's first done /
        /// first open column. Returns the spawned next occurrence when completing a
        /// recurring task, otherwise null. A no-op when the task is already in the
        /// requested state, so it never double-spawns or churns timestamps.
        /// </summary>
        public static TaskModel? SetDone(ProjectModel project, TaskModel task, bool value)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(task);

            if (task.IsDone == value)
                return null;

            var target = Target(project, value);
            if (target == null)
            {
                // Every load path runs NormalizeColumns, so a project without the column
                // we need has been built or mutated in memory. Repairing is what the rest
                // of the codebase does here (see Trash.Restore); poking IsDone directly
                // would leave ColumnId pointing at nothing and skip recurrence.
                ProjectStore.NormalizeColumns(project);
                target = Target(project, value);
            }

            return project.MoveTaskToColumn(task, target!);
        }

        private static BoardColumn? Target(ProjectModel project, bool done)
            => done ? project.FirstDoneColumn : project.FirstColumn;
    }
}
