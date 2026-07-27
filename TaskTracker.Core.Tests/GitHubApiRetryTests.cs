using System.Net;
using System.Net.Http;
using TaskTracker.Core.GitHub;

namespace TaskTracker.Core.Tests;

/// <summary>
/// End-to-end over <see cref="GitHubApi"/>'s send loop: the policy is tested separately in
/// <c>GitHubRateLimitTests</c>, this checks the client actually acts on it — retries, gives
/// up, and never resends a spent request or content.
/// </summary>
public class GitHubApiRetryTests
{
    /// <summary>Replays a queued script of responses and records what was sent.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public ScriptedHandler(params HttpResponseMessage[] responses) => _responses = new(responses);

        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null ? null : await request.Content.ReadAsStringAsync(ct));
            Assert.NotEmpty(_responses);
            return _responses.Dequeue();
        }
    }

    private static HttpResponseMessage RateLimited(int retryAfterSeconds)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"message\":\"You have exceeded a secondary rate limit\"}")
        };
        response.Headers.TryAddWithoutValidation("Retry-After", retryAfterSeconds.ToString());
        return response;
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static (GitHubApi Api, ScriptedHandler Handler, List<TimeSpan> Waits) Build(params HttpResponseMessage[] responses)
    {
        var handler = new ScriptedHandler(responses);
        var waits = new List<TimeSpan>();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        // The delay is recorded instead of served, so the retry path costs no wall time.
        var api = new GitHubApi("token", http, (wait, _) => { waits.Add(wait); return Task.CompletedTask; });
        return (api, handler, waits);
    }

    [Fact]
    public async Task ARateLimitedRequestIsRetriedAndSucceeds()
    {
        var (api, handler, waits) = Build(RateLimited(2), Json("[]"));

        var issues = await api.ListIssuesAsync("o", "r", "all");

        Assert.Empty(issues);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(2)], waits);
    }

    [Fact]
    public async Task RetriesAreBoundedAndTheFailureIsReported()
    {
        // Three attempts, so two waits, and then the caller hears about it.
        var (api, handler, waits) = Build(RateLimited(1), RateLimited(1), RateLimited(1));

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => api.ListIssuesAsync("o", "r", "all"));

        Assert.Equal(GitHubRateLimit.MaxAttempts, handler.Requests.Count);
        Assert.Equal(GitHubRateLimit.MaxAttempts - 1, waits.Count);
        Assert.Contains("403", error.Message);
        Assert.Contains("secondary rate limit", error.Message);
    }

    [Fact]
    public async Task AFailureThatIsNotARateLimitIsNotRetried()
    {
        var (api, handler, waits) = Build(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"message\":\"Not Found\"}")
        });

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => api.ListIssuesAsync("o", "r", "all"));

        Assert.Single(handler.Requests);
        Assert.Empty(waits);
        Assert.Contains("404", error.Message);
    }

    [Fact]
    public async Task EachAttemptSendsAFreshRequestWithItsHeaders()
    {
        // An HttpRequestMessage cannot be sent twice, which is why the send loop takes a
        // factory. If it ever went back to reusing one, this fails on the second attempt.
        var (api, handler, _) = Build(RateLimited(1), Json("[]"));

        await api.ListIssuesAsync("o", "r", "all");

        Assert.NotSame(handler.Requests[0], handler.Requests[1]);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("token", request.Headers.Authorization?.Parameter);
            Assert.Contains("TaskTracker", request.Headers.UserAgent.ToString());
        });
    }

    [Fact]
    public async Task ARetriedPostRebuildsItsBody()
    {
        // Content is single-use too: a retry that reused the sent StringContent would go
        // out with an empty body and silently create a blank issue.
        var (api, handler, _) = Build(RateLimited(1), Json("{\"number\": 42}"));

        var number = await api.CreateIssueAsync("o", "r", "Title", "Body", ["bug"]);

        Assert.Equal(42, number);
        Assert.Equal(2, handler.Bodies.Count);
        Assert.All(handler.Bodies, body =>
        {
            Assert.NotNull(body);
            Assert.Contains("\"title\":\"Title\"", body);
            Assert.Contains("\"body\":\"Body\"", body);
        });
    }

    [Fact]
    public async Task PatchingAnIssueStateRetriesToo()
    {
        var (api, handler, waits) = Build(RateLimited(4), Json("{}"));

        await api.CloseIssueAsync("o", "r", 7);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(4)], waits);
        Assert.All(handler.Bodies, body => Assert.Contains("closed", body!));
    }

    [Fact]
    public async Task CancellationDuringTheWaitPropagates()
    {
        var handler = new ScriptedHandler(RateLimited(30));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        using var cts = new CancellationTokenSource();
        var api = new GitHubApi("token", http, (_, ct) =>
        {
            // Stands in for the user cancelling while the retry is waiting it out.
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => api.ListIssuesAsync("o", "r", "all", cts.Token));

        Assert.Single(handler.Requests);
    }
}
