using System.Text.Json;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;
using TaskTracker.Core.Storage;

namespace TaskTracker.Core.Tests;

public class SubTaskTests
{
    [Fact]
    public void SubTasks_RoundTrip_AndOldJsonLoadsWithEmptyList()
    {
        var task = new TaskModel { Title = "t" };
        task.SubTasks.Add(new SubTaskModel { Title = "a", IsDone = true });
        task.SubTasks.Add(new SubTaskModel { Title = "b" });

        var restored = JsonSerializer.Deserialize<TaskModel>(
            JsonSerializer.Serialize(task, CoreJson.Options), CoreJson.Options)!;
        Assert.Equal(2, restored.SubTasks.Count);
        Assert.True(restored.SubTasks[0].IsDone);
        Assert.Equal("1/2", restored.SubTaskProgress);

        var legacy = JsonSerializer.Deserialize<TaskModel>("""{ "Title": "old" }""", CoreJson.Options)!;
        Assert.Empty(legacy.SubTasks);
        Assert.Equal("", legacy.SubTaskProgress);
    }

    [Fact]
    public void SubTaskProgress_UpdatesOnAddToggleRemove()
    {
        var task = new TaskModel { Title = "t" };
        var notifications = 0;
        task.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TaskModel.SubTaskProgress)) notifications++; };

        var sub = new SubTaskModel { Title = "a" };
        task.SubTasks.Add(sub);
        Assert.Equal("0/1", task.SubTaskProgress);

        sub.IsDone = true;
        Assert.Equal("1/1", task.SubTaskProgress);

        task.SubTasks.Remove(sub);
        Assert.Equal("", task.SubTaskProgress);

        Assert.Equal(3, notifications);
    }

    [Fact]
    public void ChangeTracker_RaisesOnSubTaskChanges()
    {
        var projects = new System.Collections.ObjectModel.ObservableCollection<ProjectModel>();
        using var tracker = new ChangeTracker();
        tracker.Attach(projects);
        var count = 0;
        tracker.Changed += (_, _) => count++;

        var project = new ProjectModel { Name = "p" };
        var task = new TaskModel { Title = "t" };
        project.Tasks.Add(task);
        projects.Add(project);

        var before = count;
        var sub = new SubTaskModel { Title = "s" };
        task.SubTasks.Add(sub);
        Assert.True(count > before);

        before = count;
        sub.IsDone = true;
        Assert.True(count > before);
    }

    [Fact]
    public void LabelParser_TrimsDedupesAndDropsEmpties()
    {
        Assert.Equal(new[] { "bug", "ui" }, LabelParser.Parse(" bug , ui,, BUG "));
        Assert.Empty(LabelParser.Parse(null));
    }
}
