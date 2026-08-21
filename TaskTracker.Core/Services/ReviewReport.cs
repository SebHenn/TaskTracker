using TaskTracker.Core.Models;

namespace TaskTracker.Core.Services
{
    public record ProjectReview(
        string ProjectName,
        IReadOnlyList<string> CompletedTitles,
        int Created,
        int Open,
        int Overdue,
        double TrackedHours);

    /// <param name="Label">
    /// The label the report was scoped to, echoed back so a caller can tell an empty
    /// report from a mistyped label. Null when the report covers everything.
    /// </param>
    public record WeeklyReviewResult(
        DateTime FromUtc,
        DateTime ToUtc,
        int TotalCompleted,
        int TotalCreated,
        IReadOnlyList<ProjectReview> Projects,
        string? Label = null);

    /// <summary>What happened in a recent window, per non-archived project.</summary>
    public static class ReviewReport
    {
        /// <summary>The window every caller gets unless it asks for another.</summary>
        public const int DefaultDays = 7;

        /// <param name="label">
        /// When set, every count and title below is measured over only the tasks carrying
        /// it, and a project is listed if it has any such task at all. This is the one
        /// report that crosses projects, so a label is the only way to ask about an effort
        /// whose set of boards is not known in advance. Matched case-insensitively, the
        /// same rule <see cref="BoardFilter"/> uses.
        /// </param>
        /// <param name="days">Length of the window ending at <paramref name="nowUtc"/>.</param>
        public static WeeklyReviewResult Compute(
            IEnumerable<ProjectModel> projects,
            DateTime? nowUtc = null,
            string? label = null,
            int days = DefaultDays)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);

            var to = nowUtc ?? DateTime.UtcNow;
            // A caller may ask for a window longer than the calendar; clamping keeps an
            // absurd-but-positive value from throwing out of AddDays.
            var from = days < (to - DateTime.MinValue).TotalDays ? to.AddDays(-days) : DateTime.MinValue;

            var scoped = string.IsNullOrWhiteSpace(label) ? null : label.Trim();

            var rows = new List<ProjectReview>();
            foreach (var project in projects.Where(p => !p.IsArchived))
            {
                var tasks = scoped == null
                    ? project.Tasks.ToList()
                    : project.Tasks.Where(t => t.Labels.Contains(scoped, StringComparer.OrdinalIgnoreCase)).ToList();

                var completed = tasks
                    .Where(t => t.CompletedAtUtc >= from && t.CompletedAtUtc <= to)
                    .OrderBy(t => t.CompletedAtUtc)
                    .Select(t => t.Title)
                    .ToList();
                var created = tasks.Count(t => t.CreatedAtUtc >= from && t.CreatedAtUtc <= to);
                var open = tasks.Count(t => !t.IsDone);
                var overdue = tasks.Count(t => t.IsOverdue);
                var trackedHours = Math.Round(tasks.Sum(t => t.TrackedSeconds) / 3600, 2);

                // Unscoped, a row with nothing in it is noise, so quiet boards are dropped.
                // Scoped, the label has already done the narrowing, so a board that carries
                // it belongs in the report even if it was quiet in this window: its finished
                // work and its tracked hours are half of "what did this effort cost".
                var include = scoped == null
                    ? completed.Count > 0 || created > 0 || open > 0
                    : tasks.Count > 0;

                if (include)
                    rows.Add(new ProjectReview(project.Name, completed, created, open, overdue, trackedHours));
            }

            return new WeeklyReviewResult(
                from, to,
                rows.Sum(r => r.CompletedTitles.Count),
                rows.Sum(r => r.Created),
                rows,
                scoped);
        }
    }
}
