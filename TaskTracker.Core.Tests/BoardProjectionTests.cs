using TaskTracker.Core.Models;
using TaskTracker.Core.Services;
using TaskTracker.Core.Storage;

namespace TaskTracker.Core.Tests;

public class BoardProjectionTests
{
    private static ProjectModel ProjectWithColumns()
    {
        var project = new ProjectModel { Name = "P" };
        foreach (var column in BoardColumnDefaults.NewProjectColumns())
            project.Columns.Add(column);
        return project;
    }

    private static TaskModel AddTask(ProjectModel project, string title, BoardColumn? column = null,
                                     params string[] labels)
    {
        var task = new TaskModel { Title = title, ColumnId = column?.Id, IsDone = column?.IsDoneColumn ?? false };
        foreach (var label in labels)
            task.Labels.Add(label);
        project.Tasks.Add(task);
        return task;
    }

    [Fact]
    public void Lanes_FollowColumnOrder_EvenWhenEmpty()
    {
        var project = ProjectWithColumns();

        var board = BoardProjection.Compute(project);

        Assert.Equal(project.Columns.Count, board.Lanes.Count);
        Assert.Equal(
            project.Columns.Select(c => c.Id),
            board.Lanes.Select(l => l.Column.Id));
        Assert.All(board.Lanes, lane => Assert.Empty(lane.Tasks));
    }

    [Fact]
    public void GroupsTasksIntoTheirColumn()
    {
        var project = ProjectWithColumns();
        var first = project.Columns[0];
        var done = project.FirstDoneColumn!;
        AddTask(project, "a", first);
        AddTask(project, "b", first);
        AddTask(project, "c", done);

        var board = BoardProjection.Compute(project);

        Assert.Equal(new[] { "a", "b" }, board.Lanes.Single(l => l.Column.Id == first.Id).Tasks.Select(t => t.Title));
        Assert.Equal(new[] { "c" }, board.Lanes.Single(l => l.Column.Id == done.Id).Tasks.Select(t => t.Title));
    }

    [Fact]
    public void TaskWithNoColumn_FallsBackByDoneState()
    {
        var project = ProjectWithColumns();
        AddTask(project, "open");                                        // ColumnId null, not done
        var finished = AddTask(project, "finished");
        finished.IsDone = true;                                          // ColumnId null, done

        var board = BoardProjection.Compute(project);

        var firstLane = board.Lanes.Single(l => l.Column.Id == project.FirstColumn!.Id);
        var doneLane = board.Lanes.Single(l => l.Column.Id == project.FirstDoneColumn!.Id);
        Assert.Equal(new[] { "open" }, firstLane.Tasks.Select(t => t.Title));
        Assert.Equal(new[] { "finished" }, doneLane.Tasks.Select(t => t.Title));
    }

    [Fact]
    public void TaskWithStaleColumnId_FallsBackInsteadOfDisappearing()
    {
        var project = ProjectWithColumns();
        var task = AddTask(project, "orphan");
        task.ColumnId = Guid.NewGuid(); // column deleted since the task was placed

        var board = BoardProjection.Compute(project);

        Assert.Single(board.Lanes.SelectMany(l => l.Tasks));
        Assert.Equal("orphan", board.Lanes.Single(l => l.Column.Id == project.FirstColumn!.Id).Tasks[0].Title);
    }

    [Fact]
    public void LabelFilter_NarrowsTasksButKeepsEveryLane()
    {
        var project = ProjectWithColumns();
        var first = project.Columns[0];
        AddTask(project, "bug one", first, "bug");
        AddTask(project, "chore one", first, "chore");

        var board = BoardProjection.Compute(project, "bug");

        Assert.Equal(project.Columns.Count, board.Lanes.Count);
        Assert.Equal(new[] { "bug one" }, board.Lanes.SelectMany(l => l.Tasks).Select(t => t.Title));
    }

