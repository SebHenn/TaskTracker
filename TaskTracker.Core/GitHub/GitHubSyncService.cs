using TaskTracker.Core.Models;

namespace TaskTracker.Core.GitHub
{
    /// <summary>An issue that became a new task in this sync.</summary>
    public record ImportedIssue(int Number, string Title);

    public record SyncResult(
        int Imported,
        int ClosedLocally,
        int ReopenedLocally,
        int ClosedOnGitHub,
        int ReopenedOnGitHub,
        int Unlinked)
    {
        /// <summary>
        /// Which issues arrived, not just how many — a notification wants to name the
        /// issue when only one came in. An init property rather than a positional
        /// parameter so existing callers keep compiling.
        /// </summary>
        public IReadOnlyList<ImportedIssue> ImportedIssues { get; init; } = Array.Empty<ImportedIssue>();

        /// <summary>Local tasks that became new GitHub issues in this sync.</summary>
        public int Exported { get; init; }

        /// <summary>
        /// Why the export pass could not file some tasks (missing token scope, rate limit),
        /// or null when it had nothing to report. Export failures leave the rest of the sync
        /// intact rather than aborting it, so this is the only place they surface.
        /// </summary>
        public string? ExportError { get; init; }

        public override string ToString()
        {
            var summary =
                $"imported {Imported}, exported {Exported}, closed locally {ClosedLocally}, " +
                $"reopened locally {ReopenedLocally}, closed on GitHub {ClosedOnGitHub}, " +
                $"reopened on GitHub {ReopenedOnGitHub}, unlinked {Unlinked}";
            return ExportError == null ? summary : $"{summary} (export failed: {ExportError})";
        }
    }

    /// <summary>
    /// Two-way sync between a project's tasks and the issues of its linked
    /// GitHub repository. Issues become tasks, unlinked tasks become issues, and
    /// state (open/closed vs. not-done/done) merges via a three-way compare against
    /// the state seen at the last sync. With binary state, "both sides changed"
    /// always means they agree (each flipped away from the same base), so no separate
    /// conflict resolution is needed. Title/body/labels of linked tasks always follow
    /// GitHub.
    /// </summary>
    public class GitHubSyncService
    {
        private const int MaxImportedDescriptionLength = 500;

        public async Task<SyncResult> SyncAsync(ProjectModel project, IGitHubApi api, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(project.GitHubOwner) || string.IsNullOrWhiteSpace(project.GitHubRepo))
                throw new InvalidOperationException($"Project '{project.Name}' is not linked to a GitHub repository.");

            var owner = project.GitHubOwner!;
            var repo = project.GitHubRepo!;

            int imported = 0, closedLocally = 0, reopenedLocally = 0, closedOnGitHub = 0, reopenedOnGitHub = 0, unlinked = 0;
            var importedIssues = new List<ImportedIssue>();

            var issues = (await api.ListIssuesAsync(owner, repo, "all", ct))
                .Where(i => !i.IsPullRequest)
                .ToList();

            foreach (var issue in issues)
            {
                ct.ThrowIfCancellationRequested();
                var task = project.Tasks.FirstOrDefault(t => t.GitHubIssueNumber == issue.Number);
                if (task == null)
                {
                    // Only open issues become new tasks; historic closed issues stay out.
                    if (issue.State != "open")
                        continue;
                    task = new TaskModel
                    {
                        Title = issue.Title,
                        Description = TrimBody(issue.Body),
                        GitHubIssueNumber = issue.Number,
                        LastSyncedIssueState = issue.State,
                        LastSyncedTitle = issue.Title,
                        CreatedAtUtc = DateTime.UtcNow,
                        ColumnId = project.FirstColumn?.Id,
                        DueDate = issue.MilestoneDueOn?.Date,
                    };
                    foreach (var label in issue.Labels)
                        task.Labels.Add(label);
                    project.Tasks.Add(task);
                    imported++;
                    importedIssues.Add(new ImportedIssue(issue.Number, issue.Title));
                    continue;
                }

                // Title merges 3-way against the snapshot from the last sync:
                // a local-only rename is pushed to GitHub; anything else follows remote.
                var baseTitle = task.LastSyncedTitle;
                if (baseTitle != null && task.Title != baseTitle && issue.Title == baseTitle)
                {
                    await api.UpdateIssueTitleAsync(owner, repo, issue.Number, task.Title, ct);
                }
                else if (task.Title != issue.Title)
                {
                    task.Title = issue.Title;
                }
                task.LastSyncedTitle = task.Title;

                // Description and labels stay remote-wins (imported bodies are
                // truncated, so pushing them back would corrupt the issue). Should that
                // ever become two-way, the outgoing body has to go through
                // IssueBodyMarkers.Preserve — bots find their own issues by a trailing
                // HTML comment that truncation has already dropped from the description.
                var trimmedBody = TrimBody(issue.Body);
                if (task.Description != trimmedBody)
                    task.Description = trimmedBody;
                if (!task.Labels.SequenceEqual(issue.Labels))
                {
                    task.Labels.Clear();
                    foreach (var label in issue.Labels)
                        task.Labels.Add(label);
                }

                // A milestone due date on the issue drives the task's due date.
                if (issue.MilestoneDueOn.HasValue && task.DueDate != issue.MilestoneDueOn.Value.Date)
                    task.DueDate = issue.MilestoneDueOn.Value.Date;

                var baseState = task.LastSyncedIssueState ?? (task.IsDone ? "closed" : "open");
                var localState = task.IsDone ? "closed" : "open";
                var remoteState = issue.State;
                var remoteChanged = remoteState != baseState;
                var localChanged = localState != baseState;

                if (remoteChanged && !localChanged)
                {
                    ApplyRemoteState(project, task, remoteState, ref closedLocally, ref reopenedLocally);
                }
                else if (localChanged && !remoteChanged)
                {
                    await PushLocalState(api, owner, repo, task, localState, ct);
                    Count(localState, ref closedOnGitHub, ref reopenedOnGitHub);
                }
                // else: unchanged, or both flipped to the same state — nothing to transfer.

                task.LastSyncedIssueState = task.IsDone ? "closed" : "open";
            }

