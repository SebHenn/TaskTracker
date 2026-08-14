using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;

namespace TaskTracker.Mcp;

/// <summary>
/// Task-level capability the store had but MCP could not reach: recurrence, timers,
/// ordering, the activity journal, and label discovery.
/// </summary>
public static partial class TaskTrackerTools
{
    [McpServerTool(Name = "set_recurrence", Idempotent = true, Title = "Set task recurrence"),
     Description("Make a task repeat, or stop it repeating. Completing a recurring task creates its next occurrence.")]
    public static string SetRecurrence(
        [Description("Task id (GUID)")] string taskId,
        [Description("none, daily, weekly, or monthly")] string recurrence,
        [Description("Repeat every N periods (default 1)")] int interval = 1)
    {
        if (!RecurrenceRules.IsValid(recurrence))
            throw new McpException(
                $"Invalid recurrence '{recurrence}'. Use {string.Join(", ", RecurrenceRules.All)}.");
        if (interval < 1)
            throw new McpException($"interval must be at least 1 (got {interval}).");

        ProjectModel? owner = null;
        TaskModel? updated = null;
        UpdateData(data =>
        {
            var (project, task) = FindTask(data, taskId);
            owner = project;
            task.Recurrence = RecurrenceRules.Normalize(recurrence);
            task.RecurrenceInterval = interval;
            updated = task;
        });

        return ToJson(new
        {
            updated!.Id,
            updated.Title,
            updated.Recurrence,
            updated.RecurrenceInterval,
            updated.IsRecurring,
            note = updated.IsRecurring && updated.DueDate == null
                ? "This task has no due date, so the next occurrence will be scheduled from the day it is completed."
                : null,
        });
    }

    [McpServerTool(Name = "delete_subtask", Idempotent = true, Title = "Delete a checklist item"),
     Description("Remove a checklist item from its task.")]
    public static string DeleteSubTask(
        [Description("Subtask id (GUID)")] string subtaskId)
    {
        if (!Guid.TryParse(subtaskId, out var id))
            throw new McpException($"'{subtaskId}' is not a valid subtask id (expected a GUID).");

        string? title = null;
        UpdateData(data =>
        {
            foreach (var task in data.Projects.SelectMany(p => p.Tasks))
            {
                var subTask = task.SubTasks.FirstOrDefault(s => s.Id == id);
                if (subTask == null)
                    continue;
                title = subTask.Title;
                task.SubTasks.Remove(subTask);
                return;
            }
            throw new McpException($"No checklist item with id {subtaskId}.");
        });

        return ToJson(new { deleted = true, title });
    }

    [McpServerTool(Name = "start_timer", Idempotent = true, Title = "Start a task's work timer"),
     Description("Start the work timer on a task. Only one timer runs at a time; starting this one stops any other.")]
    public static string StartTimer(
        [Description("Task id (GUID)")] string taskId)
    {
        ProjectModel? owner = null;
        TaskModel? started = null;
        TaskModel? stopped = null;

        UpdateData(data =>
        {
            var (project, task) = FindTask(data, taskId);
            owner = project;
            // StartExclusive is shared with the desktop app, which has always enforced
            // one running timer across the whole store.
            stopped = TimeTracking.StartExclusive(data.Projects, task);
            started = task;
        });

        return ToJson(new
        {
            started = true,
            task = ToDto(owner!, started!),
            started!.TimerStartedAtUtc,
            stoppedTimerOn = stopped?.Title,
        });
    }

    [McpServerTool(Name = "stop_timer", Idempotent = true, Title = "Stop a task's work timer"),
     Description("Stop a task's running work timer and bank the elapsed time.")]
    public static string StopTimer(
        [Description("Task id (GUID); omit to stop whichever timer is running")] string? taskId = null)
    {
        ProjectModel? owner = null;
        TaskModel? stopped = null;

        UpdateData(data =>
        {
            if (taskId != null)
            {
                var (project, task) = FindTask(data, taskId);
                if (task.TimerStartedAtUtc == null)
                    throw new McpException($"No timer is running on '{task.Title}'.");
                owner = project;
                stopped = task;
            }
            else
            {
                foreach (var project in data.Projects)
                {
                    var running = project.Tasks.FirstOrDefault(t => t.TimerStartedAtUtc != null);
                    if (running == null)
                        continue;
                    owner = project;
                    stopped = running;
                    break;
                }
                if (stopped == null)
                    throw new McpException("No timer is running on any task.");
            }

            TimeTracking.Stop(stopped);
        });

        return ToJson(new
        {
            stopped = true,
            task = ToDto(owner!, stopped!),
            trackedTotal = TimeTracking.Format(stopped!.TrackedSeconds),
            stopped.TrackedSeconds,
        });
    }

    [McpServerTool(Name = "timer_status", ReadOnly = true, Title = "Running timer"),
     Description("Which task's work timer is running, if any, and for how long.")]
    public static string TimerStatus()
    {
        var data = LoadData();
        var now = DateTime.UtcNow;

        foreach (var project in data.Projects)
        {
            var running = project.Tasks.FirstOrDefault(t => t.TimerStartedAtUtc != null);
            if (running == null)
                continue;

            var total = TimeTracking.TotalSeconds(running, now);
            return ToJson(new
            {
                running = true,
                project = project.Name,
                running.Id,
                running.Title,
                running.TimerStartedAtUtc,
                elapsedThisSession = TimeTracking.Format((now - running.TimerStartedAtUtc!.Value).TotalSeconds),
                trackedTotal = TimeTracking.Format(total),
            });
        }

        return ToJson(new { running = false });
    }

