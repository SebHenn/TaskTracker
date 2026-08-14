using TaskTracker.Core.Models;
using TaskTracker.Core.Storage;

namespace TaskTracker.Core.Services
{
    /// <summary>
    /// Board column edits, with the invariants <see cref="ProjectStore.NormalizeColumns"/>
    /// repairs on load enforced up front instead.
    ///
    /// These rules used to live in the desktop's ColumnsViewModel, which meant the MCP
    /// server could not manage columns at all without reimplementing them — and a second
    /// implementation of "you may not delete the last done column" is one that eventually
    /// disagrees. Both heads call this.
    /// </summary>
    public static class ColumnOperations
    {
        /// <summary>Why an edit was refused, or null when it was applied.</summary>
        public static class Refusal
        {
            public const string LastColumn = "A project must keep at least one column.";
            public const string LastDoneColumn = "A project must keep at least one done column.";
        }

        /// <summary>
        /// Adds a column, optionally at a position (clamped into range; null appends).
        /// </summary>
        public static BoardColumn Add(ProjectModel project, string name, bool isDoneColumn = false, int? position = null, int? wipLimit = null)
        {
            ArgumentNullException.ThrowIfNull(project);

            var column = new BoardColumn
            {
                Name = string.IsNullOrWhiteSpace(name) ? "New column" : name.Trim(),
                IsDoneColumn = isDoneColumn,
                WipLimit = NormalizeWip(wipLimit),
            };

            var index = position == null
                ? project.Columns.Count
                : Math.Clamp(position.Value, 0, project.Columns.Count);
            project.Columns.Insert(index, column);
            return column;
        }

        /// <summary>
        /// Renames a column and/or changes its done flag or WIP limit. Clearing the done
        /// flag on the only done column is refused, and turning it on re-derives IsDone
        /// for the tasks sitting there — otherwise the column says "done" while its cards
        /// say otherwise.
        /// </summary>
        public static string? Update(ProjectModel project, BoardColumn column, string? name = null, bool? isDoneColumn = null, int? wipLimit = null, bool clearWipLimit = false)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(column);

            if (isDoneColumn == false && column.IsDoneColumn && project.Columns.Count(c => c.IsDoneColumn) == 1)
                return Refusal.LastDoneColumn;

            if (!string.IsNullOrWhiteSpace(name))
                column.Name = name.Trim();

            if (clearWipLimit)
                column.WipLimit = null;
            else if (wipLimit.HasValue)
                column.WipLimit = NormalizeWip(wipLimit);

            if (isDoneColumn.HasValue && isDoneColumn.Value != column.IsDoneColumn)
            {
                column.IsDoneColumn = isDoneColumn.Value;
                // Re-place every task in this column so IsDone follows the new meaning.
                foreach (var task in project.Tasks.Where(t => t.ColumnId == column.Id).ToList())
                    project.MoveTaskToColumn(task, column);
            }

            return null;
        }

        /// <summary>
        /// Removes a column, rehoming its tasks. Returns a refusal message when the edit
        /// would break an invariant, otherwise null.
        /// </summary>
        public static string? Delete(ProjectModel project, BoardColumn column, BoardColumn? moveTasksTo = null)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(column);

            if (project.Columns.Count <= 1)
                return Refusal.LastColumn;
            if (column.IsDoneColumn && project.Columns.Count(c => c.IsDoneColumn) == 1)
                return Refusal.LastDoneColumn;

            project.Columns.Remove(column);

            var fallback = moveTasksTo != null && project.Columns.Contains(moveTasksTo)
                ? moveTasksTo
                : project.FirstColumn ?? project.Columns[0];

            foreach (var task in project.Tasks.Where(t => t.ColumnId == column.Id).ToList())
                project.MoveTaskToColumn(task, fallback);

            return null;
        }

        /// <summary>
        /// Reorders columns to match <paramref name="orderedIds"/>, which must be a
        /// permutation of the project's column ids — a partial list would silently drop
        /// columns and the tasks pointing at them.
        /// </summary>
        public static string? Reorder(ProjectModel project, IReadOnlyList<Guid> orderedIds)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(orderedIds);

            var current = project.Columns.Select(c => c.Id).ToHashSet();
            if (orderedIds.Count != current.Count || !orderedIds.ToHashSet().SetEquals(current))
                return $"The id list must contain every column of '{project.Name}' exactly once ({current.Count} expected).";

            var byId = project.Columns.ToDictionary(c => c.Id);
            project.Columns.Clear();
            foreach (var id in orderedIds)
                project.Columns.Add(byId[id]);

            return null;
        }

        /// <summary>Moves one column by <paramref name="delta"/> positions, clamped.</summary>
        public static void Move(ProjectModel project, BoardColumn column, int delta)
        {
            ArgumentNullException.ThrowIfNull(project);

            var index = project.Columns.IndexOf(column);
            if (index < 0)
                return;
            var target = Math.Clamp(index + delta, 0, project.Columns.Count - 1);
            if (target != index)
                project.Columns.Move(index, target);
        }

        /// <summary>Zero and negative WIP limits mean "no limit", not "nothing allowed".</summary>
        private static int? NormalizeWip(int? wipLimit) => wipLimit is > 0 ? wipLimit : null;
    }
}
