using Strazio.Domain;

namespace Strazio.Application;

public sealed record AppSettings
{
    public int Version { get; init; } = 1;
    public string? ArchivePath { get; init; }
    public SortOrder SortOrder { get; init; } = SortOrder.Desc;
    public bool MessagesEnabled { get; init; } = true;
    public string ScheduledTaskName { get; init; } = "Strazio - Archivio 10eLotto";
    public string DailyLocalTime { get; init; } = "10:00";
    public DateTimeOffset? LastSuccessfulSyncUtc { get; init; }
    public DateOnly? SyncStartDate { get; init; }
}

public interface IExtractionSource
{
    string Id { get; }
    Task<DrawDay> FetchAsync(DateOnly date, CancellationToken ct);
}
public interface IExtractionSourceProvider
{
    IReadOnlyList<IExtractionSource> GetPriorityOrderedSources();
}
public interface IArchiveWriter
{
    Task WriteAsync(DrawDay day, string destination, SortOrder order, CancellationToken ct);
}
public sealed record ArchiveFile(DateOnly Date, string Path, bool IsValid, bool IsProvisional, string? Error = null);
public interface IArchiveInspector
{
    ArchiveFile Inspect(string path, DateOnly date);
    Task ReorderAsync(string path, DateOnly date, SortOrder order, CancellationToken ct);
}
public interface ISettingsRepository
{
    Task<AppSettings> LoadAsync(CancellationToken ct);
    Task SaveAsync(AppSettings settings, CancellationToken ct);
}
public interface IUserMessages
{
    string PickRandom();
    Task PresentAsync(string message, bool showToast, CancellationToken ct);
}
public sealed record ScheduleDefinition(string Name, string ExecutablePath, TimeOnly DailyTime, string UserId);
public sealed record ScheduleStatus(bool Exists, string Description);
public interface IScheduledArchiveService
{
    Task CreateOrUpdateAsync(ScheduleDefinition schedule, CancellationToken ct);
    Task<ScheduleStatus> GetStatusAsync(CancellationToken ct);
}
public interface IArchiveLock
{
    Task<IDisposable> AcquireAsync(string directory, CancellationToken ct);
}
public interface IRequestPacer { Task WaitAsync(CancellationToken ct); }
public interface IItalianClock
{
    DateOnly Today { get; }
    DateTimeOffset UtcNow { get; }
}
public interface IWindowsSession
{
    bool IsElevated { get; }
    string ExecutablePath { get; }
    string UserId { get; }
}
public sealed class ItalianClock : IItalianClock
{
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome")).DateTime);
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
public sealed class SourceUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class DayUnavailableException(string message) : Exception(message);
public sealed class SourceAccessException(string message, DateTimeOffset? retryAfter = null) : Exception(message)
{
    public DateTimeOffset? RetryAfter { get; } = retryAfter;
}