    [McpServerTool(Name = "reorder_tasks", Idempotent = true, Title = "Reorder tasks in a column"),
     Description("Set the explicit top-to-bottom order of tasks within one board column.")]
    public static string ReorderTasks(
        [Description("Column id (GUID from list_columns)")] string columnId,
        [Description("Task ids (GUIDs) in the desired order — all tasks in that column")] string[] taskIds)
    {
        var ordered = (taskIds ?? Array.Empty<string>())
            .Select(id => Guid.TryParse(id, out var g)
                ? g
                : throw new McpException($"'{id}' is not a valid task id (expected a GUID)."))
            .ToList();

        List<string> titles = new();
        UpdateData(data =>
        {
            var (project, column) = FindColumnById(data, columnId);
            var inColumn = project.Tasks.Where(t => project.ColumnOf(t)?.Id == column.Id).ToList();

            var expected = inColumn.Select(t => t.Id).ToHashSet();
            if (ordered.Count != expected.Count || !ordered.ToHashSet().SetEquals(expected))
                throw new McpException(
                    $"Provide every task in column '{column.Name}' exactly once ({expected.Count} expected, {ordered.Count} given).");

            // SortOrder is 1-based and dense, matching what the board's drag and drop writes.
            for (var i = 0; i < ordered.Count; i++)
            {
                var task = inColumn.First(t => t.Id == ordered[i]);
                task.SortOrder = i + 1;
                titles.Add(task.Title);
            }
        });

        return ToJson(new { reordered = true, order = titles });
    }

    [McpServerTool(Name = "list_activity", ReadOnly = true, Title = "List task notes"),
     Description("List the timestamped notes on a task, newest first.")]
    public static string ListActivity(
        [Description("Task id (GUID)")] string taskId,
        [Description("Maximum notes to return (default 20)")] int limit = 20,
        [Description("Notes to skip, for paging (default 0)")] int offset = 0)
    {
        var data = LoadData();
        var (_, task) = FindTask(data, taskId);
        var notes = task.Activity
            .OrderByDescending(a => a.AtUtc)
            .Select(a => new { a.Id, a.AtUtc, a.Text })
            .ToList();
        return ToJson(McpJson.Paginate(notes, limit, offset));
    }

    [McpServerTool(Name = "delete_activity", Idempotent = true, Title = "Delete a task note"),
     Description("Remove one note from a task's activity journal.")]
    public static string DeleteActivity(
        [Description("Note id (GUID from list_activity)")] string activityId)
    {
        if (!Guid.TryParse(activityId, out var id))
            throw new McpException($"'{activityId}' is not a valid note id (expected a GUID).");

        UpdateData(data =>
        {
            foreach (var task in data.Projects.SelectMany(p => p.Tasks))
            {
                var note = task.Activity.FirstOrDefault(a => a.Id == id);
                if (note == null)
                    continue;
                task.Activity.Remove(note);
                return;
            }
            throw new McpException($"No note with id {activityId}. Use list_activity to find note ids.");
        });

        return ToJson(new { deleted = true });
    }

    [McpServerTool(Name = "list_labels", ReadOnly = true, Title = "List labels in use"),
     Description("List the labels in use, with how many tasks carry each. Use it to find the exact spelling before filtering.")]
    public static string ListLabels(
        [Description("Project id (GUID) to limit to one project; omit for all projects")] string? projectId = null)
    {
        var data = LoadData();
        var projects = projectId == null
            ? data.Projects.Where(p => !p.IsArchived)
            : new[] { FindProject(data, projectId) }.AsEnumerable();

        var labels = projects
            .SelectMany(p => p.Tasks)
            .SelectMany(t => t.Labels)
            .GroupBy(l => l, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Label, StringComparer.OrdinalIgnoreCase);

        return ToJson(labels);
    }

    [McpServerTool(Name = "store_info", ReadOnly = true, Title = "Store and server info"),
     Description("Where the data lives, when it was last written, and which version of this server is answering.")]
    public static string StoreInfo()
    {
        var store = CreateStore();
        var data = LoadData();

        return ToJson(new
        {
            // The version is the point of this tool: an installed tasktracker-mcp is a
            // snapshot, and a stale one looks exactly like a broken one. This is how you
            // tell, mid-session, without leaving the client.
            serverVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0",
            dataDirectory = store.BaseDirectory,
            saveFile = store.SaveFilePath,
            data.Version,
            data.Revision,
            data.SavedAtUtc,
            projects = data.Projects.Count,
            activeProjects = data.Projects.Count(p => !p.IsArchived),
            tasks = data.Projects.Sum(p => p.Tasks.Count),
            openTasks = data.Projects.Where(p => !p.IsArchived).Sum(p => p.Tasks.Count(t => !t.IsDone)),
            trashedTasks = data.Projects.Sum(p => p.Trash.Count),
        });
    }
}
