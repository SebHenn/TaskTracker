using System.Collections.ObjectModel;
using System.Text.Json;
using TaskTracker.Core.Models;
using TaskTracker.Core.Storage;

namespace TaskTracker.Core.Tests;

public class SerializationTests
{
    [Fact]
    public void ProjectWithTasks_RoundTrips()
    {
        var project = new ProjectModel
        {
            Name = "My Project",
            Description = "A description",
            IsFavourite = true,
            Tasks = new ObservableCollection<TaskModel>
            {
                new() { Title = "Task 1", Description = "First", IsDone = false },
                new() { Title = "Task 2", Description = "Second", IsDone = true },
            },
        };

        var json = JsonSerializer.Serialize(project);
        var restored = JsonSerializer.Deserialize<ProjectModel>(json);

        Assert.NotNull(restored);
        Assert.Equal(project.Id, restored.Id);
        Assert.Equal("My Project", restored.Name);
        Assert.Equal("A description", restored.Description);
        Assert.True(restored.IsFavourite);
        Assert.Equal(2, restored.Tasks.Count);
        Assert.Equal(project.Tasks[0].Id, restored.Tasks[0].Id);
        Assert.Equal("Task 1", restored.Tasks[0].Title);
        Assert.False(restored.Tasks[0].IsDone);
        Assert.True(restored.Tasks[1].IsDone);
    }

    [Fact]
    public void WholeStore_RoundTripsThroughTheSaveFormat()
    {
        // The single previous test covered a project and two bare tasks. Everything else
        // the store holds went unchecked, so a field could stop persisting without one
        // assertion noticing.
        var project = new ProjectModel
        {
            Name = "Full",
            Description = "Everything",
            Color = "#4C8DFF",
            IsFavourite = true,
            IsArchived = true,
            ArchivedAtUtc = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc),
            GitHubOwner = "octocat",
            GitHubRepo = "hello",
            LastSyncedAtUtc = new DateTime(2026, 5, 2, 9, 30, 0, DateTimeKind.Utc),
        };
        project.Columns.Add(new BoardColumn { Name = "Backlog" });
        project.Columns.Add(new BoardColumn { Name = "Doing", WipLimit = 3 });
        project.Columns.Add(new BoardColumn { Name = "Done", IsDoneColumn = true });

        var task = new TaskModel
        {
            Title = "Everything task",
            Description = "Body",
            DueDate = new DateTime(2026, 6, 1),
            Priority = TaskPriority.High,
            CreatedAtUtc = new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc),
            Recurrence = RecurrenceRules.Weekly,
            RecurrenceInterval = 2,
            TrackedSeconds = 5400,
            TimerStartedAtUtc = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
            SortOrder = 4,
            GitHubIssueNumber = 42,
            LastSyncedIssueState = "open",
            LastSyncedTitle = "Everything task",
            ColumnId = project.Columns[1].Id,
        };
        task.Labels.Add("chore");
        task.SubTasks.Add(new SubTaskModel { Title = "step one", IsDone = true });
        task.Activity.Add(new ActivityEntry { Text = "a note" });
        project.Tasks.Add(task);
        project.Trash.Add(new TrashedTask
        {
            Task = new TaskModel { Title = "deleted" },
            DeletedAtUtc = new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc),
        });

        var data = new StoreData
        {
            Projects = [project],
            RecentProjectIds = [project.Id],
        };

        var json = JsonSerializer.Serialize(data, CoreJson.Options);
        var restored = JsonSerializer.Deserialize<StoreData>(json, CoreJson.Options)!;
        var p = restored.Projects.Single();
        var t = p.Tasks.Single();

        Assert.Equal(project.Id, p.Id);
        Assert.Equal("#4C8DFF", p.Color);
        Assert.True(p.IsArchived);
        Assert.Equal(project.ArchivedAtUtc, p.ArchivedAtUtc);
        Assert.Equal("octocat", p.GitHubOwner);
        Assert.Equal(project.LastSyncedAtUtc, p.LastSyncedAtUtc);
        Assert.Equal(project.Id, restored.RecentProjectIds.Single());

        Assert.Equal(3, p.Columns.Count);
        Assert.Equal(3, p.Columns[1].WipLimit);
        Assert.True(p.Columns[2].IsDoneColumn);

        Assert.Equal(TaskPriority.High, t.Priority);
        Assert.Equal(task.CreatedAtUtc, t.CreatedAtUtc);
        Assert.Equal(RecurrenceRules.Weekly, t.Recurrence);
        Assert.Equal(2, t.RecurrenceInterval);
        Assert.Equal(5400, t.TrackedSeconds);
        Assert.Equal(task.TimerStartedAtUtc, t.TimerStartedAtUtc);
        Assert.Equal(4, t.SortOrder);
        Assert.Equal(42, t.GitHubIssueNumber);
        Assert.Equal("open", t.LastSyncedIssueState);
        Assert.Equal("Everything task", t.LastSyncedTitle);
        Assert.Equal(project.Columns[1].Id, t.ColumnId);
        Assert.Equal("chore", t.Labels.Single());
        Assert.True(t.SubTasks.Single().IsDone);
        Assert.Equal("a note", t.Activity.Single().Text);

        var trashed = p.Trash.Single();
        Assert.Equal("deleted", trashed.Task.Title);
        Assert.Equal(new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc), trashed.DeletedAtUtc);
    }

    [Fact]
    public void IsSelected_IsNotPersisted()
    {
        // JsonIgnore *and* ChangeTracker.IgnoredProperties; miss either and selection
        // either leaks into the save file or causes a save on every sidebar click.
        var project = new ProjectModel { Name = "P", IsSelected = true };

        var json = JsonSerializer.Serialize(project, CoreJson.Options);

        Assert.DoesNotContain("IsSelected", json);
        Assert.False(JsonSerializer.Deserialize<ProjectModel>(json, CoreJson.Options)!.IsSelected);
    }

    [Fact]
    public void V1BareArray_MigratesToTheV2Envelope()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tt-v1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // A v1 file is a bare array of projects with no envelope and no columns.
            File.WriteAllText(Path.Combine(dir, "Save.json"),
                """[{"Name":"Legacy","Tasks":[{"Title":"old","IsDone":true}]}]""");

            var store = new ProjectStore(dir);
            var loaded = store.Load();

            Assert.Equal(2, loaded.Version);
            var project = loaded.Projects.Single();
            Assert.Equal("Legacy", project.Name);
            // NormalizeColumns runs on the migration path too, so a v1 task comes out
            // sitting in a real column rather than with a null ColumnId.
            Assert.NotEmpty(project.Columns);
            Assert.Contains(project.Columns, c => c.IsDoneColumn);
            Assert.NotNull(project.ColumnOf(project.Tasks.Single()));

            store.Save(loaded);
            Assert.Equal(2, new ProjectStore(dir).Load().Version);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
