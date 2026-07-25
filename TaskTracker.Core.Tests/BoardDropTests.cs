using TaskTracker.Core.Models;
using TaskTracker.Core.Services;

namespace TaskTracker.Core.Tests;

public class BoardDropTests
{
    private static List<TaskModel> Lane(params string[] titles)
    {
        var tasks = new List<TaskModel>();
        for (var i = 0; i < titles.Length; i++)
            tasks.Add(new TaskModel { Title = titles[i], SortOrder = i + 1 });
        return tasks;
    }

    private static TaskModel Find(List<TaskModel> lane, string title) => lane.Single(t => t.Title == title);

    /// <summary>
    /// The order the board would draw after the drop: what
    /// <see cref="LaneSort"/> makes of the renumbered lane.
    /// </summary>
    private static string[] Drop(List<TaskModel> lane, string dragged, int visualIndex)
    {
        var task = Find(lane, dragged);
        var moveIndex = BoardDrop.ResolveMoveIndex(visualIndex, lane.IndexOf(task));
        BoardDrop.PlaceInLane(lane, task, moveIndex);
        return LaneSort.Apply(lane).Select(t => t.Title).ToArray();
    }

    // ---- Frame-of-reference conversion ----

    [Theory]
    [InlineData(0, -1, 0)]
    [InlineData(1, -1, 1)]
    [InlineData(3, -1, 3)]
    public void ResolveMoveIndex_FromAnotherLane_DisplacesNothing(int visualIndex, int draggedIndex, int expected)
    {
        Assert.Equal(expected, BoardDrop.ResolveMoveIndex(visualIndex, draggedIndex));
    }

    [Theory]
    [InlineData(0, 2, 0)] // gap above the card: unaffected
    [InlineData(2, 2, 2)] // the gap immediately above the card itself
    [InlineData(3, 2, 2)] // gap below: shifts up once the card is lifted out
    [InlineData(5, 1, 4)]
    public void ResolveMoveIndex_WithinTheSameLane_AccountsForLiftingTheCardOut(int visualIndex, int draggedIndex, int expected)
    {
        Assert.Equal(expected, BoardDrop.ResolveMoveIndex(visualIndex, draggedIndex));
    }

    // ---- Dropping within a lane ----
    // A card dropped into either gap touching its own position must not move; the
    // classic failure is a one-slot drop landing two slots away, or doing nothing.

    [Fact]
    public void DroppingACardIntoTheGapAboveItself_ChangesNothing()
    {
        Assert.Equal(new[] { "A", "B", "C" }, Drop(Lane("A", "B", "C"), "B", 1));
    }

    [Fact]
    public void DroppingACardIntoTheGapBelowItself_ChangesNothing()
    {
        Assert.Equal(new[] { "A", "B", "C" }, Drop(Lane("A", "B", "C"), "B", 2));
    }

    [Fact]
    public void DroppingTheFirstCardOneSlotDown_MovesItExactlyOneSlot()
    {
        Assert.Equal(new[] { "B", "A", "C" }, Drop(Lane("A", "B", "C"), "A", 2));
    }

    [Fact]
    public void DroppingTheLastCardOneSlotUp_MovesItExactlyOneSlot()
    {
        Assert.Equal(new[] { "A", "C", "B" }, Drop(Lane("A", "B", "C"), "C", 1));
    }

    [Fact]
    public void DroppingTheFirstCardAtTheEnd_PutsItLast()
    {
        Assert.Equal(new[] { "B", "C", "A" }, Drop(Lane("A", "B", "C"), "A", 3));
    }

    [Fact]
    public void DroppingTheLastCardAtTheTop_PutsItFirst()
    {
        Assert.Equal(new[] { "C", "A", "B" }, Drop(Lane("A", "B", "C"), "C", 0));
    }

    // ---- Dropping from another lane ----

    [Fact]
    public void ACardFromAnotherLane_LandsInTheGapItWasDroppedInto()
    {
        var target = Lane("X", "Y");
        var incoming = new TaskModel { Title = "T" };

        BoardDrop.PlaceInLane(target, incoming, BoardDrop.ResolveMoveIndex(1, draggedIndex: -1));
        target.Add(incoming);

        Assert.Equal(new[] { "X", "T", "Y" }, LaneSort.Apply(target).Select(t => t.Title));
    }

    [Fact]
    public void ACardDroppedOnAnEmptyLane_IsTheOnlyCardAndIsPlaced()
    {
        var incoming = new TaskModel { Title = "T" };

        var order = BoardDrop.PlaceInLane([], incoming, int.MaxValue);

        Assert.Equal(new[] { incoming }, order);
        Assert.Equal(1, incoming.SortOrder);
    }

    // ---- Renumbering ----

    [Fact]
    public void PlaceInLane_GivesEveryCardAPositionStartingAtOne()
    {
        // Unplaced cards would otherwise keep a null SortOrder and be sorted by
        // priority instead of holding the position the drop just gave them.
        var lane = new List<TaskModel>
        {
            new() { Title = "A", Priority = TaskPriority.Low },
            new() { Title = "B", Priority = TaskPriority.High },
        };
        var dragged = new TaskModel { Title = "T" };

        var order = BoardDrop.PlaceInLane(lane, dragged, 1);

        Assert.Equal(new[] { "A", "T", "B" }, order.Select(t => t.Title));
        Assert.Equal([1d, 2d, 3d], order.Select(t => t.SortOrder));
    }

    [Fact]
    public void PlaceInLane_DoesNotDuplicateACardAlreadyInTheLane()
    {
        var lane = Lane("A", "B");

        var order = BoardDrop.PlaceInLane(lane, Find(lane, "A"), 1);

        Assert.Equal(2, order.Count);
        Assert.Equal(new[] { "B", "A" }, order.Select(t => t.Title));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(int.MaxValue)]
    public void PlaceInLane_ClampsAnIndexOutsideTheLane(int insertIndex)
    {
        var lane = Lane("A", "B");
        var incoming = new TaskModel { Title = "T" };

        var order = BoardDrop.PlaceInLane(lane, incoming, insertIndex);

        Assert.Equal(3, order.Count);
        Assert.Contains(incoming, order);
    }
}
