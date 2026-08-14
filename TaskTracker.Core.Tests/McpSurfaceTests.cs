using System.Text.Json;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;
using TaskTracker.Core.Storage;
using TaskTracker.Mcp;

namespace TaskTracker.Core.Tests;

/// <summary>
/// The tools added to reach parts of the store MCP previously could not touch at all:
/// GitHub linking, columns, the trash, recurrence, timers and ordering.
/// </summary>
[Collection(McpCollection.Name)]
public class McpSurfaceTests : IDisposable
{
    private readonly string _dir;
    private readonly Func<ProjectStore> _originalStore;

    public McpSurfaceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tasktracker-surface-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _originalStore = TaskTrackerTools.CreateStore;
        TaskTrackerTools.CreateStore = () => new ProjectStore(_dir);
    }

    public void Dispose()
    {
        TaskTrackerTools.CreateStore = _originalStore;
        Directory.Delete(_dir, recursive: true);
    }

    private ProjectStore Store => new(_dir);
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private ProjectModel Seed(int taskCount = 2, Action<ProjectModel>? customize = null)
    {
        var project = new ProjectModel { Name = "P" };
        foreach (var column in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(column);
        for (var i = 0; i < taskCount; i++)
            project.Tasks.Add(new TaskModel { Title = $"t{i}", ColumnId = project.FirstColumn!.Id });
        customize?.Invoke(project);

        var store = Store;
        var data = store.Load();
        data.Projects.Add(project);
        store.Save(data);
        return project;
    }

    private ProjectModel Reload() => Store.Load().Projects.Single();

    // ---------- GitHub linking ----------

    [Fact]
    public void LinkGitHub_AcceptsOwnerSlashRepo_AndUnlinkKeepsIssueNumbers()
    {
        // Linking was impossible over MCP; github_sync's advice was "open the desktop app",
        // which is the one thing the caller cannot do.
        var project = Seed(1, p => p.Tasks[0].GitHubIssueNumber = 7);

        TaskTrackerTools.LinkGitHub(project.Id.ToString(), "octocat/hello-world");

        var linked = Reload();
        Assert.Equal("octocat", linked.GitHubOwner);
        Assert.Equal("hello-world", linked.GitHubRepo);
        Assert.True(linked.IsGitHubLinked);

        TaskTrackerTools.UnlinkGitHub(project.Id.ToString());

        var unlinked = Reload();
        Assert.False(unlinked.IsGitHubLinked);
        // Kept, so relinking the same repo resumes rather than filing everything again.
        Assert.Equal(7, unlinked.Tasks[0].GitHubIssueNumber);
    }

    [Fact]
    public void LinkGitHub_AcceptsSeparateOwnerAndRepo_AndRejectsNonsense()
    {
        var project = Seed(0);

        TaskTrackerTools.LinkGitHub(project.Id.ToString(), "octocat", "hello-world");
        Assert.Equal("hello-world", Reload().GitHubRepo);

        Assert.Contains("owner/repo", Assert.Throws<ModelContextProtocol.McpException>(
            () => TaskTrackerTools.LinkGitHub(project.Id.ToString(), "just-an-owner")).Message);
    }

    [Fact]
    public void GitHubStatus_ReportsWhatASyncWouldDo_WithoutNetwork()
    {
        var project = Seed(0, p =>
        {
            p.GitHubOwner = "octocat";
            p.GitHubRepo = "hello";
            p.Tasks.Add(new TaskModel { Title = "linked", GitHubIssueNumber = 1, ColumnId = p.FirstColumn!.Id });
            p.Tasks.Add(new TaskModel { Title = "exportable", ColumnId = p.FirstColumn!.Id });
            p.Tasks.Add(new TaskModel { Title = "vanished", GitHubIssueVanished = true, ColumnId = p.FirstColumn!.Id });
            p.Tasks.Add(new TaskModel { Title = "done", IsDone = true, ColumnId = p.FirstDoneColumn!.Id });
        });

        var status = Parse(TaskTrackerTools.GitHubStatus(project.Id.ToString()));

        Assert.True(status.GetProperty("isLinked").GetBoolean());
        Assert.Equal("octocat/hello", status.GetProperty("repository").GetString());
        Assert.Equal(1, status.GetProperty("linkedTasks").GetInt32());
        // "done" and "vanished" are both excluded from export, matching SyncAsync.
        Assert.Equal(1, status.GetProperty("tasksAwaitingExport").GetInt32());
        Assert.Equal(1, status.GetProperty("unlinkedByVanishedIssue").GetInt32());
    }

    // ---------- Columns ----------

    [Fact]
    public void AddAndUpdateColumn_RoundTripThroughTheStore()
    {
        var project = Seed(0);

        var added = Parse(TaskTrackerTools.AddColumn(project.Id.ToString(), "Review", wipLimit: 3));
        var columnId = added.GetProperty("Id").GetGuid();

        Assert.Equal(3, Reload().Columns.Single(c => c.Id == columnId).WipLimit);

        TaskTrackerTools.UpdateColumn(columnId.ToString(), name: "In review", wipLimit: 0);

        var updated = Reload().Columns.Single(c => c.Id == columnId);
        Assert.Equal("In review", updated.Name);
        Assert.Null(updated.WipLimit); // 0 means "no limit", not "nothing allowed"
    }

    [Fact]
    public void DeleteColumn_RehomesItsTasks()
    {
        var project = Seed(3);
        var doomed = project.FirstColumn!;

        var result = Parse(TaskTrackerTools.DeleteColumn(doomed.Id.ToString()));

        Assert.Equal(3, result.GetProperty("tasksMoved").GetInt32());
        var reloaded = Reload();
        Assert.DoesNotContain(reloaded.Columns, c => c.Id == doomed.Id);
        Assert.All(reloaded.Tasks, t => Assert.NotNull(reloaded.ColumnOf(t)));
    }

    [Fact]
    public void DeleteColumn_RefusesToRemoveTheLastDoneColumn()
    {
        var project = Seed(0);
        var done = project.FirstDoneColumn!;

        var ex = Assert.Throws<ModelContextProtocol.McpException>(
            () => TaskTrackerTools.DeleteColumn(done.Id.ToString()));

        Assert.Contains("done column", ex.Message);
        Assert.Contains(Reload().Columns, c => c.Id == done.Id);
    }

    [Fact]
    public void ReorderColumns_RequiresEveryColumnExactlyOnce()
    {
        var project = Seed(0);
        var ids = project.Columns.Select(c => c.Id.ToString()).ToArray();

        Assert.Throws<ModelContextProtocol.McpException>(
            () => TaskTrackerTools.ReorderColumns(project.Id.ToString(), ids.Take(2).ToArray()));

        var reversed = ids.Reverse().ToArray();
        TaskTrackerTools.ReorderColumns(project.Id.ToString(), reversed);

        Assert.Equal(reversed, Reload().Columns.Select(c => c.Id.ToString()).ToArray());
    }

    [Fact]
    public void MakingAColumnDone_MarksTheTasksSittingInIt()
    {
        // Otherwise the column says "done" and its cards say otherwise.
        var project = Seed(2);
        var open = project.FirstColumn!;

        TaskTrackerTools.UpdateColumn(open.Id.ToString(), isDoneColumn: true);

        Assert.All(Reload().Tasks, t => Assert.True(t.IsDone));
    }

    // ---------- Trash ----------

    [Fact]
    public void DeletedTaskCanBeListedAndRestored()
    {
        // delete_task always wrote to the trash; nothing could read it back.
        var project = Seed(1);
        var task = project.Tasks[0];
        TaskTrackerTools.DeleteTask(task.Id.ToString());

        var trash = Parse(TaskTrackerTools.ListTrash(project.Id.ToString()));
        Assert.Equal(1, trash.GetProperty("Total").GetInt32());
        Assert.Equal("t0", trash.GetProperty("Items")[0].GetProperty("Title").GetString());

        TaskTrackerTools.RestoreTask(task.Id.ToString());

        var restored = Reload();
        Assert.Empty(restored.Trash);
        Assert.Single(restored.Tasks);
        Assert.NotNull(restored.ColumnOf(restored.Tasks[0]));
    }

    [Fact]
    public void EmptyTrash_RequiresConfirmation()
    {
        var project = Seed(1);
        TaskTrackerTools.DeleteTask(project.Tasks[0].Id.ToString());

        Assert.Throws<ModelContextProtocol.McpException>(
            () => TaskTrackerTools.EmptyTrash(project.Id.ToString()));
        Assert.Single(Reload().Trash);

        TaskTrackerTools.EmptyTrash(project.Id.ToString(), confirm: true);
        Assert.Empty(Reload().Trash);
    }

    // ---------- Recurrence, timers, ordering ----------

    [Fact]
    public void SetRecurrence_MakesCompletionSpawnTheNextOccurrence()
    {
        var project = Seed(1, p => p.Tasks[0].DueDate = new DateTime(2026, 8, 1));
        var task = project.Tasks[0];

        TaskTrackerTools.SetRecurrence(task.Id.ToString(), "weekly", 2);
        TaskTrackerTools.UpdateTask(task.Id.ToString(), isDone: true);

        var reloaded = Reload();
        Assert.Equal(2, reloaded.Tasks.Count);
        Assert.Equal(new DateTime(2026, 8, 15), reloaded.Tasks.Single(t => t.Id != task.Id).DueDate);
    }

    [Fact]
    public void SetRecurrence_RejectsUnknownRules()
    {
        var project = Seed(1);

        var ex = Assert.Throws<ModelContextProtocol.McpException>(
            () => TaskTrackerTools.SetRecurrence(project.Tasks[0].Id.ToString(), "fortnightly"));

        Assert.Contains("weekly", ex.Message);
        Assert.False(Reload().Tasks[0].IsRecurring);
    }

    [Fact]
    public void StartTimer_StopsAnyOtherRunningTimer()
    {
        // One timer at a time across the store — the rule the desktop app has always had.
        var project = Seed(2);
        var first = project.Tasks[0];
        var second = project.Tasks[1];

        TaskTrackerTools.StartTimer(first.Id.ToString());
        var result = Parse(TaskTrackerTools.StartTimer(second.Id.ToString()));

        Assert.Equal("t0", result.GetProperty("stoppedTimerOn").GetString());
        var reloaded = Reload();
        Assert.Null(reloaded.Tasks.Single(t => t.Id == first.Id).TimerStartedAtUtc);
        Assert.NotNull(reloaded.Tasks.Single(t => t.Id == second.Id).TimerStartedAtUtc);
    }

    [Fact]
    public void TimerStatus_AnswersWhetherOneIsRunning()
    {
        var project = Seed(1);
        Assert.False(Parse(TaskTrackerTools.TimerStatus()).GetProperty("running").GetBoolean());

        TaskTrackerTools.StartTimer(project.Tasks[0].Id.ToString());

        var status = Parse(TaskTrackerTools.TimerStatus());
        Assert.True(status.GetProperty("running").GetBoolean());
        Assert.Equal("t0", status.GetProperty("Title").GetString());

        TaskTrackerTools.StopTimer();
        Assert.False(Parse(TaskTrackerTools.TimerStatus()).GetProperty("running").GetBoolean());
    }

    [Fact]
    public void StopTimer_BanksTheElapsedTime()
    {
        var project = Seed(1);
        var task = project.Tasks[0];

        TaskTrackerTools.StartTimer(task.Id.ToString());
        TaskTrackerTools.StopTimer(task.Id.ToString());

        var reloaded = Reload().Tasks[0];
        Assert.Null(reloaded.TimerStartedAtUtc);
        Assert.True(reloaded.TrackedSeconds >= 0);
    }

    [Fact]
    public void ReorderTasks_RequiresTheWholeColumn()
    {
        var project = Seed(3);
        var column = project.FirstColumn!;
        var ids = project.Tasks.Select(t => t.Id.ToString()).ToArray();

        Assert.Throws<ModelContextProtocol.McpException>(
            () => TaskTrackerTools.ReorderTasks(column.Id.ToString(), ids.Take(2).ToArray()));

        TaskTrackerTools.ReorderTasks(column.Id.ToString(), ids.Reverse().ToArray());

        var reloaded = Reload();
        var ordered = LaneSort.Apply(reloaded.Tasks).Select(t => t.Title).ToList();
        Assert.Equal(new[] { "t2", "t1", "t0" }, ordered);
    }

    // ---------- Discovery ----------

    [Fact]
    public void ListLabels_ReportsUsageCounts()
    {
        var project = Seed(0, p =>
        {
            var a = new TaskModel { Title = "a", ColumnId = p.FirstColumn!.Id };
            a.Labels.Add("chore");
            var b = new TaskModel { Title = "b", ColumnId = p.FirstColumn!.Id };
            b.Labels.Add("chore");
            b.Labels.Add("urgent");
            p.Tasks.Add(a);
            p.Tasks.Add(b);
        });

        var labels = Parse(TaskTrackerTools.ListLabels(project.Id.ToString()));

        Assert.Equal("chore", labels[0].GetProperty("Label").GetString());
        Assert.Equal(2, labels[0].GetProperty("Count").GetInt32());
        Assert.Equal("urgent", labels[1].GetProperty("Label").GetString());
    }

    [Fact]
    public void StoreInfo_ReportsTheServerVersionAndWhereTheDataLives()
    {
        // The version is the point: an installed tasktracker-mcp is a snapshot, and a
        // stale one is otherwise indistinguishable from a broken one.
        Seed(2);

        var info = Parse(TaskTrackerTools.StoreInfo());

        Assert.False(string.IsNullOrWhiteSpace(info.GetProperty("serverVersion").GetString()));
        Assert.Equal(_dir, info.GetProperty("dataDirectory").GetString());
        Assert.Equal(1, info.GetProperty("projects").GetInt32());
        Assert.Equal(2, info.GetProperty("openTasks").GetInt32());
    }

    [Fact]
    public void UpdateProject_SetsFavouriteAndValidatesColour()
    {
        var project = Seed(0);

        TaskTrackerTools.UpdateProject(project.Id.ToString(), isFavourite: true, color: "4c8dff");
        var updated = Reload();
        Assert.True(updated.IsFavourite);
        Assert.Equal("#4C8DFF", updated.Color);

        // An unvalidated colour is one the WPF head silently fails to parse, so the user
        // sets a colour that never appears.
        Assert.Throws<ModelContextProtocol.McpException>(
            () => TaskTrackerTools.UpdateProject(project.Id.ToString(), color: "burnt sienna"));

        TaskTrackerTools.UpdateProject(project.Id.ToString(), color: "none");
        Assert.Null(Reload().Color);
    }

    [Fact]
    public void SubtasksAndNotesCanBeRemovedAgain()
    {
        var project = Seed(1);
        var task = project.Tasks[0];

        var sub = Parse(TaskTrackerTools.AddSubTask(task.Id.ToString(), "step"));
        var note = Parse(TaskTrackerTools.AddNote(task.Id.ToString(), "looked into it"));

        Assert.Single(Reload().Tasks[0].SubTasks);
        Assert.Single(Reload().Tasks[0].Activity);

        TaskTrackerTools.DeleteSubTask(sub.GetProperty("Id").GetString()!);
        TaskTrackerTools.DeleteActivity(note.GetProperty("Id").GetString()!);

        Assert.Empty(Reload().Tasks[0].SubTasks);
        Assert.Empty(Reload().Tasks[0].Activity);
    }
}
