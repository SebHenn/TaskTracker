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
    }

    private static GitHubIssue Issue(int number, string state = "open", string title = "Issue", string? body = null,
        DateTime? updatedAt = null, string[]? labels = null, bool isPr = false)
        => new(number, title, body, state, updatedAt ?? DateTime.UtcNow, labels ?? Array.Empty<string>(), isPr);

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
