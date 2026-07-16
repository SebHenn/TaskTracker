using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using TaskTracker.Core.GitHub;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;
using TaskTracker.Core.Storage;

namespace TaskTracker.Mcp;

/// <summary>
/// MCP tools over the TaskTracker store. Every call is stateless:
/// lock → load → mutate → save, so a running TaskTracker desktop app picks the
/// change up live (and vice versa).
/// </summary>
[McpServerToolType]
public static class TaskTrackerTools
{
    /// <summary>Overridable for tests (points at a temp directory there).</summary>
    internal static Func<ProjectStore> CreateStore { get; set; } = () => new ProjectStore();
    internal static Func<SettingsStore> CreateSettingsStore { get; set; } = () => new SettingsStore();

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private record ProjectSummary(Guid Id, string Name, string Description, int OpenTasks, int DoneTasks, bool IsArchived, bool IsFavourite, string? GitHubRepo);
    private record TaskDto(Guid Id, string Title, string Description, bool IsDone, string Priority, DateTime? DueDate, IReadOnlyList<string> Labels, int? GitHubIssueNumber, DateTime? CompletedAtUtc);
    private record TaskWithProjectDto(Guid ProjectId, string ProjectName, TaskDto Task);

    private static TaskDto ToDto(TaskModel t) => new(
        t.Id, t.Title, t.Description, t.IsDone, t.Priority.ToString(), t.DueDate, t.Labels.ToList(), t.GitHubIssueNumber, t.CompletedAtUtc);

    private static string ToJson<T>(T value) => JsonSerializer.Serialize(value, Json);

    private static ProjectModel FindProject(StoreData data, string projectId)
    {
        if (!Guid.TryParse(projectId, out var id))
            throw new McpException($"'{projectId}' is not a valid project id (expected a GUID).");
        return data.Projects.FirstOrDefault(p => p.Id == id)
               ?? throw new McpException($"No project with id {projectId}. Use list_projects to see available projects.");
    }

    private static (ProjectModel Project, TaskModel Task) FindTask(StoreData data, string taskId)
    {
        if (!Guid.TryParse(taskId, out var id))
            throw new McpException($"'{taskId}' is not a valid task id (expected a GUID).");
        foreach (var project in data.Projects)
        {
            var task = project.Tasks.FirstOrDefault(t => t.Id == id);
            if (task != null)
                return (project, task);
        }
        throw new McpException($"No task with id {taskId}. Use list_tasks or search_tasks to find task ids.");
    }

    private static TaskPriority ParsePriority(string priority)
        => Enum.TryParse<TaskPriority>(priority, ignoreCase: true, out var parsed)
            ? parsed
            : throw new McpException($"Invalid priority '{priority}'. Use low, medium, or high.");

    private static DateTime ParseDate(string dueDate)
        => DateTime.TryParse(dueDate, out var parsed)
            ? parsed.Date
            : throw new McpException($"Invalid date '{dueDate}'. Use ISO format, e.g. 2026-08-01.");

    [McpServerTool(Name = "list_projects"), Description("List all TaskTracker projects with task counts.")]
    public static string ListProjects(
        [Description("Include archived projects (default false)")] bool includeArchived = false)
    {
        var data = CreateStore().Load();
        var projects = data.Projects
            .Where(p => includeArchived || !p.IsArchived)
            .Select(p => new ProjectSummary(
                p.Id, p.Name, p.Description,
                p.Tasks.Count(t => !t.IsDone), p.Tasks.Count(t => t.IsDone),
                p.IsArchived, p.IsFavourite,
                p.IsGitHubLinked ? $"{p.GitHubOwner}/{p.GitHubRepo}" : null));
        return ToJson(projects);
    }

    [McpServerTool(Name = "get_project"), Description("Get one project including all of its tasks.")]
    public static string GetProject(
        [Description("Project id (GUID from list_projects)")] string projectId)
    {
        var data = CreateStore().Load();
        var project = FindProject(data, projectId);
        return ToJson(new
        {
            project.Id,
            project.Name,
            project.Description,
            project.IsArchived,
            project.IsFavourite,
            GitHubRepo = project.IsGitHubLinked ? $"{project.GitHubOwner}/{project.GitHubRepo}" : null,
            project.LastSyncedAtUtc,
            Tasks = project.Tasks.Select(ToDto),
        });
    }

