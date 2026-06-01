using System.Diagnostics;
using System.IO;

namespace VSRepo_Gui.Services;

public static class AppLog
{
    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VSRepo_Gui", "logs");
    private static readonly string LogPath = Path.Combine(LogDirectory, "latest.log");
    private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB

    public static string CurrentLogPath => LogPath;

    public static void Write(string message)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded();
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Must never throw — this is called from global exception handlers.
            // If logging fails (disk full, permissions), silently degrade.
            Debug.WriteLine($"AppLog.Write failed: {message}");
        }
    }

    public static void Write(Exception exception, string context)
    {
        Write($"{context}: {exception}");
    }

    private static void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > MaxLogSizeBytes)
            {
                var bakPath = LogPath + ".bak";
                File.Delete(bakPath);
                File.Move(LogPath, bakPath, overwrite: true);
            }
        }
        catch
        {
            // Best effort — don't let rotation failure block logging.
        }
    }
}


