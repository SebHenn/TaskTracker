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

        public Task<int> CreateIssueAsync(string owner, string repo, string title, string? body, IReadOnlyList<string> labels, CancellationToken ct = default)
        {
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
}
