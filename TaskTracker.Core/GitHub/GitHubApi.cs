using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TaskTracker.Core.GitHub
{
    /// <summary>
    /// Minimal GitHub REST client — just the three endpoints the sync needs.
    /// Kept dependency-free (no Octokit) and mockable via HttpMessageHandler.
    /// </summary>
    public class GitHubApi : IGitHubApi
    {
        private readonly HttpClient _http;

        public GitHubApi(string token, HttpClient? http = null)
        {
            _http = http ?? new HttpClient();
            _http.BaseAddress ??= new Uri("https://api.github.com/");
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("TaskTracker");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        }

        public async Task<IReadOnlyList<GitHubIssue>> ListIssuesAsync(string owner, string repo, string state, CancellationToken ct = default)
        {
            var issues = new List<GitHubIssue>();
            for (var page = 1; ; page++)
            {
                var response = await _http.GetAsync(
                    $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues?state={state}&per_page=100&page={page}", ct);
                await EnsureSuccess(response, ct);

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
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(
                $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues", content, ct);
            await EnsureSuccess(response, ct);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.GetProperty("number").GetInt32();
        }

        public async Task UpdateIssueTitleAsync(string owner, string repo, int number, string title, CancellationToken ct = default)
        {
            var payload = JsonSerializer.Serialize(new { title });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _http.PatchAsync(
                $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues/{number}", content, ct);
            await EnsureSuccess(response, ct);
        }

        public Task CloseIssueAsync(string owner, string repo, int number, CancellationToken ct = default)
            => PatchStateAsync(owner, repo, number, "closed", ct);

        public Task ReopenIssueAsync(string owner, string repo, int number, CancellationToken ct = default)
            => PatchStateAsync(owner, repo, number, "open", ct);

        private async Task PatchStateAsync(string owner, string repo, int number, string state, CancellationToken ct)
        {
            var content = new StringContent($"{{\"state\":\"{state}\"}}", Encoding.UTF8, "application/json");
            var response = await _http.PatchAsync(
                $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues/{number}", content, ct);
            await EnsureSuccess(response, ct);
        }

        private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken ct)
        {
            if (response.IsSuccessStatusCode)
                return;
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"GitHub API returned {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body)}");
        }

        private static string Truncate(string value) => value.Length <= 300 ? value : value[..300] + "…";
    }
}
