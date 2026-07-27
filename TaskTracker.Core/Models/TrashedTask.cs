using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace TaskTracker.Core.Models
{
    /// <summary>
    /// A deleted task, kept so it can be put back. The whole
    /// <see cref="TaskModel"/> is retained rather than a summary, because restoring
    /// has to bring back the subtasks, activity, labels, tracked time and column —
    /// anything less makes "restore" quietly lossy.
    /// </summary>
    public partial class TrashedTask : ObservableObject
    {
        [ObservableProperty]
        private TaskModel _task = new();

        /// <summary>When it was deleted; the retention clock reads this.</summary>
        [ObservableProperty]
        private DateTime _deletedAtUtc = DateTime.UtcNow;
    }
}
