using TaskTracker.Core.Models;

namespace TaskTracker.Core.Services
{
    /// <summary>
    /// Due-date buckets a board can be narrowed to. The boundaries match
    /// <see cref="DueTasks"/> exactly, so "Due this week" means the same thing on the
    /// board as it does on the home dashboard.
    /// </summary>
    public enum DueFilter
    {
        Any,
        Overdue,
        DueToday,

        /// <summary>Today through seven days out — the same window the home page uses.</summary>
        DueThisWeek,

        /// <summary>Tasks with no due date at all, for finding what still needs scheduling.</summary>
        NoDueDate
    }

    /// <summary>
    /// What the board is currently narrowed to. One value object rather than three
    /// parameters, so adding a dimension does not ripple through every call site, and
    /// so <see cref="IsActive"/> is something the UI can ask rather than re-derive.
    /// </summary>
    /// <param name="Label">Null or empty matches every task. The "all labels" entry is
    /// a localized display string, so the caller maps it to null rather than Core
    /// knowing about display text.</param>
    public record BoardFilter(string? Label = null, DueFilter Due = DueFilter.Any, TaskPriority? Priority = null)
    {
        public static readonly BoardFilter None = new();

        /// <summary>Whether anything is being hidden — drives "showing a subset" affordances.</summary>
        public bool IsActive => !string.IsNullOrEmpty(Label) || Due != DueFilter.Any || Priority != null;

        /// <param name="today">
        /// Local calendar day the due buckets are measured against. Passed in rather than
        /// read from the clock so the rules are testable and a board rendered either side
        /// of midnight is at least self-consistent.
        /// </param>
        public bool Matches(TaskModel task, DateTime today)
        {
            ArgumentNullException.ThrowIfNull(task);

            if (!string.IsNullOrEmpty(Label) && !task.Labels.Contains(Label, StringComparer.OrdinalIgnoreCase))
                return false;

            if (Priority != null && task.Priority != Priority)
                return false;

            return MatchesDue(task, today.Date);
        }

        private bool MatchesDue(TaskModel task, DateTime day)
        {
            if (Due == DueFilter.Any)
                return true;

            if (Due == DueFilter.NoDueDate)
                return task.DueDate == null;

            if (task.DueDate == null)
                return false;

            var due = task.DueDate.Value.Date;
            return Due switch
            {
                // A completed task is not overdue however old its date, which is what
                // TaskModel.IsOverdue says and what the done column has to keep saying.
                DueFilter.Overdue => !task.IsDone && due < day,
                DueFilter.DueToday => due == day,
                DueFilter.DueThisWeek => due >= day && due <= day.AddDays(7),
                _ => true
            };
        }
    }
}
