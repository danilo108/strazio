using System.Text.Json;
using Strazio.Infrastructure.Storage;

namespace Strazio.Infrastructure.Http;

public interface ISourceCooldown
{
    Task<DateTimeOffset?> ReadAsync(CancellationToken ct);
    Task SaveAsync(DateTimeOffset notBeforeUtc, CancellationToken ct);
}

public sealed class FileSourceCooldown : ISourceCooldown
{
    private readonly string path;
    public FileSourceCooldown() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Strazio", "source-cooldown.json")) { }
    public FileSourceCooldown(string filePath) => path = filePath;

    public async Task<DateTimeOffset?> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        try { return (await JsonSerializer.DeserializeAsync<State>(file, cancellationToken: ct))?.NotBeforeUtc; }
        catch (JsonException ex) { throw new IOException("Il file del limite richieste non è valido. Controlla source-cooldown.json prima di riprovare.", ex); }
    }

    public async Task SaveAsync(DateTimeOffset notBeforeUtc, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(file, new State(notBeforeUtc), cancellationToken: ct);
                file.Flush(true);
            }
            AtomicFiles.Commit(temporary, path, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private sealed record State(DateTimeOffset NotBeforeUtc);
}
