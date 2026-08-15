using System.Text.Json;
using TaskTracker.Core.Models;
using TaskTracker.Core.Storage;
using TaskTracker.Mcp;

namespace TaskTracker.Core.Tests;

/// <summary>
/// The wire shape of tool responses.
///
/// Tool output is charged to the caller's context on every call, so the compact default,
/// the dropped empty keys and the paging are load-bearing rather than cosmetic — and all
/// three are the kind of thing that silently regresses when a field is added later.
/// </summary>
[Collection(McpCollection.Name)]
public class McpContractTests : IDisposable
{
    private readonly string _dir;
    private readonly Func<ProjectStore> _originalFactory;

    public McpContractTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tasktracker-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _originalFactory = TaskTrackerTools.CreateStore;
        TaskTrackerTools.CreateStore = () => new ProjectStore(_dir);
    }

    public void Dispose()
    {
        TaskTrackerTools.CreateStore = _originalFactory;
        Directory.Delete(_dir, recursive: true);
    }

    private ProjectModel Seed(int taskCount = 3, Action<ProjectModel>? customize = null)
    {
        var project = new ProjectModel { Name = "P" };
        foreach (var column in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(column);
        for (var i = 0; i < taskCount; i++)
        {
            var task = new TaskModel
            {
                Title = $"t{i}",
                Description = "a description nobody asked for",
                ColumnId = project.FirstColumn!.Id,
            };
            project.Tasks.Add(task);
        }
        customize?.Invoke(project);

        var store = new ProjectStore(_dir);
        var data = store.Load();
        data.Projects.Add(project);
        store.Save(data);
        return project;
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void CompactIsTheDefault_AndOmitsDescriptions()
    {
        var project = Seed();

        var items = Parse(TaskTrackerTools.ListTasks(project.Id.ToString())).GetProperty("Items");

        Assert.Equal(3, items.GetArrayLength());
        var first = items[0];
        Assert.False(first.TryGetProperty("Description", out _));
        Assert.Equal("t0", first.GetProperty("Title").GetString());
    }

    [Fact]
    public void DetailFull_IncludesTheDescription()
    {
        var project = Seed();

        var items = Parse(TaskTrackerTools.ListTasks(project.Id.ToString(), detail: "full")).GetProperty("Items");

        Assert.Equal("a description nobody asked for", items[0].GetProperty("Description").GetString());
    }

    [Fact]
    public void EmptyAndNullFields_AreOmittedEntirely()
    {
        // The single biggest source of waste in the old shape: every task carried
        // "Labels": [], "SubTasks": [], "GitHubIssueNumber": null and friends.
        var project = Seed(1);

        var first = Parse(TaskTrackerTools.ListTasks(project.Id.ToString())).GetProperty("Items")[0];

        Assert.False(first.TryGetProperty("Labels", out _));
        Assert.False(first.TryGetProperty("SubTaskProgress", out _));
        Assert.False(first.TryGetProperty("GitHubIssueNumber", out _));
        Assert.False(first.TryGetProperty("DueDate", out _));
    }

    [Fact]
    public void PopulatedFields_AreStillPresent()
    {
        var project = Seed(1, p =>
        {
            var t = p.Tasks[0];
            t.Labels.Add("chore");
            t.SubTasks.Add(new SubTaskModel { Title = "step", IsDone = true });
            t.SubTasks.Add(new SubTaskModel { Title = "step 2" });
            t.DueDate = new DateTime(2026, 8, 1);
        });

        var first = Parse(TaskTrackerTools.ListTasks(project.Id.ToString())).GetProperty("Items")[0];

        Assert.Equal("chore", first.GetProperty("Labels")[0].GetString());
        Assert.Equal("1/2", first.GetProperty("SubTaskProgress").GetString());
        Assert.True(first.TryGetProperty("DueDate", out _));
    }

    [Fact]
    public void Paging_ReportsTheUnpagedTotal()
    {
        // A silently truncated array reads as "that is everything". The envelope is what
        // lets a caller tell the difference.
        var project = Seed(10);

        var page = Parse(TaskTrackerTools.ListTasks(project.Id.ToString(), limit: 4, offset: 8));

        Assert.Equal(10, page.GetProperty("Total").GetInt32());
        Assert.Equal(2, page.GetProperty("Returned").GetInt32());
        Assert.Equal(8, page.GetProperty("Offset").GetInt32());
        Assert.Equal(2, page.GetProperty("Items").GetArrayLength());
    }

    [Fact]
    public void ListTasks_FiltersByLabelPriorityAndDue()
    {
        var project = Seed(0, p =>
        {
            var match = new TaskModel { Title = "match", Priority = TaskPriority.High, DueDate = DateTime.Today, ColumnId = p.FirstColumn!.Id };
            match.Labels.Add("urgent");
            p.Tasks.Add(match);

            var wrongLabel = new TaskModel { Title = "wrong-label", Priority = TaskPriority.High, DueDate = DateTime.Today, ColumnId = p.FirstColumn!.Id };
            p.Tasks.Add(wrongLabel);

            var wrongDue = new TaskModel { Title = "wrong-due", Priority = TaskPriority.High, ColumnId = p.FirstColumn!.Id };
            wrongDue.Labels.Add("urgent");
            p.Tasks.Add(wrongDue);
        });

        var page = Parse(TaskTrackerTools.ListTasks(
            project.Id.ToString(), label: "urgent", due: "today", priority: "high"));

        Assert.Equal(1, page.GetProperty("Total").GetInt32());
        Assert.Equal("match", page.GetProperty("Items")[0].GetProperty("Title").GetString());
    }

    [Fact]
    public void ListTasks_NoDueBucket_FindsUnscheduledWork()
    {
        var project = Seed(0, p =>
        {
            p.Tasks.Add(new TaskModel { Title = "scheduled", DueDate = DateTime.Today, ColumnId = p.FirstColumn!.Id });
            p.Tasks.Add(new TaskModel { Title = "unscheduled", ColumnId = p.FirstColumn!.Id });
        });

        var page = Parse(TaskTrackerTools.ListTasks(project.Id.ToString(), due: "none"));

        Assert.Equal(1, page.GetProperty("Total").GetInt32());
        Assert.Equal("unscheduled", page.GetProperty("Items")[0].GetProperty("Title").GetString());
    }

    [Fact]
    public void SearchTasks_SkipsArchivedProjectsUnlessAsked()
    {
        var project = Seed(1);
        var store = new ProjectStore(_dir);
        var data = store.Load();
        data.Projects.Single().IsArchived = true;
        store.Save(data);

        Assert.Equal(0, Parse(TaskTrackerTools.SearchTasks("t0")).GetProperty("Total").GetInt32());
        Assert.Equal(1, Parse(TaskTrackerTools.SearchTasks("t0", includeArchived: true)).GetProperty("Total").GetInt32());
    }

    [Fact]
    public void GetProject_ExposesWipLimitAndColumnCounts()
    {
        // Both were invisible over MCP, so an agent could not see it was pushing a column
        // past the limit the board enforces.
        var project = Seed(2, p => p.Columns[0].WipLimit = 5);

        var columns = Parse(TaskTrackerTools.GetProject(project.Id.ToString())).GetProperty("Columns");

        Assert.Equal(5, columns[0].GetProperty("WipLimit").GetInt32());
        Assert.Equal(2, columns[0].GetProperty("TaskCount").GetInt32());
    }

    [Fact]
    public void InvalidPagingAndDetail_AreRejectedWithUsableMessages()
    {
        var project = Seed(1);
        var id = project.Id.ToString();

        Assert.Contains("compact or full",
            Assert.Throws<ModelContextProtocol.McpException>(() => TaskTrackerTools.ListTasks(id, detail: "verbose")).Message);
        Assert.Contains("limit",
            Assert.Throws<ModelContextProtocol.McpException>(() => TaskTrackerTools.ListTasks(id, limit: 0)).Message);
        Assert.Contains("offset",
            Assert.Throws<ModelContextProtocol.McpException>(() => TaskTrackerTools.ListTasks(id, offset: -1)).Message);
        Assert.Contains("overdue",
            Assert.Throws<ModelContextProtocol.McpException>(() => TaskTrackerTools.ListTasks(id, due: "someday")).Message);
    }

    [Fact]
    public void ResponsesAreNotIndented()
    {
        var project = Seed(1);

        var json = TaskTrackerTools.ListTasks(project.Id.ToString());

        Assert.DoesNotContain("\n", json);
    }
}
