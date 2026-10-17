using System.Net;
using System.Net.Http.Headers;
using Strazio.Application;
using Strazio.Infrastructure.Http;

namespace Strazio.Integration.Tests;

public class HttpTests
{
    private static readonly Uri Page = new("https://www.10elotto5.it/archivio-10elotto5-minuti/?data=2026-10-01");
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Access_blocks_are_never_retried(HttpStatusCode status)
    {
        var handler = new Handler(_ => new(status));
        var fetcher = new HttpPageFetcher(new HttpClient(handler));
        await Assert.ThrowsAsync<SourceAccessException>(() => fetcher.FetchAsync(Page, default));
        await Assert.ThrowsAsync<SourceAccessException>(() => fetcher.FetchAsync(Page, default));
        Assert.Single(handler.Requests);
    }
    [Fact]
    public async Task Retry_after_is_exposed_and_source_is_suspended()
    {
        var handler = new Handler(_ => { var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests); response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1)); return response; });
        var fetcher = new HttpPageFetcher(new HttpClient(handler));
        var ex = await Assert.ThrowsAsync<SourceAccessException>(() => fetcher.FetchAsync(Page, default));
        Assert.True(ex.RetryAfter > DateTimeOffset.UtcNow.AddMinutes(59));
        await Assert.ThrowsAsync<SourceAccessException>(() => fetcher.FetchAsync(Page, default)); Assert.Single(handler.Requests);
    }
    [Fact]
    public async Task Transient_HTTP_failure_has_at_most_two_attempts()
    {
        var handler = new Handler(r => r.RequestUri!.AbsolutePath == "/robots.txt" ? new(HttpStatusCode.NotFound) : new(HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<SourceUnavailableException>(() => new HttpPageFetcher(new HttpClient(handler)).FetchAsync(Page, default));
        Assert.Equal(2, handler.Requests.Count(r => r != "/robots.txt"));
    }
    [Fact]
    public async Task Robots_disallow_prevents_page_fetch()
    {
        var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("User-agent: *\nDisallow: /archivio-10elotto5-minuti/") });
        await Assert.ThrowsAsync<SourceAccessException>(() => new HttpPageFetcher(new HttpClient(handler)).FetchAsync(Page, default));
        Assert.Single(handler.Requests);
    }
    [Fact]
    public async Task Retry_after_survives_a_separate_client_invocation()
    {
        var state = new MemoryCooldown();
        var first = new Handler(_ => new(HttpStatusCode.TooManyRequests));
        await Assert.ThrowsAsync<SourceAccessException>(() => new HttpPageFetcher(new HttpClient(first), state).FetchAsync(Page, default));
        var next = new Handler(_ => new(HttpStatusCode.OK));
        await Assert.ThrowsAsync<SourceAccessException>(() => new HttpPageFetcher(new HttpClient(next), state).FetchAsync(Page, default));
        Assert.Empty(next.Requests);
        Assert.True(state.Until > DateTimeOffset.UtcNow.AddMinutes(59));
    }
    [Theory]
    [InlineData("User-agent: *\nDisallow: /wp-admin/", "/archivio-10elotto5-minuti/?data=2026-10-01", true)]
    [InlineData("User-agent: *\nDisallow: /\nAllow: /archivio/", "/archivio/x", true)]
    [InlineData("User-agent: *\nDisallow: /\nAllow: /archivio/", "/other", false)]
    [InlineData("User-agent: *\nDisallow: /*?data=*", "/archivio/?data=2026-10-01", false)]
    [InlineData("User-agent: *\nDisallow: /\nUser-agent: Strazio\nAllow: /", "/archivio/", true)]
    public void Robots_rules_obey_specificity(string text, string path, bool allowed) => Assert.Equal(allowed, RobotsPolicy.IsAllowed(text, path));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.AbsolutePath);
            Assert.Contains("Strazio/1.0", request.Headers.UserAgent.ToString());
            return Task.FromResult(respond(request));
        }
    }
    private sealed class MemoryCooldown : ISourceCooldown
    {
        public DateTimeOffset? Until;
        public Task<DateTimeOffset?> ReadAsync(CancellationToken ct) => Task.FromResult(Until);
        public Task SaveAsync(DateTimeOffset until, CancellationToken ct) { Until = until; return Task.CompletedTask; }
    }
}
