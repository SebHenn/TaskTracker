namespace TaskTracker.Core.GitHub
{
    /// <summary>
    /// Keeps the machine-readable markers other tools park at the end of an issue body
    /// alive across a body rewrite.
    ///
    /// A bot that files issues has to recognise its own on the next run, and the usual
    /// way — GitHub's own tooling included — is a trailing HTML comment carrying an id,
    /// e.g. <c>&lt;!-- cifail:&lt;fingerprint&gt; --&gt;</c>. The comment is invisible when
    /// rendered and is often the *only* identity link: overwrite the body and the bot
    /// silently stops matching, then files a duplicate on every recurrence.
    ///
    /// Nothing here writes issue bodies today — <see cref="IGitHubApi"/> deliberately has
    /// no update-body call — so this exists for the day description sync is added. Feed it
    /// the body just fetched from GitHub and the replacement, and it carries the markers
    /// over. Note that the markers must come from the *fetched* body: a task's description
    /// is a truncated copy (see <c>GitHubSyncService.TrimBody</c>) and has usually lost the
    /// trailing comment already.
    /// </summary>
    public static class IssueBodyMarkers
    {
        private const string Open = "<!--";
        private const string Close = "-->";

        /// <summary>
        /// The HTML comments at the very end of <paramref name="body"/>, outermost first,
        /// or empty when it does not end in one. Whitespace between and after them is
        /// ignored; a comment with any text after it is part of the prose, not a marker.
        /// </summary>
        public static IReadOnlyList<string> Trailing(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return Array.Empty<string>();

            var markers = new List<string>();
            var end = body.Length;
            while (true)
            {
                // Whitespace before the scan position separates markers from each other
                // and from the prose; it is not preserved, only skipped.
                while (end > 0 && char.IsWhiteSpace(body[end - 1]))
                    end--;

                if (!body.AsSpan(0, end).EndsWith(Close, StringComparison.Ordinal))
                    break;

                // Comments cannot nest, so the last opener before this closer is its own.
                // A closer with no opener at all is stray text — stop rather than guess.
                var start = body.LastIndexOf(Open, end - Close.Length, StringComparison.Ordinal);
                if (start < 0)
                    break;

                markers.Insert(0, body[start..end]);
                end = start;
            }

            return markers;
        }

        /// <summary>
        /// <paramref name="newBody"/> with every trailing marker of
        /// <paramref name="existingBody"/> that it does not already carry appended to it.
        /// Markers already present anywhere in the new body are left where they are —
        /// bots match on the string, not on its position.
        /// </summary>
        public static string Preserve(string? existingBody, string? newBody)
        {
            var body = newBody ?? "";
            var missing = Trailing(existingBody)
                .Where(marker => !body.Contains(marker, StringComparison.Ordinal))
                .ToList();
            if (missing.Count == 0)
                return body;

            // A blank line keeps the comments off the end of the last paragraph, which is
            // how bots write them and how they survive a round trip unchanged.
            body = body.TrimEnd();
            return body.Length == 0
                ? string.Join("\n", missing)
                : body + "\n\n" + string.Join("\n", missing);
        }
    }
}
