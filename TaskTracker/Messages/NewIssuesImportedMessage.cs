using System;
using System.Collections.Generic;
using TaskTracker.Core.GitHub;

namespace TaskTracker.Messages
{
    /// <summary>
    /// Broadcast when a background sync pulled in issues that were not there before, so
    /// the tray can say so. Carries the issues rather than a count because a single new
    /// issue is worth naming in the balloon.
    /// </summary>
    /// <param name="ProjectId">Which project received them, so a click can open it.</param>
    /// <param name="ProjectName">For the balloon text, avoiding a lookup in the handler.</param>
    /// <param name="Issues">The imported issues, newest sync first.</param>
    public record NewIssuesImportedMessage(Guid ProjectId, string ProjectName, IReadOnlyList<ImportedIssue> Issues);
}
