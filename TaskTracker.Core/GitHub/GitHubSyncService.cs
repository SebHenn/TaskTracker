using TaskTracker.Core.Models;

namespace TaskTracker.Core.GitHub
{
    public record SyncResult(
        int Imported,
        int ClosedLocally,
        int ReopenedLocally,
        int ClosedOnGitHub,
        int ReopenedOnGitHub,
        int Unlinked)
    {
        public override string ToString() =>
            $"imported {Imported}, closed locally {ClosedLocally}, reopened locally {ReopenedLocally}, " +
            $"closed on GitHub {ClosedOnGitHub}, reopened on GitHub {ReopenedOnGitHub}, unlinked {Unlinked}";
    }

    /// <summary>
    /// Two-way sync between a project's tasks and the issues of its linked
    /// GitHub repository. State (open/closed vs. not-done/done) merges via a
    /// three-way compare against the state seen at the last sync. With binary
    /// state, "both sides changed" always means they agree (each flipped away
    /// from the same base), so no separate conflict resolution is needed.
    /// Title/body/labels of linked tasks always follow GitHub.
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
                // truncated, so pushing them back would corrupt the issue).
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
                unlinked++;
            }

            project.LastSyncedAtUtc = DateTime.UtcNow;
            return new SyncResult(imported, closedLocally, reopenedLocally, closedOnGitHub, reopenedOnGitHub, unlinked);
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
