using TaskTracker.Core.Models;

namespace TaskTracker.Core.Services
{
    public record DueTaskItem(ProjectModel Project, TaskModel Task);

    public record DueOverview(
        IReadOnlyList<DueTaskItem> Overdue,
        IReadOnlyList<DueTaskItem> DueToday,
        IReadOnlyList<DueTaskItem> DueThisWeek)
    {
        public bool IsEmpty => Overdue.Count == 0 && DueToday.Count == 0 && DueThisWeek.Count == 0;
    }

    /// <summary>
    /// Collects open, dated tasks across non-archived projects into
    /// overdue / due today / due within the next seven days buckets.
    /// Shared by the Home dashboard, tray reminders, and the MCP server.
    /// </summary>
    public static class DueTasks
    {
        public static DueOverview Collect(IEnumerable<ProjectModel> projects, DateTime? today = null)
        {
            var day = (today ?? DateTime.Today).Date;
            var overdue = new List<DueTaskItem>();
            var dueToday = new List<DueTaskItem>();
            var dueThisWeek = new List<DueTaskItem>();

            foreach (var project in projects.Where(p => !p.IsArchived))
            {
                foreach (var task in project.Tasks.Where(t => !t.IsDone && t.DueDate.HasValue))
                {
                    var due = task.DueDate!.Value.Date;
                    if (due < day)
                        overdue.Add(new DueTaskItem(project, task));
                    else if (due == day)
                        dueToday.Add(new DueTaskItem(project, task));
                    else if (due <= day.AddDays(7))
                        dueThisWeek.Add(new DueTaskItem(project, task));
                }
            }

            static List<DueTaskItem> Sort(List<DueTaskItem> items) =>
                items.OrderBy(i => i.Task.DueDate).ThenBy(i => i.Task.Title, StringComparer.OrdinalIgnoreCase).ToList();

            return new DueOverview(Sort(overdue), Sort(dueToday), Sort(dueThisWeek));
        }
    }
}
