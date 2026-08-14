# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **MCP list and search results are paged and far cheaper.** `list_tasks`, `search_tasks` and
  `get_project` return a page with the unpaged total, so a truncated answer is distinguishable from a
  complete one, and tasks come back compact by default — no descriptions, no empty or null fields, no
  indentation. On a 60-task project the default response is roughly a fifth the size it was. Pass
  `detail=full` for the whole record.
- **MCP `list_tasks` filters** by label, due bucket (overdue / today / this week / undated) and
  priority, using the same rules as the desktop board. `search_tasks` can now include archived
  projects.
- MCP tools declare whether they are read-only, destructive or idempotent, so clients can stop
  confirming reversible actions and start confirming irreversible ones.
- **Three MCP prompts**, surfaced as slash commands: `plan_my_day`, `review_my_week` and
  `triage_project`.
- The MCP server reports its name and version and ships usage instructions clients pick up
  automatically.
- **Optional auto-start** — a setting registers TaskTracker in the per-user Windows Run key so it
  launches at login, minimised to the notification area. The entry is repaired on every start if the
  app has been moved or republished elsewhere.
- **New-issue notifications** — with auto-sync enabled, a tray notification names the issue a
  background sync imported (or how many arrived). Clicking it opens the affected project. Only
  background syncs notify; a sync you started reports into the board you are already looking at.
- Test coverage for the MCP handlers, which had none despite the documentation claiming otherwise.

### Fixed

- **Completing a recurring task outside the board never scheduled the next occurrence.** Marking one
  done through the MCP server, or closing its issue on GitHub, set the done flag directly instead of
  moving the task to a done column — so the repeat was silently dropped and the chore simply never
  came back. Done-state now goes through one place (`TaskCompletion.SetDone`), which also keeps the
  column and the flag from disagreeing.
- **A GitHub sync started from the MCP server could overwrite changes made in the app.** It loaded the
  store, waited on the network, then saved over whatever had been written meanwhile. It now saves only
  if nothing else wrote first, retrying against fresh data before giving up.
- Store contention between the app and the MCP server reported as an unhandled error rather than
  "busy, try again".
- An unrecognised recurrence value (from a hand-edited or externally written save file) left a task
  claiming to recur while never producing an occurrence; such values are normalised on load, as is a
  recurrence interval below 1.
- Settings failed to save loudly out of a checkbox toggle if the file was briefly unwritable, and had
  no backup to fall back on when corrupt.
- Tasks now record their creation time on every path rather than only the ones that remembered to set
  it, without backdating tasks saved before the field existed.
- **The MCP `delete_task` tool destroyed tasks permanently.** It removed the task from the project
  outright instead of moving it to the trash, so which client you deleted from decided whether the
  deletion could be undone — and a running timer on the task kept accruing against something no
  longer visible. It now goes through the trash like the app, and reports when the task stops being
  recoverable.
- Starting minimised no longer overwrites the saved window placement on exit.
- `actions/checkout` and `actions/setup-dotnet` bumped to v5; v4 forced the deprecated Node 20 runtime.

## [1.0.0] - 2026-08-06

First tagged release.

### Added

- **Projects and Kanban board** with custom columns per project, "done" flags, optional WIP limits, and drag
  and drop between columns and to a position within one.
- **Recurring tasks** — daily, weekly and monthly repeats; completing one schedules the next occurrence.
- **Due dates, priorities and labels**, with board filtering by any combination of label, due bucket
  (overdue / today / this week / undated) and priority.
- **Bulk actions** — select several cards in a column and move or delete them in one go.
- **Subtasks and task details** — description, checklist with progress on the card, metadata and quick edits.
- **Activity journal and time tracking** — timestamped notes per task and a start/stop work timer.
- **Today dashboard and weekly review** — overdue / due today / due this week across all projects, plus a
  summary of what was completed and created in the last seven days.
- **Cross-project search** by title, description or label.
- **Favourites, archive and recents.**
- **Statistics** — per-project open/done/overdue counts, completion percent, tasks-completed-per-week chart.
- **Two-way GitHub issue sync** — open issues import as tasks, completing a task closes the issue and vice
  versa, with last-write-wins on conflicts. Optional auto-sync every 15 minutes.
- **MCP server** exposing projects, tasks, columns and checklists over the Model Context Protocol.
- **Per-project trash** with 30-day retention, so deleting a task stays recoverable long after the undo bar.
- **Quick capture and alerts** — global Ctrl+Alt+T mini add-task window and tray notifications for due tasks.
- **Keyboard and accessibility** — arrow keys between cards, Ctrl+←/→ to move a card between columns, Enter to
  open, Delete to trash; lanes and cards expose accessible names to screen readers.
- **Export and import** — JSON round-trip and CSV export.
- **Dark and light themes, English and German UI.**
- **Safety** — autosave with atomic writes, three rolling backups, a cross-process lock shared with the MCP
  server, single-instance guard, and live reload when the save file changes externally.

### Notes

- Windows builds are self-contained and single-file; no .NET installation is required.
- Releases are unsigned, so SmartScreen warns on first run.

[Unreleased]: https://github.com/SebHenn/TaskTracker/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/SebHenn/TaskTracker/releases/tag/v1.0.0
