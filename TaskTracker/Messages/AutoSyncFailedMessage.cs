using System;

namespace TaskTracker.Messages
{
    /// <summary>
    /// Broadcast when a background sync fails for a project.
    ///
    /// Auto-sync used to swallow every failure into the log, which made a broken token
    /// or a renamed repository indistinguishable from "nothing to sync" — the board just
    /// silently stopped updating. The board shows this instead.
    /// </summary>
    /// <param name="ProjectId">Which project failed, so only its board reacts.</param>
    /// <param name="Reason">Message from the failure, for display.</param>
    public record AutoSyncFailedMessage(Guid ProjectId, string Reason);
}
