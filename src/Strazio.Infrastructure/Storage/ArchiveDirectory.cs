namespace Strazio.Infrastructure.Storage;

public static class ArchiveDirectory
{
    public static string Normalize(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Scrivi un percorso assoluto, per esempio D:\\ArchivioLotto.");
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) throw new ArgumentException("Il percorso contiene caratteri non validi.");
        return Path.GetFullPath(path);
    }

    public static void Validate(string directory)
    {
        if (File.Exists(directory)) throw new IOException("Il percorso indica un file, non una cartella.");
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Non trovo questa cartella.");
        _ = Directory.EnumerateFileSystemEntries(directory).Take(1).ToArray();
        var root = Path.GetPathRoot(directory)!;
        if (!root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.AvailableFreeSpace < 10 * 1024 * 1024)
                throw new IOException("Nella cartella scelta serve almeno 10 MB di spazio libero.");
        }
        var probe = Path.Combine(directory, $".strazio-permessi-{Guid.NewGuid():N}.tmp");
        using var file = new FileStream(probe, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
        file.WriteByte(1);
        file.Flush(true);
        file.Position = 0;
        if (file.ReadByte() != 1) throw new IOException("Non riesco a leggere e scrivere nella cartella scelta.");
    }
}
