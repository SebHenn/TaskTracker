using TaskTracker.Core.Storage;

namespace TaskTracker.Core.Tests;

[Collection(AppLogCollection.Name)]
public class AppLogTests : IDisposable
{
    private readonly string _dir;
    private readonly string _originalPath;

    public AppLogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tt-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _originalPath = AppLog.LogFilePath;
        AppLog.LogFilePath = Path.Combine(_dir, "test.log");
    }

    public void Dispose()
    {
        AppLog.LogFilePath = _originalPath;
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Write_AppendsTimestampedLines()
    {
        AppLog.Write("test", "first");
        AppLog.Write("test", "second");

        var lines = File.ReadAllLines(AppLog.LogFilePath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("[test] first", lines[0]);
        Assert.EndsWith("[test] second", lines[1]);
    }

    [Fact]
    public void Write_RotatesWhenOversized()
    {
        File.WriteAllText(AppLog.LogFilePath, new string('x', 1_100_000));
        AppLog.Write("test", "after rotation");

        Assert.True(File.Exists(AppLog.LogFilePath + ".old"));
        var lines = File.ReadAllLines(AppLog.LogFilePath);
        Assert.Single(lines);
        Assert.Contains("after rotation", lines[0]);
    }
}
