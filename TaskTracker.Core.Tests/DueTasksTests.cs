using TaskTracker.Core.Models;
using TaskTracker.Core.Services;

namespace TaskTracker.Core.Tests;

public class DueTasksTests
{
    private static readonly DateTime Today = new(2026, 7, 17);

    private static ProjectModel Project(string name, bool archived, params TaskModel[] tasks)
    {
        var p = new ProjectModel { Name = name, IsArchived = archived };
        foreach (var t in tasks) p.Tasks.Add(t);
        return p;
    }

    [Fact]
    public void Collect_BucketsByDueDate()
    {
        var project = Project("P", false,
            new TaskModel { Title = "late", DueDate = Today.AddDays(-1) },
            new TaskModel { Title = "today", DueDate = Today },
            new TaskModel { Title = "week", DueDate = Today.AddDays(7) },
            new TaskModel { Title = "later", DueDate = Today.AddDays(8) },
            new TaskModel { Title = "undated" },
            new TaskModel { Title = "done late", DueDate = Today.AddDays(-2), IsDone = true });

        var overview = DueTasks.Collect(new[] { project }, Today);

        Assert.Equal(new[] { "late" }, overview.Overdue.Select(i => i.Task.Title));
        Assert.Equal(new[] { "today" }, overview.DueToday.Select(i => i.Task.Title));
        Assert.Equal(new[] { "week" }, overview.DueThisWeek.Select(i => i.Task.Title));
        Assert.False(overview.IsEmpty);
    }

    [Fact]
    public void Collect_SkipsArchivedProjects_AndSortsByDueDate()
    {
        var visible = Project("A", false,
            new TaskModel { Title = "b", DueDate = Today.AddDays(-1) },
            new TaskModel { Title = "a", DueDate = Today.AddDays(-3) });
        var archived = Project("B", true,
            new TaskModel { Title = "hidden", DueDate = Today.AddDays(-5) });

        var overview = DueTasks.Collect(new[] { visible, archived }, Today);

        Assert.Equal(new[] { "a", "b" }, overview.Overdue.Select(i => i.Task.Title));
    }

    [Fact]
    public void Collect_Empty_WhenNothingDue()
    {
        var overview = DueTasks.Collect(new[] { Project("A", false, new TaskModel { Title = "t" }) }, Today);
        Assert.True(overview.IsEmpty);
    }
}
