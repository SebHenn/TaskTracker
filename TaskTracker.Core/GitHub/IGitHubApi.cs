namespace TaskTracker.Core.GitHub
{
    /// <param name="UpdatedAt">
    /// Nothing reads this, deliberately. Sync resolves conflicts with a three-way merge
    /// against the state and title recorded at the last sync, which beats comparing a
    /// remote clock with a local one. It stays on the record because this type mirrors
    /// the API response, and a future consumer would otherwise have to add the parsing
    /// back before it could use it.
    /// </param>
    public record GitHubIssue(
        int Number,
        string Title,
        string? Body,
        string State,
        DateTime UpdatedAt,
        IReadOnlyList<string> Labels,
        bool IsPullRequest,
        DateTime? MilestoneDueOn = null);

    public interface IGitHubApi
    {
        /// <summary>Lists issues (including PRs, which callers must skip). state: "open", "closed", or "all".</summary>
        Task<IReadOnlyList<GitHubIssue>> ListIssuesAsync(string owner, string repo, string state, CancellationToken ct = default);

        Task CloseIssueAsync(string owner, string repo, int number, CancellationToken ct = default);

        Task ReopenIssueAsync(string owner, string repo, int number, CancellationToken ct = default);

        /// <summary>Creates an issue and returns its number.</summary>
        Task<int> CreateIssueAsync(string owner, string repo, string title, string? body, IReadOnlyList<string> labels, CancellationToken ct = default);

        /// <summary>Renames an issue.</summary>
        /// <remarks>
        /// Title only, deliberately: there is no update-body call anywhere in this
        /// interface, so an issue keeps the body it was created with. Bots that file
        /// issues identify their own by a trailing HTML comment in the body, and
        /// overwriting it makes them file duplicates instead. Anything that starts
        /// writing bodies has to run the new one through
        /// <see cref="IssueBodyMarkers.Preserve"/> first.
        /// </remarks>
        Task UpdateIssueTitleAsync(string owner, string repo, int number, string title, CancellationToken ct = default);
    }
}
