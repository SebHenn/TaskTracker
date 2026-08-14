using TaskTracker.Core.Models;
using TaskTracker.Core.Services;

namespace TaskTracker.Core.Tests;

/// <summary>
/// These rules used to live in the desktop's ColumnsViewModel, where CI cannot reach
/// them — the WPF head is only compile-checked. Both heads call this now, so the
/// invariants are testable on Linux.
/// </summary>
public class ColumnOperationsTests
{
    private static ProjectModel NewProject()
    {
        var project = new ProjectModel { Name = "P" };
        foreach (var column in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(column);
        return project;
    }

    [Fact]
    public void Add_AppendsByDefaultAndInsertsAtAPosition()
    {
        var project = NewProject();

        ColumnOperations.Add(project, "Last");
        ColumnOperations.Add(project, "First", position: 0);

        Assert.Equal("First", project.Columns[0].Name);
        Assert.Equal("Last", project.Columns[^1].Name);
    }

    [Fact]
    public void Add_ClampsAnOutOfRangePosition()
    {
        var project = NewProject();

        ColumnOperations.Add(project, "Way out", position: 99);

        Assert.Equal("Way out", project.Columns[^1].Name);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-5, null)]
    [InlineData(3, 3)]
    public void Add_TreatsNonPositiveWipLimitsAsNoLimit(int given, int? expected)
    {
        var project = NewProject();

        var column = ColumnOperations.Add(project, "C", wipLimit: given);

        Assert.Equal(expected, column.WipLimit);
    }

    [Fact]
    public void Delete_RehomesTasksToTheFirstOpenColumn()
    {
        var project = NewProject();
        var doomed = project.Columns[1];
        var task = new TaskModel { Title = "t", ColumnId = doomed.Id };
        project.Tasks.Add(task);

        var refusal = ColumnOperations.Delete(project, doomed);

        Assert.Null(refusal);
        Assert.DoesNotContain(doomed, project.Columns);
        Assert.Equal(project.FirstColumn!.Id, task.ColumnId);
    }

    [Fact]
    public void Delete_RehomesToAnExplicitTarget()
    {
        var project = NewProject();
        var doomed = project.Columns[0];
        var target = project.Columns[1];
        var task = new TaskModel { Title = "t", ColumnId = doomed.Id };
        project.Tasks.Add(task);

        ColumnOperations.Delete(project, doomed, target);

        Assert.Equal(target.Id, task.ColumnId);
    }

    [Fact]
    public void Delete_RefusesTheLastDoneColumn()
    {
        var project = NewProject();
        var done = project.FirstDoneColumn!;

        var refusal = ColumnOperations.Delete(project, done);

        Assert.Equal(ColumnOperations.Refusal.LastDoneColumn, refusal);
        Assert.Contains(done, project.Columns);
    }

    [Fact]
    public void Delete_RefusesTheLastColumn()
    {
        var project = new ProjectModel { Name = "P" };
        var only = new BoardColumn { Name = "Only", IsDoneColumn = true };
        project.Columns.Add(only);

        Assert.Equal(ColumnOperations.Refusal.LastColumn, ColumnOperations.Delete(project, only));
    }

    [Fact]
    public void Update_MakingAColumnDone_CompletesTheTasksInIt()
    {
        var project = NewProject();
        var column = project.Columns[0];
        var task = new TaskModel { Title = "t", ColumnId = column.Id };
        project.Tasks.Add(task);

        ColumnOperations.Update(project, column, isDoneColumn: true);

        Assert.True(task.IsDone);
    }

    [Fact]
    public void Update_MakingAColumnDone_SpawnsRecurringOccurrences()
    {
        // The done flag changing is a completion like any other, so it has to go through
        // the same placement path rather than setting IsDone behind recurrence's back.
        var project = NewProject();
        var column = project.Columns[0];
        var task = new TaskModel
        {
            Title = "chore",
            ColumnId = column.Id,
            Recurrence = RecurrenceRules.Daily,
            DueDate = new DateTime(2026, 8, 1),
        };
        project.Tasks.Add(task);

        ColumnOperations.Update(project, column, isDoneColumn: true);

        Assert.Equal(2, project.Tasks.Count);
        Assert.Equal(new DateTime(2026, 8, 2), project.Tasks[1].DueDate);
    }

    [Fact]
    public void Update_RefusesToClearTheOnlyDoneFlag()
    {
        var project = NewProject();
        var done = project.FirstDoneColumn!;

        var refusal = ColumnOperations.Update(project, done, isDoneColumn: false);

        Assert.Equal(ColumnOperations.Refusal.LastDoneColumn, refusal);
        Assert.True(done.IsDoneColumn);
    }

    [Fact]
    public void Reorder_RequiresAPermutation()
    {
        var project = NewProject();
        var ids = project.Columns.Select(c => c.Id).ToList();

        Assert.NotNull(ColumnOperations.Reorder(project, ids.Take(2).ToList()));
        Assert.NotNull(ColumnOperations.Reorder(project, ids.Append(Guid.NewGuid()).ToList()));
        Assert.NotNull(ColumnOperations.Reorder(project, new List<Guid> { ids[0], ids[0], ids[1] }));

        var reversed = Enumerable.Reverse(ids).ToList();
        Assert.Null(ColumnOperations.Reorder(project, reversed));
        Assert.Equal(reversed, project.Columns.Select(c => c.Id).ToList());
    }

    [Fact]
    public void Move_ClampsAtBothEnds()
    {
        var project = NewProject();
        var first = project.Columns[0];
        var last = project.Columns[^1];

        ColumnOperations.Move(project, first, -1);
        Assert.Equal(first, project.Columns[0]);

        ColumnOperations.Move(project, last, +1);
        Assert.Equal(last, project.Columns[^1]);

        ColumnOperations.Move(project, first, +1);
        Assert.Equal(first, project.Columns[1]);
    }
}
