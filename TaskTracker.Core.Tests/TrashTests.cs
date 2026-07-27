using TaskTracker.Core.Models;
using TaskTracker.Core.Services;
using TaskTracker.Core.Storage;

namespace TaskTracker.Core.Tests;

public class TrashTests
{
    private static readonly DateTime Now = new(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);

    private static ProjectModel Project()
    {
        var project = new ProjectModel { Name = "P" };
        foreach (var column in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(column);
        return project;
    }

    private static TaskModel AddTask(ProjectModel project, string title = "T", BoardColumn? column = null)
    {
        var task = new TaskModel { Title = title, ColumnId = (column ?? project.FirstColumn)!.Id };
        project.Tasks.Add(task);
        return task;
    }

    [Fact]
    public void DeletingMovesTheTaskOffTheBoardAndIntoTheTrash()
    {
        var project = Project();
        var task = AddTask(project);

        var entry = Trash.Delete(project, task, Now);

        Assert.Empty(project.Tasks);
        Assert.Same(task, Assert.Single(project.Trash).Task);
        Assert.Equal(Now, entry.DeletedAtUtc);
    }

    [Fact]
    public void DeletingKeepsEverythingNeededToPutItBack()
    {
        // A summary would make restore quietly lossy; the whole model is retained.
        var project = Project();
        var task = AddTask(project);
        task.Labels.Add("bug");
        task.SubTasks.Add(new SubTaskModel { Title = "step", IsDone = true });
        task.Activity.Add(new ActivityEntry { Text = "note" });
        task.TrackedSeconds = 120;
        task.Description = "why";

        Trash.Delete(project, task, Now);
        Trash.Restore(project, project.Trash[0]);

        var restored = Assert.Single(project.Tasks);
        Assert.Equal("why", restored.Description);
        Assert.Equal(new[] { "bug" }, restored.Labels);
        Assert.Equal("1/1", restored.SubTaskProgress);
        Assert.Single(restored.Activity);
        Assert.Equal(120, restored.TrackedSeconds);
    }

    [Fact]
    public void ARunningTimerIsStoppedOnTheWayIn()
    {
        // Left running, it would keep accruing against a task nobody can see, and a
        // restore weeks later would credit the whole interval.
        var project = Project();
        var task = AddTask(project);
        TimeTracking.Start(task, Now.AddMinutes(-10));

        Trash.Delete(project, task, Now);

        Assert.Null(task.TimerStartedAtUtc);
        Assert.Equal(600, task.TrackedSeconds, 3);
    }

    [Fact]
    public void NewestDeletionIsFirst()
    {
        var project = Project();
        var older = AddTask(project, "older");
        var newer = AddTask(project, "newer");

        Trash.Delete(project, older, Now.AddHours(-1));
        Trash.Delete(project, newer, Now);

        Assert.Equal(new[] { "newer", "older" }, project.Trash.Select(t => t.Task.Title));
    }

    [Fact]
    public void RestoringPutsTheTaskBackInItsColumn()
    {
        var project = Project();
        var done = project.FirstDoneColumn!;
        var task = AddTask(project, column: done);
        task.IsDone = true;

        Trash.Delete(project, task, Now);
        Assert.True(Trash.Restore(project, project.Trash[0]));

        Assert.Empty(project.Trash);
        Assert.Equal(done.Id, project.ColumnOf(Assert.Single(project.Tasks))!.Id);
    }

    [Fact]
    public void RestoringIntoADeletedColumnLandsSomewhereReal()
    {
        var project = Project();
        var extra = new BoardColumn { Name = "Review" };
        project.Columns.Add(extra);
        var task = AddTask(project, column: extra);

        Trash.Delete(project, task, Now);
        project.Columns.Remove(extra);
        Assert.True(Trash.Restore(project, project.Trash[0]));

        var restored = Assert.Single(project.Tasks);
        Assert.NotNull(project.ColumnOf(restored));
        Assert.Contains(project.Columns, column => column.Id == restored.ColumnId);
    }

    [Fact]
    public void RestoringAnEntryThatIsNoLongerThereReportsFailure()
    {
        var project = Project();
        var task = AddTask(project);
        var entry = Trash.Delete(project, task, Now);

        Assert.True(Trash.Restore(project, entry));
        Assert.False(Trash.Restore(project, entry));
        Assert.Single(project.Tasks);
    }

    [Fact]
    public void PurgeDropsEntriesPastRetentionAndKeepsTheRest()
    {
        var project = Project();
        Trash.Delete(project, AddTask(project, "ancient"), Now - Trash.Retention.Add(TimeSpan.FromDays(1)));
        Trash.Delete(project, AddTask(project, "recent"), Now.AddDays(-1));

        var removed = Trash.Purge(project, Now);

        Assert.Equal(1, removed);
        Assert.Equal(new[] { "recent" }, project.Trash.Select(t => t.Task.Title));
    }

    [Fact]
    public void AnEntryExactlyAtTheRetentionEdgeSurvives()
    {
        var project = Project();
        Trash.Delete(project, AddTask(project, "edge"), Now - Trash.Retention);

        Assert.Equal(0, Trash.Purge(project, Now));
        Assert.Single(project.Trash);
    }

    [Fact]
    public void TheTrashIsCappedSoOneBulkDeleteCannotBloatTheFile()
    {
        var project = Project();
        for (var i = 0; i < Trash.MaxEntries + 25; i++)
            Trash.Delete(project, AddTask(project, $"t{i}"), Now.AddSeconds(i));

        Assert.Equal(Trash.MaxEntries, project.Trash.Count);
        // Newest kept, oldest dropped.
        Assert.Equal($"t{Trash.MaxEntries + 24}", project.Trash[0].Task.Title);
        Assert.DoesNotContain(project.Trash, entry => entry.Task.Title == "t0");
    }

    [Fact]
    public void EmptyingDiscardsEverything()
    {
        var project = Project();
        Trash.Delete(project, AddTask(project, "a"), Now);
        Trash.Delete(project, AddTask(project, "b"), Now);

        Trash.Empty(project);

        Assert.Empty(project.Trash);
    }

    [Fact]
    public void PurgingAStoreCoversEveryProject()
    {
        var data = new StoreData();
        for (var i = 0; i < 3; i++)
        {
            var project = Project();
            project.Name = $"P{i}";
            Trash.Delete(project, AddTask(project), Now - Trash.Retention.Add(TimeSpan.FromDays(1)));
            data.Projects.Add(project);
        }

        Assert.Equal(3, Trash.Purge(data, Now));
        Assert.All(data.Projects, project => Assert.Empty(project.Trash));
    }

    [Fact]
    public void ATrashedTaskIsNotCountedByTheBoardOrTheStats()
    {
        // It is off Tasks entirely, so nothing that reads Tasks has to learn about it.
        var project = Project();
        AddTask(project, "kept");
        Trash.Delete(project, AddTask(project, "gone"), Now);

        var board = BoardProjection.Compute(project);

        Assert.Equal(new[] { "kept" }, board.Lanes.SelectMany(l => l.Tasks).Select(t => t.Title));
        Assert.Equal(1, ProjectStats.Compute(project).OpenCount);
    }
}
