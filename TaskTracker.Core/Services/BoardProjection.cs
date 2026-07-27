using TaskTracker.Core.Models;

namespace TaskTracker.Core.Services
{
    /// <summary>One board lane: a column and the tasks that land in it, in display order.</summary>
    public record BoardLane(BoardColumn Column, IReadOnlyList<TaskModel> Tasks);

    /// <summary>A whole board: lanes in column order, plus every label currently in use.</summary>
    public record BoardView(IReadOnlyList<BoardLane> Lanes, IReadOnlyList<string> Labels);

    /// <summary>
    /// Turns a project into the lanes a board renders — filtering, ordering, and
    /// grouping into columns.
    ///
    /// Deliberately pure and free of UI types: these are the rules most likely to
    /// be wrong, and living in Core they run under test on any OS, while the WPF
    /// layer is left only reconciling the result into view models.
    ///
    /// Projection tolerates tasks whose <see cref="TaskModel.ColumnId"/> is null or
    /// stale, because <see cref="ProjectModel.ColumnOf"/> falls back on done-state.
    /// Callers wanting the stronger column invariants (at least one column, at least
    /// one done column) run <see cref="Storage.ProjectStore.NormalizeColumns(ProjectModel)"/>
    /// first — that mutates, so it is not done here.
    /// </summary>
    public static class BoardProjection
    {
        /// <param name="labelFilter">
        /// Null or empty shows every task. The "all labels" entry in the UI is a
        /// localized string, so the caller maps it to null rather than Core
        /// knowing about display text.
        /// </param>
        public static BoardView Compute(ProjectModel project, string? labelFilter = null)
            => Compute(project, new BoardFilter(labelFilter));

        /// <param name="filter">What the board is narrowed to; <see cref="BoardFilter.None"/> shows everything.</param>
        /// <param name="today">
        /// Local calendar day the due buckets are measured against. Defaults to the
        /// clock; passed in by tests so due filtering is not a moving target.
        /// </param>
        public static BoardView Compute(ProjectModel project, BoardFilter filter, DateTime? today = null)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(filter);

            // Built from every task, not the filtered set: a label list that shrinks as
            // you filter would strand you on a value you could no longer switch away from.
            var labels = project.Tasks
                .SelectMany(task => task.Labels)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var day = (today ?? DateTime.Today).Date;
            var visible = project.Tasks.Where(task => filter.Matches(task, day));

            // Sort once over the whole project, then split — so a task's position is
            // decided by the same comparison regardless of which lane it lands in.
            var ordered = LaneSort.Apply(visible).ToList();

            var lanes = project.Columns
                .Select(column => new BoardLane(
                    column,
                    ordered.Where(task => project.ColumnOf(task)?.Id == column.Id).ToList()))
                .ToList();

            return new BoardView(lanes, labels);
        }
    }
}
