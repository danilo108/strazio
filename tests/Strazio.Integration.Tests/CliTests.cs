using Microsoft.Extensions.Logging.Abstractions;
using Strazio.Application;
using Strazio.Cli;
using Strazio.Domain;
using Strazio.Infrastructure.Excel;
using Strazio.Infrastructure.Http;
using Strazio.Infrastructure.Storage;

namespace Strazio.Integration.Tests;

[CollectionDefinition("Console", DisableParallelization = true)]
public class ConsoleCollection;

[Collection("Console")]
public class CliTests : IDisposable
{
    private readonly TextWriter oldOut = Console.Out;
    private readonly TextWriter oldError = Console.Error;
    private readonly StringWriter output = new();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "StrazioTests", Guid.NewGuid().ToString("N"));
    private readonly Store store = new();
    private readonly Messages messages = new();
    private readonly string oldDirectory = Environment.CurrentDirectory;
    public CliTests() { Directory.CreateDirectory(directory); Console.SetOut(output); Console.SetError(output); }
    public void Dispose() { Console.SetOut(oldOut); Console.SetError(oldError); Environment.CurrentDirectory = oldDirectory; Directory.Delete(directory, true); output.Dispose(); }
    private CliRunner Runner(IUserInput? input = null, bool elevated = false, Schedule? scheduler = null, bool withSource = false)
    {
        var excel = new ClosedXmlArchive(); var mutex = new ArchiveMutex(); var clock = new ItalianClock();
        return new(store, messages, new ArchiveSyncService(new ExtractionSourceProvider(withSource ? [new OfflineSource()] : []), excel, excel, mutex, new RandomRequestPacer(), clock, NullLogger<ArchiveSyncService>.Instance), excel, mutex, scheduler ?? new Schedule(), clock, new Session(elevated), input);
    }
    [Fact]
    public async Task Disable_is_saved_before_any_presentation()
    {
        var code = await Runner().RunAsync(["basta-messaggi"], default);
        Assert.Equal(0, code); Assert.False(store.Settings.MessagesEnabled); Assert.Equal(0, messages.Presented);
        await Runner().RunAsync(["stato"], default); Assert.Equal(0, messages.Presented);
    }
    [Fact]
    public async Task Reenable_saves_and_presents_exactly_once()
    {
        store.Settings = store.Settings with { MessagesEnabled = false };
        Assert.Equal(0, await Runner().RunAsync(["vabbe"], default));
        Assert.True(store.Settings.MessagesEnabled); Assert.Equal(1, messages.Presented);
    }
    [Theory]
    [InlineData("--guida")]
    [InlineData("scarica --guida")]
    [InlineData("comando-sbagliato")]
    public async Task Help_and_syntax_errors_present_one_phrase(string args)
    {
        await Runner().RunAsync(args.Split(' '), default); Assert.Equal(1, messages.Presented);
    }
    [Fact]
    public async Task Scheduled_invocation_never_presents_phrases()
    {
        await Runner().RunAsync(["__esegui-pianificato", "--non-interattivo"], default); Assert.Equal(0, messages.Presented);
    }
    [Fact]
    public async Task Noninteractive_missing_dates_return_without_prompting()
    {
        Assert.NotEqual(0, await Runner().RunAsync(["scarica", "--non-interattivo", "--cartella", directory], default));
        Assert.Contains("Manca un parametro obbligatorio", output.ToString());
    }
    [Fact]
    public async Task Conflicting_date_options_are_rejected()
    {
        Assert.NotEqual(0, await Runner().RunAsync(["scarica", "--data", "2026-10-01", "--dal", "2026-10-01", "--non-interattivo"], default));
        Assert.Contains("senza combinarli", output.ToString());
    }
    [Fact]
    public async Task English_help_is_not_an_alias()
    {
        Assert.Equal(2, await Runner().RunAsync(["--help", "--non-interattivo"], default)); Assert.DoesNotContain("Usage:", output.ToString());
    }
    [Fact]
    public async Task Command_help_is_in_Italian_and_does_not_advertise_English_aliases()
    {
        Assert.Equal(0, await Runner().RunAsync(["scarica", "--guida", "--non-interattivo"], default));
        Assert.Contains("Uso:", output.ToString()); Assert.Contains("--cartella", output.ToString()); Assert.DoesNotContain("--help", output.ToString());
    }
    [Fact]
    public async Task Root_guide_without_subcommand_is_successful()
    {
        Assert.Equal(0, await Runner().RunAsync(["--guida", "--non-interattivo"], default));
        Assert.Contains("Comandi:", output.ToString());
    }
    [Fact]
    public async Task Empty_invocation_shows_short_guide_successfully()
    {
        Assert.Equal(0, await Runner().RunAsync([], default));
        Assert.Contains("Comandi:", output.ToString());
    }
    [Fact]
    public async Task Large_noninteractive_batch_requires_explicit_confirmation()
    {
        Assert.NotEqual(0, await Runner().RunAsync(["scarica", "--dal", "2026-01-01", "--al", "2026-02-10", "--cartella", directory, "--non-interattivo"], default));
        Assert.Contains("Manca un parametro obbligatorio", output.ToString());
    }
    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("3")]
    public async Task All_three_archive_menu_choices_validate_confirm_and_persist(string choice)
    {
        Environment.CurrentDirectory = directory;
        var other = Path.Combine(directory, "altra cartella");
        Directory.CreateDirectory(other);
        var answers = choice switch
        {
            "1" => new[] { "1", "s" },
            "2" => new[] { "2", "s", "s" },
            _ => new[] { "3", $"\"{other}\"", "s" }
        };
        Assert.Equal(0, await Runner(new Input(answers)).RunAsync(["archivia-qui"], default));
        var expected = choice switch { "1" => directory, "2" => Path.Combine(directory, "archivio"), _ => other };
        Assert.Equal(expected, store.Settings.ArchivePath);
        Assert.True(Directory.Exists(expected));
        Assert.Equal(1, messages.Presented);
    }
    [Fact]
    public async Task Refused_archive_confirmation_leaves_default_unchanged()
    {
        Environment.CurrentDirectory = directory;
        Assert.Equal(0, await Runner(new Input(["1", "n"])).RunAsync(["archivia-qui"], default));
        Assert.Null(store.Settings.ArchivePath);
    }
    [Fact]
    public async Task Guided_download_override_does_not_change_default()
    {
        var other = Path.Combine(directory, "temporaneo"); Directory.CreateDirectory(other);
        store.Settings = store.Settings with { ArchivePath = directory };
        Assert.Equal(0, await Runner(new Input(["1", "01/10/2026", "s", other]), withSource: true).RunAsync(["scarica"], default));
        Assert.True(File.Exists(Path.Combine(other, "2026-10-01.xlsx")));
        Assert.Equal(directory, store.Settings.ArchivePath);
        Assert.Equal(1, messages.Presented);
    }
    [Fact]
    public async Task Sorting_command_confirms_number_of_files_and_persists_preference()
    {
        var date = new DateOnly(2026, 10, 1);
        var path = Path.Combine(directory, "2026-10-01.xlsx");
        var source = new OfflineSource();
        var day = await source.FetchAsync(date, default);
        var second = new Draw(date, 2, day.Draws[0].MainNumbers20, 1, 2, day.Draws[0].ExtraNumbers15);
        await new ClosedXmlArchive().WriteAsync(new(date, [day.Draws[0], second], "test", false), path, SortOrder.Desc, default);
        store.Settings = store.Settings with { ArchivePath = directory };
        Assert.Equal(0, await Runner(new Input(["s"])).RunAsync(["ordina-asc"], default));
        Assert.Contains("Riordino 1 file", output.ToString());
        Assert.Equal(SortOrder.Asc, store.Settings.SortOrder);
        using var workbook = new ClosedXML.Excel.XLWorkbook(path);
        Assert.Equal(1, workbook.Worksheet(1).Cell(1, 2).GetValue<int>());
    }
    [Fact]
    public async Task Unelevated_auto_archive_does_not_change_settings_or_create_task()
    {
        var schedule = new Schedule(); var before = store.Settings;
        Assert.Equal(1, await Runner(elevated: false, scheduler: schedule).RunAsync(["auto-archivia"], default));
        Assert.Equal(0, schedule.Created); Assert.Equal(before, store.Settings);
        Assert.Contains("& \"C:\\Strazio\\strazio.exe\" auto-archivia", output.ToString());
    }
    [Fact]
    public async Task Elevated_setup_registers_task_and_immediately_fetches_completed_day()
    {
        var yesterday = new ItalianClock().Today.AddDays(-1);
        var schedule = new Schedule();
        var input = new Input([directory, yesterday.ToString("yyyy-MM-dd"), "10:30", "s"]);
        Assert.Equal(0, await Runner(input, elevated: true, scheduler: schedule, withSource: true).RunAsync(["auto-archivia"], default));
        Assert.Equal(1, schedule.Created);
        Assert.Equal("10:30", store.Settings.DailyLocalTime);
        Assert.Equal(yesterday, store.Settings.SyncStartDate);
        Assert.True(File.Exists(Path.Combine(directory, $"{yesterday:yyyy-MM-dd}.xlsx")));
        Assert.NotNull(store.Settings.LastSuccessfulSyncUtc);
    }
    private sealed class Store : ISettingsRepository
    {
        public AppSettings Settings = new();
        public Task<AppSettings> LoadAsync(CancellationToken ct) => Task.FromResult(Settings);
        public Task SaveAsync(AppSettings settings, CancellationToken ct) { Settings = settings; return Task.CompletedTask; }
    }
    private sealed class Messages : IUserMessages
    {
        public int Presented; public string PickRandom() => "frase di prova";
        public Task PresentAsync(string message, bool toast, CancellationToken ct) { Presented++; return Task.CompletedTask; }
    }
    private sealed class Schedule : IScheduledArchiveService
    {
        public int Created;
        public Task CreateOrUpdateAsync(ScheduleDefinition definition, CancellationToken ct) { Created++; return Task.CompletedTask; }
        public Task<ScheduleStatus> GetStatusAsync(CancellationToken ct) => Task.FromResult(new ScheduleStatus(false, "non configurato"));
    }
    private sealed class Input(IEnumerable<string> answers) : IUserInput
    {
        private readonly Queue<string> queue = new(answers);
        public bool IsAvailable => true;
        public string? ReadLine() => queue.TryDequeue(out var next) ? next : null;
    }
    private sealed class Session(bool elevated) : IWindowsSession
    {
        public bool IsElevated => elevated;
        public string ExecutablePath => @"C:\Strazio\strazio.exe";
        public string UserId => "S-1-5-21-test";
    }
    private sealed class OfflineSource : IExtractionSource
    {
        public string Id => "test";
        public Task<DrawDay> FetchAsync(DateOnly d, CancellationToken ct) => Task.FromResult(new DrawDay(d, [new Draw(d, 1, Enumerable.Range(1, 20), 1, 2, Enumerable.Range(21, 15))], Id, false));
    }
}
