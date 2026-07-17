using TaskTracker.Core.Models;
using TaskTracker.Core.Services;

namespace TaskTracker.Core.Tests;

public class ProjectPorterTests : IDisposable
{
    private readonly string _dir;

    public ProjectPorterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tasktracker-porter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ProjectModel SampleProject()
    {
        var project = new ProjectModel { Name = "Alpha", Description = "d" };
        foreach (var c in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(c);
        var task = new TaskModel { Title = "T, with \"quotes\"", Priority = TaskPriority.High, DueDate = new DateTime(2026, 8, 1) };
        task.Labels.Add("a");
        task.Labels.Add("b");
        task.SubTasks.Add(new SubTaskModel { Title = "s1" });
        project.MoveTaskToColumn(task, project.Columns[1]);
        project.Tasks.Add(task);
        return project;
    }

    [Fact]
    public void JsonExportImport_RoundTrips_WithFreshIdsAndRemappedColumns()
    {
        var original = SampleProject();
        var path = Path.Combine(_dir, "export.json");
        ProjectPorter.ExportJson(new[] { original }, path);

        var imported = ProjectPorter.ImportJson(path, new[] { original });

        var copy = Assert.Single(imported);
        Assert.Equal("Alpha (2)", copy.Name);                 // deduped against existing
        Assert.NotEqual(original.Id, copy.Id);
        Assert.NotEqual(original.Tasks[0].Id, copy.Tasks[0].Id);
        Assert.NotEqual(original.Columns[1].Id, copy.Columns[1].Id);
        // The task still points at the copy's "In Progress" column.
        Assert.Equal(copy.Columns[1].Id, copy.Tasks[0].ColumnId);
        Assert.Equal("T, with \"quotes\"", copy.Tasks[0].Title);
        Assert.Single(copy.Tasks[0].SubTasks);
        Assert.NotEqual(original.Tasks[0].SubTasks[0].Id, copy.Tasks[0].SubTasks[0].Id);
    }

    [Fact]
    public void ImportIntoEmptyStore_KeepsName()
    {
        var path = Path.Combine(_dir, "export.json");
        ProjectPorter.ExportJson(new[] { SampleProject() }, path);

        var imported = ProjectPorter.ImportJson(path, Array.Empty<ProjectModel>());

        Assert.Equal("Alpha", imported[0].Name);
    }

    [Fact]
    public void CsvExport_QuotesSpecialCharacters()
    {
        var path = Path.Combine(_dir, "export.csv");
        ProjectPorter.ExportCsv(new[] { SampleProject() }, path);

        var lines = File.ReadAllLines(path);
        Assert.StartsWith("Project,Title,", lines[0]);
        Assert.Contains("\"T, with \"\"quotes\"\"\"", lines[1]);
        Assert.Contains("In Progress", lines[1]);
        Assert.Contains("a;b", lines[1]);
        Assert.Contains("High", lines[1]);
        Assert.Contains("2026-08-01", lines[1]);
    }
}