    [McpServerTool(Name = "create_project"), Description("Create a new project.")]
    public static string CreateProject(
        [Description("Project name (must be unique)")] string name,
        [Description("Optional project description")] string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new McpException("Project name must not be empty.");

        ProjectModel? created = null;
        CreateStore().Update(data =>
        {
            if (data.Projects.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new McpException($"A project named '{name}' already exists.");
            created = new ProjectModel { Name = name.Trim(), Description = description?.Trim() ?? "" };
            data.Projects.Add(created);
        });
        return ToJson(new { created!.Id, created.Name });
    }

    [McpServerTool(Name = "list_tasks"), Description("List the tasks of a project.")]
    public static string ListTasks(
        [Description("Project id (GUID from list_projects)")] string projectId,
        [Description("Filter: all, open, or done (default all)")] string filter = "all")
    {
        var data = CreateStore().Load();
        var project = FindProject(data, projectId);
        var tasks = filter.ToLowerInvariant() switch
        {
            "all" => project.Tasks.AsEnumerable(),
            "open" => project.Tasks.Where(t => !t.IsDone),
            "done" => project.Tasks.Where(t => t.IsDone),
            _ => throw new McpException($"Invalid filter '{filter}'. Use all, open, or done."),
        };
        return ToJson(tasks.Select(ToDto));
    }

    [McpServerTool(Name = "create_task"), Description("Create a task in a project.")]
    public static string CreateTask(
        [Description("Project id (GUID from list_projects)")] string projectId,
        [Description("Task title")] string title,
        [Description("Optional task description")] string? description = null,
        [Description("Optional due date (ISO format, e.g. 2026-08-01)")] string? dueDate = null,
        [Description("Optional priority: low, medium, or high (default medium)")] string? priority = null,
        [Description("Optional labels")] string[]? labels = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new McpException("Task title must not be empty.");

        TaskModel? created = null;
        CreateStore().Update(data =>
        {
            var project = FindProject(data, projectId);
            created = new TaskModel
            {
                Title = title.Trim(),
                Description = description?.Trim() ?? "",
                DueDate = dueDate == null ? null : ParseDate(dueDate),
                Priority = priority == null ? TaskPriority.Medium : ParsePriority(priority),
                CreatedAtUtc = DateTime.UtcNow,
            };
            foreach (var label in labels ?? Array.Empty<string>())
                created.Labels.Add(label);
            project.Tasks.Add(created);
        });
        return ToJson(ToDto(created!));
    }

    [McpServerTool(Name = "update_task"), Description("Update fields of a task. Only provided fields change; marking done/not-done is isDone.")]
    public static string UpdateTask(
        [Description("Task id (GUID)")] string taskId,
        [Description("New title")] string? title = null,
        [Description("New description")] string? description = null,
        [Description("Mark done (true) or reopen (false)")] bool? isDone = null,
        [Description("New due date (ISO format), or 'none' to clear")] string? dueDate = null,
        [Description("New priority: low, medium, or high")] string? priority = null,
        [Description("Replacement label list")] string[]? labels = null)
    {
        TaskModel? updated = null;
        CreateStore().Update(data =>
        {
            var (_, task) = FindTask(data, taskId);
            if (title != null) task.Title = title.Trim();
            if (description != null) task.Description = description;
            if (isDone.HasValue) task.IsDone = isDone.Value;
            if (dueDate != null)
                task.DueDate = dueDate.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : ParseDate(dueDate);
            if (priority != null) task.Priority = ParsePriority(priority);
            if (labels != null)
            {
                task.Labels.Clear();
                foreach (var label in labels)
                    task.Labels.Add(label);
            }
            updated = task;
        });
        return ToJson(ToDto(updated!));
    }

    [McpServerTool(Name = "delete_task"), Description("Delete a task permanently.")]
    public static string DeleteTask(
        [Description("Task id (GUID)")] string taskId)
    {
        CreateStore().Update(data =>
        {
            var (project, task) = FindTask(data, taskId);
            project.Tasks.Remove(task);
        });
        return ToJson(new { deleted = true, taskId });
    }

    [McpServerTool(Name = "search_tasks"), Description("Search tasks across all projects by title, description, or label.")]
    public static string SearchTasks(
        [Description("Text to search for (case-insensitive substring)")] string query,
        [Description("Only tasks carrying this label")] string? label = null,
        [Description("Include completed tasks (default true)")] bool includeDone = true)
    {
        var data = CreateStore().Load();
        var results = TaskSearch.Search(data.Projects, query, label, includeDone)
            .Select(r => new TaskWithProjectDto(r.Project.Id, r.Project.Name, ToDto(r.Task)));
        return ToJson(results);
    }

    [McpServerTool(Name = "github_sync"), Description("Run the two-way GitHub issue sync for a linked project. Requires a token saved in the TaskTracker app settings.")]
    public static async Task<string> GitHubSync(
        [Description("Project id (GUID from list_projects)")] string projectId)
    {
        var settings = CreateSettingsStore().Load();
        var token = TokenProtector.Unprotect(settings.GitHubTokenProtected, settings.GitHubTokenIsPlaintext);
        if (string.IsNullOrEmpty(token))
            throw new McpException("No GitHub token configured. Save a Personal Access Token in the TaskTracker app settings first.");

        var store = CreateStore();
        var data = store.Load();
        var project = FindProject(data, projectId);
        if (!project.IsGitHubLinked)
            throw new McpException($"Project '{project.Name}' is not linked to a GitHub repository. Link it in the TaskTracker app first.");

        SyncResult result;
        try
        {
            result = await new GitHubSyncService().SyncAsync(project, new GitHubApi(token));
        }
        catch (HttpRequestException ex)
        {
            throw new McpException($"GitHub sync failed: {ex.Message}");
        }
        store.Save(data);
        return ToJson(new
        {
            project.Name,
            repository = $"{project.GitHubOwner}/{project.GitHubRepo}",
            result.Imported,
            result.ClosedLocally,
            result.ReopenedLocally,
            result.ClosedOnGitHub,
            result.ReopenedOnGitHub,
            result.Unlinked,
            summary = result.ToString(),
        });
    }
}
