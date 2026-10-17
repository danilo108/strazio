namespace Strazio.Domain;

public enum SortOrder { Asc, Desc }

public sealed record Draw
{
    public DateOnly Date { get; }
    public int CompetitionNumber { get; }
    public IReadOnlyList<int> MainNumbers20 { get; }
    public int Oro { get; }
    public int DoppioOroSecondNumber { get; }
    public IReadOnlyList<int>? ExtraNumbers15 { get; }

    public Draw(DateOnly date, int competitionNumber, IEnumerable<int> mainNumbers20,
        int oro, int doppioOroSecondNumber, IEnumerable<int>? extraNumbers15)
    {
        Date = date;
        CompetitionNumber = competitionNumber;
        MainNumbers20 = Array.AsReadOnly(mainNumbers20.ToArray());
        Oro = oro;
        DoppioOroSecondNumber = doppioOroSecondNumber;
        ExtraNumbers15 = extraNumbers15 is null ? null : Array.AsReadOnly(extraNumbers15.ToArray());
    }
}

public sealed record DrawDay
{
    public DateOnly Date { get; }
    public IReadOnlyList<Draw> Draws { get; }
    public string SourceId { get; }
    public bool IsProvisional { get; }
    public DrawDay(DateOnly date, IEnumerable<Draw> draws, string sourceId, bool isProvisional)
    {
        Date = date;
        Draws = Array.AsReadOnly(draws.ToArray());
        SourceId = sourceId;
        IsProvisional = isProvisional;
    }
}

public sealed record ArchiveRow(DateOnly Date, int CompetitionNumber, IReadOnlyList<int> MainNumbers18,
    int Oro, int DoppioOroSecondNumber, IReadOnlyList<int>? ExtraNumbers15);

public sealed class InvalidDrawException(string message) : Exception(message);

public static class DrawValidation
{
    public static readonly DateOnly ExtraStartDate = new(2020, 9, 10);

    public static void Validate(Draw draw)
    {
        if (draw.CompetitionNumber is < 1 or > 288)
            throw new InvalidDrawException("Il numero di concorso deve essere compreso tra 1 e 288.");
        ValidateNumbers(draw.MainNumbers20, 20, "estratti");
        if (draw.Oro == draw.DoppioOroSecondNumber || !draw.MainNumbers20.Contains(draw.Oro) ||
            !draw.MainNumbers20.Contains(draw.DoppioOroSecondNumber))
            throw new InvalidDrawException("Oro e Doppio Oro devono essere distinti e presenti nei 20 estratti.");
        if (draw.ExtraNumbers15 is not null) ValidateNumbers(draw.ExtraNumbers15, 15, "Extra");
        else if (draw.Date >= ExtraStartDate)
            throw new InvalidDrawException("Mancano i 15 numeri Extra del concorso.");
    }

    public static void Validate(DrawDay day)
    {
        if (day.Draws.Count is < 1 or > 288)
            throw new InvalidDrawException("La giornata deve contenere da 1 a 288 concorsi.");
        if (day.Draws.Select(d => d.CompetitionNumber).Distinct().Count() != day.Draws.Count)
            throw new InvalidDrawException("Ci sono numeri di concorso duplicati nella giornata.");
        foreach (var draw in day.Draws)
        {
            if (draw.Date != day.Date) throw new InvalidDrawException("Le date dei concorsi non coincidono.");
            Validate(draw);
        }
    }

    public static ArchiveRow Normalize(Draw draw)
    {
        Validate(draw);
        return new(draw.Date, draw.CompetitionNumber,
            Array.AsReadOnly(draw.MainNumbers20.Where(n => n != draw.Oro && n != draw.DoppioOroSecondNumber).ToArray()),
            draw.Oro, draw.DoppioOroSecondNumber, draw.ExtraNumbers15);
    }

    private static void ValidateNumbers(IReadOnlyList<int> numbers, int count, string label)
    {
        if (numbers.Count != count || numbers.Distinct().Count() != count || numbers.Any(n => n is < 1 or > 90))
            throw new InvalidDrawException($"I numeri {label} devono essere {count}, distinti e compresi tra 1 e 90.");
    }
}
