using System.Runtime.InteropServices;
using System.Security.Principal;
using Strazio.Application;

namespace Strazio.Infrastructure.Windows;

public sealed class WindowsSession : IWindowsSession
{
    public bool IsElevated => WindowsIdentityInfo.IsElevated();
    public string ExecutablePath => Environment.ProcessPath!;
    public string UserId => WindowsIdentityInfo.UserId;
}

public static class WindowsIdentityInfo
{
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
    public static string UserId { get { using var identity = WindowsIdentity.GetCurrent(); return identity.User!.Value; } }
}

public sealed class WindowsScheduleService : IScheduledArchiveService
{
    public const string TaskName = "Strazio - Archivio 10eLotto";
    public Task CreateOrUpdateAsync(ScheduleDefinition schedule, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!WindowsIdentityInfo.IsElevated()) throw new UnauthorizedAccessException("Per configurare il task occorre aprire Strazio come amministratore.");
        if (!Path.IsPathFullyQualified(schedule.ExecutablePath) || !File.Exists(schedule.ExecutablePath))
            throw new ArgumentException("Il percorso dell'eseguibile non è valido.");
        if (!schedule.ExecutablePath.EndsWith("strazio.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Prima pubblica strazio.exe e avvia auto-archivia dall'eseguibile definitivo.");
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        try
        {
            service.Connect();
            dynamic definition = service.NewTask(0);
            definition.RegistrationInfo.Description = "Archivia le giornate concluse del 10eLotto ogni 5 minuti e recupera i giorni mancanti.";
            definition.Principal.UserId = schedule.UserId;
            definition.Principal.LogonType = 3; // Interactive token: S4U has no network access.
            definition.Principal.RunLevel = 0; // Daily downloads never need elevation.
            definition.Settings.Enabled = true;
            definition.Settings.StartWhenAvailable = true;
            definition.Settings.MultipleInstances = 2; // IgnoreNew
            definition.Settings.DisallowStartIfOnBatteries = false;
            definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.RestartCount = 2;
            definition.Settings.RestartInterval = "PT15M";
            definition.Settings.ExecutionTimeLimit = "PT0S"; // Initial long backfills may take hours.
            dynamic daily = definition.Triggers.Create(2);
            daily.DaysInterval = 1;
            daily.StartBoundary = DateTime.Today.Add(schedule.DailyTime.ToTimeSpan()).ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            daily.Enabled = true;
            dynamic logon = definition.Triggers.Create(9);
            logon.UserId = schedule.UserId;
            logon.Delay = "PT1M";
            logon.Enabled = true;
            dynamic action = definition.Actions.Create(0);
            action.Path = schedule.ExecutablePath;
            action.Arguments = "__esegui-pianificato --non-interattivo";
            action.WorkingDirectory = Path.GetDirectoryName(schedule.ExecutablePath);
            dynamic root = service.GetFolder(@"\");
            root.RegisterTaskDefinition(schedule.Name, definition, 6, schedule.UserId, null, 3, null);
        }
        finally { Marshal.FinalReleaseComObject(service); }
        return Task.CompletedTask;
    }

    public Task<ScheduleStatus> GetStatusAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        try
        {
            service.Connect();
            dynamic root = service.GetFolder(@"\");
            try
            {
                dynamic task = root.GetTask(TaskName);
                var state = (int)task.State switch { 0 => "sconosciuto", 1 => "disabilitato", 2 => "in attesa", 3 => "pronto", 4 => "in esecuzione", _ => "sconosciuto" };
                return Task.FromResult(new ScheduleStatus(true, $"{state}; prossima esecuzione: {task.NextRunTime}; ultimo esito: {task.LastTaskResult}"));
            }
            catch (COMException ex) when ((uint)ex.HResult is 0x80070002 or 0x8004130F)
            { return Task.FromResult(new ScheduleStatus(false, "non configurato")); }
            catch (FileNotFoundException)
            { return Task.FromResult(new ScheduleStatus(false, "non configurato")); }
        }
        finally { Marshal.FinalReleaseComObject(service); }
    }
}
