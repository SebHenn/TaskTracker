using System.ComponentModel;
using ModelContextProtocol.Server;

namespace TaskTracker.Mcp;

/// <summary>
/// Prompts, which clients surface as slash commands. They are workflows over the tools
/// rather than new capability — the point is that the useful sequence ("look before you
/// mutate", "read the whole week before ranking it") is spelled out once here instead of
/// being re-derived, differently, on every invocation.
/// </summary>
[McpServerPromptType]
public static class TaskTrackerPrompts
{
    [McpServerPrompt(Name = "plan_my_day"), Description("Propose an order of work for today from what is overdue and due.")]
    public static string PlanMyDay()
        => """
           Call due_overview to see what is overdue, due today, and due this week.

           Then propose an order of work for today:
           - Overdue items first, but say plainly if there are too many to finish and which
             ones you would let slip.
           - Group by project so the day is not a constant context switch.
           - Note anything due this week that is large enough it should be started today.

           Do not change anything. This is a proposal for the user to accept or reject.
           """;

    [McpServerPrompt(Name = "review_my_week"), Description("Summarize the past week from the tracked data and suggest next week's focus.")]
    public static string ReviewMyWeek()
        => """
           Call weekly_review for the last seven days, then due_overview for what is coming.
           If the user named an effort that spans several boards, pass its label to
           weekly_review (list_labels has the spellings) so the review is about that
           effort rather than about everything.

           Write a short review:
           - What was completed, grouped by project. Lead with the substantial items rather
             than listing everything.
           - What slipped: tasks still open and overdue, and how long they have been.
           - Where the tracked time went, and whether it matches where the completions were.
           - Three concrete things to focus on next week, each tied to a real task.

           Be honest about a thin week rather than padding it. Do not change anything.
           """;

    [McpServerPrompt(Name = "triage_project"), Description("Find under-specified tasks in a project and propose fixes.")]
    public static string TriageProject(
        [Description("Project id (GUID from list_projects)")] string projectId)
        => $$"""
           Call get_project with projectId "{{projectId}}" to see its columns and tasks.

           Find the tasks that are not ready to be worked on:
           - No due date, on anything that clearly needs one.
           - No labels, where the rest of the project is labelled.
           - Titles too vague to act on without opening them.
           - Sitting in an in-progress column but untouched for a long time.
           - Columns over their WIP limit.

           Present the findings as a numbered list with a specific proposed fix for each
           (the actual due date, the actual label, the actual rewritten title). Ask the user
           which to apply before calling update_task — do not mutate anything first.
           """;
}
