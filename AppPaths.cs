using System.IO;

namespace ClaudeUsageWidget;

/// <summary>
/// Resolves the app data directory once, defensively. During early logon
/// Environment.GetFolderPath can fail or return "", which silently broke token
/// loading and logging — so we fall back through env vars to the exe directory.
/// </summary>
public static class AppPaths
{
    public static string ResolutionNote { get; private set; } = "";

    public static string DataDir { get; } = Resolve();

    static string Resolve()
    {
        string? baseDir = null;
        try
        {
            baseDir = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);
        }
        catch { }

        if (string.IsNullOrEmpty(baseDir))
        {
            baseDir = Environment.GetEnvironmentVariable("APPDATA");
            ResolutionNote = "GetFolderPath 失敗，改用 %APPDATA%";
        }
        if (string.IsNullOrEmpty(baseDir))
        {
            var profile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(profile))
            {
                baseDir = Path.Combine(profile, "AppData", "Roaming");
                ResolutionNote = "GetFolderPath 與 %APPDATA% 皆失敗，改用 %USERPROFILE%";
            }
        }
        if (string.IsNullOrEmpty(baseDir))
        {
            baseDir = AppContext.BaseDirectory;
            ResolutionNote = "所有使用者路徑皆失敗，改用執行檔目錄";
        }

        return Path.Combine(baseDir, "ClaudeUsageWidget");
    }
}
