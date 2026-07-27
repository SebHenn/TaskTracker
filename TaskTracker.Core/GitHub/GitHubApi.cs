using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TaskTracker.Core.GitHub
{
    /// <summary>
    /// Minimal GitHub REST client — just the endpoints the sync needs.
    /// Kept dependency-free (no Octokit) and mockable via HttpMessageHandler.
    ///
    /// Headers go on each request rather than on HttpClient.DefaultRequestHeaders,
    /// so one HttpClient can be shared across instances (see
    /// <see cref="GitHubApiFactory"/>) without instances trampling each other's
    /// credentials.
    /// </summary>
    public class GitHubApi : IGitHubApi
    {
        private readonly HttpClient _http;
        private readonly string _token;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        /// <param name="delay">
        /// How a rate-limit wait is served. Injected so tests can exercise the retry
        /// without actually sleeping through it.
        /// </param>
        public GitHubApi(string token, HttpClient? http = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _token = token;
            _http = http ?? new HttpClient();
            _http.BaseAddress ??= new Uri("https://api.github.com/");
            _delay = delay ?? Task.Delay;
        }

        private HttpRequestMessage Request(HttpMethod method, string url, HttpContent? content = null)
        {
            var request = new HttpRequestMessage(method, url) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            request.Headers.UserAgent.ParseAdd("TaskTracker");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            return request;
        }

        /// <summary>
        /// Sends a request, waiting and retrying while GitHub reports a rate limit, and
        /// throws on any other failure. The caller passes a *factory* rather than a
        /// request because an <see cref="HttpRequestMessage"/> cannot be sent twice.
        /// </summary>
        private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> factory, CancellationToken ct)
        {
            for (var attempt = 1; ; attempt++)
            {
                using var request = factory();
                var response = await _http.SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                    return response;

                var wait = attempt < GitHubRateLimit.MaxAttempts
                    ? GitHubRateLimit.RetryDelay(response, DateTimeOffset.UtcNow)
                    : null;
                if (wait == null)
                {
                    // Out of retries, or not a rate limit: let the caller see the failure,
                    // body and all.
                    using (response)
                        throw await FailureFor(response, ct);
                }

                // Nothing below reads the body, so the failed response is done with.
                response.Dispose();
                await _delay(wait.Value, ct);
            }
        }

        public async Task<IReadOnlyList<GitHubIssue>> ListIssuesAsync(string owner, string repo, string state, CancellationToken ct = default)
        {
            var issues = new List<GitHubIssue>();
            for (var page = 1; ; page++)
            {
                var url = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues?state={state}&per_page=100&page={page}";
                using var response = await SendAsync(() => Request(HttpMethod.Get, url), ct);

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var pageCount = 0;
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    pageCount++;
                    issues.Add(new GitHubIssue(
                        Number: element.GetProperty("number").GetInt32(),
                        Title: element.GetProperty("title").GetString() ?? "",
                        Body: element.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() : null,
                        State: element.GetProperty("state").GetString() ?? "open",
                        UpdatedAt: element.GetProperty("updated_at").GetDateTime(),
                        Labels: element.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array
                            ? labels.EnumerateArray()
                                .Select(l => l.ValueKind == JsonValueKind.Object ? l.GetProperty("name").GetString() : l.GetString())
                                .Where(n => !string.IsNullOrEmpty(n))
                                .Select(n => n!)
                                .ToList()
                            : new List<string>(),
                        IsPullRequest: element.TryGetProperty("pull_request", out _),
                        MilestoneDueOn: element.TryGetProperty("milestone", out var milestone)
                                        && milestone.ValueKind == JsonValueKind.Object
                                        && milestone.TryGetProperty("due_on", out var dueOn)
                                        && dueOn.ValueKind == JsonValueKind.String
                            ? dueOn.GetDateTime()
                            : null));
                }

                if (pageCount < 100)
                    return issues;
            }
        }

        public async Task<int> CreateIssueAsync(string owner, string repo, string title, string? body, IReadOnlyList<string> labels, CancellationToken ct = default)
        {
            var payload = JsonSerializer.Serialize(new { title, body = body ?? "", labels });
            // The content is rebuilt per attempt: a StringContent that has been sent
            // cannot be reused, so a retry with a shared instance would fail on the wire.
            using var response = await SendAsync(() => Request(HttpMethod.Post,
                $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues",
                new StringContent(payload, Encoding.UTF8, "application/json")), ct);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.GetProperty("number").GetInt32();
        }

        public async Task UpdateIssueTitleAsync(string owner, string repo, int number, string title, CancellationToken ct = default)
        {
            var payload = JsonSerializer.Serialize(new { title });
            using var response = await SendAsync(() => Request(HttpMethod.Patch,
                $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues/{number}",
                new StringContent(payload, Encoding.UTF8, "application/json")), ct);
        }

        public Task CloseIssueAsync(string owner, string repo, int number, CancellationToken ct = default)
            => PatchStateAsync(owner, repo, number, "closed", ct);

        public Task ReopenIssueAsync(string owner, string repo, int number, CancellationToken ct = default)
            => PatchStateAsync(owner, repo, number, "open", ct);

        private async Task PatchStateAsync(string owner, string repo, int number, string state, CancellationToken ct)
        {
            using var response = await SendAsync(() => Request(HttpMethod.Patch,
                $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues/{number}",
                new StringContent($"{{\"state\":\"{state}\"}}", Encoding.UTF8, "application/json")), ct);
        }

        private static async Task<HttpRequestException> FailureFor(HttpResponseMessage response, CancellationToken ct)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return new HttpRequestException(
                $"GitHub API returned {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body)}");
        }

        private static string Truncate(string value) => value.Length <= 300 ? value : value[..300] + "…";
    }
}
