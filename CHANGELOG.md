# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
