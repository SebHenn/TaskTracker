using TaskTracker.Core.GitHub;
using TaskTracker.Core.Models;

namespace TaskTracker.Core.Tests;

public class GitHubSyncTests
{
    private class FakeGitHubApi : IGitHubApi
    {
        public List<GitHubIssue> Issues { get; } = new();
        public List<int> Closed { get; } = new();
        public List<int> Reopened { get; } = new();

        public Task<IReadOnlyList<GitHubIssue>> ListIssuesAsync(string owner, string repo, string state, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GitHubIssue>>(Issues.ToList());

        public Task CloseIssueAsync(string owner, string repo, int number, CancellationToken ct = default)
        {
            Closed.Add(number);
            return Task.CompletedTask;
        }

        public Task ReopenIssueAsync(string owner, string repo, int number, CancellationToken ct = default)
        {
            Reopened.Add(number);
            return Task.CompletedTask;
        }

        public int NextIssueNumber { get; set; } = 100;
        public List<(string Title, string? Body, IReadOnlyList<string> Labels)> Created { get; } = new();

        /// <summary>Creating this one title fails — a stand-in for a rejected create.</summary>
        public string? FailCreateForTitle { get; set; }

        public Task<int> CreateIssueAsync(string owner, string repo, string title, string? body, IReadOnlyList<string> labels, CancellationToken ct = default)
        {
            if (title == FailCreateForTitle)
                throw new InvalidOperationException("Resource not accessible by personal access token");
            Created.Add((title, body, labels));
            return Task.FromResult(NextIssueNumber++);
        }

        public List<(int Number, string Title)> Renamed { get; } = new();

        public Task UpdateIssueTitleAsync(string owner, string repo, int number, string title, CancellationToken ct = default)
        {
            Renamed.Add((number, title));
            return Task.CompletedTask;
        }
    }

    private static GitHubIssue Issue(int number, string state = "open", string title = "Issue", string? body = null,
        DateTime? updatedAt = null, string[]? labels = null, bool isPr = false, DateTime? milestoneDueOn = null)
        => new(number, title, body, state, updatedAt ?? DateTime.UtcNow, labels ?? Array.Empty<string>(), isPr, milestoneDueOn);

    private static ProjectModel LinkedProject()
        => new() { Name = "P", GitHubOwner = "octocat", GitHubRepo = "hello" };

    private static TaskModel LinkedTask(int issueNumber, bool isDone, string lastSyncedState)
        => new() { Title = $"t{issueNumber}", GitHubIssueNumber = issueNumber, IsDone = isDone, LastSyncedIssueState = lastSyncedState };

    private readonly GitHubSyncService _sync = new();

    [Fact]
    public async Task ClosingIssueForRecurringTask_SpawnsTheNextOccurrence()
    {
        // ApplyRemoteState used to set task.IsDone directly, so a recurring task closed
        // on GitHub was completed locally but never came back.
        var project = LinkedProject();
        foreach (var column in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(column);
        var task = LinkedTask(1, isDone: false, lastSyncedState: "open");
        task.Recurrence = RecurrenceRules.Weekly;
        task.DueDate = new DateTime(2026, 8, 1);
        task.ColumnId = project.FirstColumn!.Id;
        project.Tasks.Add(task);

        var api = new FakeGitHubApi();
        api.Issues.Add(Issue(1, "closed", title: "t1"));

        var result = await _sync.SyncAsync(project, api);

        Assert.Equal(1, result.ClosedLocally);
        Assert.True(task.IsDone);
        var next = Assert.Single(project.Tasks.Where(t => t.Id != task.Id));
        Assert.Equal(new DateTime(2026, 8, 8), next.DueDate);
        Assert.False(next.IsDone);
        // The spawn is a distinct piece of work, so the export pass — which runs last in
        // the same sync — files it as its own issue rather than relinking the closed one.
        Assert.NotEqual(1, next.GitHubIssueNumber);
        Assert.Equal(1, result.Exported);
    }

    [Fact]
    public async Task ClosingIssueLocally_PutsTheTaskInADoneColumn()
    {
        // Remote-driven completion has to be as coherent as completing on the board:
        // IsDone and ColumnId must agree, not just the flag.
        var project = LinkedProject();
        foreach (var column in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(column);
        var task = LinkedTask(1, isDone: false, lastSyncedState: "open");
        task.ColumnId = project.FirstColumn!.Id;
        project.Tasks.Add(task);

        var api = new FakeGitHubApi();
        api.Issues.Add(Issue(1, "closed", title: "t1"));

        await _sync.SyncAsync(project, api);

        Assert.True(task.IsDone);
        Assert.True(project.ColumnOf(task)!.IsDoneColumn);
    }

    [Fact]
    public async Task UnlinkedProject_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sync.SyncAsync(new ProjectModel { Name = "P" }, new FakeGitHubApi()));
    }

