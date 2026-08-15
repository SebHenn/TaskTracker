using System.Text.Json;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;
using TaskTracker.Core.Storage;

namespace TaskTracker.Core.Tests;

public class RecurrenceAndLaneSortTests
{
    private static ProjectModel ProjectWithColumns()
    {
        var project = new ProjectModel { Name = "P" };
        foreach (var c in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(c);
        return project;
    }

    [Theory]
    [InlineData("none", true)]
    [InlineData("daily", true)]
    [InlineData("WEEKLY", true)]
    [InlineData("Monthly", true)]
    [InlineData("fortnightly", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void RecurrenceRules_IsValid_AcceptsOnlyTheKnownRules(string? value, bool expected)
        => Assert.Equal(expected, RecurrenceRules.IsValid(value));

    [Theory]
    [InlineData("WEEKLY", RecurrenceRules.Weekly)]
    [InlineData("Daily", RecurrenceRules.Daily)]
    [InlineData("fortnightly", RecurrenceRules.None)]
    [InlineData(null, RecurrenceRules.None)]
    public void RecurrenceRules_Normalize_LowercasesKnownRulesAndDropsTheRest(string? value, string expected)
        => Assert.Equal(expected, RecurrenceRules.Normalize(value));

    [Fact]
    public void MonthlyRecurrence_ClampsToTheShorterMonth()
    {
        // Jan 31 + 1 month has no 31st to land on. AddMonths clamps to Feb 28; pinning it
        // down so a future change to the date maths cannot silently drift the due date.
        var project = ProjectWithColumns();
        var task = new TaskModel
        {
            Title = "Pay rent",
            DueDate = new DateTime(2026, 1, 31),
            Recurrence = RecurrenceRules.Monthly,
        };
        project.Tasks.Add(task);
        task.ColumnId = project.Columns[0].Id;

        project.MoveTaskToColumn(task, project.FirstDoneColumn!);

        Assert.Equal(new DateTime(2026, 2, 28), project.Tasks[1].DueDate);
    }

    [Fact]
    public void UnknownRecurrence_DoesNotSpawn()
    {
        // Belt and braces: ProjectStore.NormalizeTasks turns these into "none" on load,
        // but an in-memory value must not produce a phantom occurrence either.
        var project = ProjectWithColumns();
        var task = new TaskModel { Title = "T", DueDate = new DateTime(2026, 7, 17), Recurrence = "fortnightly" };
        project.Tasks.Add(task);
        task.ColumnId = project.Columns[0].Id;

        var spawned = project.MoveTaskToColumn(task, project.FirstDoneColumn!);

        Assert.Null(spawned);
        Assert.Single(project.Tasks);
    }

    [Fact]
    public void ZeroInterval_StillAdvancesByOne()
    {
        var project = ProjectWithColumns();
        var task = new TaskModel
        {
            Title = "T",
            DueDate = new DateTime(2026, 7, 17),
            Recurrence = RecurrenceRules.Daily,
            RecurrenceInterval = 0,
        };
        project.Tasks.Add(task);
        task.ColumnId = project.Columns[0].Id;

        project.MoveTaskToColumn(task, project.FirstDoneColumn!);

        Assert.Equal(new DateTime(2026, 7, 18), project.Tasks[1].DueDate);
    }

    [Theory]
    [InlineData(RecurrenceRules.Daily, 1, "2026-07-18")]
    [InlineData(RecurrenceRules.Daily, 3, "2026-07-20")]
    [InlineData(RecurrenceRules.Weekly, 1, "2026-07-24")]
    [InlineData(RecurrenceRules.Monthly, 1, "2026-08-17")]
    public void CompletingRecurringTask_SpawnsNextOccurrence(string rule, int interval, string expectedDue)
    {
        var project = ProjectWithColumns();
        var task = new TaskModel
        {
            Title = "Water plants",
            DueDate = new DateTime(2026, 7, 17),
            Recurrence = rule,
            RecurrenceInterval = interval,
            Priority = TaskPriority.High,
            GitHubIssueNumber = 42,
        };
        task.Labels.Add("chore");
        task.SubTasks.Add(new SubTaskModel { Title = "front", IsDone = true });
        project.Tasks.Add(task);
        task.ColumnId = project.Columns[0].Id;

        project.MoveTaskToColumn(task, project.FirstDoneColumn!);

        Assert.Equal(2, project.Tasks.Count);
        var next = project.Tasks[1];
        Assert.Equal("Water plants", next.Title);
        Assert.False(next.IsDone);
        Assert.Equal(DateTime.Parse(expectedDue), next.DueDate);
        Assert.Equal(rule, next.Recurrence);
        Assert.Equal(TaskPriority.High, next.Priority);
        Assert.Equal(new[] { "chore" }, next.Labels);
        var sub = Assert.Single(next.SubTasks);
        Assert.False(sub.IsDone);                       // checklist resets
        Assert.Null(next.GitHubIssueNumber);            // link is not inherited
        Assert.Equal(project.FirstColumn!.Id, next.ColumnId);
        Assert.NotEqual(task.Id, next.Id);
    }

    [Fact]
    public void NonRecurringOrAlreadyDone_DoesNotSpawn()
    {
        var project = ProjectWithColumns();
        var task = new TaskModel { Title = "once", DueDate = DateTime.Today };
        project.Tasks.Add(task);

        project.MoveTaskToColumn(task, project.FirstDoneColumn!);
        Assert.Single(project.Tasks);

        var recurring = new TaskModel { Title = "r", Recurrence = RecurrenceRules.Daily, DueDate = DateTime.Today };
        project.Tasks.Add(recurring);
        project.MoveTaskToColumn(recurring, project.FirstDoneColumn!);
        Assert.Equal(3, project.Tasks.Count);

        // Re-moving within done columns must not spawn again.
        project.MoveTaskToColumn(recurring, project.FirstDoneColumn!);
        Assert.Equal(3, project.Tasks.Count);
    }

    [Fact]
    public void RecurringTaskWithoutDueDate_UsesTodayAsBase()
    {
        var project = ProjectWithColumns();
        var task = new TaskModel { Title = "r", Recurrence = RecurrenceRules.Weekly };
        project.Tasks.Add(task);

        var spawned = Recurrence.SpawnNextIfRecurring(project, task, new DateTime(2026, 7, 17));

        Assert.NotNull(spawned);
        Assert.Equal(new DateTime(2026, 7, 24), spawned!.DueDate);
    }

    [Fact]
    public void LaneSort_ExplicitOrderBeforeAutoSort()
    {
        var manualSecond = new TaskModel { Title = "manual-2", SortOrder = 2 };
        var manualFirst = new TaskModel { Title = "manual-1", SortOrder = 1 };
        var autoHigh = new TaskModel { Title = "auto-high", Priority = TaskPriority.High };
        var autoLow = new TaskModel { Title = "auto-low", Priority = TaskPriority.Low };

        var sorted = LaneSort.Apply(new[] { autoLow, manualSecond, autoHigh, manualFirst }).ToList();

        Assert.Equal(new[] { "manual-1", "manual-2", "auto-high", "auto-low" }, sorted.Select(t => t.Title));
    }

    [Fact]
    public void NewFields_RoundTrip_AndLegacyJsonDefaults()
    {
        var task = new TaskModel
        {
            Title = "t",
            SortOrder = 3.5,
            Recurrence = RecurrenceRules.Weekly,
            RecurrenceInterval = 2,
            TrackedSeconds = 90,
        };
        task.Activity.Add(new ActivityEntry { Text = "note" });

        var restored = JsonSerializer.Deserialize<TaskModel>(
            JsonSerializer.Serialize(task, CoreJson.Options), CoreJson.Options)!;
        Assert.Equal(3.5, restored.SortOrder);
        Assert.Equal(RecurrenceRules.Weekly, restored.Recurrence);
        Assert.Equal(2, restored.RecurrenceInterval);
        Assert.Equal(90, restored.TrackedSeconds);
        Assert.Equal("note", Assert.Single(restored.Activity).Text);

        var legacy = JsonSerializer.Deserialize<TaskModel>("""{ "Title": "old" }""", CoreJson.Options)!;
        Assert.Null(legacy.SortOrder);
        Assert.Equal(RecurrenceRules.None, legacy.Recurrence);
        Assert.False(legacy.IsRecurring);
        Assert.Empty(legacy.Activity);
        Assert.Equal(0, legacy.TrackedSeconds);

        var column = JsonSerializer.Deserialize<BoardColumn>("""{ "Name": "c" }""", CoreJson.Options)!;
        Assert.Null(column.WipLimit);
        var project = JsonSerializer.Deserialize<ProjectModel>("""{ "Name": "p" }""", CoreJson.Options)!;
        Assert.Null(project.Color);
    }
}
