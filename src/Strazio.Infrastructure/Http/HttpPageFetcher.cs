using System.Net;
using System.Text.RegularExpressions;
using Polly;
using Polly.Retry;
using Strazio.Application;

namespace Strazio.Infrastructure.Http;

public sealed class RandomRequestPacer : IRequestPacer
{
    private bool hasRequested;
    public async Task WaitAsync(CancellationToken ct)
    {
        if (hasRequested) await Task.Delay(Random.Shared.Next(3000, 8001), ct);
        hasRequested = true;
    }
}

public sealed class HttpPageFetcher(HttpClient client, ISourceCooldown? cooldown = null) : IPageFetcher
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool robotsChecked;
    private string? robots;
    private SourceAccessException? suspended;
    private readonly ResiliencePipeline<string> retry = new ResiliencePipelineBuilder<string>()
        .AddRetry(new RetryStrategyOptions<string>
        {
            MaxRetryAttempts = 1, // Two attempts total, including the initial request.
            Delay = TimeSpan.FromSeconds(3),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = new PredicateBuilder<string>().Handle<HttpRequestException>().Handle<SourceUnavailableException>()
        }).Build();

    public async Task<string> FetchAsync(Uri uri, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (suspended is not null) throw suspended;
            if (cooldown is not null && await cooldown.ReadAsync(ct) is { } until && until > DateTimeOffset.UtcNow)
                throw new SourceAccessException($"La fonte è ancora sospesa per un precedente HTTP 429. Riprova dopo {until:O}.", until);
            if (!robotsChecked)
            {
                try { robots = await SendAsync(new Uri(uri, "/robots.txt"), ct, robotsRequest: true); }
                catch (DayUnavailableException) { robots = ""; } // RFC 9309: a 404 has no rules.
                robotsChecked = true;
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
            if (!RobotsPolicy.IsAllowed(robots ?? "", uri.PathAndQuery))
                throw new SourceAccessException("La fonte vieta questo percorso nel file robots.txt. Scaricamento interrotto.");
            return await retry.ExecuteAsync(token => new ValueTask<string>(SendAsync(uri, token)), ct);
        }
        catch (SourceAccessException ex)
        {
            suspended = ex;
            if (cooldown is not null && ex.RetryAfter is { } until)
            {
                // Retain Retry-After across separate CLI/task invocations as well.
                try { await cooldown.SaveAsync(until, ct); }
                catch (IOException) { /* Current batch remains stopped even if persistence is unavailable. */ }
                catch (UnauthorizedAccessException) { }
            }
            throw;
        }
        catch (HttpRequestException ex) { throw new SourceUnavailableException("Oggi il sito non risponde correttamente. Puoi riprovare più tardi.", ex); }
        finally { gate.Release(); }
    }

    private async Task<string> SendAsync(Uri uri, CancellationToken ct, bool robotsRequest = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Strazio/1.0 (archivio-personale-10eLotto)");
            request.Headers.Accept.ParseAdd(robotsRequest ? "text/plain" : "text/html");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.Unauthorized)
                throw new SourceAccessException("La fonte ha negato l'accesso (HTTP 403/401). Operazione interrotta senza aggirare il blocco.");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var after = response.Headers.RetryAfter?.Date ?? (response.Headers.RetryAfter?.Delta is { } delta ? DateTimeOffset.UtcNow + delta : DateTimeOffset.UtcNow.AddHours(1));
                throw new SourceAccessException("La fonte limita le richieste (HTTP 429). Operazione sospesa" +
                    $" almeno fino a {after:O}.", after);
            }
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                throw new DayUnavailableException("La fonte non rende disponibile questa giornata (HTTP 404/410).");
            if ((int)response.StatusCode >= 500) throw new SourceUnavailableException($"Il sito risponde HTTP {(int)response.StatusCode}.");
            if (response.StatusCode != HttpStatusCode.OK)
                throw new SourceAccessException($"Risposta HTTP {(int)response.StatusCode}: operazione interrotta.");
            if (response.Content.Headers.ContentLength > 12_000_000)
                throw new SourceUnavailableException("La risposta del sito è troppo grande.");
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var bytes = new byte[65536];
            int count;
            while ((count = await stream.ReadAsync(bytes, timeout.Token)) > 0)
            {
                if (buffer.Length + count > 12_000_000) throw new SourceUnavailableException("La risposta del sito è troppo grande.");
                buffer.Write(bytes, 0, count);
            }
            var html = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
            if (!robotsRequest && Regex.IsMatch(html, @"cf-chl-|g-recaptcha|h-captcha|checking your browser|verify (that )?you are human", RegexOptions.IgnoreCase))
                throw new SourceAccessException("La fonte richiede una verifica di accesso. Operazione interrotta.");
            return html;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new SourceUnavailableException("Il sito non ha risposto entro 30 secondi.", ex);
        }
    }
}

public static class RobotsPolicy
{
    public static bool IsAllowed(string text, string path)
    {
        var groups = new List<(List<string> Agents, List<(bool Allow, string Path)> Rules)>();
        List<string> agents = [];
        List<(bool Allow, string Path)> rules = [];
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (key.Equals("User-agent", StringComparison.OrdinalIgnoreCase))
            {
                if (rules.Count > 0) { groups.Add((agents, rules)); agents = []; rules = []; }
                agents.Add(value);
            }
            else if (agents.Count > 0 && (key.Equals("Allow", StringComparison.OrdinalIgnoreCase) || key.Equals("Disallow", StringComparison.OrdinalIgnoreCase)))
                rules.Add((key.Equals("Allow", StringComparison.OrdinalIgnoreCase), value));
        }
        groups.Add((agents, rules));
        var specific = groups.Where(g => g.Agents.Any(a => a != "*" && "Strazio".Contains(a, StringComparison.OrdinalIgnoreCase))).ToArray();
        var selected = specific.Length > 0 ? specific : groups.Where(g => g.Agents.Contains("*")).ToArray();
        var match = selected.SelectMany(g => g.Rules).Where(r => r.Path.Length > 0 &&
            Regex.IsMatch(path, "^" + Regex.Escape(r.Path.TrimEnd('$')).Replace(@"\*", ".*") + (r.Path.EndsWith('$') ? "$" : "")))
            .OrderByDescending(r => r.Path.Replace("*", "").Length).ThenByDescending(r => r.Allow).ToArray();
        return match.Length == 0 || match[0].Allow;
    }
}