    [Theory]
    [InlineData("bug")]
    [InlineData("BUG")]
    [InlineData("Bug")]
    public void LabelFilter_IsCaseInsensitive(string filter)
    {
        var project = ProjectWithColumns();
        AddTask(project, "tagged", project.Columns[0], "Bug");

        var board = BoardProjection.Compute(project, filter);

        Assert.Equal(new[] { "tagged" }, board.Lanes.SelectMany(l => l.Tasks).Select(t => t.Title));
    }

    [Fact]
    public void NullOrEmptyFilter_ShowsEverything()
    {
        var project = ProjectWithColumns();
        AddTask(project, "a", project.Columns[0], "x");
        AddTask(project, "b", project.Columns[0]);

        Assert.Equal(2, BoardProjection.Compute(project, null).Lanes.SelectMany(l => l.Tasks).Count());
        Assert.Equal(2, BoardProjection.Compute(project, "").Lanes.SelectMany(l => l.Tasks).Count());
    }

    [Fact]
    public void Labels_AreDistinctCaseInsensitiveAndSorted()
    {
        var project = ProjectWithColumns();
        AddTask(project, "a", project.Columns[0], "zeta", "Alpha");
        AddTask(project, "b", project.Columns[0], "alpha", "mid");

        var board = BoardProjection.Compute(project);

        Assert.Equal(new[] { "Alpha", "mid", "zeta" }, board.Labels);
    }

    [Fact]
    public void Labels_AreUnaffectedByTheActiveFilter()
    {
        // Otherwise picking a label would empty the very dropdown you picked it from.
        var project = ProjectWithColumns();
        AddTask(project, "a", project.Columns[0], "bug");
        AddTask(project, "b", project.Columns[0], "chore");

        var board = BoardProjection.Compute(project, "bug");

        Assert.Equal(new[] { "bug", "chore" }, board.Labels);
    }

    [Fact]
    public void WithinALane_ExplicitSortOrderWinsOverPriority()
    {
        var project = ProjectWithColumns();
        var first = project.Columns[0];
        var low = AddTask(project, "low but pinned", first);
        low.Priority = TaskPriority.Low;
        low.SortOrder = 1;
        var high = AddTask(project, "high", first);
        high.Priority = TaskPriority.High;

        var board = BoardProjection.Compute(project);

        Assert.Equal(
            new[] { "low but pinned", "high" },
            board.Lanes.Single(l => l.Column.Id == first.Id).Tasks.Select(t => t.Title));
    }

    [Fact]
    public void Compute_DoesNotMutateTheProject()
    {
        // The WPF layer calls NormalizeColumns explicitly; projection must stay a read.
        var project = new ProjectModel { Name = "no columns at all" };
        var task = new TaskModel { Title = "t" };
        project.Tasks.Add(task);

        var board = BoardProjection.Compute(project);

        Assert.Empty(project.Columns);
        Assert.Empty(board.Lanes);
        Assert.Null(task.ColumnId);
    }

    [Fact]
    public void AfterNormalizeColumns_ADegenerateProjectStillRenders()
    {
        var project = new ProjectModel { Name = "no columns at all" };
        project.Tasks.Add(new TaskModel { Title = "t" });

        ProjectStore.NormalizeColumns(project);
        var board = BoardProjection.Compute(project);

        Assert.NotEmpty(board.Lanes);
        Assert.Equal(new[] { "t" }, board.Lanes.SelectMany(l => l.Tasks).Select(t => t.Title));
    }

    // --- filtering ------------------------------------------------------------

    private static readonly DateTime Today = new(2026, 7, 27);

    [Fact]
    public void TheStringOverloadStillFiltersByLabel()
    {
        // Kept so existing callers and the "all labels" mapping do not have to change.
        var project = ProjectWithColumns();
        AddTask(project, "tagged", project.Columns[0], "bug");
        AddTask(project, "untagged", project.Columns[0]);

        var board = BoardProjection.Compute(project, "bug");

        Assert.Equal(new[] { "tagged" }, board.Lanes.SelectMany(l => l.Tasks).Select(t => t.Title));
    }

