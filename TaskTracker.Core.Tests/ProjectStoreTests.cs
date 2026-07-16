using System.Collections.ObjectModel;
using TaskTracker.Core.Models;
using TaskTracker.Core.Storage;

namespace TaskTracker.Core.Tests;

public class ProjectStoreTests : IDisposable
{
    private readonly string _dir;

    public ProjectStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tasktracker-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ProjectStore NewStore() => new(_dir);

    private static StoreData SampleData()
    {
        var project = new ProjectModel { Name = "Alpha", Description = "First" };
        project.Tasks.Add(new TaskModel { Title = "Do it" });
        return new StoreData
        {
            Projects = new ObservableCollection<ProjectModel> { project },
            RecentProjectIds = new List<Guid> { project.Id },
        };
    }

    [Fact]
    public void SaveThenLoad_RoundTripsProjectsAndRecents()
    {
        var store = NewStore();
        var data = SampleData();
        store.Save(data);

        var loaded = NewStore().Load();

        Assert.Single(loaded.Projects);
        Assert.Equal("Alpha", loaded.Projects[0].Name);
        Assert.Single(loaded.Projects[0].Tasks);
        Assert.Equal(data.Projects[0].Id, loaded.Projects[0].Id);
        Assert.Equal(new[] { data.Projects[0].Id }, loaded.RecentProjectIds);
        Assert.Equal(2, loaded.Version);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyStore()
    {
        var loaded = NewStore().Load();
        Assert.Empty(loaded.Projects);
        Assert.Empty(loaded.RecentProjectIds);
    }

    [Fact]
    public void Load_V1ArrayFile_MigratesProjectsAndRecents()
    {
        // v1 layout: Save.json is a bare array; SaveRecent.json holds duplicated
        // copies of recent projects, historically deduped by name.
        var v1 = """
            [
              { "Name": "Alpha", "Description": "a", "Tasks": [ { "Title": "t1", "IsDone": false, "Id": "6f9619ff-8b86-d011-b42d-00c04fc964ff" } ], "IsFavourite": true, "Id": "11111111-1111-1111-1111-111111111111" },
              { "Name": "Beta", "Description": "b", "Tasks": [], "IsFavourite": false, "Id": "22222222-2222-2222-2222-222222222222" }
            ]
            """;
        var recentV1 = """
            [ { "Name": "Beta", "Description": "b", "Tasks": [], "Id": "99999999-9999-9999-9999-999999999999" } ]
            """;
        File.WriteAllText(Path.Combine(_dir, "Save.json"), v1);
        File.WriteAllText(Path.Combine(_dir, "SaveRecent.json"), recentV1);

        var loaded = NewStore().Load();

        Assert.Equal(2, loaded.Projects.Count);
        Assert.Equal("Alpha", loaded.Projects[0].Name);
        Assert.True(loaded.Projects[0].IsFavourite);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), loaded.Projects[0].Id);
        Assert.Single(loaded.Projects[0].Tasks);
        // Recents resolve by name onto the real project's id, not the stale copy's id.
        Assert.Equal(new[] { Guid.Parse("22222222-2222-2222-2222-222222222222") }, loaded.RecentProjectIds);
    }

    [Fact]
    public void Save_AfterV1Migration_WritesV2AndDeletesLegacyRecentFile()
    {
        File.WriteAllText(Path.Combine(_dir, "Save.json"), """[ { "Name": "Alpha", "Tasks": [] } ]""");
        File.WriteAllText(Path.Combine(_dir, "SaveRecent.json"), "[]");

        var store = NewStore();
        var data = store.Load();
        store.Save(data);

        Assert.False(File.Exists(Path.Combine(_dir, "SaveRecent.json")));
        var json = File.ReadAllText(Path.Combine(_dir, "Save.json"));
        Assert.StartsWith("{", json.TrimStart());
        Assert.Contains("\"version\": 2", json);
    }

    [Fact]
    public void Save_RotatesThreeBackups()
    {
        var store = NewStore();
        for (var i = 0; i < 5; i++)
            store.Save(SampleData());

        Assert.True(File.Exists(Path.Combine(_dir, "Save.json.bak1")));
        Assert.True(File.Exists(Path.Combine(_dir, "Save.json.bak2")));
        Assert.True(File.Exists(Path.Combine(_dir, "Save.json.bak3")));
        Assert.False(File.Exists(Path.Combine(_dir, "Save.json.bak4")));
        Assert.False(File.Exists(Path.Combine(_dir, "Save.json.tmp")));
    }

    [Fact]
    public void Save_ChangesRevisionEachTime_AndPeekRevisionReadsIt()
    {
        var store = NewStore();
        store.Save(SampleData());
        var first = store.LastWrittenRevision;
        store.Save(SampleData());
        var second = store.LastWrittenRevision;

        Assert.NotEqual(first, second);
        Assert.Equal(second, store.PeekRevision());
    }

    [Fact]
    public void PeekRevision_V1OrMissingFile_ReturnsNull()
    {
        var store = NewStore();
        Assert.Null(store.PeekRevision());
        File.WriteAllText(Path.Combine(_dir, "Save.json"), "[]");
        Assert.Null(store.PeekRevision());
    }

    [Fact]
    public void Update_LoadsMutatesAndSavesUnderOneLock()
    {
        var store = NewStore();
        store.Save(SampleData());

        store.Update(data => data.Projects.Add(new ProjectModel { Name = "Beta" }));

        var loaded = NewStore().Load();
        Assert.Equal(2, loaded.Projects.Count);
        Assert.Contains(loaded.Projects, p => p.Name == "Beta");
    }

    [Fact]
    public void Load_DropsRecentIdsWithNoMatchingProject()
    {
        var store = NewStore();
        var data = SampleData();
        data.RecentProjectIds.Add(Guid.NewGuid());
        store.Save(data);

        var loaded = NewStore().Load();
        Assert.Single(loaded.RecentProjectIds);
    }

    [Fact]
    public void ChangeTracker_RaisesOnPropertyTaskAndCollectionChanges()
    {
        var projects = new ObservableCollection<ProjectModel>();
        using var tracker = new ChangeTracker();
        var count = 0;
        tracker.Attach(projects);
        tracker.Changed += (_, _) => count++;

        var project = new ProjectModel { Name = "P" };
        projects.Add(project);                       // collection change
        project.Name = "P2";                         // project property
        var task = new TaskModel { Title = "T" };
        project.Tasks.Add(task);                     // nested collection change
        task.IsDone = true;                          // task property (added after attach); also raises the two timestamp properties
        project.IsSelected = true;                   // ignored UI-only property

        Assert.Equal(6, count);
    }
}
