namespace Strazio.Infrastructure.Storage;

public static class AtomicFiles
{
    public static void Commit(string temporary, string destination, bool backup)
    {
        // Both paths are on the destination volume. File.Replace fails safely for locked files.
        if (File.Exists(destination))
        {
            string? backupPath = null;
            if (backup)
            {
                var directory = Path.Combine(Path.GetDirectoryName(destination)!, ".strazio-backups");
                Directory.CreateDirectory(directory);
                backupPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(destination)}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}{Path.GetExtension(destination)}");
            }
            File.Replace(temporary, destination, backupPath);
        }
        else File.Move(temporary, destination, overwrite: false);
    }
}
