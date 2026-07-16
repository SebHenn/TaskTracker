using TaskTracker.Core.Models;

namespace TaskTracker.Core.Services
{
    public record WeeklyDone(DateTime WeekStart, int Count);

    public record ProjectStatsResult(
        int OpenCount,
        int DoneCount,
        int OverdueCount,
        double CompletionPercent,
        IReadOnlyList<WeeklyDone> DonePerWeek);

    public static class ProjectStats
    {
        public const int Weeks = 8;

        /// <summary>
        /// Counts plus tasks completed per ISO-week for the last <see cref="Weeks"/> weeks
        /// (oldest first). Tasks done before completion timestamps existed count only in totals.
        /// </summary>
        public static ProjectStatsResult Compute(ProjectModel project, DateTime? todayUtc = null)
        {
            var today = (todayUtc ?? DateTime.UtcNow).Date;
            var open = project.Tasks.Count(t => !t.IsDone);
            var done = project.Tasks.Count(t => t.IsDone);
            var overdue = project.Tasks.Count(t => t.IsOverdue);
            var total = open + done;
            var percent = total == 0 ? 0 : (double)done / total * 100;

            // Weeks start on Monday.
            var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
            var currentWeekStart = today.AddDays(-daysSinceMonday);

            var weekly = new List<WeeklyDone>();
            for (var i = Weeks - 1; i >= 0; i--)
            {
                var weekStart = currentWeekStart.AddDays(-7 * i);
                var weekEnd = weekStart.AddDays(7);
                var count = project.Tasks.Count(t =>
                    t.CompletedAtUtc.HasValue &&
                    t.CompletedAtUtc.Value.Date >= weekStart &&
                    t.CompletedAtUtc.Value.Date < weekEnd);
                weekly.Add(new WeeklyDone(weekStart, count));
            }

            return new ProjectStatsResult(open, done, overdue, percent, weekly);
        }
    }
}