    [Fact]
    public async Task InitialSync_ImportsOpenIssues_SkipsClosedAndPRs()
    {
        var api = new FakeGitHubApi();
        api.Issues.Add(Issue(1, "open", "Fix bug", body: "details", labels: new[] { "bug" }));
        api.Issues.Add(Issue(2, "closed", "Old issue"));
        api.Issues.Add(Issue(3, "open", "A PR", isPr: true));
        var project = LinkedProject();

        var result = await _sync.SyncAsync(project, api);

        Assert.Equal(1, result.Imported);
        var task = Assert.Single(project.Tasks);
        Assert.Equal("Fix bug", task.Title);
        Assert.Equal("details", task.Description);
        Assert.Equal(1, task.GitHubIssueNumber);
        Assert.Equal(new[] { "bug" }, task.Labels);
        Assert.False(task.IsDone);
        Assert.Equal("open", task.LastSyncedIssueState);
        Assert.NotNull(project.LastSyncedAtUtc);
    }

    [Fact]
    public async Task RemoteClosed_MarksTaskDone()
    {
        var api = new FakeGitHubApi();
        api.Issues.Add(Issue(1, "closed", "t1"));
        var project = LinkedProject();
        project.Tasks.Add(LinkedTask(1, isDone: false, lastSyncedState: "open"));

        var result = await _sync.SyncAsync(project, api);

        Assert.Equal(1, result.ClosedLocally);
        Assert.True(project.Tasks[0].IsDone);
        Assert.Equal("closed", project.Tasks[0].LastSyncedIssueState);
        Assert.Empty(api.Closed);
    }

    [Fact]
    public async Task RemoteReopened_MarksTaskNotDone()
    {
        var api = new FakeGitHubApi();
        api.Issues.Add(Issue(1, "open", "t1"));
        var project = LinkedProject();
        project.Tasks.Add(LinkedTask(1, isDone: true, lastSyncedState: "closed"));

        var result = await _sync.SyncAsync(project, api);

        Assert.Equal(1, result.ReopenedLocally);
        Assert.False(project.Tasks[0].IsDone);
    }

    [Fact]
    public async Task LocalDone_ClosesIssueOnGitHub()
    {
        var api = new FakeGitHubApi();
        api.Issues.Add(Issue(1, "open", "t1"));
        var project = LinkedProject();
        project.Tasks.Add(LinkedTask(1, isDone: true, lastSyncedState: "open"));

        var result = await _sync.SyncAsync(project, api);

        Assert.Equal(1, result.ClosedOnGitHub);
        Assert.Equal(new[] { 1 }, api.Closed);
        Assert.True(project.Tasks[0].IsDone);
        Assert.Equal("closed", project.Tasks[0].LastSyncedIssueState);
    }

    [Fact]
    public async Task LocalReopened_ReopensIssueOnGitHub()
    {
        var api = new FakeGitHubApi();
        api.Issues.Add(Issue(1, "closed", "t1"));
        var project = LinkedProject();
        project.Tasks.Add(LinkedTask(1, isDone: false, lastSyncedState: "closed"));

        var result = await _sync.SyncAsync(project, api);

        Assert.Equal(1, result.ReopenedOnGitHub);
        Assert.Equal(new[] { 1 }, api.Reopened);
    }

    [Fact]
    public async Task BothChangedToSameState_NothingTransferred()
    {
        // Base open; user marked the task done AND the issue was closed on
        // GitHub. They agree — no push, no local flip, snapshot updated.
        var api = new FakeGitHubApi();
        var project = LinkedProject();
        var task = LinkedTask(1, isDone: true, lastSyncedState: "open");
        project.Tasks.Add(task);
        api.Issues.Add(Issue(1, "closed", "t1"));

        var result = await _sync.SyncAsync(project, api);

        Assert.Equal(0, result.ClosedLocally + result.ClosedOnGitHub + result.ReopenedLocally + result.ReopenedOnGitHub);
        Assert.Empty(api.Closed);
        Assert.True(task.IsDone);
        Assert.Equal("closed", task.LastSyncedIssueState);
    }

