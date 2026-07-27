using TaskTracker.Core.Models;
using TaskTracker.Core.Services;

namespace TaskTracker.Core.Tests;

public class BoardFilterTests
{
    private static readonly DateTime Today = new(2026, 7, 27);

    private static TaskModel Task(
        string title = "T",
        DateTime? due = null,
        TaskPriority priority = TaskPriority.Medium,
        bool done = false,
        params string[] labels)
    {
        var task = new TaskModel { Title = title, DueDate = due, Priority = priority, IsDone = done };
        foreach (var label in labels)
            task.Labels.Add(label);
        return task;
    }

    [Fact]
    public void NoneMatchesEverythingAndIsNotActive()
    {
        Assert.False(BoardFilter.None.IsActive);
        Assert.True(BoardFilter.None.Matches(Task(due: Today.AddYears(-1)), Today));
        Assert.True(BoardFilter.None.Matches(Task(), Today));
    }

    [Theory]
    [InlineData("bug", DueFilter.Any, null)]
    [InlineData(null, DueFilter.Overdue, null)]
    [InlineData(null, DueFilter.Any, TaskPriority.High)]
    public void AnySetDimensionMakesTheFilterActive(string? label, DueFilter due, TaskPriority? priority)
    {
        Assert.True(new BoardFilter(label, due, priority).IsActive);
    }

    [Fact]
    public void AnEmptyLabelIsNotAFilter()
    {
        // The "all labels" entry maps to null, but an empty string should behave the same
        // rather than matching nothing.
        Assert.False(new BoardFilter("").IsActive);
        Assert.True(new BoardFilter("").Matches(Task(), Today));
    }

    [Fact]
    public void LabelsMatchWithoutRegardToCase()
    {
        var filter = new BoardFilter("Bug");
        Assert.True(filter.Matches(Task(labels: "bug"), Today));
        Assert.False(filter.Matches(Task(labels: "feature"), Today));
        Assert.False(filter.Matches(Task(), Today));
    }

    [Theory]
    [InlineData(TaskPriority.High, true)]
    [InlineData(TaskPriority.Medium, false)]
    [InlineData(TaskPriority.Low, false)]
    public void PriorityMatchesExactly(TaskPriority priority, bool expected)
    {
        var filter = new BoardFilter(Priority: TaskPriority.High);
        Assert.Equal(expected, filter.Matches(Task(priority: priority), Today));
    }

    [Fact]
    public void OverdueIsPastAndStillOpen()
    {
        var filter = new BoardFilter(Due: DueFilter.Overdue);
        Assert.True(filter.Matches(Task(due: Today.AddDays(-1)), Today));
        Assert.False(filter.Matches(Task(due: Today), Today));
        Assert.False(filter.Matches(Task(due: Today.AddDays(1)), Today));
    }

    [Fact]
    public void ACompletedTaskIsNeverOverdueHoweverOldItsDate()
    {
        // Otherwise the done column would fill with red once its dates aged, which is
        // the opposite of what finishing something means.
        var filter = new BoardFilter(Due: DueFilter.Overdue);
        Assert.False(filter.Matches(Task(due: Today.AddDays(-30), done: true), Today));
    }

    [Fact]
    public void DueTodayIsTheCalendarDayNotTheInstant()
    {
        var filter = new BoardFilter(Due: DueFilter.DueToday);
        Assert.True(filter.Matches(Task(due: Today.AddHours(23)), Today));
        Assert.True(filter.Matches(Task(due: Today), Today));
        Assert.False(filter.Matches(Task(due: Today.AddDays(-1)), Today));
        Assert.False(filter.Matches(Task(due: Today.AddDays(1)), Today));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    [InlineData(-1, false)]
    public void DueThisWeekSpansTodayThroughSevenDaysOut(int offset, bool expected)
    {
        // Same boundaries as DueTasks.Collect, so the board and the home dashboard
        // cannot disagree about what "this week" covers.
        var filter = new BoardFilter(Due: DueFilter.DueThisWeek);
        Assert.Equal(expected, filter.Matches(Task(due: Today.AddDays(offset)), Today));
    }

    [Fact]
    public void ADatelessTaskMatchesOnlyTheNoDueDateBucket()
    {
        Assert.True(new BoardFilter(Due: DueFilter.NoDueDate).Matches(Task(), Today));
        Assert.False(new BoardFilter(Due: DueFilter.Overdue).Matches(Task(), Today));
        Assert.False(new BoardFilter(Due: DueFilter.DueToday).Matches(Task(), Today));
        Assert.False(new BoardFilter(Due: DueFilter.DueThisWeek).Matches(Task(), Today));
    }

    [Fact]
    public void ADatedTaskNeverMatchesNoDueDate()
    {
        Assert.False(new BoardFilter(Due: DueFilter.NoDueDate).Matches(Task(due: Today), Today));
    }

    [Fact]
    public void DimensionsCombineAsAnd()
    {
        var filter = new BoardFilter("bug", DueFilter.Overdue, TaskPriority.High);
        var match = Task(due: Today.AddDays(-2), priority: TaskPriority.High, labels: "bug");
        Assert.True(filter.Matches(match, Today));

        // Each dimension on its own is enough to exclude.
        Assert.False(filter.Matches(Task(due: Today.AddDays(-2), priority: TaskPriority.High, labels: "chore"), Today));
        Assert.False(filter.Matches(Task(due: Today.AddDays(-2), priority: TaskPriority.Low, labels: "bug"), Today));
        Assert.False(filter.Matches(Task(due: Today.AddDays(2), priority: TaskPriority.High, labels: "bug"), Today));
    }

    [Fact]
    public void TheTimeOfDayOnTodayIsIgnored()
    {
        // The board passes DateTime.Today, but a caller handing over a wall-clock time
        // must not shift the buckets by half a day.
        var afternoon = Today.AddHours(15).AddMinutes(30);
        Assert.True(new BoardFilter(Due: DueFilter.DueToday).Matches(Task(due: Today), afternoon));
        Assert.True(new BoardFilter(Due: DueFilter.Overdue).Matches(Task(due: Today.AddDays(-1)), afternoon));
    }
}
