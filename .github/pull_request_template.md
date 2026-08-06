<!--
Thanks for the pull request. Keep this short — a couple of sentences per section is plenty.
CONTRIBUTING.md has the build, test and format commands if you need them.
-->

## What does this change?

<!-- The effect on the user or the codebase, not a summary of the diff. -->

## Why?

<!-- Link the issue it closes, e.g. "Closes #12", or describe the problem it solves. -->

## How was it verified?

<!--
CI runs the tests and the format check on Linux, but it never runs the WPF UI.
If you touched the UI, say what you exercised by hand.
-->

- [ ] `dotnet test TaskTracker.Core.Tests` passes
- [ ] `dotnet format TaskTracker.sln --verify-no-changes` is clean
- [ ] New logic in `Core` has tests
- [ ] `Core` and `Mcp` still contain no WPF or WinForms types
- [ ] UI changes checked by hand (themes / DPI scaling / drag and drop / dialogs) — or not applicable
- [ ] `README.md` updated if user-visible behaviour changed — or not applicable