    [Fact]
    public async Task LegacyTaskWithoutSnapshot_UsesLocalStateAsBase()
    {
        // No LastSyncedIssueState (pre-sync data): base falls back to the local
        // state, so only remote changes apply and nothing is pushed.
        var api = new FakeGitHubApi();
        var project = LinkedProject();
        var task = new TaskModel { Title = "t1", GitHubIssueNumber = 1, IsDone = false };
        project.Tasks.Add(task);
        api.Issues.Add(Issue(1, "closed", "t1"));

        var result = await _sync.SyncAsync(project, api);

        Assert.Equal(1, result.ClosedLocally);
        Assert.True(task.IsDone);
        Assert.Empty(api.Closed);
    }

    [Fact]
    public async Task PushTask_CreatesAndLinksIssue()
    {
        var api = new FakeGitHubApi();
        var project = LinkedProject();
        var task = new TaskModel { Title = "local", Description = "body" };
        task.Labels.Add("bug");
        project.Tasks.Add(task);

        var number = await _sync.PushTaskAsync(project, task, api);

        Assert.Equal(100, number);
        Assert.Equal(100, task.GitHubIssueNumber);
        Assert.Equal("open", task.LastSyncedIssueState);
        Assert.Single(api.Created);
        Assert.Equal("local", api.Created[0].Title);
        Assert.Equal(new[] { "bug" }, api.Created[0].Labels);
        Assert.Empty(api.Closed);
    }

    [Fact]
    public async Task PushTask_DoneTask_ClosesNewIssue()
    {
        var api = new FakeGitHubApi();
        var project = LinkedProject();
        var task = new TaskModel { Title = "finished", IsDone = true };
        project.Tasks.Add(task);

        await _sync.PushTaskAsync(project, task, api);

        Assert.Equal(new[] { 100 }, api.Closed);
        Assert.Equal("closed", task.LastSyncedIssueState);
    }

    [Fact]
    public async Task PushTask_AlreadyLinked_Throws()
    {
        var project = LinkedProject();
        var task = LinkedTask(1, false, "open");
        project.Tasks.Add(task);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sync.PushTaskAsync(project, task, new FakeGitHubApi()));
    }

    [Fact]
    public async Task SyncAfterPush_IsStable()
    {
        var api = new FakeGitHubApi();
        var project = LinkedProject();
        var task = new TaskModel { Title = "local" };
        project.Tasks.Add(task);

        var number = await _sync.PushTaskAsync(project, task, api);
        // Remote now returns the created issue; a sync must not duplicate or flip it.
        api.Issues.Add(Issue(number, "local"));
        var result = await _sync.SyncAsync(project, api);

        Assert.Equal(0, result.Imported);
        Assert.Single(project.Tasks);
        Assert.False(task.IsDone);
    }

    [Fact]
    public async Task LocalRename_IsPushedToGitHub()
    {
        var api = new FakeGitHubApi();
        api.Issues.Add(Issue(1, "open", "Old title"));
        var project = LinkedProject();
        var task = LinkedTask(1, isDone: false, lastSyncedState: "open");
        task.LastSyncedTitle = "Old title";
        task.Title = "Renamed locally";
        project.Tasks.Add(task);

        await _sync.SyncAsync(project, api);

        Assert.Equal(new[] { (1, "Renamed locally") }, api.Renamed);
        Assert.Equal("Renamed locally", task.Title);
        Assert.Equal("Renamed locally", task.LastSyncedTitle);
    }

    [Fact]
    public async Task RemoteRename_Wins_EvenWhenLocalAlsoChanged()
    {
        var api = new FakeGitHubApi();
        api.Issues.Add(Issue(1, "open", "Remote new title"));
        var project = LinkedProject();
        var task = LinkedTask(1, isDone: false, lastSyncedState: "open");
        task.LastSyncedTitle = "Base title";
        task.Title = "Local new title";
        project.Tasks.Add(task);

        await _sync.SyncAsync(project, api);

        Assert.Empty(api.Renamed);
        Assert.Equal("Remote new title", task.Title);
        Assert.Equal("Remote new title", task.LastSyncedTitle);
    }

    [Fact]
    public async Task MilestoneDueDate_DrivesTaskDueDate()
    {
        var api = new FakeGitHubApi();
        api.Issues.Add(Issue(1, "open", "with milestone", milestoneDueOn: new DateTime(2026, 9, 1, 7, 0, 0)));
        api.Issues.Add(Issue(2, "open", "without milestone"));
        var project = LinkedProject();
        var linked = LinkedTask(2, isDone: false, lastSyncedState: "open");
        linked.DueDate = new DateTime(2026, 8, 15);
        project.Tasks.Add(linked);

        await _sync.SyncAsync(project, api);

        var imported = project.Tasks.First(t => t.GitHubIssueNumber == 1);
        Assert.Equal(new DateTime(2026, 9, 1), imported.DueDate);      // milestone applied on import
        Assert.Equal(new DateTime(2026, 8, 15), linked.DueDate);       // no milestone → untouched
    }

