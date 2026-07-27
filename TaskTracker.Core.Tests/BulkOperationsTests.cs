using TaskTracker.Core.Models;
using TaskTracker.Core.Services;

namespace TaskTracker.Core.Tests;

public class BulkOperationsTests
{
    private static readonly DateTime Now = new(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);

    private static ProjectModel Project()
    {
        var project = new ProjectModel { Name = "P" };
        foreach (var column in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(column);
        return project;
    }

    private static TaskModel AddTask(ProjectModel project, string title, BoardColumn? column = null)
    {
        var target = column ?? project.FirstColumn!;
        var task = new TaskModel { Title = title, ColumnId = target.Id, IsDone = target.IsDoneColumn };
        project.Tasks.Add(task);
        return task;
    }

    private static string[] TitlesIn(ProjectModel project, BoardColumn column) =>
        BoardProjection.Compute(project).Lanes
            .First(lane => lane.Column.Id == column.Id)
            .Tasks.Select(task => task.Title).ToArray();

    [Fact]
    public void MovingABatchLandsItAllInTheTargetColumn()
    {
        var project = Project();
        var done = project.FirstDoneColumn!;
        var a = AddTask(project, "a");
        var b = AddTask(project, "b");
        AddTask(project, "c");

        Assert.Equal(2, BulkOperations.MoveAll(project, [a, b], done));

        Assert.Equal(new[] { "a", "b" }, TitlesIn(project, done));
        Assert.Equal(new[] { "c" }, TitlesIn(project, project.FirstColumn!));
    }

    [Fact]
    public void MovingToADoneColumnMarksTheWholeBatchDone()
    {
        // Routed through MoveTaskToColumn, so IsDone and the completion timestamp are
        // derived rather than set by hand.
        var project = Project();
        var done = project.FirstDoneColumn!;
        var a = AddTask(project, "a");
        var b = AddTask(project, "b");

        BulkOperations.MoveAll(project, [a, b], done);

        Assert.All(new[] { a, b }, task =>
        {
            Assert.True(task.IsDone);
            Assert.NotNull(task.CompletedAtUtc);
        });
    }

    [Fact]
    public void TheBatchKeepsItsOwnOrderAndGoesAfterWhatWasAlreadyThere()
    {
        var project = Project();
        var done = project.FirstDoneColumn!;
        var settled = AddTask(project, "settled", done);
        settled.SortOrder = 1;
        var first = AddTask(project, "first");
        var second = AddTask(project, "second");

        BulkOperations.MoveAll(project, [first, second], done);

        Assert.Equal(new[] { "settled", "first", "second" }, TitlesIn(project, done));
    }

    [Fact]
    public void EveryTaskInTheColumnEndsUpWithADistinctOrder()
    {
        var project = Project();
        var done = project.FirstDoneColumn!;
        AddTask(project, "settled", done);
        var a = AddTask(project, "a");
        var b = AddTask(project, "b");

        BulkOperations.MoveAll(project, [a, b], done);

        var orders = project.Tasks
            .Where(task => project.ColumnOf(task)!.Id == done.Id)
            .Select(task => task.SortOrder)
            .ToList();
        Assert.All(orders, order => Assert.NotNull(order));
        Assert.Equal(orders.Count, orders.Distinct().Count());
    }

    [Fact]
    public void ARecurringTaskInTheBatchSpawnsItsNextOccurrenceAndTheSpawnStaysPut()
    {
        // Completing a recurring task adds a new one to project.Tasks mid-move. If the
        // batch were enumerated lazily off the board it would sweep the new copy into
        // the done column too, completing a task that was just created.
        var project = Project();
        var done = project.FirstDoneColumn!;
        var repeating = AddTask(project, "weekly");
        repeating.Recurrence = RecurrenceRules.Weekly;
        repeating.DueDate = Now;

        BulkOperations.MoveAll(project, project.Tasks.Where(t => !t.IsDone), done);

        Assert.Equal(new[] { "weekly" }, TitlesIn(project, done));
        var spawned = Assert.Single(project.Tasks.Where(task => !task.IsDone));
        Assert.Equal("weekly", spawned.Title);
        Assert.NotEqual(repeating.Id, spawned.Id);
    }

    [Fact]
    public void MovingAnEmptyBatchDoesNothing()
    {
        var project = Project();
        AddTask(project, "a");

        Assert.Equal(0, BulkOperations.MoveAll(project, [], project.FirstDoneColumn!));

        Assert.Empty(TitlesIn(project, project.FirstDoneColumn!));
    }

    [Fact]
    public void MovingToTheColumnATaskIsAlreadyInIsHarmless()
    {
        var project = Project();
        var first = project.FirstColumn!;
        var a = AddTask(project, "a");
        var b = AddTask(project, "b");

        BulkOperations.MoveAll(project, [a, b], first);

        Assert.Equal(new[] { "a", "b" }, TitlesIn(project, first));
        Assert.Equal(2, project.Tasks.Count);
    }

    [Fact]
    public void DeletingABatchSendsEveryTaskToTheTrash()
    {
        var project = Project();
        var a = AddTask(project, "a");
        var b = AddTask(project, "b");
        AddTask(project, "c");

        var entries = BulkOperations.DeleteAll(project, [a, b], Now);

        Assert.Equal(2, entries.Count);
        Assert.Equal(new[] { "c" }, project.Tasks.Select(task => task.Title));
        Assert.Equal(2, project.Trash.Count);
    }

    [Fact]
    public void TheReturnedEntriesAreInTheOrderTheyWereGiven()
    {
        // That is the order the undo bar restores them in.
        var project = Project();
        var a = AddTask(project, "a");
        var b = AddTask(project, "b");

        var entries = BulkOperations.DeleteAll(project, [a, b], Now);

        Assert.Equal(new[] { "a", "b" }, entries.Select(entry => entry.Task.Title));
    }

    [Fact]
    public void ADeletedBatchCanBeRestoredWhole()
    {
        var project = Project();
        var a = AddTask(project, "a");
        var b = AddTask(project, "b");

        var entries = BulkOperations.DeleteAll(project, [a, b], Now);
        foreach (var entry in entries)
            Assert.True(Trash.Restore(project, entry));

        Assert.Equal(new[] { "a", "b" }, project.Tasks.Select(task => task.Title).Order());
        Assert.Empty(project.Trash);
    }

    [Fact]
    public void DeletingEveryTaskInAProjectLeavesTheColumnsAlone()
    {
        var project = Project();
        var columns = project.Columns.Count;
        AddTask(project, "a");
        AddTask(project, "b");

        BulkOperations.DeleteAll(project, project.Tasks, Now);

        Assert.Empty(project.Tasks);
        Assert.Equal(columns, project.Columns.Count);
    }
}
