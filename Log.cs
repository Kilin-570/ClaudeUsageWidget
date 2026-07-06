using System.IO;

namespace ClaudeUsageWidget;

/// <summary>Minimal file logger for diagnosing startup issues (%APPDATA%\ClaudeUsageWidget\log.txt).</summary>
public static class Log
{
    static readonly object Gate = new();
    static string Dir => AppPaths.DataDir;
    public static string FilePath => Path.Combine(Dir, "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                // keep the log from growing unbounded
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 512 * 1024)
                    File.Delete(FilePath);
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\r\n");
            }
        }
        catch { }
    }

    public static void Error(string context, Exception ex) => Write($"{context}: {ex}");
}
