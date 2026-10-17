using System.Text.Json;
using System.Text.Json.Serialization;
using Strazio.Application;

namespace Strazio.Infrastructure.Storage;

public sealed class JsonSettingsStore : ISettingsRepository
{
    public string FilePath { get; }
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public JsonSettingsStore() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Strazio", "settings.json")) { }
    public JsonSettingsStore(string filePath) => FilePath = Path.GetFullPath(filePath);

    public async Task<AppSettings> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(FilePath)) return new();
        try
        {
            await using var file = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(file, Options, ct)
                ?? throw new JsonException();
            if (settings.Version != 1 || !Enum.IsDefined(settings.SortOrder) ||
                !TimeOnly.TryParseExact(settings.DailyLocalTime, "HH:mm", out _)) throw new JsonException();
            return settings;
        }
        catch (JsonException ex)
        {
            throw new IOException($"Le impostazioni non sono valide. Conserva una copia e controlla il file: {FilePath}", ex);
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(file, settings, Options, ct);
                await file.FlushAsync(ct);
                file.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            AtomicFiles.Commit(temporary, FilePath, backup: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
