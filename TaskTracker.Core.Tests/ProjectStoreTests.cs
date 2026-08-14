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
    public void PrepareThenWrite_RoundTripsTheSameAsSave()
    {
        // The UI thread prepares, a background thread writes.
        var store = NewStore();
        var prepared = store.Prepare(SampleData());

        Assert.False(File.Exists(store.SaveFilePath)); // Prepare must not touch disk
        store.Write(prepared);

        var loaded = NewStore().Load();
        Assert.Single(loaded.Projects);
        Assert.Equal("Alpha", loaded.Projects[0].Name);
        Assert.Equal("Do it", loaded.Projects[0].Tasks[0].Title);
    }

    [Fact]
    public void Prepare_ClaimsTheRevisionBeforeTheWriteLands()
    {
        // The file watcher uses LastWrittenRevision to recognise the app's own
        // writes. If it only updated after an async write finished, a watcher event
        // arriving mid-write would look external and trigger a spurious reload.
        var store = NewStore();

        var prepared = store.Prepare(SampleData());

        Assert.Equal(prepared.Revision, store.LastWrittenRevision);
        store.Write(prepared);
        Assert.Equal(prepared.Revision, store.LastWrittenRevision);
        Assert.Equal(prepared.Revision, NewStore().PeekRevision());
    }

    [Fact]
    public async Task Write_FromABackgroundThread_Persists()
    {
        var store = NewStore();
        var prepared = store.Prepare(SampleData());

        await Task.Run(() => store.Write(prepared));

        Assert.Equal("Alpha", NewStore().Load().Projects[0].Name);
    }

    [Fact]
    public void SequentialPreparedWrites_KeepBackupsAndLatestState()
    {
        var store = NewStore();
        var data = SampleData();

        store.Write(store.Prepare(data));
        data.Projects[0].Name = "Beta";
        store.Write(store.Prepare(data));

        Assert.Equal("Beta", NewStore().Load().Projects[0].Name);
        Assert.True(File.Exists(store.SaveFilePath + ".bak1"));
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
        Assert.True(count > 0);

        var afterAdd = count;
        project.Name = "P2";                         // project property
        Assert.True(count > afterAdd);

        var afterRename = count;
        var task = new TaskModel { Title = "T" };
        project.Tasks.Add(task);                     // nested collection change
        Assert.True(count > afterRename);

        var afterTaskAdd = count;
        task.IsDone = true;                          // task property (added after attach)
        Assert.True(count > afterTaskAdd);

        var afterDone = count;
        project.IsSelected = true;                   // ignored UI-only property
        Assert.Equal(afterDone, count);
    }

    [Fact]
    public void AcquireLock_ThrowsStoreLocked_WhenTheOtherProcessHoldsIt()
    {
        // Contention is a normal condition with two processes on one store, and callers
        // need to tell it apart from a corrupt or missing file.
        var store = NewStore();
        using var held = new FileStream(
            Path.Combine(_dir, "store.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        Assert.Throws<StoreLockedException>(() => store.Load());
    }

    [Fact]
    public void TrySaveIfUnchanged_Saves_WhenNothingElseWrote()
    {
        var store = NewStore();
        store.Save(SampleData());

        var data = store.Load();
        data.Projects[0].Name = "Renamed";

        Assert.True(store.TrySaveIfUnchanged(data, data.Revision));
        Assert.Equal("Renamed", NewStore().Load().Projects[0].Name);
    }

    [Fact]
    public void TrySaveIfUnchanged_Refuses_WhenTheFileMovedOn()
    {
        // This is what stops a slow GitHub sync from silently overwriting whatever the
        // desktop app wrote while it was waiting on the network.
        var store = NewStore();
        store.Save(SampleData());

        var data = store.Load();
        var baseRevision = data.Revision;
        data.Projects[0].Name = "Renamed by sync";

        // Another process writes in the meantime.
        var other = NewStore();
        var otherData = other.Load();
        otherData.Projects[0].Description = "Edited in the app";
        other.Save(otherData);

        Assert.False(store.TrySaveIfUnchanged(data, baseRevision));

        var onDisk = NewStore().Load();
        Assert.Equal("Alpha", onDisk.Projects[0].Name);              // sync's write did not land
        Assert.Equal("Edited in the app", onDisk.Projects[0].Description); // the app's did
    }

    [Fact]
    public void Load_LeavesCreatedAtNull_ForTasksSavedWithoutIt()
    {
        // TaskModel's constructor stamps CreatedAtUtc so no creation path can forget it.
        // Deserializing runs that constructor, so without the back-out every legacy task
        // would acquire its load time as a creation date and the next save would persist
        // it — rewriting creation history for the whole store.
        var id = Guid.NewGuid();
        var json = $$"""
        {
          "version": 2,
          "revision": "{{Guid.NewGuid()}}",
          "projects": [
            { "Name": "Legacy", "Tasks": [ { "Id": "{{id}}", "Title": "Ancient", "IsDone": false } ] }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(_dir, "Save.json"), json);

        var loaded = NewStore().Load();
        var task = loaded.Projects[0].Tasks.Single(t => t.Id == id);
        Assert.Null(task.CreatedAtUtc);

        // And it stays null across a save/reload, rather than being backfilled later.
        NewStore().Save(loaded);
        Assert.Null(NewStore().Load().Projects[0].Tasks.Single(t => t.Id == id).CreatedAtUtc);
    }

    [Fact]
    public void Load_KeepsCreatedAt_WhenTheFileHasIt()
    {
        var store = NewStore();
        var data = SampleData();
        var stamped = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        data.Projects[0].Tasks[0].CreatedAtUtc = stamped;
        store.Save(data);

        Assert.Equal(stamped, NewStore().Load().Projects[0].Tasks[0].CreatedAtUtc);
    }

    [Fact]
    public void Load_NormalizesAnUnknownRecurrence()
    {
        // An unrecognised value leaves IsRecurring true while SpawnNextIfRecurring
        // refuses to spawn — a recurring task that silently never recurs.
        var json = $$"""
        {
          "version": 2,
          "revision": "{{Guid.NewGuid()}}",
          "projects": [
            { "Name": "P", "Tasks": [ { "Id": "{{Guid.NewGuid()}}", "Title": "T", "Recurrence": "fortnightly", "RecurrenceInterval": 0 } ] }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(_dir, "Save.json"), json);

        var task = NewStore().Load().Projects[0].Tasks[0];

        Assert.Equal(RecurrenceRules.None, task.Recurrence);
        Assert.False(task.IsRecurring);
        Assert.Equal(1, task.RecurrenceInterval);
    }
}
