using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;

namespace TaskTracker.Mcp;

/// <summary>
/// The trash, which delete_task has always written to and nothing could read.
/// A 30-day recovery window nobody can reach is not a recovery window.
/// </summary>
public static partial class TaskTrackerTools
{
    [McpServerTool(Name = "list_trash", ReadOnly = true, Title = "List deleted tasks"),
     Description("List a project's deleted tasks, newest first, with how long each stays recoverable.")]
    public static string ListTrash(
        [Description("Project id (GUID from list_projects)")] string projectId,
        [Description("Maximum entries to return (default 20)")] int limit = 20,
        [Description("Entries to skip, for paging (default 0)")] int offset = 0)
    {
        var data = LoadData();
        var project = FindProject(data, projectId);

        var entries = project.Trash.Select(e => new
        {
            e.Task.Id,
            e.Task.Title,
            e.DeletedAtUtc,
            RecoverableUntilUtc = e.DeletedAtUtc + Trash.Retention,
            DaysLeft = Math.Max(0, (int)Math.Ceiling((e.DeletedAtUtc + Trash.Retention - DateTime.UtcNow).TotalDays)),
        }).ToList();

        return ToJson(McpJson.Paginate(entries, limit, offset));
    }

    [McpServerTool(Name = "restore_task", Idempotent = true, Title = "Restore a deleted task"),
     Description("Put a task from the trash back on its project's board.")]
    public static string RestoreTask(
        [Description("Task id (GUID from list_trash)")] string taskId)
    {
        if (!Guid.TryParse(taskId, out var id))
            throw new McpException($"'{taskId}' is not a valid task id (expected a GUID).");

        ProjectModel? owner = null;
        TaskModel? restored = null;

        UpdateData(data =>
        {
            foreach (var project in data.Projects)
            {
                var entry = project.Trash.FirstOrDefault(e => e.Task.Id == id);
                if (entry == null)
                    continue;

                // Trash.Restore re-normalizes columns, because the column the task came
                // from may have been deleted while it sat in the trash.
                if (!Trash.Restore(project, entry))
                    throw new McpException($"Task {taskId} could not be restored; it may have been restored already.");

                owner = project;
                restored = entry.Task;
                return;
            }

            throw new McpException(
                $"No task with id {taskId} in any project's trash. It may have passed the {Trash.Retention.TotalDays:0}-day retention, " +
                "or still be on the board — use list_trash or search_tasks.");
        });

        return ToJson(new { restored = true, task = ToDto(owner!, restored!) });
    }

    [McpServerTool(Name = "empty_trash", Destructive = true, Idempotent = true, Title = "Empty a project's trash"),
     Description("Permanently discard everything in a project's trash. Requires confirm=true and cannot be undone.")]
    public static string EmptyTrash(
        [Description("Project id (GUID from list_projects)")] string projectId,
        [Description("Must be true; this is irreversible")] bool confirm = false)
    {
        var discarded = 0;
        string? name = null;

        UpdateData(data =>
        {
            var project = FindProject(data, projectId);
            name = project.Name;
            if (!confirm)
                throw new McpException(
                    $"'{project.Name}' has {project.Trash.Count} task(s) in the trash. " +
                    "This permanently discards them; call again with confirm=true.");
            discarded = project.Trash.Count;
            Trash.Empty(project);
        });

        return ToJson(new { emptied = true, project = name, discarded });
    }
}
