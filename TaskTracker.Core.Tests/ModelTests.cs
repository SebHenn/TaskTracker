using System.Text.Json;
using TaskTracker.Core.Models;
using TaskTracker.Core.Storage;

namespace TaskTracker.Core.Tests;

public class ModelTests
{
    [Fact]
    public void OldJsonWithoutNewFields_LoadsWithDefaults()
    {
        var json = """{ "Title": "Old task", "Description": "", "IsDone": true, "Id": "11111111-1111-1111-1111-111111111111" }""";
        var task = JsonSerializer.Deserialize<TaskModel>(json, CoreJson.Options)!;

        Assert.Equal("Old task", task.Title);
        Assert.True(task.IsDone);
        Assert.Null(task.DueDate);
        Assert.Equal(TaskPriority.Medium, task.Priority);
        Assert.Empty(task.Labels);
        Assert.Null(task.GitHubIssueNumber);
        // Deserializing bare runs the constructor, which stamps CreatedAtUtc so no
        // creation path can forget it. Loading through ProjectStore clears it again for
        // tasks whose stored JSON predates the field — see
        // ProjectStoreTests.Load_LeavesCreatedAtNull_ForTasksSavedWithoutIt.
        Assert.NotNull(task.CreatedAtUtc);
    }

    [Fact]
    public void Priority_SerializesAsString()
    {
        var task = new TaskModel { Title = "t", Priority = TaskPriority.High };
        var json = JsonSerializer.Serialize(task, CoreJson.Options);
        Assert.Contains("\"High\"", json);
    }

    [Fact]
    public void NewTaskFields_RoundTrip()
    {
        var task = new TaskModel
        {
            Title = "t",
            DueDate = new DateTime(2026, 8, 1),
            Priority = TaskPriority.Low,
            GitHubIssueNumber = 42,
            LastSyncedIssueState = "open",
        };
        task.Labels.Add("bug");

        var restored = JsonSerializer.Deserialize<TaskModel>(JsonSerializer.Serialize(task, CoreJson.Options), CoreJson.Options)!;

        Assert.Equal(new DateTime(2026, 8, 1), restored.DueDate);
        Assert.Equal(TaskPriority.Low, restored.Priority);
        Assert.Equal(new[] { "bug" }, restored.Labels);
        Assert.Equal(42, restored.GitHubIssueNumber);
        Assert.Equal("open", restored.LastSyncedIssueState);
    }

    [Fact]
    public void MarkingDone_SetsCompletionTimestamps_AndUndoneClearsThem()
    {
        var task = new TaskModel { Title = "t" };
        var before = DateTime.UtcNow;

        task.IsDone = true;
        Assert.NotNull(task.CompletedAtUtc);
        Assert.NotNull(task.StateChangedUtc);
        Assert.InRange(task.CompletedAtUtc!.Value, before, DateTime.UtcNow);

        task.IsDone = false;
        Assert.Null(task.CompletedAtUtc);
        Assert.NotNull(task.StateChangedUtc);
    }

    [Fact]
    public void ProjectGitHubFields_RoundTrip_AndIsSelectedIsNotPersisted()
    {
        var project = new ProjectModel
        {
            Name = "p",
            GitHubOwner = "octocat",
            GitHubRepo = "hello",
            IsArchived = true,
            ArchivedAtUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            IsSelected = true,
        };

        var json = JsonSerializer.Serialize(project, CoreJson.Options);
        Assert.DoesNotContain("IsSelected", json);

        var restored = JsonSerializer.Deserialize<ProjectModel>(json, CoreJson.Options)!;
        Assert.Equal("octocat", restored.GitHubOwner);
        Assert.Equal("hello", restored.GitHubRepo);
        Assert.True(restored.IsGitHubLinked);
        Assert.True(restored.IsArchived);
        Assert.False(restored.IsSelected);
    }

    [Fact]
    public void UnlinkedProject_IsNotGitHubLinked()
    {
        Assert.False(new ProjectModel { Name = "p" }.IsGitHubLinked);
        Assert.False(new ProjectModel { Name = "p", GitHubOwner = "octocat" }.IsGitHubLinked);
    }
}
