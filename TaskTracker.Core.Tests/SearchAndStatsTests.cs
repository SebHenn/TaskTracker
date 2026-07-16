using System.Collections.ObjectModel;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;

namespace TaskTracker.Core.Tests;

public class SearchAndStatsTests
{
    private static ProjectModel Project(string name, bool archived = false, params TaskModel[] tasks)
    {
        var p = new ProjectModel { Name = name, IsArchived = archived };
        foreach (var t in tasks) p.Tasks.Add(t);
        return p;
    }

    private static TaskModel Task(string title, bool done = false, string? label = null, string description = "")
    {
        var t = new TaskModel { Title = title, IsDone = done, Description = description };
        if (label != null) t.Labels.Add(label);
        return t;
    }

    [Fact]
    public void Search_MatchesTitleDescriptionAndLabels_CaseInsensitive()
    {
        var projects = new[]
        {
            Project("A", false, Task("Fix login BUG"), Task("Write docs", description: "bug tracker docs")),
            Project("B", false, Task("Refactor", label: "Bugfix")),
        };

        var results = TaskSearch.Search(projects, "bug");

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void Search_SkipsArchivedProjects_UnlessRequested()
    {
        var projects = new[] { Project("A", archived: true, Task("secret bug")) };

        Assert.Empty(TaskSearch.Search(projects, "bug"));
        Assert.Single(TaskSearch.Search(projects, "bug", includeArchived: true));
    }

    [Fact]
    public void Search_FiltersDoneAndLabel()
    {
        var projects = new[]
        {
            Project("A", false,
                Task("one", done: true, label: "ui"),
                Task("two", done: false, label: "ui"),
                Task("three", done: false, label: "api")),
        };

        Assert.Equal(2, TaskSearch.Search(projects, "", label: "UI").Count);
        Assert.Single(TaskSearch.Search(projects, "", label: "ui", includeDone: false));
        // Empty query with no label returns everything (browse mode).
        Assert.Equal(3, TaskSearch.Search(projects, "").Count);
    }

    [Fact]
    public void Stats_CountsAndCompletionPercent()
    {
        var project = Project("A", false, Task("a"), Task("b", done: true), Task("c", done: true), Task("d"));

        var stats = ProjectStats.Compute(project);

        Assert.Equal(2, stats.OpenCount);
        Assert.Equal(2, stats.DoneCount);
        Assert.Equal(50, stats.CompletionPercent);
    }

    [Fact]
    public void Stats_EmptyProject_HasZeroPercent()
    {
        var stats = ProjectStats.Compute(Project("A"));
        Assert.Equal(0, stats.CompletionPercent);
        Assert.Equal(ProjectStats.Weeks, stats.DonePerWeek.Count);
    }

    [Fact]
    public void Stats_BucketsCompletionsIntoWeeks()
    {
        // Fixed "today": Wednesday 2026-07-15 → current week starts Monday 2026-07-13.
        var today = new DateTime(2026, 7, 15);
        var project = Project("A");

        var thisWeek = new TaskModel { Title = "recent", IsDone = true };
        thisWeek.CompletedAtUtc = new DateTime(2026, 7, 14);
        var lastWeek = new TaskModel { Title = "older", IsDone = true };
        lastWeek.CompletedAtUtc = new DateTime(2026, 7, 8);
        var ancient = new TaskModel { Title = "ancient", IsDone = true };
        ancient.CompletedAtUtc = new DateTime(2020, 1, 1);
        var untimestamped = new TaskModel { Title = "legacy", IsDone = true };
        untimestamped.CompletedAtUtc = null;

        foreach (var t in new[] { thisWeek, lastWeek, ancient, untimestamped })
            project.Tasks.Add(t);

        var stats = ProjectStats.Compute(project, today);

        Assert.Equal(4, stats.DoneCount);
        Assert.Equal(new DateTime(2026, 7, 13), stats.DonePerWeek[^1].WeekStart);
        Assert.Equal(1, stats.DonePerWeek[^1].Count);   // thisWeek
        Assert.Equal(1, stats.DonePerWeek[^2].Count);   // lastWeek
        Assert.Equal(0, stats.DonePerWeek[0].Count);    // ancient falls outside the window
    }

    [Fact]
    public void Stats_CountsOverdueTasks()
    {
        var overdue = new TaskModel { Title = "late", DueDate = DateTime.Today.AddDays(-1) };
        var onTime = new TaskModel { Title = "ok", DueDate = DateTime.Today.AddDays(1) };
        var doneLate = new TaskModel { Title = "done", DueDate = DateTime.Today.AddDays(-1), IsDone = true };
        var project = Project("A", false, overdue, onTime, doneLate);

        Assert.True(overdue.IsOverdue);
        Assert.False(onTime.IsOverdue);
        Assert.False(doneLate.IsOverdue);
        Assert.Equal(1, ProjectStats.Compute(project).OverdueCount);
    }
}
