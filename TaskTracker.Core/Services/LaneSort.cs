using TaskTracker.Core.Models;

namespace TaskTracker.Core.Services
{
    public static class LaneSort
    {
        /// <summary>
        /// Board ordering: explicitly positioned tasks (SortOrder) first in
        /// their order, then unordered tasks by priority desc, due date, title.
        /// </summary>
        public static IOrderedEnumerable<TaskModel> Apply(IEnumerable<TaskModel> tasks) =>
            tasks
                .OrderBy(t => t.SortOrder ?? double.MaxValue)
                .ThenByDescending(t => t.Priority)
                .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
                .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase);
    }
}
