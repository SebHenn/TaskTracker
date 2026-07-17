using TaskTracker.Core.Models;

namespace TaskTracker.Core.Services
{
    public static class TimeTracking
    {
        public static void Start(TaskModel task, DateTime? nowUtc = null)
        {
            task.TimerStartedAtUtc ??= nowUtc ?? DateTime.UtcNow;
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
