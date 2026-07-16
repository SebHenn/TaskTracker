using System.Collections.ObjectModel;
using System.Text.Json;
using TaskTracker.Core.Models;

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
}
