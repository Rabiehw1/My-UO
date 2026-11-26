#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ClassicUO.Configuration;

/// <summary>Serializes synchronous operations on a path shared by multiple client processes.</summary>
internal sealed class CrossProcessPathLock : IDisposable
{
    private readonly Mutex _mutex;

    private CrossProcessPathLock(Mutex mutex) => _mutex = mutex;

    /// <summary>Tries to acquire a path lock, which must be released on the acquiring thread.</summary>
    /// <param name="path">Canonical file or directory shared by the callers.</param>
    /// <param name="timeout">How long to wait for an active operation.</param>
    /// <returns>A lease to dispose, or null when the wait expired.</returns>
    internal static CrossProcessPathLock? TryAcquire(string path, TimeSpan timeout)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (OperatingSystem.IsWindows())
            fullPath = fullPath.ToUpperInvariant();

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath));
        var mutex = new Mutex(false, $"{ClientBranding.SHORT_NAME}_Path_{Convert.ToHexString(digest)}");
        try
        {
            if (mutex.WaitOne(timeout))
                return new CrossProcessPathLock(mutex);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner exited. The operating system has transferred ownership to this thread.
            return new CrossProcessPathLock(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }

        mutex.Dispose();
        return null;
    }

    /// <summary>Releases the lock on the acquiring thread.</summary>
    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
