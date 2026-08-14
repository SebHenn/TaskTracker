using TaskTracker.Core.Models;
using TaskTracker.Core.Services;

namespace TaskTracker.Core.Tests;

public class AgendaTests
{
    private static readonly DateTime Today = new(2026, 8, 14);

    private static ProjectModel ProjectWith(params (string Title, DateTime? Due, bool Done)[] tasks)
    {
        var project = new ProjectModel { Name = "P" };
        foreach (var column in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(column);
        foreach (var (title, due, done) in tasks)
        {
            var task = new TaskModel { Title = title, DueDate = due, IsDone = done };
            task.ColumnId = (done ? project.FirstDoneColumn : project.FirstColumn)!.Id;
            project.Tasks.Add(task);
        }
        return project;
    }

    [Fact]
    public void CoversTodayPlusSevenDays()
    {
        var result = Agenda.Compute(new[] { ProjectWith() }, Today);

        Assert.Equal(8, result.Days.Count);
        Assert.Equal(Today, result.Days[0].Date);
        Assert.True(result.Days[0].IsToday);
        Assert.Equal(Today.AddDays(7), result.Days[^1].Date);
        Assert.All(result.Days.Skip(1), d => Assert.False(d.IsToday));
    }

    [Fact]
    public void GroupsTasksOntoTheirDueDay()
    {
        var project = ProjectWith(
            ("today", Today, false),
            ("also today", Today, false),
            ("in three days", Today.AddDays(3), false));

        var result = Agenda.Compute(new[] { project }, Today);

        Assert.Equal(2, result.Days[0].Items.Count);
        Assert.Equal("in three days", result.Days[3].Items.Single().Task.Title);
        Assert.Equal(3, result.TotalScheduled);
    }

    [Fact]
    public void KeepsEmptyDays()
    {
        // The gaps are what makes the week readable as a shape; dropping them would
        // silently renumber the days.
        var result = Agenda.Compute(new[] { ProjectWith(("today", Today, false)) }, Today);

        Assert.Equal(8, result.Days.Count);
        Assert.False(result.Days[0].IsEmpty);
        Assert.All(result.Days.Skip(1), d => Assert.True(d.IsEmpty));
    }

    [Fact]
    public void OverdueIsSeparateFromTheWeek()
    {
        var project = ProjectWith(
            ("late", Today.AddDays(-3), false),
            ("today", Today, false));

        var result = Agenda.Compute(new[] { project }, Today);

        Assert.Equal("late", result.Overdue.Single().Task.Title);
        // An overdue task must not also appear on a day of the week ahead.
        Assert.DoesNotContain(result.Days.SelectMany(d => d.Items), i => i.Task.Title == "late");
    }

    [Fact]
    public void ExcludesDoneUndatedAndBeyondTheWindow()
    {
        var project = ProjectWith(
            ("done today", Today, true),
            ("undated", null, false),
            ("next month", Today.AddDays(30), false),
            ("day eight", Today.AddDays(8), false));

        var result = Agenda.Compute(new[] { project }, Today);

        Assert.Equal(0, result.TotalScheduled);
        Assert.Empty(result.Overdue);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void IncludesTheSeventhDayButNotTheEighth()
    {
        var project = ProjectWith(
            ("day seven", Today.AddDays(7), false),
            ("day eight", Today.AddDays(8), false));

        var result = Agenda.Compute(new[] { project }, Today);

        Assert.Equal("day seven", result.Days[^1].Items.Single().Task.Title);
        Assert.Equal(1, result.TotalScheduled);
    }

    [Fact]
    public void SkipsArchivedProjects()
    {
        var project = ProjectWith(("today", Today, false));
        project.IsArchived = true;

        Assert.True(Agenda.Compute(new[] { project }, Today).IsEmpty);
    }

    [Fact]
    public void AgreesWithTheHomeDashboard()
    {
        // The two views must never disagree about what "this week" means, which is why
        // Agenda regroups DueTasks rather than running its own query.
        var project = ProjectWith(
            ("late", Today.AddDays(-1), false),
            ("today", Today, false),
            ("later", Today.AddDays(4), false),
            ("outside", Today.AddDays(9), false));

        var agenda = Agenda.Compute(new[] { project }, Today);
        var overview = DueTasks.Collect(new[] { project }, Today);

        Assert.Equal(overview.Overdue.Count, agenda.Overdue.Count);
        Assert.Equal(overview.DueToday.Count + overview.DueThisWeek.Count, agenda.TotalScheduled);
    }

    [Fact]
    public void SpansProjects()
    {
        var a = ProjectWith(("from a", Today, false));
        var b = ProjectWith(("from b", Today, false));
        b.Name = "Q";

        var result = Agenda.Compute(new[] { a, b }, Today);

        Assert.Equal(2, result.Days[0].Items.Count);
        Assert.Contains(result.Days[0].Items, i => i.Project.Name == "Q");
    }
}
