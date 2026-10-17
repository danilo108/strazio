using System.Globalization;
using Microsoft.Extensions.Logging;
using Strazio.Domain;

namespace Strazio.Application;

public sealed record SyncRequest(DateOnly From, DateOnly To, string Directory, SortOrder Order,
    bool Force = false, bool NonInteractive = false);
public sealed record SyncProgress(DateOnly Date, string Message);
public sealed record SyncResult(int Saved, int AlreadyPresent, int Unavailable, int Errors, bool Stopped);

public sealed class ArchiveSyncService(IExtractionSourceProvider sources, IArchiveWriter writer,
    IArchiveInspector inspector, IArchiveLock archiveLock, IRequestPacer pacer,
    IItalianClock clock, ILogger<ArchiveSyncService> logger)
{
    public async Task<SyncResult> SyncAsync(SyncRequest request, IProgress<SyncProgress>? progress,
        Func<ArchiveFile, CancellationToken, Task<bool>>? confirmReplace, CancellationToken ct)
    {
        if (request.To < request.From) throw new ArgumentException("La data finale precede quella iniziale.");
        if (request.To > clock.Today) throw new ArgumentException("Non posso scaricare giornate future.");
        using var lease = await archiveLock.AcquireAsync(request.Directory, ct);
        int saved = 0, present = 0, unavailable = 0, errors = 0;
        bool stopped = false;
        for (var date = request.From; date <= request.To; date = date.AddDays(1))
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(request.Directory, $"{date:yyyy-MM-dd}.xlsx");
            try
            {
                if (File.Exists(path) && !request.Force)
                {
                    var file = inspector.Inspect(path, date);
                    // Provisional workbooks are deliberately refreshed, even after midnight.
                    if (file.IsValid && !file.IsProvisional && date != clock.Today)
                    {
                        present++;
                        progress?.Report(new(date, "Già presente"));
                        continue;
                    }
                    if (!file.IsValid && (request.NonInteractive || confirmReplace is null ||
                        !await confirmReplace(file, ct)))
                    {
                        errors++;
                        progress?.Report(new(date, "File non valido: conservato. Usa --forza per sostituirlo dopo averlo controllato."));
                        continue;
                    }
                }
                DrawDay? day = null;
                Exception? lastError = null;
                foreach (var source in sources.GetPriorityOrderedSources())
                {
                    try
                    {
                        await pacer.WaitAsync(ct);
                        var candidate = await source.FetchAsync(date, ct);
                        if (candidate.Date != date) throw new InvalidDrawException("La fonte ha restituito una data diversa.");
                        DrawValidation.Validate(candidate);
                        day = candidate;
                        break;
                    }
                    catch (Exception ex) when (ex is SourceUnavailableException or InvalidDrawException)
                    {
                        lastError = ex;
                        logger.LogWarning("Fonte {Source}: {Error}", source.Id, ex.Message);
                    }
                }
                if (day is null) throw new SourceUnavailableException(lastError?.Message ?? "Non ci sono fonti disponibili.");
                // Only the Italian calendar decides whether a workbook is definitive.
                day = new DrawDay(day.Date, day.Draws, day.SourceId, date == clock.Today);
                await writer.WriteAsync(day, path, request.Order, ct);
                saved++;
                var numbers = day.Draws.Select(d => d.CompetitionNumber).Order().ToArray();
                var gaps = Enumerable.Range(1, numbers[^1]).Except(numbers).Count();
                progress?.Report(new(date, $"Salvato: {day.Draws.Count} concorsi{(day.IsProvisional ? " (provvisorio)" : "")}. {path}" +
                    (gaps > 0 ? $" Attenzione: {gaps} numeri di concorso mancanti nella fonte." : "")));
            }
            catch (SourceAccessException ex)
            {
                errors++;
                stopped = true;
                progress?.Report(new(date, ex.Message));
                break; // A block/429 never falls through to another source or day.
            }
            catch (DayUnavailableException ex)
            {
                unavailable++;
                progress?.Report(new(date, ex.Message));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors++;
                logger.LogWarning("Giornata {Date}: {Error}", date, ex.Message);
                var description = ex is IOException or UnauthorizedAccessException
                    ? "Non riesco a salvare il file. Controlla spazio e permessi; se è aperto in Excel, chiudilo e riprova."
                    : ex.Message;
                progress?.Report(new(date, $"Errore: {description}"));
            }
        }
        return new(saved, present, unavailable, errors, stopped);
    }
}

public static class ArchiveCatalog
{
    public static IReadOnlyList<ArchiveFile> Scan(string directory, IArchiveInspector inspector)
    {
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, "*.xlsx")
            .Where(p => Path.GetFileName(p).Length == 15)
            .Select(p => (Path: p, Name: Path.GetFileNameWithoutExtension(p)))
            .Where(p => DateOnly.TryParseExact(p.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .Select(p => inspector.Inspect(p.Path, DateOnly.ParseExact(p.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .OrderBy(p => p.Date).ToArray();
    }
}

public static class ItalianDate
{
    public static DateOnly Parse(string value, DateOnly today)
    {
        value = value.Trim();
        if (value.Equals("oggi", StringComparison.OrdinalIgnoreCase)) return today;
        if (value.Equals("ieri", StringComparison.OrdinalIgnoreCase)) return today.AddDays(-1);
        if (DateOnly.TryParseExact(value, ["d/M/yyyy", "dd/MM/yyyy", "yyyy-MM-dd"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        throw new ArgumentException($"Questa data non sembra giusta: {value}. Prova giorno/mese/anno, per esempio 01/10/2026.");
    }
}
