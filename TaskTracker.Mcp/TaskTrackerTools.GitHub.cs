using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using TaskTracker.Core.GitHub;
using TaskTracker.Core.Models;

namespace TaskTracker.Mcp;

/// <summary>
/// Linking projects to GitHub repositories, and reporting on the link.
///
/// Linking used to be possible only in the desktop app, so github_sync's advice when a
/// project was unlinked was "go open the app" — which is exactly the thing an agent
/// cannot do.
/// </summary>
public static partial class TaskTrackerTools
{
    [McpServerTool(Name = "link_github", Idempotent = true, Title = "Link project to a GitHub repository"),
     Description("Link a project to a GitHub repository so github_sync can two-way sync its issues.")]
    public static string LinkGitHub(
        [Description("Project id (GUID from list_projects)")] string projectId,
        [Description("Repository as 'owner/repo', or just the owner when repo is given separately")] string owner,
        [Description("Repository name, when not included in owner")] string? repo = null)
    {
        var (parsedOwner, parsedRepo) = ParseRepository(owner, repo);

        string? name = null;
        UpdateData(data =>
        {
            var project = FindProject(data, projectId);
            name = project.Name;
            project.GitHubOwner = parsedOwner;
            project.GitHubRepo = parsedRepo;
            // A previous link's sync clock does not describe this repository.
            project.LastSyncedAtUtc = null;
        });

        return ToJson(new
        {
            linked = true,
            project = name,
            repository = $"{parsedOwner}/{parsedRepo}",
            next = "Run github_sync to import issues and export unlinked tasks.",
        });
    }

    [McpServerTool(Name = "unlink_github", Idempotent = true, Title = "Unlink project from GitHub"),
     Description("Stop syncing a project with its GitHub repository. Local tasks and their issue numbers are kept.")]
    public static string UnlinkGitHub(
        [Description("Project id (GUID from list_projects)")] string projectId)
    {
        string? name = null;
        string? was = null;
        UpdateData(data =>
        {
            var project = FindProject(data, projectId);
            name = project.Name;
            was = project.IsGitHubLinked ? $"{project.GitHubOwner}/{project.GitHubRepo}" : null;
            project.GitHubOwner = null;
            project.GitHubRepo = null;
            project.LastSyncedAtUtc = null;
            // Issue numbers stay on the tasks on purpose: relinking the same repository
            // then picks up where it left off instead of filing everything a second time.
        });

        return ToJson(new { unlinked = true, project = name, wasLinkedTo = was });
    }

    [McpServerTool(Name = "github_status", ReadOnly = true, Title = "GitHub link status"),
     Description("Whether a project is linked to GitHub, when it last synced, and what a sync would have to do. Makes no network calls.")]
    public static string GitHubStatus(
        [Description("Project id (GUID from list_projects)")] string projectId)
    {
        var data = LoadData();
        var project = FindProject(data, projectId);

        var linked = project.Tasks.Where(t => t.GitHubIssueNumber.HasValue).ToList();
        var exportable = project.Tasks.Where(t => t.GitHubIssueNumber == null && !t.IsDone && !t.GitHubIssueVanished && !string.IsNullOrWhiteSpace(t.Title)).ToList();
        var vanished = project.Tasks.Count(t => t.GitHubIssueVanished);

        return ToJson(new
        {
            project = project.Name,
            isLinked = project.IsGitHubLinked,
            repository = project.IsGitHubLinked ? $"{project.GitHubOwner}/{project.GitHubRepo}" : null,
            project.LastSyncedAtUtc,
            hasToken = HasToken(),
            linkedTasks = linked.Count,
            tasksAwaitingExport = exportable.Count,
            // Tasks whose issue was deleted on GitHub. Sync deliberately leaves these
            // alone, so without surfacing the count they look like a sync that silently
            // does nothing.
            unlinkedByVanishedIssue = vanished,
            note = project.IsGitHubLinked
                ? null
                : "Not linked. Use link_github with owner/repo, then github_sync.",
        });
    }

    [McpServerTool(Name = "push_task_to_github", Idempotent = false, OpenWorld = true, Title = "File a task as a GitHub issue"),
     Description("Create a GitHub issue from a single task and link it, without running a full sync.")]
    public static async Task<string> PushTaskToGitHub(
        [Description("Task id (GUID)")] string taskId)
    {
        var token = RequireToken();
        var store = CreateStore();

        // Same shape as github_sync: never hold the cross-process lock across the network.
        var data = LoadData();
        var (project, task) = FindTask(data, taskId);
        if (!project.IsGitHubLinked)
            throw new McpException($"Project '{project.Name}' is not linked to a GitHub repository. Use link_github first.");
        if (task.GitHubIssueNumber.HasValue)
            throw new McpException($"Task '{task.Title}' is already linked to issue #{task.GitHubIssueNumber}.");

        var baseRevision = data.Revision;
        int number;
        try
        {
            number = await Sync.PushTaskAsync(project, task, ApiFactory.Create(token));
        }
        catch (HttpRequestException ex)
        {
            throw new McpException($"Could not create the issue: {ex.Message}");
        }

        if (!store.TrySaveIfUnchanged(data, baseRevision))
            throw new McpException(
                $"Issue #{number} was created on GitHub, but the TaskTracker store changed before the link could be saved. " +
                "Run github_sync to reconcile.");

        return ToJson(new
        {
            issueNumber = number,
            url = $"https://github.com/{project.GitHubOwner}/{project.GitHubRepo}/issues/{number}",
            task = ToDto(project, task),
        });
    }

    /// <summary>Accepts "owner/repo" in one argument or split across two.</summary>
    private static (string Owner, string Repo) ParseRepository(string owner, string? repo)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new McpException("Provide a repository as 'owner/repo'.");

        if (!string.IsNullOrWhiteSpace(repo))
            return (owner.Trim(), repo.Trim());

        var parts = owner.Trim().Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            throw new McpException($"'{owner}' is not a repository. Use 'owner/repo', e.g. octocat/hello-world.");
        return (parts[0], parts[1]);
    }

    private static bool HasToken()
    {
        var settings = CreateSettingsStore().Load();
        return !string.IsNullOrEmpty(TokenProtector.Unprotect(settings.GitHubTokenProtected, settings.GitHubTokenIsPlaintext));
    }

    private static string RequireToken()
    {
        var settings = CreateSettingsStore().Load();
        var token = TokenProtector.Unprotect(settings.GitHubTokenProtected, settings.GitHubTokenIsPlaintext);
        return string.IsNullOrEmpty(token)
            ? throw new McpException("No GitHub token configured. Save a Personal Access Token in the TaskTracker app settings first.")
            : token;
    }
}
