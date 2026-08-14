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

    // Long-lived: the factory owns one shared HttpClient, and sync is stateless.
    private static readonly GitHubSyncService Sync = new();
    internal static IGitHubApiFactory ApiFactory { get; set; } = new GitHubApiFactory();

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private record ProjectSummary(Guid Id, string Name, string Description, int OpenTasks, int DoneTasks, bool IsArchived, bool IsFavourite, string? GitHubRepo);
    private record SubTaskDto(Guid Id, string Title, bool IsDone);
    private record TaskDto(Guid Id, string Title, string Description, bool IsDone, string? Column, string Priority, DateTime? DueDate, IReadOnlyList<string> Labels, IReadOnlyList<SubTaskDto> SubTasks, int? GitHubIssueNumber, DateTime? CompletedAtUtc, string Recurrence, double TrackedSeconds, int ActivityCount);
    private record TaskWithProjectDto(Guid ProjectId, string ProjectName, TaskDto Task);

    private static TaskDto ToDto(ProjectModel project, TaskModel t) => new(
        t.Id, t.Title, t.Description, t.IsDone, project.ColumnOf(t)?.Name, t.Priority.ToString(), t.DueDate, t.Labels.ToList(),
        t.SubTasks.Select(s => new SubTaskDto(s.Id, s.Title, s.IsDone)).ToList(), t.GitHubIssueNumber, t.CompletedAtUtc,
        t.Recurrence, t.TrackedSeconds, t.Activity.Count);

    private static string ToJson<T>(T value) => JsonSerializer.Serialize(value, Json);

    /// <summary>
    /// Store access with store-level failures translated into McpException.
    ///
    /// The desktop app and this server share one lock file, so contention is a normal
    /// condition rather than a bug — but ProjectStore surfaces it as an IOException,
    /// which reaches the client as an unhandled crash instead of "busy, try again".
    /// Guarding at this seam rather than in each tool body means every tool, including
    /// ones added later, gets the treatment without remembering to ask for it.
    /// </summary>
    private static StoreData LoadData()
    {
        try
        {
            return CreateStore().Load();
        }
        catch (Exception ex) when (Translate(ex) is { } translated)
        {
            throw translated;
        }
    }

    /// <inheritdoc cref="LoadData"/>
    private static StoreData UpdateData(Action<StoreData> mutate)
    {
        try
        {
            return CreateStore().Update(mutate);
        }
        catch (Exception ex) when (Translate(ex) is { } translated)
        {
            throw translated;
        }
    }

    /// <summary>
    /// Maps store-level failures onto actionable messages, or null to let the exception
    /// through untouched — McpException from a tool's own validation must not be caught.
    /// </summary>
    private static McpException? Translate(Exception ex) => ex switch
    {
        StoreLockedException => new McpException(
            "The TaskTracker store is busy — the desktop app is writing to it. Try again in a moment."),
        JsonException => new McpException(
            "The TaskTracker save file could not be parsed. A backup may be recoverable: see Save.json.bak1 in the data directory."),
        UnauthorizedAccessException => new McpException(
            "No permission to read the TaskTracker data directory."),
        _ => null,
    };

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
        var data = LoadData();
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
        var data = LoadData();
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
            Columns = project.Columns.Select(c => new { c.Id, c.Name, c.IsDoneColumn }),
            Tasks = project.Tasks.Select(t => ToDto(project, t)),
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
        UpdateData(data =>
        {
            if (data.Projects.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new McpException($"A project named '{name}' already exists.");
            created = new ProjectModel { Name = name.Trim(), Description = description?.Trim() ?? "" };
            foreach (var column in BoardColumnDefaults.NewProjectColumns())
                created.Columns.Add(column);
            data.Projects.Add(created);
        });
        return ToJson(new { created!.Id, created.Name });
    }

    [McpServerTool(Name = "list_tasks"), Description("List the tasks of a project.")]
    public static string ListTasks(
        [Description("Project id (GUID from list_projects)")] string projectId,
        [Description("Filter: all, open, or done (default all)")] string filter = "all")
    {
        var data = LoadData();
        var project = FindProject(data, projectId);
        var tasks = filter.ToLowerInvariant() switch
        {
            "all" => project.Tasks.AsEnumerable(),
            "open" => project.Tasks.Where(t => !t.IsDone),
            "done" => project.Tasks.Where(t => t.IsDone),
            _ => throw new McpException($"Invalid filter '{filter}'. Use all, open, or done."),
        };
        return ToJson(tasks.Select(t => ToDto(project, t)));
    }

    [McpServerTool(Name = "create_task"), Description("Create a task in a project.")]
    public static string CreateTask(
        [Description("Project id (GUID from list_projects)")] string projectId,
        [Description("Task title")] string title,
        [Description("Optional task description")] string? description = null,
        [Description("Optional due date (ISO format, e.g. 2026-08-01)")] string? dueDate = null,
        [Description("Optional priority: low, medium, or high (default medium)")] string? priority = null,
        [Description("Optional labels")] string[]? labels = null,
        [Description("Optional board column (name or GUID); defaults to the first column")] string? column = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new McpException("Task title must not be empty.");

        TaskModel? created = null;
        ProjectModel? owner = null;
        UpdateData(data =>
        {
            var project = FindProject(data, projectId);
            owner = project;
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
            project.MoveTaskToColumn(created, column == null ? project.FirstColumn! : FindColumn(project, column));
        });
        return ToJson(ToDto(owner!, created!));
    }

    private static BoardColumn FindColumn(ProjectModel project, string column)
    {
        var byId = Guid.TryParse(column, out var id) ? project.Columns.FirstOrDefault(c => c.Id == id) : null;
        var found = byId ?? project.Columns.FirstOrDefault(c => c.Name.Equals(column, StringComparison.OrdinalIgnoreCase));
        return found ?? throw new McpException(
            $"No column '{column}' in project '{project.Name}'. Available: {string.Join(", ", project.Columns.Select(c => c.Name))}.");
    }

    [McpServerTool(Name = "update_task"), Description("Update fields of a task. Only provided fields change; marking done/not-done is isDone.")]
    public static string UpdateTask(
        [Description("Task id (GUID)")] string taskId,
        [Description("New title")] string? title = null,
        [Description("New description")] string? description = null,
        [Description("Mark done (true) or reopen (false)")] bool? isDone = null,
        [Description("New due date (ISO format), or 'none' to clear")] string? dueDate = null,
        [Description("New priority: low, medium, or high")] string? priority = null,
        [Description("Replacement label list")] string[]? labels = null,
        [Description("Move to this board column (name or GUID)")] string? column = null)
    {
        TaskModel? updated = null;
        ProjectModel? owner = null;
        UpdateData(data =>
        {
            var (project, task) = FindTask(data, taskId);
            owner = project;
            if (title != null) task.Title = title.Trim();
            if (description != null) task.Description = description;
            if (column != null)
                project.MoveTaskToColumn(task, FindColumn(project, column));
            if (isDone.HasValue)
            {
                // Route through TaskCompletion so ColumnId stays coherent and completing
                // a recurring task spawns its next occurrence. A no-op when the column
                // move above already put the task in the requested state.
                TaskCompletion.SetDone(project, task, isDone.Value);
            }
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
        return ToJson(ToDto(owner!, updated!));
    }

    [McpServerTool(Name = "delete_task"), Description("Move a task to its project's trash, where it stays recoverable for 30 days.")]
    public static string DeleteTask(
        [Description("Task id (GUID)")] string taskId)
    {
        DateTime deletedAtUtc = default;
        UpdateData(data =>
        {
            var (project, task) = FindTask(data, taskId);
            // Trash.Delete, not project.Tasks.Remove: removing straight from the list
            // loses the task outright and leaves a running timer accruing against
            // something invisible. Deleting in the app has always been recoverable —
            // going through the MCP server should not quietly be the one way to
            // destroy a task for good.
            deletedAtUtc = Trash.Delete(project, task).DeletedAtUtc;
        });
        return ToJson(new { deleted = true, taskId, recoverableUntilUtc = deletedAtUtc + Trash.Retention });
    }

    [McpServerTool(Name = "search_tasks"), Description("Search tasks across all projects by title, description, or label.")]
    public static string SearchTasks(
        [Description("Text to search for (case-insensitive substring)")] string query,
        [Description("Only tasks carrying this label")] string? label = null,
        [Description("Include completed tasks (default true)")] bool includeDone = true)
    {
        var data = LoadData();
        var results = TaskSearch.Search(data.Projects, query, label, includeDone)
            .Select(r => new TaskWithProjectDto(r.Project.Id, r.Project.Name, ToDto(r.Project, r.Task)));
        return ToJson(results);
    }

    [McpServerTool(Name = "create_tasks"), Description("Create several tasks in one call. Shared column/priority/dueDate/labels apply to all of them.")]
    public static string CreateTasks(
        [Description("Project id (GUID from list_projects)")] string projectId,
        [Description("One task title per entry")] string[] titles,
        [Description("Optional board column (name or GUID) for all tasks")] string? column = null,
        [Description("Optional priority for all tasks: low, medium, or high")] string? priority = null,
        [Description("Optional due date for all tasks (ISO format)")] string? dueDate = null,
        [Description("Optional labels applied to all tasks")] string[]? labels = null)
    {
        var cleaned = (titles ?? Array.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (cleaned.Count == 0)
            throw new McpException("Provide at least one non-empty task title.");

        var created = new List<TaskModel>();
        ProjectModel? owner = null;
        UpdateData(data =>
        {
            var project = FindProject(data, projectId);
            owner = project;
            var targetColumn = column == null ? project.FirstColumn! : FindColumn(project, column);
            foreach (var title in cleaned)
            {
                var task = new TaskModel
                {
                    Title = title.Trim(),
                    DueDate = dueDate == null ? null : ParseDate(dueDate),
                    Priority = priority == null ? TaskPriority.Medium : ParsePriority(priority),
                    CreatedAtUtc = DateTime.UtcNow,
                };
                foreach (var label in labels ?? Array.Empty<string>())
                    task.Labels.Add(label);
                project.Tasks.Add(task);
                project.MoveTaskToColumn(task, targetColumn);
                created.Add(task);
            }
        });
        return ToJson(created.Select(t => ToDto(owner!, t)));
    }

    [McpServerTool(Name = "add_note"), Description("Add a timestamped note to a task's activity journal.")]
    public static string AddNote(
        [Description("Task id (GUID)")] string taskId,
        [Description("Note text")] string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new McpException("Note text must not be empty.");
        ActivityEntry? entry = null;
        UpdateData(data =>
        {
            var (_, task) = FindTask(data, taskId);
            entry = new ActivityEntry { Text = text.Trim() };
            task.Activity.Insert(0, entry);
        });
        return ToJson(new { entry!.Id, entry.AtUtc, entry.Text });
    }

    [McpServerTool(Name = "weekly_review"), Description("What happened in the last 7 days: completed and created tasks per project, plus open/overdue counts and tracked hours.")]
    public static string WeeklyReview()
    {
        var data = LoadData();
        return ToJson(ReviewReport.Compute(data.Projects));
    }

    [McpServerTool(Name = "update_project"), Description("Update a project's name, description, or archived state. Only provided fields change.")]
    public static string UpdateProject(
        [Description("Project id (GUID from list_projects)")] string projectId,
        [Description("New name")] string? name = null,
        [Description("New description")] string? description = null,
        [Description("Archive (true) or unarchive (false)")] bool? isArchived = null)
    {
        ProjectModel? updated = null;
        UpdateData(data =>
        {
            var project = FindProject(data, projectId);
            if (!string.IsNullOrWhiteSpace(name))
            {
                if (data.Projects.Any(p => p != project && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    throw new McpException($"A project named '{name}' already exists.");
                project.Name = name.Trim();
            }
            if (description != null)
                project.Description = description;
            if (isArchived.HasValue && project.IsArchived != isArchived.Value)
            {
                project.IsArchived = isArchived.Value;
                project.ArchivedAtUtc = isArchived.Value ? DateTime.UtcNow : null;
            }
            updated = project;
        });
        return ToJson(new { updated!.Id, updated.Name, updated.Description, updated.IsArchived });
    }

    [McpServerTool(Name = "delete_project"), Description("Delete a project and all of its tasks permanently. Requires confirm=true.")]
    public static string DeleteProject(
        [Description("Project id (GUID from list_projects)")] string projectId,
        [Description("Must be true to actually delete")] bool confirm = false)
    {
        if (!confirm)
            throw new McpException("Deletion is permanent. Call again with confirm=true to delete the project.");
        string? name = null;
        UpdateData(data =>
        {
            var project = FindProject(data, projectId);
            name = project.Name;
            data.Projects.Remove(project);
            data.RecentProjectIds.Remove(project.Id);
        });
        return ToJson(new { deleted = true, name });
    }

    [McpServerTool(Name = "project_stats"), Description("Get statistics for a project: open/done/overdue counts, completion percent, tasks completed per week.")]
    public static string ProjectStatsTool(
        [Description("Project id (GUID from list_projects)")] string projectId)
    {
        var data = LoadData();
        var project = FindProject(data, projectId);
        var stats = ProjectStats.Compute(project);
        return ToJson(new
        {
            project.Name,
            stats.OpenCount,
            stats.DoneCount,
            stats.OverdueCount,
            CompletionPercent = Math.Round(stats.CompletionPercent, 1),
            DonePerWeek = stats.DonePerWeek.Select(w => new { WeekStart = w.WeekStart.ToString("yyyy-MM-dd"), w.Count }),
        });
    }

    [McpServerTool(Name = "move_task"), Description("Move a task to another board column of its project.")]
    public static string MoveTask(
        [Description("Task id (GUID)")] string taskId,
        [Description("Target column (name, case-insensitive, or GUID)")] string column)
    {
        TaskModel? moved = null;
        ProjectModel? owner = null;
        UpdateData(data =>
        {
            var (project, task) = FindTask(data, taskId);
            owner = project;
            project.MoveTaskToColumn(task, FindColumn(project, column));
            moved = task;
        });
        return ToJson(ToDto(owner!, moved!));
    }

    [McpServerTool(Name = "add_subtask"), Description("Add a checklist item to a task.")]
    public static string AddSubTask(
        [Description("Task id (GUID)")] string taskId,
        [Description("Checklist item text")] string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new McpException("Subtask title must not be empty.");
        SubTaskModel? created = null;
        UpdateData(data =>
        {
            var (_, task) = FindTask(data, taskId);
            created = new SubTaskModel { Title = title.Trim() };
            task.SubTasks.Add(created);
        });
        return ToJson(new { created!.Id, created.Title, created.IsDone });
    }

    [McpServerTool(Name = "update_subtask"), Description("Rename or check/uncheck a checklist item.")]
    public static string UpdateSubTask(
        [Description("Subtask id (GUID, from get_project/list_tasks)")] string subtaskId,
        [Description("New text")] string? title = null,
        [Description("Check (true) or uncheck (false)")] bool? isDone = null)
    {
        if (!Guid.TryParse(subtaskId, out var id))
            throw new McpException($"'{subtaskId}' is not a valid subtask id (expected a GUID).");
        SubTaskModel? updated = null;
        UpdateData(data =>
        {
            updated = data.Projects.SelectMany(p => p.Tasks).SelectMany(t => t.SubTasks).FirstOrDefault(s => s.Id == id)
                      ?? throw new McpException($"No subtask with id {subtaskId}.");
            if (!string.IsNullOrWhiteSpace(title)) updated.Title = title.Trim();
            if (isDone.HasValue) updated.IsDone = isDone.Value;
        });
        return ToJson(new { updated!.Id, updated.Title, updated.IsDone });
    }

    [McpServerTool(Name = "due_overview"), Description("Overdue, due-today, and due-this-week tasks across all non-archived projects.")]
    public static string DueOverviewTool()
    {
        var data = LoadData();
        var overview = DueTasks.Collect(data.Projects);
        object Row(DueTaskItem i) => new { ProjectName = i.Project.Name, i.Task.Id, i.Task.Title, i.Task.DueDate };
        return ToJson(new
        {
            Overdue = overview.Overdue.Select(Row),
            DueToday = overview.DueToday.Select(Row),
            DueThisWeek = overview.DueThisWeek.Select(Row),
        });
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
        var api = ApiFactory.Create(token);

        // Sync cannot use Update(): it does network I/O in the middle, and holding the
        // cross-process lock across that would make the desktop app's autosave start
        // failing within two seconds. So load, sync, then save only if nothing else wrote
        // meanwhile — and if something did, redo the sync against the fresh data rather
        // than overwriting the other process's work. SyncAsync re-lists issues, so a
        // second run is safe; one retry, then hand it back to the caller.
        SyncResult? result = null;
        ProjectModel? project = null;

        for (var attempt = 0; attempt < 2 && result == null; attempt++)
        {
            var data = LoadData();
            project = FindProject(data, projectId);
            if (!project.IsGitHubLinked)
                throw new McpException($"Project '{project.Name}' is not linked to a GitHub repository. Link it in the TaskTracker app first.");

            var baseRevision = data.Revision;
            SyncResult attempted;
            try
            {
                attempted = await Sync.SyncAsync(project, api);
            }
            catch (HttpRequestException ex)
            {
                throw new McpException($"GitHub sync failed: {ex.Message}");
            }

            if (store.TrySaveIfUnchanged(data, baseRevision))
                result = attempted;
        }

        if (result == null)
            throw new McpException(
                "GitHub sync could not be saved: the TaskTracker store kept changing underneath it. " +
                "Close or idle the desktop app and try again.");

        return ToJson(new
        {
            project!.Name,
            repository = $"{project.GitHubOwner}/{project.GitHubRepo}",
            result.Imported,
            result.Exported,
            result.ClosedLocally,
            result.ReopenedLocally,
            result.ClosedOnGitHub,
            result.ReopenedOnGitHub,
            result.Unlinked,
            result.ExportError,
            summary = result.ToString(),
        });
    }
}
