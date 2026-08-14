using TaskTracker.Core.Storage;

namespace TaskTracker.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir;

    public SettingsStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tasktracker-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        AppLog.LogFilePath = Path.Combine(_dir, "test.log");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private SettingsStore NewStore() => new(_dir);

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var settings = new AppSettings
        {
            Theme = "light",
            Language = "de",
            AutoSyncEnabled = true,
            SidebarWidth = 312.5,
            GitHubTokenProtected = "abc",
            GitHubTokenIsPlaintext = true,
        };

        NewStore().Save(settings);
        var loaded = NewStore().Load();

        Assert.Equal("light", loaded.Theme);
        Assert.Equal("de", loaded.Language);
        Assert.True(loaded.AutoSyncEnabled);
        Assert.Equal(312.5, loaded.SidebarWidth);
        Assert.Equal("abc", loaded.GitHubTokenProtected);
        Assert.True(loaded.GitHubTokenIsPlaintext);
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenTheFileIsMissing()
    {
        var loaded = NewStore().Load();

        Assert.Equal("dark", loaded.Theme);
        Assert.Equal("en", loaded.Language);
        Assert.True(loaded.NotifyOnNewIssues);
        Assert.Null(loaded.GitHubTokenProtected);
    }

    [Fact]
    public void Load_FallsBackToTheBackup_WhenTheFileIsCorrupt()
    {
        var store = NewStore();
        store.Save(new AppSettings { Theme = "light" });
        store.Save(new AppSettings { Theme = "sepia" }); // first save becomes .bak1
        File.WriteAllText(store.SettingsFilePath, "{ not json");

        var loaded = NewStore().Load();

        // The backup holds the previous good write, not the defaults.
        Assert.Equal("light", loaded.Theme);
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenBothTheFileAndBackupAreCorrupt()
    {
        var store = NewStore();
        store.Save(new AppSettings { Theme = "light" });
        store.Save(new AppSettings { Theme = "sepia" });
        File.WriteAllText(store.SettingsFilePath, "{ not json");
        File.WriteAllText(store.SettingsFilePath + ".bak1", "also not json");

        Assert.Equal("dark", NewStore().Load().Theme);
    }

    [Fact]
    public void Save_DoesNotThrow_WhenTheTargetIsUnwritable()
    {
        // Settings are saved from property setters as the user toggles a checkbox.
        // An exception out of one of those would take down the interaction; a lost
        // preference would not.
        var store = new SettingsStore(_dir);
        store.Save(new AppSettings());
        // A directory where the file belongs makes every write fail.
        File.Delete(store.SettingsFilePath);
        Directory.CreateDirectory(store.SettingsFilePath);

        var ex = Record.Exception(() => store.Save(new AppSettings { Theme = "light" }));

        Assert.Null(ex);
    }

    [Fact]
    public void Save_KeepsOneBackupOfThePreviousWrite()
    {
        var store = NewStore();
        store.Save(new AppSettings { Theme = "light" });
        store.Save(new AppSettings { Theme = "dark" });

        Assert.True(File.Exists(store.SettingsFilePath + ".bak1"));
        Assert.Contains("light", File.ReadAllText(store.SettingsFilePath + ".bak1"));
    }
}
