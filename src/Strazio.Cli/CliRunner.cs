using System.Globalization;
using Spectre.Console;
using Strazio.Application;
using Strazio.Domain;
using Strazio.Infrastructure.Storage;
using Strazio.Infrastructure.Windows;

namespace Strazio.Cli;

public sealed class CliRunner(ISettingsRepository settingsStore, IUserMessages messages,
    ArchiveSyncService sync, IArchiveInspector inspector, IArchiveLock archiveLock,
    IScheduledArchiveService scheduler, IItalianClock clock, IWindowsSession windows,
    IUserInput? userInput = null)
{
    private readonly IUserInput input = userInput ?? new ConsoleUserInput();
    private readonly string startupDirectory = Environment.CurrentDirectory;
    private bool nonInteractive;
    private bool yes;
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var cli = new CommandLine();
        var parsed = cli.Root.Parse(args);
        var command = ReferenceEquals(parsed.CommandResult.Command, cli.Root) ? "strazio" : parsed.CommandResult.Command.Name;
        nonInteractive = args.Contains("--non-interattivo", StringComparer.Ordinal) || command == "__esegui-pianificato";
        yes = parsed.GetValue(cli.Yes);
        try
        {
            var settings = await settingsStore.LoadAsync(ct);
            if (command == "basta-messaggi" && parsed.Errors.Count == 0 && !parsed.GetValue(cli.Guide))
            {
                await settingsStore.SaveAsync(settings with { MessagesEnabled = false }, ct);
                Console.WriteLine("Va bene, da adesso niente più frasi. Se cambi idea, scrivi: strazio.exe vabbe");
                return 0;
            }
            if (command == "vabbe" && parsed.Errors.Count == 0 && !parsed.GetValue(cli.Guide))
            {
                settings = settings with { MessagesEnabled = true };
                await settingsStore.SaveAsync(settings, ct);
                if (!nonInteractive) Console.WriteLine("E daje, rieccoci!");
            }
            // The disabling command is silent even for a malformed invocation or help.
            if (!nonInteractive && settings.MessagesEnabled && command != "basta-messaggi")
                await messages.PresentAsync(messages.PickRandom(), true, ct);
            if (parsed.Errors.Count > 0)
            {
                Console.Error.WriteLine("Comando o opzioni non validi. Controlla il nome, i valori e le virgolette dei percorsi.\nScrivi: strazio.exe --guida");
                return 2;
            }
            if (parsed.GetValue(cli.Guide) || args.Length == 0)
            {
                CommandLine.PrintGuide(args.Length == 0 ? null : command);
                return 0;
            }
            switch (command)
            {
                case "archivia-qui": await ChooseDefaultAsync(settings, ct); break;
                case "scarica":
                    return await DownloadAsync(settings, parsed.GetValue(cli.Date), parsed.GetValue(cli.From), parsed.GetValue(cli.To),
                        parsed.GetValue(cli.Folder), parsed.GetValue(cli.Force), ct);
                case "auto-archivia": return await ConfigureScheduleAsync(settings, ct);
                case "__esegui-pianificato": return await ScheduledAsync(settings, ct);
                case "ordina-asc": await ReorderAsync(settings, SortOrder.Asc, ct); break;
                case "ordina-desc": await ReorderAsync(settings, SortOrder.Desc, ct); break;
                case "stato": await ShowStatusAsync(settings, ct); break;
                case "vabbe": break;
                default: CommandLine.PrintGuide(); break;
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operazione interrotta. I file già salvati restano disponibili.");
            return 130;
        }
        catch (Exception ex)
        {
            var message = ex switch
            {
                UnauthorizedAccessException => "Non riesco ad accedere alla cartella o al servizio Windows. Controlla i permessi.",
                IOException => "Operazione sui file non riuscita. Controlla il percorso, lo spazio libero e i permessi. Se un file è aperto in Excel, chiudilo e riprova.",
                ArgumentException or InvalidOperationException => ex.Message,
                _ => $"Non riesco a completare l'operazione ({ex.GetType().Name}). Controlla cartella, connessione e configurazione."
            };
            Console.Error.WriteLine(message);
            return 1;
        }
    }

    private string Ask(string prompt, string? defaultValue = null)
    {
        if (nonInteractive || !input.IsAvailable) throw new ArgumentException($"Manca un parametro obbligatorio: {prompt}. Esegui il comando in un terminale interattivo o specifica tutte le opzioni.");
        Console.Write(prompt + (defaultValue is null ? " " : $" [{defaultValue}] "));
        var value = input.ReadLine() ?? throw new OperationCanceledException("Input terminato.");
        return string.IsNullOrWhiteSpace(value) ? defaultValue ?? "" : value.Trim();
    }

    private bool Confirm(string prompt, bool allowYes = true)
    {
        if (allowYes && yes) { Console.WriteLine(prompt + " Sì (--si)"); return true; }
        var value = Ask(prompt + " [S/n]");
        return value.Length == 0 || value.Equals("s", StringComparison.OrdinalIgnoreCase) || value.Equals("si", StringComparison.OrdinalIgnoreCase) || value.Equals("sì", StringComparison.OrdinalIgnoreCase);
    }

    private string ValidateDirectory(string raw)
    {
        var path = ArchiveDirectory.Normalize(raw);
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            if (!Confirm("Non trovo questa cartella. Controlla che il nome sia giusto. Vuoi che la crei io?"))
            {
                Console.WriteLine("Apri Esplora file (il simbolo della cartella gialla), vai nel punto desiderato, premi il tasto destro, scegli Nuovo > Cartella, scrivi il nome e premi Invio. Poi torna qui.");
                Ask("Quando hai creato la cartella, premi Invio per riprovare:");
            }
            else Directory.CreateDirectory(path);
        }
        ArchiveDirectory.Validate(path);
        return path;
    }

    private async Task ChooseDefaultAsync(AppSettings settings, CancellationToken ct)
    {
        Console.WriteLine($"Dove vuoi conservare i file Excel?\n  1. Usa questa cartella: {startupDirectory}\n" +
            "  2. Crea una cartella chiamata \"archivio\" qui e usa quella\n  3. Scegli un'altra cartella");
        var choice = Ask("Scelta [1/2/3]:");
        var raw = choice switch
        {
            "1" => startupDirectory,
            "2" => Path.Combine(startupDirectory, "archivio"),
            "3" => Ask("Scrivi il percorso completo della cartella:"),
            _ => throw new ArgumentException("Scegli 1, 2 oppure 3.")
        };
        var directory = ValidateDirectory(raw);
        if (!Confirm($"Il percorso definitivo è {directory}. Confermi?")) return;
        await settingsStore.SaveAsync(settings with { ArchivePath = directory }, ct);
        Console.WriteLine($"Perfetto! D'ora in poi conserverò i file qui: {directory}");
    }

    private async Task<int> DownloadAsync(AppSettings settings, string? dateValue, string? fromValue,
        string? toValue, string? folder, bool force, CancellationToken ct)
    {
        if (dateValue is not null && (fromValue is not null || toValue is not null))
            throw new ArgumentException("Usa --data oppure --dal e --al, senza combinarli.");
        if (dateValue is null && fromValue is null && toValue is null)
        {
            Console.WriteLine("Che cosa vuoi scaricare?\n  1. Un solo giorno\n  2. Più giorni di seguito");
            var choice = Ask("Scelta [1/2]:");
            if (choice == "1") dateValue = Ask("Inserisci il giorno (per esempio 01/10/2026):");
            else if (choice == "2")
            {
                fromValue = Ask("Inserisci il primo giorno:");
                toValue = Ask("Inserisci l'ultimo giorno:");
            }
            else throw new ArgumentException("Scegli 1 oppure 2.");
        }
        var from = ItalianDate.Parse(dateValue ?? fromValue ?? Ask("Inserisci il primo giorno (--dal):"), clock.Today);
        var to = dateValue is not null ? from : ItalianDate.Parse(toValue ?? Ask("Inserisci l'ultimo giorno (--al):"), clock.Today);
        ValidateDates(from, to);
        PrintDateSummary(from, to);
        if (!nonInteractive && input.IsAvailable && !Confirm("Confermi le date?")) return 0;
        if (to.DayNumber - from.DayNumber + 1 > 31 && !Confirm("Hai scelto più di 31 giorni. Il recupero può richiedere tempo. Confermi?")) return 0;
        if (folder is null && !nonInteractive && input.IsAvailable)
        {
            Console.WriteLine("Dove salvo il file?" + (settings.ArchivePath is null ? " Scegli una cartella per questa esecuzione." : $"\n  [Invio] Usa la cartella predefinita: {settings.ArchivePath}"));
            folder = Ask("Oppure scrivi un'altra cartella:", settings.ArchivePath);
        }
        folder ??= settings.ArchivePath ?? throw new ArgumentException("Manca la cartella: usa --cartella oppure imposta prima archivia-qui.");
        var directory = ValidateDirectory(folder);
        var result = await sync.SyncAsync(new(from, to, directory, settings.SortOrder, force, nonInteractive), Progress(),
            (file, _) => Task.FromResult(Confirm($"Il file {file.Path} non è valido. Vuoi sostituirlo conservando una copia?")), ct);
        // Download overrides never modify the configured archive or scheduled synchronization state.
        PrintResult(result);
        return result.Errors > 0 || result.Stopped ? 1 : 0;
    }

    private void ValidateDates(DateOnly from, DateOnly to)
    {
        if (to < from) throw new ArgumentException("L'ultimo giorno viene prima del primo. Controlla le date.");
        if (to > clock.Today) throw new ArgumentException("Non posso scaricare giornate future: oggi in Italia è " + clock.Today.ToString("dd/MM/yyyy", Italian) + ".");
    }

    private static void PrintDateSummary(DateOnly from, DateOnly to) => Console.WriteLine(from == to
        ? $"Hai scelto {from.ToString("dddd d MMMM yyyy", Italian)}."
        : $"Hai scelto dal {from.ToString("dddd d MMMM yyyy", Italian)} al {to.ToString("dddd d MMMM yyyy", Italian)}.");

    private async Task ReorderAsync(AppSettings settings, SortOrder order, CancellationToken ct)
    {
        IReadOnlyList<ArchiveFile> files = [];
        IDisposable? lease = null;
        try
        {
            if (settings.ArchivePath is not null)
            {
                ArchiveDirectory.Validate(settings.ArchivePath);
                lease = await archiveLock.AcquireAsync(settings.ArchivePath, ct);
                files = ArchiveCatalog.Scan(settings.ArchivePath, inspector);
            }
            var valid = files.Where(f => f.IsValid).ToArray();
            if (valid.Length > 0 && !Confirm($"Riordino {valid.Length} file Excel, conservando una copia di ciascuno. Confermi?")) return;
            await settingsStore.SaveAsync(settings with { SortOrder = order }, ct);
            Console.WriteLine($"Ordinamento {(order == SortOrder.Asc ? "crescente" : "decrescente")} salvato.");
            var failures = 0;
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (!file.IsValid) { failures++; Console.WriteLine($"Non modificato: {file.Path}. {file.Error}"); continue; }
                try { await inspector.ReorderAsync(file.Path, file.Date, order, ct); Console.WriteLine($"Riordinato: {file.Path}"); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { failures++; Console.WriteLine($"Non riesco a riordinare {file.Path}. Controlla i permessi e lo spazio libero; chiudi il file in Excel e riprova."); }
            }
            if (failures > 0) throw new IOException($"{failures} file non modificati; gli altri sono stati riordinati");
        }
        finally { lease?.Dispose(); }
    }

    private async Task<int> ConfigureScheduleAsync(AppSettings settings, CancellationToken ct)
    {
        // Check privilege before any scheduling setting or task mutation.
        var executable = windows.ExecutablePath;
        if (!windows.IsElevated)
        {
            Console.WriteLine("Per preparare il salvataggio automatico serve aprire Strazio\ncome amministratore. Non hai fatto niente di sbagliato.\n\n" +
                "1. Premi il pulsante Start di Windows.\n2. Scrivi \"Terminale\" oppure \"Prompt dei comandi\".\n" +
                "3. Premi il tasto destro sul programma trovato.\n4. Scegli \"Esegui come amministratore\".\n" +
                "5. Quando Windows chiede il permesso, premi \"Sì\".\n6. In PowerShell copia questo comando e premi Invio:\n\n" +
                $"   & \"{executable}\" auto-archivia\n\n" +
                "Nel Prompt dei comandi ometti il simbolo &.\n" +
                "Se non sai dove si trova Strazio, apri la cartella che contiene strazio.exe e copia il suo percorso dalla barra in alto.\n" +
                "Attenzione: account amministratore diversi hanno profili e impostazioni diversi.");
            return 1;
        }
        Console.WriteLine("Stai configurando l'archivio per questo account Windows. Se hai usato le credenziali di un altro amministratore, il suo profilo e le sue impostazioni sono differenti.");
        var directory = ValidateDirectory(Ask("Dove conservo l'archivio? Premi Invio per confermare oppure scrivi un nuovo percorso:", settings.ArchivePath));
        var files = ArchiveCatalog.Scan(directory, inspector);
        var valid = files.Where(f => f.IsValid && f.Date < clock.Today).ToArray();
        foreach (var file in files.Where(f => !f.IsValid)) Console.WriteLine($"File da controllare: {file.Path}. Non verrà sostituito automaticamente.");
        var last = valid.LastOrDefault();
        var proposed = last?.Date.AddDays(1);
        var from = ItalianDate.Parse(Ask(last is null ? "Da quale giorno vuoi iniziare?" : $"Ultimo file valido: {last.Date:dd/MM/yyyy}. Da quale giorno riparto?", proposed?.ToString("dd/MM/yyyy", Italian)), clock.Today);
        if (from > clock.Today) throw new ArgumentException("La prima data non può essere nel futuro.");
        var earliest = files.FirstOrDefault(f => f.Date < clock.Today)?.Date;
        DateOnly? previousStart = string.Equals(settings.ArchivePath, directory, StringComparison.OrdinalIgnoreCase) ? settings.SyncStartDate : null;
        var scanStart = new[] { earliest, previousStart, (DateOnly?)from }.Where(d => d.HasValue).Select(d => d!.Value).Min();
        var completed = files.Where(f => f.IsValid && !f.IsProvisional).Select(f => f.Date).ToHashSet();
        var holes = Enumerable.Range(scanStart.DayNumber, Math.Max(0, from.DayNumber - scanStart.DayNumber))
            .Select(DateOnly.FromDayNumber).Where(d => !completed.Contains(d)).ToArray();
        if (holes.Length > 0)
        {
            Console.WriteLine($"Ho trovato {holes.Length} giornate mancanti o provvisorie prima della partenza.");
            if (Confirm("Vuoi recuperare anche queste giornate?")) from = scanStart;
        }
        var timeText = Ask("A che ora vuoi controllare le nuove estrazioni? (ora locale del computer)", settings.DailyLocalTime);
        if (!TimeOnly.TryParseExact(timeText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new ArgumentException("Scrivi l'orario come HH:mm, per esempio 10:00.");
        Console.WriteLine($"Cartella: {directory}\nPrima data: {from:dd/MM/yyyy}\nFrequenza: ogni giorno alle {time:HH:mm} ora locale e all'accesso dell'utente.\n" +
            "Il task funziona solo quando questo utente ha effettuato l'accesso; al prossimo accesso recupera i giorni mancanti.\n" +
            "Se sposti strazio.exe da questa cartella, il salvataggio automatico non funzionerà finché non lo riconfiguri.");
        if (!Confirm("Confermi la configurazione e il recupero iniziale?")) return 0;
        var schedule = new ScheduleDefinition(WindowsScheduleService.TaskName, executable, time, windows.UserId);
        // Task registration must succeed before storing the new scheduling preferences.
        await scheduler.CreateOrUpdateAsync(schedule, ct);
        var updated = settings with { ArchivePath = directory, SyncStartDate = from, DailyLocalTime = time.ToString("HH:mm", CultureInfo.InvariantCulture) };
        try { await settingsStore.SaveAsync(updated, ct); }
        catch { Console.Error.WriteLine("Il task è stato registrato, ma non ho potuto salvare le impostazioni. Esegui di nuovo auto-archivia prima di affidarti al task."); throw; }
        return await ScheduledAsync(updated, ct);
    }

    private async Task<int> ScheduledAsync(AppSettings settings, CancellationToken ct)
    {
        if (settings.ArchivePath is null || settings.SyncStartDate is null)
            throw new ArgumentException("La pianificazione non è completa. Esegui strazio.exe auto-archivia.");
        ArchiveDirectory.Validate(settings.ArchivePath);
        var to = clock.Today.AddDays(-1);
        if (settings.SyncStartDate > to) { Console.WriteLine("Nessuna giornata conclusa da recuperare."); return 0; }
        var result = await sync.SyncAsync(new(settings.SyncStartDate.Value, to, settings.ArchivePath, settings.SortOrder, NonInteractive: true), Progress(), null, ct);
        if (result.Errors == 0 && !result.Stopped && result.Unavailable == 0)
            await settingsStore.SaveAsync(settings with { LastSuccessfulSyncUtc = clock.UtcNow }, ct);
        PrintResult(result);
        return result.Errors > 0 || result.Stopped ? 1 : 0;
    }

    private async Task ShowStatusAsync(AppSettings settings, CancellationToken ct)
    {
        AnsiConsole.Write(new Text("Configurazione di Strazio\n", new Style(decoration: Decoration.Bold)));
        Console.WriteLine($"Cartella: {settings.ArchivePath ?? "non scelta"}\nOrdinamento: {(settings.SortOrder == SortOrder.Asc ? "crescente" : "decrescente")}\n" +
            $"Frasi e notifiche: {(settings.MessagesEnabled ? "abilitate (toast se disponibili e senza privilegi amministrativi)" : "disabilitate")}\n" +
            $"Orario locale del task: {settings.DailyLocalTime}\nUltima sincronizzazione completata: {settings.LastSuccessfulSyncUtc?.ToString("dd/MM/yyyy HH:mm 'UTC'", Italian) ?? "mai"}");
        if (settings.ArchivePath is not null)
        {
            var files = ArchiveCatalog.Scan(settings.ArchivePath, inspector);
            Console.WriteLine($"Ultimo giorno valido: {files.LastOrDefault(f => f.IsValid)?.Date.ToString("dd/MM/yyyy", Italian) ?? "nessuno"}");
            Console.WriteLine($"File da controllare: {files.Count(f => !f.IsValid)}; provvisori: {files.Count(f => f.IsProvisional)}");
        }
        try { Console.WriteLine("Task: " + (await scheduler.GetStatusAsync(ct)).Description); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Console.WriteLine($"Task: stato non disponibile ({ex.GetType().Name})."); }
    }

    private static IProgress<SyncProgress> Progress() => new ImmediateProgress(p => Console.WriteLine($"{p.Date:dd/MM/yyyy} — {p.Message}"));
    private static void PrintResult(SyncResult result) => Console.WriteLine($"Giorni archiviati: {result.Saved}; già presenti: {result.AlreadyPresent}; non disponibili: {result.Unavailable}; errori: {result.Errors}.");
    private sealed class ImmediateProgress(Action<SyncProgress> report) : IProgress<SyncProgress> { public void Report(SyncProgress value) => report(value); }
}
