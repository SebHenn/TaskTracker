using TaskTracker.Core.Models;

namespace TaskTracker.Core.Services
{
    /// <summary>
    /// Moving a task from one project to another.
    ///
    /// Not a delete-and-recreate: the task keeps its id, subtasks, notes and tracked
    /// time, so history survives the move. Deliberately not routed through
    /// <see cref="Trash"/> either — nothing is being deleted, and a trash entry for a task
    /// that still exists elsewhere would be restorable into a second copy.
    /// </summary>
    public static class TaskTransfer
    {
        /// <summary>
        /// Moves <paramref name="task"/> from <paramref name="from"/> to
        /// <paramref name="to"/>, placing it in <paramref name="targetColumn"/> (or the
        /// destination's first column when null). Returns false when the task is not in
        /// the source project or the two projects are the same.
        /// </summary>
        public static bool Move(ProjectModel from, ProjectModel to, TaskModel task, BoardColumn? targetColumn = null)
        {
            ArgumentNullException.ThrowIfNull(from);
            ArgumentNullException.ThrowIfNull(to);
            ArgumentNullException.ThrowIfNull(task);

            if (ReferenceEquals(from, to) || from.Id == to.Id)
                return false;
            if (!from.Tasks.Remove(task))
                return false;

            // The issue belongs to the old project's repository, so carrying the link
            // across would make the destination's next sync mutate an issue in a
            // repository it has nothing to do with. GitHubIssueVanished is cleared too:
            // this is not the "deleted on GitHub" case, and leaving it set would stop the
            // destination ever exporting the task.
            task.GitHubIssueNumber = null;
            task.LastSyncedIssueState = null;
            task.LastSyncedTitle = null;
            task.GitHubIssueVanished = false;

            // Positions are per-column and dense; an order from the old board means
            // nothing here. Null puts it back under the automatic sort.
            task.SortOrder = null;

            to.Tasks.Add(task);

            var column = targetColumn != null && to.Columns.Contains(targetColumn)
                ? targetColumn
                : task.IsDone ? to.FirstDoneColumn : to.FirstColumn;

            if (column == null)
            {
                Storage.ProjectStore.NormalizeColumns(to);
                column = task.IsDone ? to.FirstDoneColumn : to.FirstColumn;
            }

            // Through MoveTaskToColumn so IsDone and the column agree at the destination.
            // Landing in a done column completes the task, which also means a recurring
            // one spawns its next occurrence — in the destination project, which is where
            // the task now lives.
            to.MoveTaskToColumn(task, column!);
            return true;
        }
    }
}
