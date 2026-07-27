using TaskTracker.Core.Models;

namespace TaskTracker.Core.Services
{
    /// <summary>
    /// Applies one action across a set of tasks.
    ///
    /// Clearing a finished column meant dragging or right-clicking every card one at a
    /// time. Doing it in a loop from the view model is not quite the same thing: the
    /// order the batch lands in, and what happens when a recurring task completes
    /// mid-loop, are exactly the parts that go wrong, so they live here under test.
    /// </summary>
    public static class BulkOperations
    {
        /// <summary>
        /// Moves every task to <paramref name="column"/>, appended after whatever is
        /// already there and keeping the batch's own relative order. Returns how many
        /// moved.
        /// </summary>
        public static int MoveAll(ProjectModel project, IEnumerable<TaskModel> tasks, BoardColumn column)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(tasks);
            ArgumentNullException.ThrowIfNull(column);

            // Materialized before anything moves: completing a recurring task spawns its
            // next occurrence straight into project.Tasks, and a lazy sequence over the
            // board would then pick the new copy up and move it too.
            var batch = tasks.ToList();
            foreach (var task in batch)
                project.MoveTaskToColumn(task, column);

            Renumber(project, column, batch);
            return batch.Count;
        }

        /// <summary>
        /// Sends every task to the trash. Returns the entries in the order they were
        /// given, which is the order a caller should restore them in.
        /// </summary>
        public static IReadOnlyList<TrashedTask> DeleteAll(
            ProjectModel project, IEnumerable<TaskModel> tasks, DateTime? nowUtc = null)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(tasks);

            // Materialized for the same reason, and because Trash.Delete removes from
            // the collection being enumerated.
            return tasks.ToList().Select(task => Trash.Delete(project, task, nowUtc)).ToList();
        }

        /// <summary>
        /// Gives the column a single explicit order: what was already there first, in the
        /// order it was displayed, then the batch. Done once at the end rather than per
        /// task, so a five-card move does not renumber the lane five times.
        /// </summary>
        private static void Renumber(ProjectModel project, BoardColumn column, IReadOnlyList<TaskModel> batch)
        {
            var moved = batch.Select(task => task.Id).ToHashSet();
            var settled = LaneSort.Apply(
                project.Tasks.Where(task =>
                    !moved.Contains(task.Id) && project.ColumnOf(task)?.Id == column.Id));

            var order = settled.Concat(batch).ToList();
            for (var i = 0; i < order.Count; i++)
                order[i].SortOrder = i + 1;
        }
    }
}
