using TaskTracker.Core.Models;

namespace TaskTracker.Core.Services
{
    /// <summary>One day of the agenda. Present even when nothing is due, so the week reads as a shape.</summary>
    public record AgendaDay(DateTime Date, bool IsToday, IReadOnlyList<DueTaskItem> Items)
    {
        public bool IsEmpty => Items.Count == 0;
    }

    public record AgendaResult(IReadOnlyList<AgendaDay> Days, IReadOnlyList<DueTaskItem> Overdue)
    {
        public int TotalScheduled => Days.Sum(d => d.Items.Count);
        public bool IsEmpty => Overdue.Count == 0 && TotalScheduled == 0;
    }

    /// <summary>
    /// The next few days of dated, open work, grouped by day.
    ///
    /// Deliberately a regrouping of <see cref="DueTasks.Collect"/> rather than its own
    /// query: the home dashboard, the tray reminders and the MCP server all read the same
    /// buckets, and a second definition of "this week" that drifted from theirs would be
    /// worse than no agenda at all.
    /// </summary>
    public static class Agenda
    {
        /// <summary>Days after today the agenda covers — matches the DueTasks window exactly.</summary>
        public const int DefaultDaysAhead = 7;

        public static AgendaResult Compute(IEnumerable<ProjectModel> projects, DateTime? today = null, int daysAhead = DefaultDaysAhead)
        {
            ArgumentNullException.ThrowIfNull(projects);

            var day = (today ?? DateTime.Today).Date;
            var span = Math.Max(0, daysAhead);
            var overview = DueTasks.Collect(projects, day);

            // DueToday covers day 0 and DueThisWeek days 1..7, so their union is exactly
            // the window rendered here.
            var scheduled = overview.DueToday
                .Concat(overview.DueThisWeek)
                .GroupBy(item => item.Task.DueDate!.Value.Date)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<DueTaskItem>)g.ToList());

            var days = new List<AgendaDay>(span + 1);
            for (var offset = 0; offset <= span; offset++)
            {
                var date = day.AddDays(offset);
                days.Add(new AgendaDay(
                    date,
                    offset == 0,
                    scheduled.TryGetValue(date, out var items) ? items : Array.Empty<DueTaskItem>()));
            }

            return new AgendaResult(days, overview.Overdue);
        }
    }
}
