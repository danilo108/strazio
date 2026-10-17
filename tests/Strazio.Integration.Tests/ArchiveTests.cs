using AngleSharp.Html.Parser;
using ClosedXML.Excel;
using Strazio.Application;
using Strazio.Domain;
using Strazio.Infrastructure.Excel;
using Strazio.Infrastructure.Http;
using Strazio.Infrastructure.Storage;
using Strazio.Infrastructure.Windows;

namespace Strazio.Integration.Tests;

public class ArchiveTests : IDisposable
{
    private static readonly DateOnly Date = new(2026, 10, 1);
    private readonly string directory = Path.Combine(Path.GetTempPath(), "StrazioTests", Guid.NewGuid().ToString("N"));
    private static string Html => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "2026-10-01.html"));
    private readonly ClosedXmlArchive archive = new();
    public ArchiveTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private string Destination => Path.Combine(directory, "2026-10-01.xlsx");
    private static DrawDay Parse(string? html = null) => new DrawMapper(new ItalianClock()).Map(Date, new RawExtractionParser().Parse(html ?? Html, Date));

    [Fact]
    public void Real_fixture_has_exactly_288_distinct_draws_and_matches_example()
    {
        var day = Parse();
        Assert.Equal(288, day.Draws.Count);
        var first = day.Draws.Single(d => d.CompetitionNumber == 1);
        Assert.Equal(new[] { 10, 12, 20, 25, 27, 30, 31, 39, 42, 43, 46, 51, 53, 66, 73, 74, 75, 76, 83, 85 }, first.MainNumbers20);
        Assert.Equal(30, first.Oro); Assert.Equal(25, first.DoppioOroSecondNumber);
        Assert.Equal(new[] { 2, 4, 11, 24, 28, 40, 44, 55, 59, 65, 70, 71, 77, 79, 88 }, first.ExtraNumbers15);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(96)]
    [InlineData(287)]
    public async Task Partial_source_produces_exact_number_of_rows(int count)
    {
        using var doc = new HtmlParser().ParseDocument(Html);
        var rows = doc.QuerySelectorAll("table.tabella-desktop tbody > tr");
        foreach (var row in rows.Skip(count * 2)) row.Remove();
        var heading = doc.QuerySelectorAll("h2").Single(h => h.TextContent.Contains("Estrazioni 10eLotto 5 minuti"));
        heading.TextContent = heading.TextContent.Replace("288 concorsi", $"{count} concorsi");
        var day = Parse(doc.DocumentElement.OuterHtml);
        Assert.Equal(count, day.Draws.Count);
        await archive.WriteAsync(day, Destination, SortOrder.Desc, default);
        using var workbook = new XLWorkbook(Destination);
        Assert.Equal(count, workbook.Worksheet(1).LastRowUsed()!.RowNumber());
    }
    [Fact]
    public void Missing_competition_is_not_synthesized()
    {
        using var doc = new HtmlParser().ParseDocument(Html);
        var row = doc.QuerySelectorAll("table.tabella-desktop .riga-principale")[1];
        row.NextElementSibling!.Remove(); row.Remove();
        var heading = doc.QuerySelector("h2")!; heading.TextContent = heading.TextContent.Replace("288 concorsi", "287 concorsi");
        var day = Parse(doc.DocumentElement.OuterHtml);
        Assert.Equal(287, day.Draws.Count); Assert.DoesNotContain(day.Draws, d => d.CompetitionNumber == 2);
    }
    [Fact] public void Heading_count_mismatch_fails() => Assert.Throws<SourceUnavailableException>(() => Parse(Html.Replace("(288 concorsi)", "(287 concorsi)")));
    [Fact] public void Wrong_day_fails() => Assert.Throws<SourceUnavailableException>(() => new RawExtractionParser().Parse(Html, new(2026, 10, 2)));
    [Fact] public void Missing_desktop_table_fails() => Assert.Throws<SourceUnavailableException>(() => Parse(Html.Replace("class=\"tabella-desktop\"", "class=\"changed\"")));
    [Fact]
    public void Missing_extra_cannot_be_associated_with_next_draw()
    {
        using var doc = new HtmlParser().ParseDocument(Html); doc.QuerySelector("table.tabella-desktop .extra-row")!.Remove();
        Assert.Throws<SourceUnavailableException>(() => Parse(doc.DocumentElement.OuterHtml));
    }
    [Fact]
    public async Task Workbook_columns_types_and_gold_style_survive_both_reorders()
    {
        await archive.WriteAsync(Parse(), Destination, SortOrder.Desc, default);
        CheckWorkbook(288);
        await archive.ReorderAsync(Destination, Date, SortOrder.Asc, default);
        CheckWorkbook(1);
        await archive.ReorderAsync(Destination, Date, SortOrder.Desc, default);
        CheckWorkbook(288);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(directory, ".strazio-backups"), "*.xlsx").Length);
    }
    private void CheckWorkbook(int first)
    {
        Assert.True(archive.Inspect(Destination, Date).IsValid);
        using var workbook = new XLWorkbook(Destination); Assert.Single(workbook.Worksheets);
        var sheet = workbook.Worksheet("bravo figlio"); Assert.Equal(288, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal(first, sheet.Cell(1, 2).GetValue<int>());
        var row = sheet.RowsUsed().Single(r => r.Cell(2).GetValue<int>() == 1).RowNumber();
        Assert.Equal(Date.ToDateTime(TimeOnly.MinValue), sheet.Cell(row, 1).GetDateTime());
        Assert.Equal(sheet.Cell(row, 1).Value, sheet.Cell(row, 25).Value);
        Assert.Equal(1, sheet.Cell(row, 26).GetValue<int>()); Assert.False(sheet.Cell(row, 26).HasFormula);
        Assert.Equal(new[] { 10, 12, 20, 27, 31, 39, 42, 43, 46, 51, 53, 66, 73, 74, 75, 76, 83, 85 }, Enumerable.Range(3, 18).Select(c => sheet.Cell(row, c).GetValue<int>()));
        Assert.Equal(30, sheet.Cell(row, 21).GetValue<int>()); Assert.Equal(25, sheet.Cell(row, 22).GetValue<int>());
        Assert.Equal(new[] { 2, 4, 11, 24, 28, 40, 44, 55, 59, 65, 70, 71, 77, 79, 88 }, Enumerable.Range(27, 15).Select(c => sheet.Cell(row, c).GetValue<int>()));
        foreach (var r in sheet.RowsUsed())
        {
            Assert.True(r.Cell(23).IsEmpty()); Assert.True(r.Cell(24).IsEmpty());
            foreach (var c in new[] { 21, 22 }) { Assert.True(r.Cell(c).Style.Font.Bold); Assert.Equal("FF0000FF", r.Cell(c).Style.Font.FontColor.Color.ToArgb().ToString("X8")); }
            Assert.False(r.Cell(3).Style.Font.Bold);
        }
    }
    [Fact]
    public async Task File_locked_by_Excel_remains_byte_for_byte_intact()
    {
        await archive.WriteAsync(Parse(), Destination, SortOrder.Desc, default);
        var before = File.ReadAllBytes(Destination);
        using (var locked = new FileStream(Destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAnyAsync<IOException>(() => archive.WriteAsync(Parse(), Destination, SortOrder.Asc, default));
        Assert.Equal(before, File.ReadAllBytes(Destination));
    }
    [Fact]
    public async Task Cancellation_never_publishes_partial_workbook()
    {
        using var token = new CancellationTokenSource(); token.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => archive.WriteAsync(Parse(), Destination, SortOrder.Desc, token.Token));
        Assert.False(File.Exists(Destination));
    }
    [Fact]
    public async Task Historical_days_leave_extra_really_empty()
    {
        var date = new DateOnly(2020, 9, 9); var destination = Path.Combine(directory, "2020-09-09.xlsx");
        await archive.WriteAsync(new(date, [new Draw(date, 1, Enumerable.Range(1, 20), 1, 2, null)], "test", false), destination, SortOrder.Desc, default);
        Assert.True(archive.Inspect(destination, date).IsValid);
        using var book = new XLWorkbook(destination); Assert.All(Enumerable.Range(27, 15), c => Assert.True(book.Worksheet(1).Cell(1, c).IsEmpty()));
    }
    [Fact]
    public async Task Provisional_state_survives_disk_roundtrip()
    {
        var day = Parse(); await archive.WriteAsync(new(day.Date, day.Draws, day.SourceId, true), Destination, SortOrder.Desc, default);
        Assert.True(archive.Inspect(Destination, Date).IsProvisional);
    }
    [Fact]
    public async Task Settings_are_atomic_and_preserve_defaults()
    {
        var path = Path.Combine(directory, "settings.json"); var store = new JsonSettingsStore(path); var initial = await store.LoadAsync(default);
        Assert.True(initial.MessagesEnabled); Assert.Equal(SortOrder.Desc, initial.SortOrder);
        var settings = initial with { ArchivePath = directory, MessagesEnabled = false, SyncStartDate = Date };
        await store.SaveAsync(settings, default); await store.SaveAsync(settings with { SortOrder = SortOrder.Asc }, default);
        var loaded = await store.LoadAsync(default); Assert.False(loaded.MessagesEnabled); Assert.Equal(Date, loaded.SyncStartDate); Assert.Equal(SortOrder.Asc, loaded.SortOrder);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }
    [Fact]
    public async Task Broken_settings_are_not_silently_overwritten()
    {
        var path = Path.Combine(directory, "settings.json"); File.WriteAllText(path, "invalid");
        await Assert.ThrowsAsync<IOException>(() => new JsonSettingsStore(path).LoadAsync(default)); Assert.Equal("invalid", File.ReadAllText(path));
    }
    [Fact]
    public void Failed_atomic_commit_keeps_original()
    {
        var original = Path.Combine(directory, "original"); File.WriteAllText(original, "intatto");
        Assert.ThrowsAny<IOException>(() => AtomicFiles.Commit(Path.Combine(directory, "missing-temp"), original, true));
        Assert.Equal("intatto", File.ReadAllText(original));
    }
    [Fact]
    public async Task Mutex_prevents_overlapping_archive_operations()
    {
        var mutex = new ArchiveMutex(); using var first = await mutex.AcquireAsync(directory, default);
        await Assert.ThrowsAsync<IOException>(() => new ArchiveMutex().AcquireAsync(directory, default));
    }
    [Fact]
    public void Phrase_catalogue_is_embedded_and_not_copied_to_output()
    {
        Assert.Contains("Strazio.Frasi", typeof(EmbeddedUserMessages).Assembly.GetManifestResourceNames());
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "frasi_affettuose_strazio.txt")));
        Assert.False(string.IsNullOrWhiteSpace(new EmbeddedUserMessages().PickRandom()));
    }
}
