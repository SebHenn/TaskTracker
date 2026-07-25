using TaskTracker.Core.Models;

namespace TaskTracker.Core.Services
{
    /// <summary>
    /// The index arithmetic behind dropping a card on a lane.
    ///
    /// Lives here rather than in the view because the two frames of reference — the
    /// lane as drawn, which still holds the card being dragged, and the lane without
    /// it — are exactly where an off-by-one hides, and this is the layer where that
    /// can be pinned down by a test.
    /// </summary>
    public static class BoardDrop
    {
        /// <summary>
        /// Converts a drop position in the lane as drawn into the same position in the
        /// lane with the dragged card taken out, which is the frame
        /// <see cref="PlaceInLane"/> works in.
        /// </summary>
        /// <param name="visualIndex">Gap the card was dropped into, counting the card itself.</param>
        /// <param name="draggedIndex">
        /// Where the card currently sits in this lane, or -1 when it is coming from
        /// another one.
        /// </param>
        public static int ResolveMoveIndex(int visualIndex, int draggedIndex)
        {
            // Lifting the card out shifts everything below it up one, so a gap below
            // its old position is one lower once it's gone. Gaps above are unaffected,
            // and a card from another lane displaces nothing.
            return draggedIndex >= 0 && draggedIndex < visualIndex ? visualIndex - 1 : visualIndex;
        }

        /// <summary>
        /// Puts <paramref name="task"/> at <paramref name="insertIndex"/> in the lane
        /// and renumbers the lane's explicit order, so the position survives the next
        /// projection through <see cref="LaneSort"/>.
        /// </summary>
        /// <param name="laneTasks">The lane as drawn; the task itself may be in it.</param>
        /// <returns>The lane's new order.</returns>
        public static IReadOnlyList<TaskModel> PlaceInLane(IEnumerable<TaskModel> laneTasks, TaskModel task, int insertIndex)
        {
            var order = laneTasks.Where(t => t.Id != task.Id).ToList();
            order.Insert(Math.Clamp(insertIndex, 0, order.Count), task);

            // 1-based: LaneSort treats a null SortOrder as "unplaced, sort by priority",
            // so starting at 0 would be indistinguishable from placed-at-the-top for
            // nothing while still being a real position.
            for (var i = 0; i < order.Count; i++)
                order[i].SortOrder = i + 1;

            return order;
        }
    }
}
