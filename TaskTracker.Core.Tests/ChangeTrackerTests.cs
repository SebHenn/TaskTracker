using System.Collections.ObjectModel;
using TaskTracker.Core.Models;
using TaskTracker.Core.Storage;

namespace TaskTracker.Core.Tests;

/// <summary>
/// Autosave's trigger. Two failure modes matter and neither is loud: missing a mutation
/// loses work silently, and firing on UI-only state causes a save storm — CLAUDE.md
/// singles out the IsSelected case for exactly that reason.
/// </summary>
public class ChangeTrackerTests
{
    private sealed class Harness : IDisposable
    {
        public ChangeTracker Tracker { get; } = new();
        public int Count { get; private set; }
        public ObservableCollection<ProjectModel> Projects { get; } = [];

        public Harness()
        {
            Tracker.Changed += (_, _) => Count++;
            Tracker.Attach(Projects);
        }

        public int Since(int mark) => Count - mark;
        public void Dispose() => Tracker.Dispose();
    }

    private static ProjectModel NewProject()
    {
        var project = new ProjectModel { Name = "P" };
        foreach (var column in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(column);
        return project;
    }

    [Fact]
    public void FiresForNestedMutationsOfItemsAddedAfterAttach()
    {
        using var h = new Harness();
        var project = NewProject();
        h.Projects.Add(project);

        var task = new TaskModel { Title = "t" };
        project.Tasks.Add(task);

        var mark = h.Count;
        task.Title = "renamed";
        Assert.True(h.Since(mark) > 0);

        mark = h.Count;
        task.SubTasks.Add(new SubTaskModel { Title = "step" });
        Assert.True(h.Since(mark) > 0);

        mark = h.Count;
        task.SubTasks[0].IsDone = true;
        Assert.True(h.Since(mark) > 0);

        mark = h.Count;
        task.Activity.Add(new ActivityEntry { Text = "note" });
        Assert.True(h.Since(mark) > 0);

        mark = h.Count;
        project.Columns[0].Name = "Renamed column";
        Assert.True(h.Since(mark) > 0);

        mark = h.Count;
        project.Trash.Add(new TrashedTask { Task = new TaskModel { Title = "gone" } });
        Assert.True(h.Since(mark) > 0);
    }

    [Fact]
    public void IgnoresIsSelected()
    {
        // The save-storm guard: selection changes as the user clicks around the sidebar,
        // and each one would otherwise rewrite the whole store.
        using var h = new Harness();
        var project = NewProject();
        h.Projects.Add(project);

        var mark = h.Count;
        project.IsSelected = true;
        project.IsSelected = false;

        Assert.Equal(0, h.Since(mark));
    }

    [Fact]
    public void ReHooksWhenATaskCollectionIsReplacedWholesale()
    {
        // Loading replaces the collection rather than clearing it, so a tracker that only
        // hooked the original instance would go deaf after the first load.
        using var h = new Harness();
        var project = NewProject();
        h.Projects.Add(project);

        var task = new TaskModel { Title = "fresh" };
        project.Tasks = [task];

        var mark = h.Count;
        task.Title = "renamed after replace";

        Assert.True(h.Since(mark) > 0);
    }

    [Fact]
    public void StopsAfterDetach()
    {
        using var h = new Harness();
        var project = NewProject();
        var task = new TaskModel { Title = "t" };
        project.Tasks.Add(task);
        h.Projects.Add(project);

        h.Tracker.Detach();

        var mark = h.Count;
        project.Name = "renamed";
        task.Title = "renamed";
        task.SubTasks.Add(new SubTaskModel { Title = "step" });
        h.Projects.Add(NewProject());

        Assert.Equal(0, h.Since(mark));
    }

    [Fact]
    public void KeepsTrackingItemsRemovedFromTheCollection()
    {
        // Removal does not untrack: the Track* methods are add-only, and deliberately so.
        // A task being deleted is removed from Tasks and added to Trash in one step, and
        // one moved between projects leaves one collection before joining another —
        // untracking on removal would drop the subscription in the middle of both.
        //
        // The cost is that removed models stay subscribed until Detach. Pinned here so
        // the trade is visible rather than discovered.
        using var h = new Harness();
        var project = NewProject();
        var task = new TaskModel { Title = "t" };
        project.Tasks.Add(task);
        h.Projects.Add(project);

        h.Projects.Remove(project);

        var mark = h.Count;
        task.Title = "renamed after removal";

        Assert.True(h.Since(mark) > 0);
    }

    [Fact]
    public void ReAddingAPreviouslyTrackedProjectDoesNotDoubleSubscribe()
    {
        // Undoing a project delete puts the same instance back. If that re-subscribed,
        // every later edit would raise Changed twice and autosave would do double work.
        using var h = new Harness();
        var project = NewProject();
        h.Projects.Add(project);
        h.Projects.Remove(project);
        h.Projects.Add(project);

        var mark = h.Count;
        project.Name = "renamed";

        Assert.Equal(1, h.Since(mark));
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var h = new Harness();
        h.Projects.Add(NewProject());

        h.Tracker.Dispose();
        var ex = Record.Exception(() => h.Tracker.Dispose());

        Assert.Null(ex);
    }

    [Fact]
    public void AttachTwiceDoesNotDoubleCount()
    {
        // Re-attaching has to detach first, or every mutation raises Changed twice and
        // the debounce does twice the work for nothing.
        using var h = new Harness();
        var project = NewProject();
        h.Projects.Add(project);

        h.Tracker.Attach(h.Projects);

        var mark = h.Count;
        project.Name = "renamed";

        Assert.Equal(1, h.Since(mark));
    }
}
