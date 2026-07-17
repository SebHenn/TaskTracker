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

    public record WeeklyReviewResult(
        DateTime FromUtc,
        DateTime ToUtc,
        int TotalCompleted,
        int TotalCreated,
        IReadOnlyList<ProjectReview> Projects);

    /// <summary>What happened in the last seven days, per non-archived project.</summary>
    public static class ReviewReport
    {
        public static WeeklyReviewResult Compute(IEnumerable<ProjectModel> projects, DateTime? nowUtc = null)
        {
            var to = nowUtc ?? DateTime.UtcNow;
            var from = to.AddDays(-7);

            var rows = new List<ProjectReview>();
            foreach (var project in projects.Where(p => !p.IsArchived))
            {
                var completed = project.Tasks
                    .Where(t => t.CompletedAtUtc >= from && t.CompletedAtUtc <= to)
                    .OrderBy(t => t.CompletedAtUtc)
                    .Select(t => t.Title)
                    .ToList();
                var created = project.Tasks.Count(t => t.CreatedAtUtc >= from && t.CreatedAtUtc <= to);
                var open = project.Tasks.Count(t => !t.IsDone);
                var overdue = project.Tasks.Count(t => t.IsOverdue);
                var trackedHours = Math.Round(project.Tasks.Sum(t => t.TrackedSeconds) / 3600, 2);

                if (completed.Count > 0 || created > 0 || open > 0)
                    rows.Add(new ProjectReview(project.Name, completed, created, open, overdue, trackedHours));
            }

            return new WeeklyReviewResult(
                from, to,
                rows.Sum(r => r.CompletedTitles.Count),
                rows.Sum(r => r.Created),
                rows);
        }
    }
}
