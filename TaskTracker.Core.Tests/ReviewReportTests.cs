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

    // ---------- Scoping the review to one effort ----------

    private static ProjectModel Labelled(string name, params TaskModel[] tasks)
    {
        var project = new ProjectModel { Name = name };
        foreach (var task in tasks)
            project.Tasks.Add(task);
        return project;
    }

    private static TaskModel NewTask(string title, string? label = null, bool done = false, DateTime? completed = null, DateTime? created = null)
    {
        var task = new TaskModel { Title = title, IsDone = done, CompletedAtUtc = completed, CreatedAtUtc = created ?? Now.AddDays(-1) };
        if (label != null)
            task.Labels.Add(label);
        return task;
    }

    [Fact]
    public void Compute_Label_CountsOnlyLabelledTasks_AndDropsBoardsWithout()
    {
        // The reported case: an effort spread over several boards, each of which also
        // carries unrelated work, and eleven more boards that carry none of it.
        var effort = Labelled("cifail",
            NewTask("labelled shipped", "cartographer", done: true, completed: Now.AddDays(-2), created: Now.AddDays(-3)),
            NewTask("labelled open", "cartographer"),
            NewTask("unrelated open"));
        var unrelated = Labelled("Wordle", NewTask("someone else's open task"));

        var review = ReviewReport.Compute(new[] { effort, unrelated }, Now, label: "cartographer");

        var row = Assert.Single(review.Projects);
        Assert.Equal("cifail", row.ProjectName);
        Assert.Equal(new[] { "labelled shipped" }, row.CompletedTitles);
        Assert.Equal(1, row.Open);
        Assert.Equal(2, row.Created);
        Assert.Equal(1, review.TotalCompleted);
        Assert.Equal("cartographer", review.Label);
    }

    [Fact]
    public void Compute_Label_MatchesCaseInsensitivelyAndIgnoresSurroundingSpace()
    {
        var project = Labelled("P", NewTask("t", "Cartographer"));

        var review = ReviewReport.Compute(new[] { project }, Now, label: "  cartographer ");

        Assert.Single(review.Projects);
        Assert.Equal("cartographer", review.Label);
    }

    [Fact]
    public void Compute_Label_KeepsABoardWhoseLabelledWorkIsAlreadyFinished()
    {
        // Unscoped this row would be dropped as noise. Scoped it is half the answer:
        // "what did this effort cost" is exactly the finished work and its tracked hours.
        var finished = Labelled("done and dusted",
            NewTask("shipped long ago", "cartographer", done: true, completed: Now.AddDays(-40), created: Now.AddDays(-50)));
        finished.Tasks[0].TrackedSeconds = 3600;

        var review = ReviewReport.Compute(new[] { finished }, Now, label: "cartographer");

        var row = Assert.Single(review.Projects);
        Assert.Empty(row.CompletedTitles);
        Assert.Equal(0, row.Open);
        Assert.Equal(1, row.TrackedHours);
    }

    [Fact]
    public void Compute_Label_ThatNothingCarries_ReportsNothingRatherThanEverything()
    {
        var project = Labelled("P", NewTask("t", "chore"));

        var review = ReviewReport.Compute(new[] { project }, Now, label: "typo");

        Assert.Empty(review.Projects);
        Assert.Equal(0, review.TotalCompleted);
        Assert.Equal("typo", review.Label);
    }

    [Fact]
    public void Compute_WithoutALabel_IsUnchanged_AndSaysSo()
    {
        var project = Labelled("P", NewTask("t", "chore"));

        var review = ReviewReport.Compute(new[] { project }, Now);

        Assert.Single(review.Projects);
        Assert.Null(review.Label);
        Assert.Equal(7, (review.ToUtc - review.FromUtc).TotalDays);
    }

    [Fact]
    public void Compute_Days_WidensTheWindow()
    {
        var project = Labelled("P",
            NewTask("recent", done: true, completed: Now.AddDays(-2)),
            NewTask("older", done: true, completed: Now.AddDays(-20)));

        var week = ReviewReport.Compute(new[] { project }, Now);
        var month = ReviewReport.Compute(new[] { project }, Now, days: 30);

        Assert.Equal(new[] { "recent" }, week.Projects.Single().CompletedTitles);
        Assert.Equal(new[] { "older", "recent" }, month.Projects.Single().CompletedTitles);
        Assert.Equal(Now.AddDays(-30), month.FromUtc);
    }

    [Fact]
    public void Compute_Days_RejectsAWindowThatIsNotAWindow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReviewReport.Compute(Array.Empty<ProjectModel>(), Now, days: 0));
    }

    [Fact]
    public void Compute_Days_LongerThanTheCalendarClampsInsteadOfThrowing()
    {
        var review = ReviewReport.Compute(Array.Empty<ProjectModel>(), Now, days: int.MaxValue);

        Assert.Equal(DateTime.MinValue, review.FromUtc);
    }
}