            // Issues that disappeared (deleted/transferred): unlink, keep the task.
            var issueNumbers = issues.Select(i => i.Number).ToHashSet();
            foreach (var task in project.Tasks.Where(t => t.GitHubIssueNumber.HasValue && !issueNumbers.Contains(t.GitHubIssueNumber.Value)))
            {
                task.GitHubIssueNumber = null;
                task.LastSyncedIssueState = null;
                task.GitHubIssueVanished = true;
                unlinked++;
            }

            // Export last, so the numbers handed out here are not run through the
            // unlink pass above — which knows only the issues the API listed.
            var (exported, exportError) = await ExportNewTasksAsync(project, api, ct);

            project.LastSyncedAtUtc = DateTime.UtcNow;
            return new SyncResult(imported, closedLocally, reopenedLocally, closedOnGitHub, reopenedOnGitHub, unlinked)
            {
                ImportedIssues = importedIssues,
                Exported = exported,
                ExportError = exportError,
            };
        }

        /// <summary>
        /// Files every local task that has no issue yet, mirroring the import rule:
        /// only open issues become tasks, so only tasks that are not done become issues.
        /// A board linked to a repository after the fact therefore exports its open work
        /// and leaves its finished history alone.
        /// </summary>
        /// <remarks>
        /// A failed creation is counted and reported, never thrown: the tasks already
        /// filed in this pass carry live issue numbers that only exist in memory until
        /// the caller saves. Aborting here would drop those links on the floor and file
        /// every one of them a second time on the next sync.
        /// </remarks>
        private static async Task<(int Exported, string? Error)> ExportNewTasksAsync(
            ProjectModel project, IGitHubApi api, CancellationToken ct)
        {
            var owner = project.GitHubOwner!;
            var repo = project.GitHubRepo!;
            var pending = project.Tasks
                .Where(t => t.GitHubIssueNumber == null
                            && !t.IsDone
                            && !t.GitHubIssueVanished
                            && !string.IsNullOrWhiteSpace(t.Title))
                .ToList();

            int exported = 0;
            string? error = null;
            foreach (var task in pending)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var number = await api.CreateIssueAsync(owner, repo, task.Title, task.Description, task.Labels.ToList(), ct);
                    task.GitHubIssueNumber = number;
                    task.LastSyncedIssueState = "open";
                    task.LastSyncedTitle = task.Title;
                    exported++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // First failure only. A dead token fails once per pending task, and
                    // repeating the same message once per task tells nobody anything new.
                    error ??= ex.Message;
                }
            }

            return (exported, error);
        }

        /// <summary>
        /// Creates a GitHub issue from a local, unlinked task and links it.
        /// A task that is already done gets its new issue closed immediately.
        /// </summary>
        public async Task<int> PushTaskAsync(ProjectModel project, TaskModel task, IGitHubApi api, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(project.GitHubOwner) || string.IsNullOrWhiteSpace(project.GitHubRepo))
                throw new InvalidOperationException($"Project '{project.Name}' is not linked to a GitHub repository.");
            if (task.GitHubIssueNumber.HasValue)
                throw new InvalidOperationException($"Task '{task.Title}' is already linked to issue #{task.GitHubIssueNumber}.");

            var number = await api.CreateIssueAsync(project.GitHubOwner!, project.GitHubRepo!, task.Title, task.Description, task.Labels.ToList(), ct);
            task.GitHubIssueNumber = number;
            task.LastSyncedIssueState = "open";
            task.LastSyncedTitle = task.Title;
            // Pushing by hand is the way back for a task whose issue was deleted: the
            // user asked for this one explicitly, so stop treating it as opted out.
            task.GitHubIssueVanished = false;

            if (task.IsDone)
            {
                await api.CloseIssueAsync(project.GitHubOwner!, project.GitHubRepo!, number, ct);
                task.LastSyncedIssueState = "closed";
            }

            return number;
        }

        private static void ApplyRemoteState(ProjectModel project, TaskModel task, string remoteState, ref int closedLocally, ref int reopenedLocally)
        {
            var shouldBeDone = remoteState == "closed";
            if (task.IsDone == shouldBeDone)
                return;
            var target = shouldBeDone ? project.FirstDoneColumn : project.FirstColumn;
            if (target != null)
                project.MoveTaskToColumn(task, target);
            else
                task.IsDone = shouldBeDone;
            if (shouldBeDone) closedLocally++; else reopenedLocally++;
        }

        private static Task PushLocalState(IGitHubApi api, string owner, string repo, TaskModel task, string localState, CancellationToken ct)
            => localState == "closed"
                ? api.CloseIssueAsync(owner, repo, task.GitHubIssueNumber!.Value, ct)
                : api.ReopenIssueAsync(owner, repo, task.GitHubIssueNumber!.Value, ct);

        private static void Count(string localState, ref int closedOnGitHub, ref int reopenedOnGitHub)
        {
            if (localState == "closed") closedOnGitHub++; else reopenedOnGitHub++;
        }

        private static string TrimBody(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "";
            var trimmed = body.Trim();
            return trimmed.Length <= MaxImportedDescriptionLength
                ? trimmed
                : trimmed[..MaxImportedDescriptionLength] + "…";
        }
    }
}
