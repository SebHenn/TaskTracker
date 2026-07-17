using TaskTracker.Core.Models;

namespace TaskTracker.Core.Models
{
    public static class RecurrenceRules
    {
        public const string None = "none";
        public const string Daily = "daily";
        public const string Weekly = "weekly";
        public const string Monthly = "monthly";

        public static readonly string[] All = { None, Daily, Weekly, Monthly };
    }
}

namespace TaskTracker.Core.Services
{
    public static class Recurrence
    {
        /// <summary>
        /// Called when a task transitions to done. For recurring tasks, spawns
        /// the next occurrence into the project's first column: same
        /// title/description/priority/labels/recurrence, subtasks copied
        /// unchecked, fresh ids, next due date, no GitHub link.
        /// Returns the spawned task, or null when not recurring.
        /// </summary>
        public static TaskModel? SpawnNextIfRecurring(ProjectModel project, TaskModel completed, DateTime? todayUtc = null)
        {
            if (!completed.IsRecurring)
                return null;

            var interval = Math.Max(1, completed.RecurrenceInterval);
            var baseDate = completed.DueDate ?? (todayUtc ?? DateTime.Today).Date;
            var nextDue = completed.Recurrence switch
            {
                RecurrenceRules.Daily => baseDate.AddDays(interval),
                RecurrenceRules.Weekly => baseDate.AddDays(7 * interval),
                RecurrenceRules.Monthly => baseDate.AddMonths(interval),
                _ => (DateTime?)null,
            };
            if (nextDue == null)
                return null;

            var next = new TaskModel
            {
                Title = completed.Title,
                Description = completed.Description,
                Priority = completed.Priority,
                DueDate = nextDue,
                Recurrence = completed.Recurrence,
                RecurrenceInterval = completed.RecurrenceInterval,
                CreatedAtUtc = DateTime.UtcNow,
                ColumnId = project.FirstColumn?.Id,
            };
            foreach (var label in completed.Labels)
                next.Labels.Add(label);
            foreach (var subTask in completed.SubTasks)
                next.SubTasks.Add(new SubTaskModel { Title = subTask.Title });

            project.Tasks.Add(next);
            return next;
        }
    }
}
