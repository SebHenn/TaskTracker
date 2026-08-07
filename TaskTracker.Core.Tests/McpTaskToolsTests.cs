using System.Text.Json;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;
using TaskTracker.Core.Storage;
using TaskTracker.Mcp;

namespace TaskTracker.Core.Tests;

/// <summary>
/// The MCP handlers over a real store in a temp directory, through the CreateStore seam.
///
/// These exist because the server is a second writer to the same files as the desktop app,
/// and nothing was checking that it honours the same rules. It did not: delete_task removed
/// the task from the list outright while the app moved it to the trash, so which client you
/// used decided whether a delete could be undone.
/// </summary>
public class McpTaskToolsTests : IDisposable
{
    private readonly string _dir;
    private readonly Func<ProjectStore> _originalFactory;

    public McpTaskToolsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tasktracker-mcp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _originalFactory = TaskTrackerTools.CreateStore;
        TaskTrackerTools.CreateStore = () => new ProjectStore(_dir);
    }

    public void Dispose()
    {
        // Restored so the seam cannot leak into another test class through the static.
        TaskTrackerTools.CreateStore = _originalFactory;
        Directory.Delete(_dir, recursive: true);
    }

    private ProjectStore Store => new(_dir);

    private (ProjectModel Project, TaskModel Task) Seed(string taskTitle = "t1")
    {
        var project = new ProjectModel { Name = "P" };
        foreach (var column in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(column);
        var task = new TaskModel { Title = taskTitle, ColumnId = project.FirstColumn!.Id };
        project.Tasks.Add(task);

        var store = Store;
        var data = store.Load();
        data.Projects.Add(project);
        store.Save(data);
        return (project, task);
    }

    private ProjectModel Reload() => Store.Load().Projects.Single();

    [Fact]
    public void DeleteTask_MovesToTrashRatherThanDestroying()
    {
        var (_, task) = Seed();

        TaskTrackerTools.DeleteTask(task.Id.ToString());

        var project = Reload();
        Assert.Empty(project.Tasks);
        var trashed = Assert.Single(project.Trash);
        Assert.Equal(task.Id, trashed.Task.Id);
        Assert.Equal("t1", trashed.Task.Title);
    }

    [Fact]
    public void DeleteTask_KeepsTheTaskRestorable()
    {
        var (_, task) = Seed();
        TaskTrackerTools.DeleteTask(task.Id.ToString());

        // The whole point of routing through the trash: it can come back.
        var store = Store;
        store.Update(data =>
        {
            var project = data.Projects.Single();
            Assert.True(Trash.Restore(project, project.Trash[0]));
        });

        var restored = Reload();
        Assert.Single(restored.Tasks);
        Assert.Equal(task.Id, restored.Tasks[0].Id);
        Assert.Empty(restored.Trash);
    }

    [Fact]
    public void DeleteTask_StopsARunningTimer()
    {
        // A timer left running would keep accruing against a task nobody can see.
        var (_, task) = Seed();
        Store.Update(data => TimeTracking.Start(data.Projects.Single().Tasks[0]));

        TaskTrackerTools.DeleteTask(task.Id.ToString());

        Assert.Null(Reload().Trash[0].Task.TimerStartedAtUtc);
    }

    [Fact]
    public void DeleteTask_ReportsWhenItStopsBeingRecoverable()
    {
        var (_, task) = Seed();

        var json = TaskTrackerTools.DeleteTask(task.Id.ToString());

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("deleted").GetBoolean());
        var until = doc.RootElement.GetProperty("recoverableUntilUtc").GetDateTime();
        Assert.True(until > DateTime.UtcNow.AddDays(29), $"expected ~30 days of retention, got {until:o}");
    }

    [Fact]
    public void DeleteTask_RejectsAnUnknownId()
    {
        Seed();
        Assert.ThrowsAny<Exception>(() => TaskTrackerTools.DeleteTask(Guid.NewGuid().ToString()));
        Assert.Single(Reload().Tasks);
    }

    [Fact]
    public void DeleteTask_LeavesOtherTasksAlone()
    {
        var (project, task) = Seed();
        Store.Update(data => data.Projects.Single().Tasks.Add(
            new TaskModel { Title = "keep me", ColumnId = project.FirstColumn!.Id }));

        TaskTrackerTools.DeleteTask(task.Id.ToString());

        var reloaded = Reload();
        Assert.Equal("keep me", Assert.Single(reloaded.Tasks).Title);
        Assert.Single(reloaded.Trash);
    }

    [Fact]
    public void MoveTask_DerivesDoneFromTheTargetColumn()
    {
        // Placement must go through MoveTaskToColumn, which is what keeps IsDone and the
        // completion timestamp coherent with the column the card actually sits in.
        var (project, task) = Seed();
        var done = project.Columns.First(c => c.IsDoneColumn);

        TaskTrackerTools.UpdateTask(task.Id.ToString(), column: done.Name);

        var moved = Reload().Tasks.Single();
        Assert.True(moved.IsDone);
        Assert.Equal(done.Id, moved.ColumnId);
    }

    [Fact]
    public void CreateTask_LandsInTheFirstColumnAndPersists()
    {
        var (project, _) = Seed();

        TaskTrackerTools.CreateTask(project.Id.ToString(), "fresh");

        var reloaded = Reload();
        var created = reloaded.Tasks.Single(t => t.Title == "fresh");
        Assert.Equal(reloaded.FirstColumn!.Id, created.ColumnId);
        Assert.False(created.IsDone);
    }
}
