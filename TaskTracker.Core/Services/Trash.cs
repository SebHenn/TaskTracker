using TaskTracker.Core.Models;
using TaskTracker.Core.Storage;

namespace TaskTracker.Core.Services
{
    /// <summary>
    /// Per-project recycle bin for deleted tasks.
    ///
    /// Deleting used to be final after an eight-second undo window: miss the bar, or
    /// delete the wrong card and notice a minute later, and the task was gone with its
    /// subtasks, notes and tracked time. Deletion now moves the task here and the undo
    /// bar is just the fastest way to reach the newest entry.
    /// </summary>
    public static class Trash
    {
        /// <summary>How long a deleted task stays recoverable.</summary>
        public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

        /// <summary>
        /// Hard cap per project, so one bulk delete of a huge board cannot bloat the
        /// save file for a month. The oldest entries go first.
        /// </summary>
        public const int MaxEntries = 200;

        /// <summary>
        /// Moves a task out of the board and into the trash. Returns the entry, which is
        /// what <see cref="Restore"/> takes — callers keep it for the undo bar.
        /// </summary>
        public static TrashedTask Delete(ProjectModel project, TaskModel task, DateTime? nowUtc = null)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(task);

            // A timer left running would keep accruing against a task nobody can see,
            // and restoring weeks later would credit it with the whole interval.
            TimeTracking.Stop(task, nowUtc);

            project.Tasks.Remove(task);

            var entry = new TrashedTask { Task = task, DeletedAtUtc = nowUtc ?? DateTime.UtcNow };
            project.Trash.Insert(0, entry);
            Trim(project);
            return entry;
        }

        /// <summary>
        /// Puts a task back on the board. False when the entry is not in this project's
        /// trash any more — it may have been restored already, or purged.
        /// </summary>
        public static bool Restore(ProjectModel project, TrashedTask entry)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(entry);

            if (!project.Trash.Remove(entry))
                return false;

            // The column it came from may have been deleted meanwhile; a ColumnId that
            // no longer resolves is exactly what NormalizeColumns exists to repair.
            project.Tasks.Add(entry.Task);
            ProjectStore.NormalizeColumns(project);
            return true;
        }

        /// <summary>Drops entries past their retention. Returns how many went.</summary>
        public static int Purge(ProjectModel project, DateTime? nowUtc = null)
        {
            ArgumentNullException.ThrowIfNull(project);

            var cutoff = (nowUtc ?? DateTime.UtcNow) - Retention;
            var removed = 0;
            for (var i = project.Trash.Count - 1; i >= 0; i--)
            {
                if (project.Trash[i].DeletedAtUtc >= cutoff)
                    continue;
                project.Trash.RemoveAt(i);
                removed++;
            }
            return removed + Trim(project);
        }

        /// <summary>Purges every project in a store; called on each load path.</summary>
        public static int Purge(StoreData data, DateTime? nowUtc = null)
        {
            ArgumentNullException.ThrowIfNull(data);
            return data.Projects.Sum(project => Purge(project, nowUtc));
        }

        /// <summary>Discards everything in a project's trash, irrecoverably.</summary>
        public static void Empty(ProjectModel project)
        {
            ArgumentNullException.ThrowIfNull(project);
            project.Trash.Clear();
        }

        /// <summary>Enforces <see cref="MaxEntries"/>, dropping the oldest first.</summary>
        private static int Trim(ProjectModel project)
        {
            var removed = 0;
            while (project.Trash.Count > MaxEntries)
            {
                // Newest first, so the tail is the oldest.
                project.Trash.RemoveAt(project.Trash.Count - 1);
                removed++;
            }
            return removed;
        }
    }
}
