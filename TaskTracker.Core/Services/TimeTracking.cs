using TaskTracker.Core.Models;

namespace TaskTracker.Core.Services
{
    public static class TimeTracking
    {
        public static void Start(TaskModel task, DateTime? nowUtc = null)
        {
            task.TimerStartedAtUtc ??= nowUtc ?? DateTime.UtcNow;
        }

        /// <summary>
        /// Starts a timer after stopping every other one in the store, and returns the
        /// task whose timer it stopped (null when none was running).
        ///
        /// One timer at a time across the whole store is the rule the desktop app
        /// enforces; it lived in the board view model, so the MCP server would happily
        /// have left two running and double-counted the overlap.
        /// </summary>
        public static TaskModel? StartExclusive(IEnumerable<ProjectModel> projects, TaskModel task, DateTime? nowUtc = null)
        {
            ArgumentNullException.ThrowIfNull(projects);
            ArgumentNullException.ThrowIfNull(task);

            TaskModel? stopped = null;
            foreach (var running in projects.SelectMany(p => p.Tasks).Where(t => t.TimerStartedAtUtc != null && t.Id != task.Id).ToList())
            {
                Stop(running, nowUtc);
                stopped ??= running;
            }

            Start(task, nowUtc);
            return stopped;
        }

        public static void Stop(TaskModel task, DateTime? nowUtc = null)
        {
            if (task.TimerStartedAtUtc == null)
                return;
            var elapsed = ((nowUtc ?? DateTime.UtcNow) - task.TimerStartedAtUtc.Value).TotalSeconds;
            task.TrackedSeconds += Math.Max(0, elapsed);
            task.TimerStartedAtUtc = null;
        }

        /// <summary>Accumulated time plus the currently running timer, if any.</summary>
        public static double TotalSeconds(TaskModel task, DateTime? nowUtc = null)
        {
            var total = task.TrackedSeconds;
            if (task.TimerStartedAtUtc != null)
                total += Math.Max(0, ((nowUtc ?? DateTime.UtcNow) - task.TimerStartedAtUtc.Value).TotalSeconds);
            return total;
        }

        /// <summary>"h:mm" display, e.g. "1:05" or "0:00".</summary>
        public static string Format(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return $"{(int)span.TotalHours}:{span.Minutes:00}";
        }
    }
}
