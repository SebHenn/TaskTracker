using TaskTracker.Core.Models;
using TaskTracker.Core.Services;

namespace TaskTracker.Core.Tests;

public class TaskTransferTests
{
    private static ProjectModel NewProject(string name = "P")
    {
        var project = new ProjectModel { Name = name };
        foreach (var column in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(column);
        return project;
    }

    private static TaskModel AddTask(ProjectModel project, string title = "t")
    {
        var task = new TaskModel { Title = title, ColumnId = project.FirstColumn!.Id };
        project.Tasks.Add(task);
        return task;
    }

    [Fact]
    public void Move_TakesTheTaskAndItsHistoryAcross()
    {
        // Not a delete-and-recreate: the id, checklist, notes and tracked time survive,
        // which is the whole point of moving rather than retyping.
        var from = NewProject("A");
        var to = NewProject("B");
        var task = AddTask(from, "carry me");
        task.SubTasks.Add(new SubTaskModel { Title = "step" });
        task.Activity.Add(new ActivityEntry { Text = "note" });
        task.TrackedSeconds = 120;
        var id = task.Id;

        Assert.True(TaskTransfer.Move(from, to, task));

        Assert.Empty(from.Tasks);
        var moved = Assert.Single(to.Tasks);
        Assert.Equal(id, moved.Id);
        Assert.Single(moved.SubTasks);
        Assert.Single(moved.Activity);
        Assert.Equal(120, moved.TrackedSeconds);
    }

    [Fact]
    public void Move_DropsTheGitHubLink()
    {
        // The issue lives in the source project's repository. Carrying the number across
        // would make the destination's next sync close or rename an issue in a repository
        // it has nothing to do with.
        var from = NewProject("A");
        var to = NewProject("B");
        var task = AddTask(from);
        task.GitHubIssueNumber = 42;
        task.LastSyncedIssueState = "open";
        task.LastSyncedTitle = "t";
        task.GitHubIssueVanished = true;

        TaskTransfer.Move(from, to, task);

        Assert.Null(task.GitHubIssueNumber);
        Assert.Null(task.LastSyncedIssueState);
        Assert.Null(task.LastSyncedTitle);
        // Cleared too: this is not the "deleted on GitHub" case, and leaving it set would
        // stop the destination ever exporting the task.
        Assert.False(task.GitHubIssueVanished);
    }

    [Fact]
    public void Move_ClearsTheSortOrder()
    {
        var from = NewProject("A");
        var to = NewProject("B");
        var task = AddTask(from);
        task.SortOrder = 3;

        TaskTransfer.Move(from, to, task);

        Assert.Null(task.SortOrder);
    }

    [Fact]
    public void Move_LandsInTheDestinationsFirstColumnByDefault()
    {
        var from = NewProject("A");
        var to = NewProject("B");
        var task = AddTask(from);

        TaskTransfer.Move(from, to, task);

        Assert.Equal(to.FirstColumn!.Id, task.ColumnId);
        Assert.False(task.IsDone);
    }

    [Fact]
    public void Move_HonoursAnExplicitColumn()
    {
        var from = NewProject("A");
        var to = NewProject("B");
        var task = AddTask(from);

        TaskTransfer.Move(from, to, task, to.Columns[1]);

        Assert.Equal(to.Columns[1].Id, task.ColumnId);
    }

    [Fact]
    public void Move_OfADoneTaskLandsInADoneColumn()
    {
        var from = NewProject("A");
        var to = NewProject("B");
        var task = AddTask(from);
        TaskCompletion.SetDone(from, task, true);

        TaskTransfer.Move(from, to, task);

        Assert.True(task.IsDone);
        Assert.True(to.ColumnOf(task)!.IsDoneColumn);
    }

    [Fact]
    public void Move_IntoADoneColumnSpawnsARecurringOccurrenceInTheDestination()
    {
        // Landing in a done column completes the task like any other placement would,
        // and the successor belongs to the project the task now lives in.
        var from = NewProject("A");
        var to = NewProject("B");
        var task = AddTask(from, "chore");
        task.Recurrence = RecurrenceRules.Daily;
        task.DueDate = new DateTime(2026, 8, 1);

        TaskTransfer.Move(from, to, task, to.FirstDoneColumn);

        Assert.Empty(from.Tasks);
        Assert.Equal(2, to.Tasks.Count);
        Assert.Equal(new DateTime(2026, 8, 2), to.Tasks.Single(t => t.Id != task.Id).DueDate);
    }

    [Fact]
    public void Move_RepairsADestinationWithNoColumns()
    {
        var from = NewProject("A");
        var to = new ProjectModel { Name = "B" };
        var task = AddTask(from);

        Assert.True(TaskTransfer.Move(from, to, task));

        Assert.NotEmpty(to.Columns);
        Assert.NotNull(to.ColumnOf(task));
    }

    [Fact]
    public void Move_RefusesTheSameProjectAndAForeignTask()
    {
        var from = NewProject("A");
        var to = NewProject("B");
        var task = AddTask(from);

        Assert.False(TaskTransfer.Move(from, from, task));
        Assert.Single(from.Tasks);

        var stranger = new TaskModel { Title = "elsewhere" };
        Assert.False(TaskTransfer.Move(from, to, stranger));
        Assert.Empty(to.Tasks);
    }
}
