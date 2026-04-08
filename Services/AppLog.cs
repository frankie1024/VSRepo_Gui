using System.Diagnostics;
using System.IO;

namespace VSRepo_Gui.Services;

public static class AppLog
{
    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VSRepo_Gui", "logs");
    private static readonly string LogPath = Path.Combine(LogDirectory, "latest.log");

    public static string CurrentLogPath => LogPath;

    public static void Write(string message)
    {
        lock (SyncRoot)
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
    }

    public static void Write(Exception exception, string context)
    {
        Write($"{context}: {exception}");
    }
}


