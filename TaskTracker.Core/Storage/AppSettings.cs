namespace TaskTracker.Core.Storage
{
    public class AppSettings
    {
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

        /// <summary>Global Ctrl+Alt+T quick-add hotkey.</summary>
        public bool QuickAddHotkeyEnabled { get; set; } = true;

        // Last main-window placement; restored on startup when still on-screen.
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }
        public bool WindowMaximized { get; set; }
    }
}
