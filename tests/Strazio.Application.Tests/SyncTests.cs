using Microsoft.Extensions.Logging.Abstractions;
using Strazio.Domain;

namespace Strazio.Application.Tests;

public class SyncTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "StrazioTests", Guid.NewGuid().ToString("N"));
    private static readonly DateOnly First = new(2026, 10, 1);
    private readonly FakeClock clock = new();
    public SyncTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    [Theory]
    [InlineData("01/10/2026")]
    [InlineData("1/10/2026")]
    [InlineData("2026-10-01")]
    public void Italian_date_formats(string value) => Assert.Equal(First, ItalianDate.Parse(value, First));
    [Theory]
    [InlineData("31/02/2026")]
    [InlineData("01-10-2026")]
    [InlineData("")]
    public void Invalid_dates_fail(string value) => Assert.Throws<ArgumentException>(() => ItalianDate.Parse(value, First));
    [Fact]
    public void Relative_dates_use_given_Italian_day()
    {
        Assert.Equal(First, ItalianDate.Parse("OGGI", First));
        Assert.Equal(First.AddDays(-1), ItalianDate.Parse("ieri", First));
    }

    [Fact]
    public async Task Failed_day_does_not_block_later_days()
    {
        var source = new Source(d => d == First ? throw new SourceUnavailableException("errore") : Day(d));
        var writer = new Writer();
        var result = await Service([source], writer).SyncAsync(new(First, First.AddDays(2), directory, SortOrder.Desc), null, null, default);
        Assert.Equal(1, result.Errors); Assert.Equal(2, result.Saved); Assert.Equal(2, writer.Days.Count);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Access_block_or_rate_limit_stops_batch_and_never_falls_back(bool rateLimit)
    {
        var first = new Source(_ => throw new SourceAccessException(rateLimit ? "429" : "403"));
        var second = new Source(Day);
        var result = await Service([first, second], new Writer()).SyncAsync(new(First, First.AddDays(2), directory, SortOrder.Desc), null, null, default);
        Assert.True(result.Stopped); Assert.Equal(1, first.Calls); Assert.Equal(0, second.Calls);
    }
    [Fact]
    public async Task Real_second_adapter_receives_transient_fallback()
    {
        var second = new Source(Day);
        var result = await Service([new Source(_ => throw new SourceUnavailableException("500")), second], new Writer()).SyncAsync(new(First, First, directory, SortOrder.Desc), null, null, default);
        Assert.Equal(1, result.Saved); Assert.Equal(1, second.Calls);
    }
    [Fact]
    public async Task Existing_valid_files_are_skipped_but_missing_days_are_fetched()
    {
        File.WriteAllText(Path.Combine(directory, "2026-10-01.xlsx"), "placeholder");
        File.WriteAllText(Path.Combine(directory, "2026-10-03.xlsx"), "placeholder");
        var source = new Source(Day);
        var result = await Service([source], new Writer()).SyncAsync(new(First, First.AddDays(2), directory, SortOrder.Desc), null, null, default);
        Assert.Equal(2, result.AlreadyPresent); Assert.Equal(1, source.Calls); Assert.Equal(1, result.Saved);
    }
    [Fact]
    public async Task Provisional_file_is_refreshed_after_midnight()
    {
        File.WriteAllText(Path.Combine(directory, "2026-10-01.xlsx"), "placeholder");
        var writer = new Writer();
        var result = await Service([new Source(Day)], writer, new Inspector(true, true)).SyncAsync(new(First, First, directory, SortOrder.Desc), null, null, default);
        Assert.Equal(1, result.Saved); Assert.False(writer.Days[0].IsProvisional);
    }
    [Fact]
    public async Task Corrupt_file_is_preserved_in_unattended_batch()
    {
        var path = Path.Combine(directory, "2026-10-01.xlsx");
        File.WriteAllText(path, "corrotto");
        var source = new Source(Day);
        var result = await Service([source], new Writer(), new Inspector(false, false)).SyncAsync(new(First, First, directory, SortOrder.Desc, NonInteractive: true), null, null, default);
        Assert.Equal(1, result.Errors); Assert.Equal(0, source.Calls); Assert.Equal("corrotto", File.ReadAllText(path));
    }
    [Fact]
    public async Task Force_explicitly_replaces_corrupt_file()
    {
        File.WriteAllText(Path.Combine(directory, "2026-10-01.xlsx"), "corrotto");
        var result = await Service([new Source(Day)], new Writer(), new Inspector(false, false)).SyncAsync(new(First, First, directory, SortOrder.Desc, Force: true, NonInteractive: true), null, null, default);
        Assert.Equal(1, result.Saved);
    }
    [Fact]
    public async Task Source_cannot_return_a_different_day()
    {
        var result = await Service([new Source(_ => Day(First.AddDays(1)))], new Writer()).SyncAsync(new(First, First, directory, SortOrder.Desc), null, null, default);
        Assert.Equal(1, result.Errors); Assert.Equal(0, result.Saved);
    }
    [Fact]
    public async Task Future_days_are_rejected_before_fetch()
    {
        var source = new Source(Day);
        await Assert.ThrowsAsync<ArgumentException>(() => Service([source], new Writer()).SyncAsync(new(First, clock.Today.AddDays(1), directory, SortOrder.Desc), null, null, default));
        Assert.Equal(0, source.Calls);
    }
    [Fact]
    public async Task Cancellation_is_propagated()
    {
        using var token = new CancellationTokenSource(); token.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service([new Source(Day)], new Writer()).SyncAsync(new(First, First, directory, SortOrder.Desc), null, null, token.Token));
    }

    private ArchiveSyncService Service(IReadOnlyList<IExtractionSource> sources, Writer writer, Inspector? inspector = null) =>
        new(new Provider(sources), writer, inspector ?? new Inspector(true, false), new NoLock(), new Pacer(), clock, NullLogger<ArchiveSyncService>.Instance);
    private static DrawDay Day(DateOnly d) => new(d, [new Draw(d, 1, Enumerable.Range(1, 20), 1, 2, Enumerable.Range(21, 15))], "test", false);
    private sealed class Source(Func<DateOnly, DrawDay> fetch) : IExtractionSource
    { public int Calls; public string Id => "test"; public Task<DrawDay> FetchAsync(DateOnly d, CancellationToken ct) { Calls++; return Task.FromResult(fetch(d)); } }
    private sealed class Provider(IReadOnlyList<IExtractionSource> sources) : IExtractionSourceProvider { public IReadOnlyList<IExtractionSource> GetPriorityOrderedSources() => sources; }
    private sealed class Writer : IArchiveWriter { public List<DrawDay> Days { get; } = []; public Task WriteAsync(DrawDay day, string path, SortOrder order, CancellationToken ct) { Days.Add(day); return Task.CompletedTask; } }
    private sealed class Inspector(bool valid, bool provisional) : IArchiveInspector
    { public ArchiveFile Inspect(string p, DateOnly d) => new(d, p, valid, provisional); public Task ReorderAsync(string p, DateOnly d, SortOrder o, CancellationToken ct) => Task.CompletedTask; }
    private sealed class FakeClock : IItalianClock { public DateOnly Today => new(2026, 10, 17); public DateTimeOffset UtcNow => new(2026, 10, 17, 8, 0, 0, TimeSpan.Zero); }
    private sealed class Pacer : IRequestPacer { public Task WaitAsync(CancellationToken ct) => Task.CompletedTask; }
    private sealed class NoLock : IArchiveLock, IDisposable { public Task<IDisposable> AcquireAsync(string d, CancellationToken ct) => Task.FromResult<IDisposable>(this); public void Dispose() { } }
}
