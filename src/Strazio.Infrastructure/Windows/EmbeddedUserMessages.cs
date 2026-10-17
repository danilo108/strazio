using System.Security.Cryptography;
using Strazio.Application;

namespace Strazio.Infrastructure.Windows;

public sealed class EmbeddedUserMessages : IUserMessages
{
    private readonly string[] phrases;
    public EmbeddedUserMessages()
    {
        using var stream = typeof(EmbeddedUserMessages).Assembly.GetManifestResourceStream("Strazio.Frasi")
            ?? throw new InvalidOperationException("Risorsa delle frasi incorporata non trovata.");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        phrases = reader.ReadToEnd().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (phrases.Length == 0) throw new InvalidOperationException("La risorsa delle frasi è vuota.");
    }
    public string PickRandom() => phrases[RandomNumberGenerator.GetInt32(phrases.Length)];
    public Task PresentAsync(string message, bool showToast, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Console.WriteLine(message);
        Console.WriteLine();
        if (showToast && Environment.UserInteractive && !WindowsIdentityInfo.IsElevated())
        {
            try { NativeToast.Show(message); }
            catch (Exception) { /* Optional UI failure must never interrupt archiving or log the phrase. */ }
        }
        return Task.CompletedTask;
    }
}
