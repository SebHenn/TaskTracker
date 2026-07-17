using TaskTracker.Core.Models;
using TaskTracker.Core.Services;

namespace TaskTracker.Core.Tests;

public class ReviewReportTests
{
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Compute_CountsCompletionsAndCreationsInWindow()
    {
        var project = new ProjectModel { Name = "P" };
        var doneThisWeek = new TaskModel { Title = "shipped", IsDone = true, CompletedAtUtc = Now.AddDays(-2) };
        var doneLongAgo = new TaskModel { Title = "ancient", IsDone = true, CompletedAtUtc = Now.AddDays(-30) };
        var createdThisWeek = new TaskModel { Title = "fresh", CreatedAtUtc = Now.AddDays(-1) };
        var openOld = new TaskModel { Title = "lingering", CreatedAtUtc = Now.AddDays(-60), TrackedSeconds = 7200 };
        foreach (var t in new[] { doneThisWeek, doneLongAgo, createdThisWeek, openOld })
            project.Tasks.Add(t);

        var review = ReviewReport.Compute(new[] { project }, Now);

        var row = Assert.Single(review.Projects);
        Assert.Equal(new[] { "shipped" }, row.CompletedTitles);
        Assert.Equal(1, row.Created);
        Assert.Equal(2, row.Open);
        Assert.Equal(2, row.TrackedHours);
        Assert.Equal(1, review.TotalCompleted);
        Assert.Equal(1, review.TotalCreated);
    }

    [Fact]
    public void Compute_SkipsArchivedAndInactiveProjects()
    {
        var archived = new ProjectModel { Name = "Archived", IsArchived = true };
        archived.Tasks.Add(new TaskModel { Title = "t", CompletedAtUtc = Now.AddDays(-1), IsDone = true });
        var empty = new ProjectModel { Name = "Empty" };

        var review = ReviewReport.Compute(new[] { archived, empty }, Now);

        Assert.Empty(review.Projects);
        Assert.Equal(0, review.TotalCompleted);
    }
}
