using TaskTracker.Core.Models;
using TaskTracker.Core.Services;

namespace TaskTracker.Core.Tests;

/// <summary>
/// The exact edges of the due buckets. Four things read these — the home dashboard, the
/// agenda, tray reminders and the MCP tools — so an off-by-one day here is visible in
/// four places and obvious in none.
/// </summary>
public class DueTasksBoundaryTests
{
    private static readonly DateTime Today = new(2026, 8, 14);

    private static ProjectModel ProjectWith(params (string Title, DateTime? Due, bool Done)[] tasks)
    {
        var project = new ProjectModel { Name = "P" };
        foreach (var column in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(column);
        foreach (var (title, due, done) in tasks)
            project.Tasks.Add(new TaskModel { Title = title, DueDate = due, IsDone = done });
        return project;
    }

    [Theory]
    [InlineData(-1, "overdue")]
    [InlineData(0, "today")]
    [InlineData(1, "week")]
    [InlineData(7, "week")]     // inclusive upper edge
    [InlineData(8, "none")]     // just outside
    public void BucketsByExactDayOffset(int offset, string expected)
    {
        var project = ProjectWith(("t", Today.AddDays(offset), false));

        var overview = DueTasks.Collect(new[] { project }, Today);

        var actual = overview.Overdue.Count > 0 ? "overdue"
            : overview.DueToday.Count > 0 ? "today"
            : overview.DueThisWeek.Count > 0 ? "week"
            : "none";

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TimeOfDayDoesNotChangeTheBucket()
    {
        // Due dates are conceptually date-only but stored as DateTime, so a value with a
        // time component must still land on its own day.
        var project = ProjectWith(("late in the day", Today.AddHours(23).AddMinutes(59), false));

        Assert.Single(DueTasks.Collect(new[] { project }, Today).DueToday);
    }

    [Fact]
    public void CollectIgnoresDoneUndatedAndArchived()
    {
        var project = ProjectWith(
            ("done and overdue", Today.AddDays(-5), true),
            ("undated", null, false));
        var archived = ProjectWith(("archived and overdue", Today.AddDays(-5), false));
        archived.IsArchived = true;

        var overview = DueTasks.Collect(new[] { project, archived }, Today);

        Assert.True(overview.IsEmpty);
    }

    [Fact]
    public void SortsByDueThenTitle()
    {
        var project = ProjectWith(
            ("zebra", Today.AddDays(1), false),
            ("apple", Today.AddDays(2), false),
            ("Banana", Today.AddDays(1), false));

        var week = DueTasks.Collect(new[] { project }, Today).DueThisWeek;

        Assert.Equal(new[] { "Banana", "zebra", "apple" }, week.Select(i => i.Task.Title).ToArray());
    }

    [Fact]
    public void BoardFilterAgreesWithTheBuckets()
    {
        // Two implementations of "this week" that disagree would put a task on the
        // dashboard that the board's own filter then hides.
        var project = ProjectWith(
            ("overdue", Today.AddDays(-1), false),
            ("today", Today, false),
            ("in week", Today.AddDays(7), false),
            ("outside", Today.AddDays(8), false),
            ("undated", null, false));

        var overview = DueTasks.Collect(new[] { project }, Today);

        foreach (var (filter, expected) in new (DueFilter, IReadOnlyList<DueTaskItem>)[]
                 {
                     (DueFilter.Overdue, overview.Overdue),
                     (DueFilter.DueToday, overview.DueToday),
                 })
        {
            var matched = project.Tasks.Where(t => new BoardFilter(Due: filter).Matches(t, Today)).ToList();
            Assert.Equal(expected.Count, matched.Count);
        }

        // DueThisWeek on the dashboard excludes today; the board's bucket includes it,
        // deliberately — "due this week" as a filter means the next seven days inclusive.
        var weekFiltered = project.Tasks.Where(t => new BoardFilter(Due: DueFilter.DueThisWeek).Matches(t, Today)).ToList();
        Assert.Equal(overview.DueToday.Count + overview.DueThisWeek.Count, weekFiltered.Count);
    }
}