    [Fact]
    public void ADueFilterNarrowsEveryLane()
    {
        var project = ProjectWithColumns();
        var first = project.Columns[0];
        var done = project.FirstDoneColumn!;
        AddTask(project, "late", first).DueDate = Today.AddDays(-3);
        AddTask(project, "soon", first).DueDate = Today.AddDays(2);
        AddTask(project, "finished late", done).DueDate = Today.AddDays(-3);

        var board = BoardProjection.Compute(project, new BoardFilter(Due: DueFilter.Overdue), Today);

        // The completed one drops out: finishing something stops it being overdue.
        Assert.Equal(new[] { "late" }, board.Lanes.SelectMany(l => l.Tasks).Select(t => t.Title));
    }

    [Fact]
    public void APriorityFilterNarrowsEveryLane()
    {
        var project = ProjectWithColumns();
        AddTask(project, "urgent", project.Columns[0]).Priority = TaskPriority.High;
        AddTask(project, "whenever", project.Columns[0]).Priority = TaskPriority.Low;

        var board = BoardProjection.Compute(project, new BoardFilter(Priority: TaskPriority.High), Today);

        Assert.Equal(new[] { "urgent" }, board.Lanes.SelectMany(l => l.Tasks).Select(t => t.Title));
    }

    [Fact]
    public void LanesSurviveAFilterThatEmptiesThem()
    {
        // The board must keep its columns when a filter matches nothing, or the whole
        // layout collapses and there is nowhere left to drop a card.
        var project = ProjectWithColumns();
        AddTask(project, "a", project.Columns[0]);

        var board = BoardProjection.Compute(project, new BoardFilter(Priority: TaskPriority.High), Today);

        Assert.Equal(project.Columns.Count, board.Lanes.Count);
        Assert.All(board.Lanes, lane => Assert.Empty(lane.Tasks));
    }

    [Fact]
    public void TheLabelListIsBuiltFromEveryTaskNotTheFilteredOnes()
    {
        // Otherwise filtering to "bug" would leave "bug" as the only option in the
        // dropdown, with no way back to the other labels.
        var project = ProjectWithColumns();
        AddTask(project, "a", project.Columns[0], "bug");
        AddTask(project, "b", project.Columns[0], "chore");

        var board = BoardProjection.Compute(project, new BoardFilter("bug"), Today);

        Assert.Equal(new[] { "bug", "chore" }, board.Labels);
        Assert.Single(board.Lanes.SelectMany(l => l.Tasks));
    }

    [Fact]
    public void FilterDimensionsCombineAcrossLanes()
    {
        var project = ProjectWithColumns();
        var first = project.Columns[0];
        var wanted = AddTask(project, "wanted", first, "bug");
        wanted.Priority = TaskPriority.High;
        wanted.DueDate = Today.AddDays(-1);
        var wrongLabel = AddTask(project, "wrong label", first, "chore");
        wrongLabel.Priority = TaskPriority.High;
        wrongLabel.DueDate = Today.AddDays(-1);

        var board = BoardProjection.Compute(
            project, new BoardFilter("bug", DueFilter.Overdue, TaskPriority.High), Today);

        Assert.Equal(new[] { "wanted" }, board.Lanes.SelectMany(l => l.Tasks).Select(t => t.Title));
    }

    [Fact]
    public void FilteringDoesNotMutateTheProject()
    {
        var project = ProjectWithColumns();
        AddTask(project, "a", project.Columns[0]).Priority = TaskPriority.Low;
        AddTask(project, "b", project.Columns[0]).Priority = TaskPriority.High;

        BoardProjection.Compute(project, new BoardFilter(Priority: TaskPriority.High), Today);

        Assert.Equal(2, project.Tasks.Count);
    }
}
