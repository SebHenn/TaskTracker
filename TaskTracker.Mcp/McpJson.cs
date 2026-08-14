using System.Text.Json;
using System.Text.Json.Serialization;
using TaskTracker.Core.Models;

namespace TaskTracker.Mcp;

/// <summary>
/// The wire shape of every tool response.
///
/// Tool output is charged to the caller's context on every call, so the defaults here
/// are deliberately lean: no indentation, no null or empty keys, and a compact task
/// projection that leaves out the description. A 200-task project used to cost several
/// thousand tokens to list; almost all of it was whitespace, repeated key names and
/// descriptions nobody asked for. Callers that want the full record ask for
/// <see cref="Detail.Full"/>.
/// </summary>
internal static class McpJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>How much of each task to return.</summary>
    public enum Detail
    {
        /// <summary>Identity and the fields you filter or triage on. No description.</summary>
        Compact,

        /// <summary>Everything the store holds about the task.</summary>
        Full,
    }

    public static Detail ParseDetail(string? detail) => detail?.ToLowerInvariant() switch
    {
        null or "" or "compact" => Detail.Compact,
        "full" => Detail.Full,
        _ => throw new ModelContextProtocol.McpException(
            $"Invalid detail '{detail}'. Use compact or full."),
    };

    /// <summary>
    /// A page of results. Lists are capped rather than truncated silently: Total is the
    /// unpaged count, so a caller can tell "that is everything" from "there is more".
    /// </summary>
    public record Page<T>(int Total, int Returned, int Offset, IReadOnlyList<T> Items);

    public static Page<T> Paginate<T>(IReadOnlyList<T> all, int limit, int offset)
    {
        if (limit < 1)
            throw new ModelContextProtocol.McpException($"limit must be at least 1 (got {limit}).");
        if (offset < 0)
            throw new ModelContextProtocol.McpException($"offset must not be negative (got {offset}).");

        var items = all.Skip(offset).Take(limit).ToList();
        return new Page<T>(all.Count, items.Count, offset, items);
    }

    public record SubTaskDto(Guid Id, string Title, bool IsDone);

    /// <summary>
    /// The list/search projection. Nulls and empty collections are dropped by
    /// <see cref="Options"/>, so an unlabelled task with no due date costs a handful of
    /// keys. SubTaskProgress ("2/5") stands in for the full checklist.
    /// </summary>
    public record TaskBrief(
        Guid Id,
        string Title,
        bool IsDone,
        string? Column,
        string Priority,
        DateTime? DueDate,
        IReadOnlyList<string>? Labels,
        string? SubTaskProgress,
        int? GitHubIssueNumber);

    /// <summary>The whole record, for when the caller actually needs the body.</summary>
    public record TaskFull(
        Guid Id,
        string Title,
        string Description,
        bool IsDone,
        string? Column,
        string Priority,
        DateTime? DueDate,
        IReadOnlyList<string>? Labels,
        IReadOnlyList<SubTaskDto>? SubTasks,
        int? GitHubIssueNumber,
        DateTime? CreatedAtUtc,
        DateTime? CompletedAtUtc,
        string Recurrence,
        int RecurrenceInterval,
        double TrackedSeconds,
        bool TimerRunning,
        double? SortOrder,
        int ActivityCount);

    public static object ToTask(ProjectModel project, TaskModel t, Detail detail)
        => detail == Detail.Full ? ToFull(project, t) : ToBrief(project, t);

    public static TaskBrief ToBrief(ProjectModel project, TaskModel t) => new(
        t.Id,
        t.Title,
        t.IsDone,
        project.ColumnOf(t)?.Name,
        t.Priority.ToString(),
        t.DueDate,
        NullIfEmpty(t.Labels),
        t.SubTasks.Count == 0 ? null : t.SubTaskProgress,
        t.GitHubIssueNumber);

    public static TaskFull ToFull(ProjectModel project, TaskModel t) => new(
        t.Id,
        t.Title,
        t.Description,
        t.IsDone,
        project.ColumnOf(t)?.Name,
        t.Priority.ToString(),
        t.DueDate,
        NullIfEmpty(t.Labels),
        t.SubTasks.Count == 0 ? null : t.SubTasks.Select(s => new SubTaskDto(s.Id, s.Title, s.IsDone)).ToList(),
        t.GitHubIssueNumber,
        t.CreatedAtUtc,
        t.CompletedAtUtc,
        t.Recurrence,
        t.RecurrenceInterval,
        t.TrackedSeconds,
        t.TimerStartedAtUtc.HasValue,
        t.SortOrder,
        t.Activity.Count);

    private static IReadOnlyList<string>? NullIfEmpty(IEnumerable<string> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : list;
    }
}
