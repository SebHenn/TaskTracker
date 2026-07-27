using System.Text.Json;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;
using TaskTracker.Core.Storage;

namespace TaskTracker.Core.Tests;

/// <summary>
/// The trash changes what is on disk, so it gets the same treatment as the rest of the
/// save format: it round-trips, older files without it still load, and retention is
/// applied on the way in.
/// </summary>
public class TrashPersistenceTests : IDisposable
{
    private readonly string _dir;

    public TrashPersistenceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tasktracker-trash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ProjectModel Project(string name = "P")
    {
        var project = new ProjectModel { Name = name };
        foreach (var column in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(column);
        return project;
    }

    private static TaskModel AddTask(ProjectModel project, string title)
    {
        var task = new TaskModel { Title = title, ColumnId = project.FirstColumn!.Id };
        project.Tasks.Add(task);
        return task;
    }

    [Fact]
    public void ATrashedTaskSurvivesASaveAndLoad()
    {
        var store = new ProjectStore(_dir);
        var project = Project();
        var task = AddTask(project, "deleted");
        task.Labels.Add("bug");
        task.SubTasks.Add(new SubTaskModel { Title = "step" });
        Trash.Delete(project, task, DateTime.UtcNow.AddMinutes(-5));

        var data = new StoreData();
        data.Projects.Add(project);
        store.Save(data);

        var loaded = new ProjectStore(_dir).Load();
        var entry = Assert.Single(Assert.Single(loaded.Projects).Trash);
        Assert.Equal("deleted", entry.Task.Title);
        Assert.Equal(new[] { "bug" }, entry.Task.Labels);
        Assert.Single(entry.Task.SubTasks);
        Assert.NotEqual(default, entry.DeletedAtUtc);
    }

    [Fact]
    public void ItCanStillBeRestoredAfterARestart()
    {
        var store = new ProjectStore(_dir);
        var project = Project();
        Trash.Delete(project, AddTask(project, "oops"), DateTime.UtcNow);
        var data = new StoreData();
        data.Projects.Add(project);
        store.Save(data);

        var reloaded = new ProjectStore(_dir).Load();
        var restoredInto = reloaded.Projects[0];
        Assert.True(Trash.Restore(restoredInto, restoredInto.Trash[0]));

        Assert.Empty(restoredInto.Trash);
        Assert.Equal("oops", Assert.Single(restoredInto.Tasks).Title);
        Assert.NotNull(restoredInto.ColumnOf(restoredInto.Tasks[0]));
    }

    [Fact]
    public void RetentionIsAppliedOnLoad()
    {
        var store = new ProjectStore(_dir);
        var project = Project();
        Trash.Delete(project, AddTask(project, "ancient"), DateTime.UtcNow - Trash.Retention.Add(TimeSpan.FromDays(2)));
        Trash.Delete(project, AddTask(project, "recent"), DateTime.UtcNow.AddDays(-1));
        var data = new StoreData();
        data.Projects.Add(project);
        store.Save(data);

        var loaded = new ProjectStore(_dir).Load();

        var entry = Assert.Single(Assert.Single(loaded.Projects).Trash);
        Assert.Equal("recent", entry.Task.Title);
    }

    [Fact]
    public void AFileWrittenBeforeTheTrashExistedLoadsWithAnEmptyOne()
    {
        // Every save file on disk today predates this field.
        var json = """
        {
          "version": 2,
          "revision": "8f1c8f4e-0000-4000-8000-000000000001",
          "recentProjectIds": [],
          "projects": [
            { "Name": "Legacy", "Description": "", "Tasks": [ { "Title": "t" } ], "Columns": [] }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(_dir, "Save.json"), json);

        var loaded = new ProjectStore(_dir).Load();

        var project = Assert.Single(loaded.Projects);
        Assert.NotNull(project.Trash);
        Assert.Empty(project.Trash);
        Assert.Single(project.Tasks);
    }

    [Fact]
    public void AV1ArrayFileAlsoLoadsWithAnEmptyTrash()
    {
        File.WriteAllText(Path.Combine(_dir, "Save.json"),
            """[ { "Name": "Old", "Tasks": [ { "Title": "t" } ] } ]""");

        var loaded = new ProjectStore(_dir).Load();

        Assert.Empty(Assert.Single(loaded.Projects).Trash);
    }

    [Fact]
    public void ExportedTrashComesBackWithFreshIdsOnImport()
    {
        // Importing the same export twice must not leave two entries claiming one id.
        var project = Project("Exported");
        var task = AddTask(project, "deleted");
        Trash.Delete(project, task, DateTime.UtcNow);
        var originalId = task.Id;

        var path = Path.Combine(_dir, "export.json");
        ProjectPorter.ExportJson([project], path);

        var first = ProjectPorter.ImportJson(path, []);
        var second = ProjectPorter.ImportJson(path, first);

        var a = Assert.Single(first[0].Trash).Task.Id;
        var b = Assert.Single(second[0].Trash).Task.Id;
        Assert.NotEqual(originalId, a);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void TheEnvelopeCarriesTrashUnderTheProject()
    {
        // Guards the shape itself: the MCP server and hand-edits read this file too.
        var store = new ProjectStore(_dir);
        var project = Project();
        Trash.Delete(project, AddTask(project, "gone"), DateTime.UtcNow);
        var data = new StoreData();
        data.Projects.Add(project);
        store.Save(data);

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "Save.json")));
        var trash = doc.RootElement.GetProperty("projects")[0].GetProperty("Trash");
        Assert.Equal(JsonValueKind.Array, trash.ValueKind);
        Assert.Equal("gone", trash[0].GetProperty("Task").GetProperty("Title").GetString());
    }
}
