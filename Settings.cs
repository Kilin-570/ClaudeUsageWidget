using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace ClaudeUsageWidget;

public class Settings
{
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool WidgetVisible { get; set; } = true;
    public bool FirstRunDone { get; set; }
    public string? Language { get; set; }            // "zh" | "en" | null = follow system
    public string Theme { get; set; } = "dark";      // "dark" | "light"
    public int BgTransparency { get; set; } = 10;    // 0 = solid, 100 = fully transparent
    public int RefreshIntervalSec { get; set; } = 90;
    public bool Collapsed { get; set; }
    public double UiScale { get; set; } = 1.0;   // 0.7–2.5, drag widget edges to change
    public string ActiveProvider { get; set; } = "claude";
    public string? CodexExecutablePath { get; set; }
    [JsonIgnore]
    public bool DoNotPersist { get; set; }

    static string Dir => AppPaths.DataDir;
    static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        if (DoNotPersist) return;
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { }
    }
}

public sealed record AutoStartResult(bool Succeeded, string? Detail = null);

public static class AutoStart
{
    // Legacy mechanism (v1 used HKCU Run; on this machine Windows silently ignored the
    // entry at logon, so we switched to a Startup-folder shortcut).
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "ClaudeUsageWidget";

    static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "ClaudeUsageWidget.lnk");

    public static bool IsEnabled()
    {
        if (File.Exists(ShortcutPath)) return true;
        try
        {
            return RunKeyExists();
        }
        catch (Exception ex)
        {
            Log.Error("Could not inspect the auto-start registry entry", ex);
            return false;
        }
    }

    public static AutoStartResult TryEnable()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return new AutoStartResult(false, "ProcessPathUnavailable");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath)!);
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("WScript.Shell is unavailable");
            dynamic shell = Activator.CreateInstance(shellType)!;
            var lnk = shell.CreateShortcut(ShortcutPath);
            lnk.TargetPath = exe;
            lnk.Arguments = "--autostart";
            lnk.WorkingDirectory = Path.GetDirectoryName(exe);
            lnk.Description = "Claude + ChatGPT Usage Widget";
            lnk.Save();
            Log.Write($"Created auto-start shortcut: {ShortcutPath} -> {exe}");
        }
        catch (Exception ex)
        {
            Log.Error("Could not create the auto-start shortcut", ex);
            return new AutoStartResult(false, SafeFailureCode(ex));
        }

        // A policy-blocked registry cleanup is reported but does not invalidate
        // the shortcut that was successfully created.
        return TryRemoveRunKey(out var warning)
            ? new AutoStartResult(true)
            : new AutoStartResult(true, warning);
    }

    public static AutoStartResult TryDisable()
    {
        string? shortcutFailure = null;
        try
        {
            File.Delete(ShortcutPath);
        }
        catch (Exception ex)
        {
            Log.Error("Could not remove the auto-start shortcut", ex);
            shortcutFailure = SafeFailureCode(ex);
        }

        var registryRemoved = TryRemoveRunKey(out var registryFailure);
        var succeeded = shortcutFailure is null && registryRemoved;
        var detail = string.Join(
            ",",
            new[] { shortcutFailure, registryFailure }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return new AutoStartResult(succeeded, detail.Length == 0 ? null : detail);
    }

    static bool RunKeyExists()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    static bool TryRemoveRunKey(out string? failure)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            failure = null;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not remove the legacy auto-start registry entry", ex);
            failure = SafeFailureCode(ex);
            return false;
        }
    }

    internal static string SafeFailureCode(Exception ex) =>
        $"{ex.GetType().Name}:0x{ex.HResult:X8}";
}
