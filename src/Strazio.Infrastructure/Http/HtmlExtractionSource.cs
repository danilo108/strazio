using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Strazio.Application;
using Strazio.Domain;

namespace Strazio.Infrastructure.Http;

public interface IPageFetcher { Task<string> FetchAsync(Uri uri, CancellationToken ct); }
public sealed record RawDraw(int Competition, IReadOnlyList<int> Main, int Oro, int SecondOro, IReadOnlyList<int>? Extra);
public interface IRawExtractionParser { IReadOnlyList<RawDraw> Parse(string html, DateOnly date); }
public interface IDrawMapper { DrawDay Map(DateOnly date, IReadOnlyList<RawDraw> draws); }

public sealed partial class RawExtractionParser : IRawExtractionParser
{
    public IReadOnlyList<RawDraw> Parse(string html, DateOnly date)
    {
        using var document = new HtmlParser().ParseDocument(html);
        var headings = document.QuerySelectorAll("h2").Where(h =>
            Normalize(h.TextContent).Contains("Estrazioni 10eLotto 5 minuti", StringComparison.OrdinalIgnoreCase)).ToArray();
        var heading = headings.SingleOrDefault(h => Normalize(h.TextContent).Contains(date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), StringComparison.Ordinal));
        if (heading is null)
            throw new SourceUnavailableException("Il titolo della pagina manca o mostra una data diversa da quella richiesta.");
        // The matching table must follow the heading in the same archive section.
        IElement? table = null;
        for (var sibling = heading.NextElementSibling; sibling is not null; sibling = sibling.NextElementSibling)
        {
            if (sibling.TagName == "H2") break;
            table = sibling.Matches("table.tabella-desktop") ? sibling : sibling.QuerySelector("table.tabella-desktop");
            if (table is not null) break;
        }
        if (table is null) throw new SourceUnavailableException("Non trovo la tabella desktop delle estrazioni.");
        var result = new List<RawDraw>();
        var rows = table.QuerySelectorAll("tbody > tr");
        RawDraw? pending = null;
        foreach (var row in rows)
        {
            if (row.ClassList.Contains("riga-principale"))
            {
                if (pending is not null) AddPending();
                var meta = row.QuerySelector("td.meta") ?? throw SchemaError();
                var competition = Integer(meta.TextContent);
                var chips = row.QuerySelectorAll("td > span.chip");
                var main = chips.Where(c => !c.ClassList.Contains("chip-oro") && !c.ClassList.Contains("chip-extra")).Select(c => Integer(c.TextContent)).ToArray();
                var gold = chips.Where(c => c.ClassList.Contains("chip-oro")).Select(c => Integer(c.TextContent)).ToArray();
                if (main.Length != 20 || gold.Length != 2) throw SchemaError();
                pending = new(competition, Array.AsReadOnly(main), gold[0], gold[1], null);
            }
            else if (row.ClassList.Contains("extra-row"))
            {
                if (pending is null || !Normalize(row.QuerySelector("td.meta")?.TextContent ?? "").Equals("EXTRA", StringComparison.OrdinalIgnoreCase))
                    throw SchemaError();
                var extra = row.QuerySelectorAll(".chip-extra").Select(c => Integer(c.TextContent)).ToArray();
                if (extra.Length != 15) throw SchemaError();
                result.Add(pending with { Extra = Array.AsReadOnly(extra) });
                pending = null;
            }
            else if (row.QuerySelector("td") is not null)
                throw SchemaError(); // Unknown data rows must not silently disappear.
        }
        if (pending is not null) AddPending();
        if (result.Count == 0) throw new DayUnavailableException("Nessun concorso disponibile per questa giornata.");
        if (result.Select(r => r.Competition).Distinct().Count() != result.Count) throw SchemaError();
        var count = CountPattern().Match(heading.TextContent);
        if (count.Success && Integer(count.Groups[1].Value) != result.Count)
            throw new SourceUnavailableException("Il numero dei concorsi nel titolo non coincide con la tabella: file non creato.");
        return result.AsReadOnly();

        void AddPending()
        {
            if (date >= DrawValidation.ExtraStartDate) throw new SourceUnavailableException("Manca la riga Extra associata a un concorso.");
            result.Add(pending!);
            pending = null;
        }
    }

    private static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();
    private static int Integer(string text) => int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : throw SchemaError();
    private static SourceUnavailableException SchemaError() => new("La struttura HTML delle estrazioni è cambiata o contiene dati incoerenti.");
    [GeneratedRegex(@"\((\d+)\s+concorsi?\)", RegexOptions.IgnoreCase)]
    private static partial Regex CountPattern();
}

public sealed class DrawMapper(IItalianClock clock) : IDrawMapper
{
    public DrawDay Map(DateOnly date, IReadOnlyList<RawDraw> draws)
    {
        var day = new DrawDay(date, draws.Select(d => new Draw(date, d.Competition, d.Main, d.Oro, d.SecondOro, d.Extra)),
            HtmlExtractionSource.SourceId, date == clock.Today);
        DrawValidation.Validate(day);
        return day;
    }
}

public sealed class HtmlExtractionSource(IPageFetcher fetcher, IRawExtractionParser parser, IDrawMapper mapper) : IExtractionSource
{
    public const string SourceId = "10elotto5.it-html";
    public string Id => SourceId;
    public async Task<DrawDay> FetchAsync(DateOnly date, CancellationToken ct)
    {
        var html = await fetcher.FetchAsync(new($"https://www.10elotto5.it/archivio-10elotto5-minuti/?data={date:yyyy-MM-dd}"), ct);
        return mapper.Map(date, parser.Parse(html, date));
    }
}

public sealed class ExtractionSourceProvider(IEnumerable<IExtractionSource> sources) : IExtractionSourceProvider
{
    private readonly IReadOnlyList<IExtractionSource> ordered = Array.AsReadOnly(sources.ToArray());
    public IReadOnlyList<IExtractionSource> GetPriorityOrderedSources() => ordered;
}
