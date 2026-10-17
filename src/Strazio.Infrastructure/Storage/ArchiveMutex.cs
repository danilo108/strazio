using System.Security.Cryptography;
using System.Text;
using Strazio.Application;

namespace Strazio.Infrastructure.Storage;

public sealed class ArchiveMutex : IArchiveLock
{
    public Task<IDisposable> AcquireAsync(string directory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var canonical = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        var name = @"Global\Strazio.Archive." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var ready = new TaskCompletionSource<IDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new ManualResetEventSlim();
        var finished = new ManualResetEventSlim();
        // Mutex ownership is thread-affine: keep acquisition and release on this dedicated thread.
        var thread = new Thread(() =>
        {
            try
            {
                using var mutex = new Mutex(false, name);
                bool acquired;
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("Un'altra esecuzione di Strazio sta usando questa cartella. Riprova quando ha finito.");
                try
                {
                    // A physical lock also protects aliases (junctions, mapped drives, UNC paths).
                    using var fileLock = new FileStream(Path.Combine(directory, ".strazio.lock"), FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                    if (!ready.TrySetResult(new Lease(release, finished))) return;
                    release.Wait();
                }
                finally { mutex.ReleaseMutex(); }
            }
            catch (Exception ex) { ready.TrySetException(ex); }
            finally { finished.Set(); }
        })
        { IsBackground = true, Name = "Strazio archive mutex" };
        thread.Start();
        return ready.Task;
    }

    private sealed class Lease(ManualResetEventSlim release, ManualResetEventSlim finished) : IDisposable
    {
        public void Dispose() { release.Set(); finished.Wait(); }
    }
}
