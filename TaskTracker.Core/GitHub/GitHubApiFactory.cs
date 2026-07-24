namespace TaskTracker.Core.GitHub
{
    /// <summary>
    /// Hands out configured API clients. Exists so callers can be given a fake in
    /// tests, and so nobody constructs an <see cref="System.Net.Http.HttpClient"/>
    /// per sync.
    /// </summary>
    public interface IGitHubApiFactory
    {
        IGitHubApi Create(string token);
    }

    /// <summary>
    /// Shares one <see cref="HttpClient"/> across every API instance.
    ///
    /// Each `new HttpClient()` opens its own connection pool and holds those
    /// sockets in TIME_WAIT after disposal, so creating one per sync — as the
    /// manual sync button and the 15-minute auto-sync tick both used to — leaks
    /// connections for as long as the app runs. A single long-lived client is the
    /// documented pattern; <see cref="GitHubApi"/> authenticates per request so
    /// sharing stays safe.
    /// </summary>
    public sealed class GitHubApiFactory : IGitHubApiFactory
    {
        private static readonly HttpClient Shared = new()
        {
            BaseAddress = new Uri("https://api.github.com/"),
        };

        public IGitHubApi Create(string token) => new GitHubApi(token, Shared);
    }
}
