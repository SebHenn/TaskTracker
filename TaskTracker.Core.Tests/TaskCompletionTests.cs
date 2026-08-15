using TaskTracker.Core.Models;
using TaskTracker.Core.Services;

namespace TaskTracker.Core.Tests;

public class TaskCompletionTests
{
    private static ProjectModel NewProject()
    {
        var project = new ProjectModel { Name = "P" };
        foreach (var column in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(column);
        return project;
    }

    private static TaskModel AddTask(ProjectModel project, string recurrence = RecurrenceRules.None)
    {
        var task = new TaskModel
        {
            Title = "Water the plants",
            Recurrence = recurrence,
            DueDate = new DateTime(2026, 8, 1),
            ColumnId = project.FirstColumn!.Id,
        };
        project.Tasks.Add(task);
        return task;
    }

    [Fact]
    public void SetDone_SpawnsTheNextOccurrence_ForARecurringTask()
    {
        // The bug this type exists for: completing a recurring task anywhere other than
        // the board used to skip recurrence entirely.
        var project = NewProject();
        var task = AddTask(project, RecurrenceRules.Weekly);

        var spawned = TaskCompletion.SetDone(project, task, true);

        Assert.NotNull(spawned);
        Assert.Equal("Water the plants", spawned!.Title);
        Assert.Equal(new DateTime(2026, 8, 8), spawned.DueDate);
        Assert.False(spawned.IsDone);
        Assert.Equal(2, project.Tasks.Count);
    }

    [Fact]
    public void SetDone_MovesTheTaskToADoneColumn_NotJustTheFlag()
    {
        var project = NewProject();
        var task = AddTask(project);

        TaskCompletion.SetDone(project, task, true);

        Assert.True(task.IsDone);
        Assert.Equal(project.FirstDoneColumn!.Id, task.ColumnId);
        Assert.True(project.ColumnOf(task)!.IsDoneColumn);
    }

    [Fact]
    public void SetDone_False_ReopensIntoAnOpenColumn()
    {
        var project = NewProject();
        var task = AddTask(project);
        TaskCompletion.SetDone(project, task, true);
        project.Tasks.Clear();
        project.Tasks.Add(task); // drop the spawn noise; this task is not recurring anyway

        var spawned = TaskCompletion.SetDone(project, task, false);

        Assert.Null(spawned);
        Assert.False(task.IsDone);
        Assert.False(project.ColumnOf(task)!.IsDoneColumn);
    }

    [Fact]
    public void SetDone_IsANoOp_WhenAlreadyInThatState()
    {
        // Guards against double-spawning when two code paths both "complete" a task,
        // and against churning CompletedAtUtc on every no-op sync.
        var project = NewProject();
        var task = AddTask(project, RecurrenceRules.Daily);
        TaskCompletion.SetDone(project, task, true);
        var completedAt = task.CompletedAtUtc;
        var countAfterFirst = project.Tasks.Count;

        var spawned = TaskCompletion.SetDone(project, task, true);

        Assert.Null(spawned);
        Assert.Equal(countAfterFirst, project.Tasks.Count);
        Assert.Equal(completedAt, task.CompletedAtUtc);
    }

    [Fact]
    public void SetDone_RepairsTheBoard_WhenThereIsNoUsableColumn()
    {
        // The old fallback poked IsDone directly here, leaving ColumnId dangling and
        // skipping recurrence. Repairing is what the rest of the codebase does.
        var project = new ProjectModel { Name = "P" };
        var task = new TaskModel { Title = "Orphan", Recurrence = RecurrenceRules.Daily, DueDate = new DateTime(2026, 8, 1) };
        project.Tasks.Add(task);
        Assert.Empty(project.Columns);

        var spawned = TaskCompletion.SetDone(project, task, true);

        Assert.NotEmpty(project.Columns);
        Assert.True(task.IsDone);
        Assert.NotNull(task.ColumnId);
        Assert.True(project.ColumnOf(task)!.IsDoneColumn);
        Assert.NotNull(spawned);
    }

    [Fact]
    public void SetDone_DoesNotSpawn_WhenReopeningARecurringTask()
    {
        var project = NewProject();
        var task = AddTask(project, RecurrenceRules.Weekly);
        TaskCompletion.SetDone(project, task, true);
        var afterComplete = project.Tasks.Count;

        var spawned = TaskCompletion.SetDone(project, task, false);

        Assert.Null(spawned);
        Assert.Equal(afterComplete, project.Tasks.Count);
    }
}
