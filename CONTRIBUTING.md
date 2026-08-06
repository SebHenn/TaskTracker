# Contributing to TaskTracker

Thanks for taking an interest. Issues and pull requests are both welcome.

## Getting set up

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Running the desktop app
requires Windows; everything else is cross-platform.

```
git clone https://github.com/SebHenn/TaskTracker.git
cd TaskTracker
dotnet build TaskTracker.sln
dotnet test TaskTracker.Core.Tests
dotnet run --project TaskTracker/TaskTracker.csproj
```

On Linux/macOS the WPF head can be *compiled* but not run, and only with the Microsoft-built SDK:

```
dotnet build TaskTracker.sln -p:EnableWindowsTargeting=true
```

Distro-packaged SDKs (for example Ubuntu's `dotnet-sdk-8.0`) do not ship the WindowsDesktop targets. There,
build the cross-platform projects individually and let CI cover the WPF head:

```
dotnet build TaskTracker.Core TaskTracker.Mcp
dotnet test TaskTracker.Core.Tests
```

## Before you open a pull request

CI runs these on Linux and will fail the build if either does. Run them locally first:

```
dotnet test TaskTracker.Core.Tests
dotnet format TaskTracker.sln --verify-no-changes
```

The format check catches a non-obvious one: most of the tree is UTF-8 **with** a BOM, and files created by
editors or code generators often are not. Such a file compiles fine and fails CI. `dotnet format` fixes it.

`dotnet format` accepts no `-p:` properties, so on non-Windows it reads the targeting override from the
environment instead:

```
EnableWindowsTargeting=true dotnet format TaskTracker.sln --verify-no-changes
```

A clean tree builds with **0 errors and 0 warnings** and passes **218 tests**. `TaskTracker.Core` and
`TaskTracker.Mcp` build with `TreatWarningsAsErrors`, so a warning there is a build break. The WPF head does
not, but is warning-free — please keep it that way.

## Where code belongs

| Project | Target | Purpose |
|---|---|---|
| `TaskTracker` | `net8.0-windows` | WPF desktop app (Windows only) |
| `TaskTracker.Core` | `net8.0` | Models, storage, search, stats, GitHub sync |
| `TaskTracker.Mcp` | `net8.0` | MCP stdio server |
| `TaskTracker.Core.Tests` | `net8.0` | xunit tests for Core |

**`Core` and `Mcp` must stay free of WPF and WinForms types.** That separation is the reason the logic is
testable and CI can run on Linux at all. Anything worth a unit test belongs in `Core`, not in the view model.
A PR that pulls a WPF type into `Core` will fail CI on Linux even if it builds on your machine.

## Things that will bite you

The desktop app and the MCP server are **separate processes over the same files** in `Documents/TaskTracker/`.
Most of the rules below exist to keep that safe.

- **`ProjectStore` is the only gateway to `Save.json`.** It does atomic writes, three rolling backups, a
  cross-process lock, and a fresh revision GUID per write. Out-of-process callers mutate via `Update(mutate)`,
  which is one lock → load → mutate → save. Do not write the file directly.
- **Saving is automatic.** `ChangeTracker` subscribes to the whole model graph and `ProjectsService` debounces
  a save 500 ms later. Mutating a model property persists on its own — do not add save calls.
- **Deleting a task goes through `Core/Services/Trash.cs`**, not `project.Tasks.Remove`, which loses the task
  outright. `Trash.Delete` moves it to the project's trash with 30-day retention and stops any running timer.
- **`ProjectModel.MoveTaskToColumn` is the only place task placement happens.** It derives `IsDone` from the
  column and spawns the next occurrence of a recurring task. Setting `task.IsDone` or `task.ColumnId` directly
  bypasses both.
- **MVVM Toolkit generators rename things.** `[ObservableProperty] private T _foo` becomes `Foo`, and
  `[RelayCommand] void OnDoThing()` becomes **`DoThingCommand`** — the leading `On` is stripped. Bind the
  generated name in XAML.
- **The MCP server speaks protocol on stdout.** Logging goes to stderr; a stray `Console.WriteLine` corrupts
  the stream and breaks the client.
- **Assets ship only if listed as `<Resource>`** in `TaskTracker.csproj`. Adding the file is not enough.

## Testing

New logic in `Core` should come with tests in `TaskTracker.Core.Tests`. Useful filters:

```
dotnet test TaskTracker.Core.Tests --filter FullyQualifiedName~ProjectStoreTests
dotnet test TaskTracker.Core.Tests --filter "DisplayName~migrates"
```

CI validates the WPF UI by **compilation only** — it never runs it. If your change touches the UI, please say
in the PR what you exercised by hand: theme switching, DPI scaling, drag and drop, the dialogs, sync.

## Pull requests

- Branch off `master` and keep the change focused; a small PR gets reviewed faster than a large one.
- Write commit messages in the imperative mood, describing the effect rather than the mechanics
  ("Filter the board by due date and priority", not "update ProjectViewModel.cs").
- Say what you verified. "218 tests pass, format clean, checked drag and drop at 150% scaling" is worth more
  than a description of the diff.
- If you changed behaviour a user would notice, update `README.md` in the same PR.

## Reporting bugs

Open an issue with what you did, what you expected, and what happened. Include your Windows version, and
`Documents/TaskTracker/TaskTracker.log` if the app misbehaved. Please do not attach `Save.json` — it contains
all of your tasks. A trimmed-down excerpt that still reproduces the problem is ideal.

Security issues go through [SECURITY.md](SECURITY.md) instead, not a public issue.

## License

By contributing you agree that your contributions are licensed under the [MIT License](LICENSE) that covers
this project.