    [Fact]
    public async Task VanishedIssue_UnlinksTaskButKeepsIt()
    {
        var api = new FakeGitHubApi(); // no issues at all
        var project = LinkedProject();
        project.Tasks.Add(LinkedTask(7, isDone: false, lastSyncedState: "open"));

        var result = await _sync.SyncAsync(project, api);

        Assert.Equal(1, result.Unlinked);
        var task = Assert.Single(project.Tasks);
        Assert.Null(task.GitHubIssueNumber);
        Assert.Null(task.LastSyncedIssueState);
    }

    [Fact]
    public async Task LinkedTask_TitleAndLabelsFollowRemote()
    {
        var api = new FakeGitHubApi();
        api.Issues.Add(Issue(1, "open", "New title", body: "new body", labels: new[] { "enhancement" }));
        var project = LinkedProject();
        var task = LinkedTask(1, isDone: false, lastSyncedState: "open");
        task.Title = "Old title";
        task.Labels.Add("bug");
        project.Tasks.Add(task);

        await _sync.SyncAsync(project, api);

        Assert.Equal("New title", task.Title);
        Assert.Equal("new body", task.Description);
        Assert.Equal(new[] { "enhancement" }, task.Labels);
    }

    [Fact]
    public async Task LongIssueBody_IsTruncatedOnImport()
    {
        var api = new FakeGitHubApi();
        api.Issues.Add(Issue(1, "open", "t", body: new string('x', 1000)));
        var project = LinkedProject();

        await _sync.SyncAsync(project, api);

        Assert.Equal(501, project.Tasks[0].Description.Length); // 500 chars + ellipsis
    }

    [Fact]
    public async Task ImportedIssues_NameWhatArrived()
    {
        var api = new FakeGitHubApi();
        api.Issues.Add(Issue(7, "open", "Fix the login redirect"));
        api.Issues.Add(Issue(9, "open", "Crash on empty board"));
        var project = LinkedProject();

        var result = await _sync.SyncAsync(project, api);

        Assert.Equal(2, result.Imported);
        Assert.Equal(new[] { (7, "Fix the login redirect"), (9, "Crash on empty board") },
            result.ImportedIssues.Select(i => (i.Number, i.Title)));
    }

    [Fact]
    public async Task ImportedIssues_ExcludesIssuesThatWereAlreadyLinked()
    {
        // The notification must only announce genuinely new issues. Counting every
        // issue in the response would re-announce the whole repository every 15 minutes.
        var api = new FakeGitHubApi();
        api.Issues.Add(Issue(1, "open", "Already here"));
        api.Issues.Add(Issue(2, "open", "Brand new"));
        var project = LinkedProject();
        project.Tasks.Add(LinkedTask(1, isDone: false, lastSyncedState: "open"));

        var result = await _sync.SyncAsync(project, api);

        Assert.Equal(1, result.Imported);
        var only = Assert.Single(result.ImportedIssues);
        Assert.Equal(2, only.Number);
        Assert.Equal("Brand new", only.Title);
    }

    [Fact]
    public async Task ImportedIssues_ExcludesClosedAndPullRequests()
    {
        var api = new FakeGitHubApi();
        api.Issues.Add(Issue(1, "closed", "Historic"));
        api.Issues.Add(Issue(2, "open", "A pull request", isPr: true));
        var project = LinkedProject();

        var result = await _sync.SyncAsync(project, api);

        Assert.Equal(0, result.Imported);
        Assert.Empty(result.ImportedIssues);
    }

    [Fact]
    public async Task ImportedIssues_IsEmptyRatherThanNull_WhenNothingArrived()
    {
        // The tray reads .Count on this without a null check.
        var result = await _sync.SyncAsync(LinkedProject(), new FakeGitHubApi());

        Assert.NotNull(result.ImportedIssues);
        Assert.Empty(result.ImportedIssues);
    }

    [Fact]
    public async Task Sync_FilesLocalTaskAsNewIssue()
    {
        // The point of the export pass: a task created on a linked board reaches
        // GitHub on the next sync, without anyone pushing it by hand.
        var api = new FakeGitHubApi();
        var project = LinkedProject();
        var task = new TaskModel { Title = "local work", Description = "details" };
        task.Labels.Add("bug");
        project.Tasks.Add(task);

        var result = await _sync.SyncAsync(project, api);

        Assert.Equal(1, result.Exported);
        Assert.Null(result.ExportError);
        var created = Assert.Single(api.Created);
        Assert.Equal("local work", created.Title);
        Assert.Equal("details", created.Body);
        Assert.Equal(new[] { "bug" }, created.Labels);
        Assert.Equal(100, task.GitHubIssueNumber);
        Assert.Equal("open", task.LastSyncedIssueState);
        Assert.Equal("local work", task.LastSyncedTitle);
    }

