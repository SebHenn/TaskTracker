using TaskTracker.Core.Models;
using TaskTracker.Core.Services;

namespace TaskTracker.Core.Tests;

public class TimeTrackingTests
{
    [Fact]
    public void StartStop_AccumulatesElapsedTime()
    {
        var task = new TaskModel { Title = "t" };
        var start = new DateTime(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);

        TimeTracking.Start(task, start);
        Assert.Equal(start, task.TimerStartedAtUtc);

        TimeTracking.Stop(task, start.AddMinutes(30));
        Assert.Equal(1800, task.TrackedSeconds, precision: 1);
        Assert.Null(task.TimerStartedAtUtc);

        TimeTracking.Start(task, start.AddHours(1));
        TimeTracking.Stop(task, start.AddHours(1).AddMinutes(15));
        Assert.Equal(2700, task.TrackedSeconds, precision: 1);
    }

    [Fact]
    public void Start_WhileRunning_DoesNotResetTheClock()
    {
        var task = new TaskModel { Title = "t" };
        var start = new DateTime(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);
        TimeTracking.Start(task, start);
        TimeTracking.Start(task, start.AddMinutes(10));
        Assert.Equal(start, task.TimerStartedAtUtc);
    }

    [Fact]
    public void TotalSeconds_IncludesRunningTimer()
    {
        var task = new TaskModel { Title = "t", TrackedSeconds = 600 };
        var start = new DateTime(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);
        TimeTracking.Start(task, start);

        Assert.Equal(600 + 120, TimeTracking.TotalSeconds(task, start.AddMinutes(2)), precision: 1);
    }

    [Fact]
    public void StopWithoutTimer_IsANoOp()
    {
        var task = new TaskModel { Title = "t", TrackedSeconds = 5 };
        TimeTracking.Stop(task);
        Assert.Equal(5, task.TrackedSeconds);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(59, "0:00")]
    [InlineData(60, "0:01")]
    [InlineData(3900, "1:05")]
    [InlineData(90000, "25:00")]
    public void Format_RendersHoursAndMinutes(double seconds, string expected)
    {
        Assert.Equal(expected, TimeTracking.Format(seconds));
    }

    [Fact]
    public void CsvExport_IncludesTrackedHours()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tt-time-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var project = new ProjectModel { Name = "P" };
            foreach (var c in BoardColumnDefaults.NewProjectColumns())
                project.Columns.Add(c);
            project.Tasks.Add(new TaskModel { Title = "t", TrackedSeconds = 5400 });

            var path = Path.Combine(dir, "e.csv");
            ProjectPorter.ExportCsv(new[] { project }, path);

            // By column position rather than end-of-line: the export gained columns after
            // TrackedHours, and asserting on the last field would break again next time.
            var lines = File.ReadAllLines(path);
            var column = Array.IndexOf(lines[0].Split(','), "TrackedHours");
            Assert.True(column >= 0, "TrackedHours column is missing from the CSV header.");
            Assert.Equal("1.5", lines[1].Split(',')[column]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
