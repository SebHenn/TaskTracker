using System.Collections.ObjectModel;
using TaskTracker.Core.GitHub;
using TaskTracker.Core.Models;
using TaskTracker.Core.Storage;

namespace TaskTracker.Core.Tests;

public class BoardColumnTests
{
    private static StoreData DataWith(params ProjectModel[] projects)
        => new() { Projects = new ObservableCollection<ProjectModel>(projects) };

    [Fact]
    public void Normalize_ProjectWithoutColumns_GetsMigrationColumnsAndTasksAreAssigned()
    {
        var project = new ProjectModel { Name = "P" };
        var open = new TaskModel { Title = "open", IsDone = false };
        var done = new TaskModel { Title = "done", IsDone = true };
        project.Tasks.Add(open);
        project.Tasks.Add(done);

        ProjectStore.NormalizeColumns(DataWith(project));

        Assert.Equal(2, project.Columns.Count);
        Assert.Equal("In Progress", project.Columns[0].Name);
        Assert.False(project.Columns[0].IsDoneColumn);
        Assert.True(project.Columns[1].IsDoneColumn);
        Assert.Equal(project.Columns[0].Id, open.ColumnId);
        Assert.Equal(project.Columns[1].Id, done.ColumnId);
    }

    [Fact]
    public void Normalize_NoDoneColumn_AppendsOne()
    {
        var project = new ProjectModel { Name = "P" };
        project.Columns.Add(new BoardColumn { Name = "Only" });

        ProjectStore.NormalizeColumns(DataWith(project));

        Assert.Equal(2, project.Columns.Count);
        Assert.True(project.Columns[1].IsDoneColumn);
    }

    [Fact]
    public void Normalize_StaleColumnId_ReassignedByDoneState()
    {
        var project = new ProjectModel { Name = "P" };
        foreach (var c in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(c);
        var task = new TaskModel { Title = "t", IsDone = true, ColumnId = Guid.NewGuid() };
        project.Tasks.Add(task);

        ProjectStore.NormalizeColumns(DataWith(project));

        Assert.Equal(project.FirstDoneColumn!.Id, task.ColumnId);
    }

    [Fact]
    public void MoveTaskToColumn_DerivesIsDone_AndStampsOnce()
    {
        var project = new ProjectModel { Name = "P" };
        foreach (var c in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(c);
        var task = new TaskModel { Title = "t" };
        project.Tasks.Add(task);

        project.MoveTaskToColumn(task, project.FirstDoneColumn!);
        Assert.True(task.IsDone);
        Assert.NotNull(task.CompletedAtUtc);
        var stamp = task.CompletedAtUtc;

        // Re-moving within done columns must not re-stamp the completion time.
        project.MoveTaskToColumn(task, project.FirstDoneColumn!);
        Assert.Equal(stamp, task.CompletedAtUtc);

        project.MoveTaskToColumn(task, project.Columns[0]);
        Assert.False(task.IsDone);
        Assert.Null(task.CompletedAtUtc);
    }

    [Fact]
    public void ColumnOf_ResolvesById_AndFallsBackByDoneState()
    {
        var project = new ProjectModel { Name = "P" };
        foreach (var c in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(c);
        var task = new TaskModel { Title = "t", ColumnId = project.Columns[1].Id };
        Assert.Equal(project.Columns[1], project.ColumnOf(task));

        task.ColumnId = null;
        task.IsDone = true;
        Assert.Equal(project.FirstDoneColumn, project.ColumnOf(task));
    }

    [Fact]
    public void ColumnsAndColumnId_RoundTripThroughStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tasktracker-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(dir);
            var project = new ProjectModel { Name = "P" };
            foreach (var c in BoardColumnDefaults.NewProjectColumns())
                project.Columns.Add(c);
            var task = new TaskModel { Title = "t" };
            project.MoveTaskToColumn(task, project.Columns[1]);
            project.Tasks.Add(task);
            store.Save(DataWith(project));

            var loaded = new ProjectStore(dir).Load();
            Assert.Equal(3, loaded.Projects[0].Columns.Count);
            Assert.Equal(project.Columns[1].Id, loaded.Projects[0].Tasks[0].ColumnId);
            Assert.Equal("In Progress", loaded.Projects[0].ColumnOf(loaded.Projects[0].Tasks[0])!.Name);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Sync_RemoteClosed_MovesTaskToDoneColumn()
    {
        var project = new ProjectModel { Name = "P", GitHubOwner = "o", GitHubRepo = "r" };
        foreach (var c in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(c);
        var task = new TaskModel { Title = "t", GitHubIssueNumber = 1, LastSyncedIssueState = "open" };
        task.ColumnId = project.Columns[0].Id;
        project.Tasks.Add(task);

        var api = new FakeApi(new GitHubIssue(1, "t", null, "closed", DateTime.UtcNow, Array.Empty<string>(), false));
        await new GitHubSyncService().SyncAsync(project, api);

        Assert.True(task.IsDone);
        Assert.Equal(project.FirstDoneColumn!.Id, task.ColumnId);
    }

    [Fact]
    public async Task Sync_ImportedIssue_LandsInFirstColumn()
    {
        var project = new ProjectModel { Name = "P", GitHubOwner = "o", GitHubRepo = "r" };
        foreach (var c in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(c);

        var api = new FakeApi(new GitHubIssue(2, "new", null, "open", DateTime.UtcNow, Array.Empty<string>(), false));
        await new GitHubSyncService().SyncAsync(project, api);

        Assert.Equal(project.Columns[0].Id, project.Tasks[0].ColumnId);
    }

    private class FakeApi : IGitHubApi
    {
        private readonly List<GitHubIssue> _issues;
        public FakeApi(params GitHubIssue[] issues) => _issues = issues.ToList();
        public Task<IReadOnlyList<GitHubIssue>> ListIssuesAsync(string owner, string repo, string state, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GitHubIssue>>(_issues);
        public Task CloseIssueAsync(string owner, string repo, int number, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReopenIssueAsync(string owner, string repo, int number, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> CreateIssueAsync(string owner, string repo, string title, string? body, IReadOnlyList<string> labels, CancellationToken ct = default) => Task.FromResult(1);
    }
}