    [Fact]
    public async Task Sync_DoesNotUnlinkTheIssueItJustFiled()
    {
        // The new number is not in the listing the sync started from, so an export
        // that ran before the unlink pass would immediately orphan its own issue.
        var api = new FakeGitHubApi();
        var project = LinkedProject();
        project.Tasks.Add(new TaskModel { Title = "local work" });

        var result = await _sync.SyncAsync(project, api);

        Assert.Equal(0, result.Unlinked);
        Assert.Equal(100, project.Tasks[0].GitHubIssueNumber);
    }

    [Fact]
    public async Task Sync_AfterExport_IsStable()
    {
        var api = new FakeGitHubApi();
        var project = LinkedProject();
        project.Tasks.Add(new TaskModel { Title = "local work" });

        await _sync.SyncAsync(project, api);
        api.Issues.Add(Issue(100, "open", "local work"));   // remote now returns it
        var second = await _sync.SyncAsync(project, api);

        Assert.Equal(0, second.Imported);
        Assert.Equal(0, second.Exported);
        Assert.Single(project.Tasks);
        Assert.Single(api.Created);
    }

    [Fact]
    public async Task Sync_DoesNotExportDoneTasks()
    {
        // Mirrors the import rule — historic closed issues stay out, so finished
        // local history does not become a pile of freshly closed issues.
        var api = new FakeGitHubApi();
        var project = LinkedProject();
        project.Tasks.Add(new TaskModel { Title = "finished", IsDone = true });

        var result = await _sync.SyncAsync(project, api);

        Assert.Equal(0, result.Exported);
        Assert.Empty(api.Created);
        Assert.Null(project.Tasks[0].GitHubIssueNumber);
    }

    [Fact]
    public async Task Sync_DoesNotExportBlankTitles()
    {
        var api = new FakeGitHubApi();
        var project = LinkedProject();
        project.Tasks.Add(new TaskModel { Title = "   " });

        var result = await _sync.SyncAsync(project, api);

        Assert.Equal(0, result.Exported);
        Assert.Empty(api.Created);
    }

    [Fact]
    public async Task Sync_DoesNotRefileAnIssueThatWasDeletedOnGitHub()
    {
        // Unlink marks the task; without that mark the export pass would file the
        // deleted issue again on this very sync, and on every one after it.
        var api = new FakeGitHubApi();   // issue 7 is gone
        var project = LinkedProject();
        var task = LinkedTask(7, isDone: false, lastSyncedState: "open");
        project.Tasks.Add(task);

        var first = await _sync.SyncAsync(project, api);
        var second = await _sync.SyncAsync(project, api);

        Assert.Equal(1, first.Unlinked);
        Assert.True(task.GitHubIssueVanished);
        Assert.Equal(0, first.Exported);
        Assert.Equal(0, second.Exported);
        Assert.Empty(api.Created);
    }

    [Fact]
    public async Task PushTask_ClearsTheVanishedMark()
    {
        // Pushing by hand is the way back for a task whose issue was deleted.
        var api = new FakeGitHubApi();
        var project = LinkedProject();
        var task = new TaskModel { Title = "back please", GitHubIssueVanished = true };
        project.Tasks.Add(task);

        await _sync.PushTaskAsync(project, task, api);

        Assert.False(task.GitHubIssueVanished);
        Assert.Equal(100, task.GitHubIssueNumber);
    }

    [Fact]
    public async Task FailedExport_KeepsTheRestOfTheSync_AndReportsWhy()
    {
        // A create that throws must not abort: the issues already filed in this pass
        // exist on GitHub, and losing their numbers would file all of them twice.
        var api = new FakeGitHubApi { FailCreateForTitle = "rejected" };
        api.Issues.Add(Issue(1, "open", "from github"));
        var project = LinkedProject();
        var first = new TaskModel { Title = "fine" };
        var bad = new TaskModel { Title = "rejected" };
        project.Tasks.Add(first);
        project.Tasks.Add(bad);

        var result = await _sync.SyncAsync(project, api);

        Assert.Equal(1, result.Exported);
        Assert.Equal(100, first.GitHubIssueNumber);
        Assert.Null(bad.GitHubIssueNumber);
        Assert.Contains("not accessible", result.ExportError);
        Assert.Equal(1, result.Imported);                  // import still applied
        Assert.NotNull(project.LastSyncedAtUtc);
    }
}
