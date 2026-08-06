# Security Policy

## Supported versions

This is a personal project maintained by one person. Only the latest release receives fixes.

| Version | Supported |
|---|---|
| 1.0.x | yes |
| < 1.0 | no |

## Reporting a vulnerability

**Please do not open a public issue for a security problem.**

Use GitHub's private vulnerability reporting instead:
[Report a vulnerability](https://github.com/SebHenn/TaskTracker/security/advisories/new). That opens a private
advisory visible only to the maintainer.

Please include what an attacker could achieve, the steps to reproduce, and the affected version. As a hobby
project there is no formal response window, but reports are taken seriously and you will be credited in the
advisory unless you prefer otherwise.

## What this app touches

Worth knowing if you are assessing the risk surface:

- **All data stays on your machine.** Projects and tasks live in `Documents/TaskTracker/Save.json`; settings
  live beside it. Nothing is uploaded anywhere.
- **The only outbound network calls are to the GitHub API**, and only for repositories you explicitly link,
  when you trigger a sync or enable auto-sync.
- **Your GitHub token is stored encrypted with DPAPI** (Windows, current-user scope) in `Settings.json`. On
  other platforms it falls back to plain base64 and the app says so. The token is never written to the log,
  never included in error messages, and never leaves the machine except as an `Authorization` header to
  github.com.
- **The MCP server has full read/write access to your task data** and runs as a local stdio process. Any MCP
  client you register it with can read and modify every project.
- **Releases are unsigned.** The binaries are built by the `release.yml` GitHub Actions workflow from a tagged
  commit, but there is no code-signing certificate, so Windows SmartScreen will warn on first run.
