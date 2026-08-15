namespace TaskTracker.Core.Storage
{
    public class AppSettings
    {
        /// <summary>
        /// Schema version of this file. Bumped when a field changes meaning rather than
        /// merely being added, so a future load can tell "written by an older build" from
        /// "written by a build that did not have the field".
        /// </summary>
        public int Version { get; set; } = 1;

        public string Theme { get; set; } = "dark";

        public string Language { get; set; } = "en";

        /// <summary>
        /// GitHub Personal Access Token, base64-encoded. DPAPI-encrypted on
        /// Windows; stored as plain base64 elsewhere (see <see cref="GitHubTokenIsPlaintext"/>).
        /// </summary>
        public string? GitHubTokenProtected { get; set; }

        /// <summary>True when the token could not be OS-encrypted and is stored as plain base64.</summary>
        public bool GitHubTokenIsPlaintext { get; set; }

        /// <summary>Periodically sync GitHub-linked projects while the app runs.</summary>
        public bool AutoSyncEnabled { get; set; }

        /// <summary>
        /// Show a tray notification when a background sync imports issues that were not
        /// there before. Only reachable through auto-sync, which is what discovers them.
        /// </summary>
        public bool NotifyOnNewIssues { get; set; } = true;

        /// <summary>Start the app minimised when Windows starts.</summary>
        public bool LaunchOnStartupEnabled { get; set; }

        /// <summary>Global quick-add hotkey, on or off.</summary>
        public bool QuickAddHotkeyEnabled { get; set; } = true;

        /// <summary>
        /// The combination itself, like "Ctrl+Alt+T". Configurable because a global
        /// hotkey can clash with another application, and a hard-coded one leaves no way
        /// out. Parsed by <see cref="Services.HotkeyBinding"/>; unparseable falls back to
        /// the default rather than leaving no hotkey.
        /// </summary>
        public string QuickAddHotkey { get; set; } = Services.HotkeyBinding.Default;

        /// <summary>Sidebar width in pixels, as left by the GridSplitter.</summary>
        public double? SidebarWidth { get; set; }

        /// <summary>Compact board cards: same information, less vertical space per card.</summary>
        public bool CompactCards { get; set; }

        // Last main-window placement; restored on startup when still on-screen.
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }
        public bool WindowMaximized { get; set; }
    }
}
