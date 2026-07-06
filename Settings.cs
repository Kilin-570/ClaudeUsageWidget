using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace ClaudeUsageWidget;

public class Settings
{
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool WidgetVisible { get; set; } = true;
    public bool FirstRunDone { get; set; }

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
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { }
    }
}

public static class AutoStart
{
    // Legacy mechanism (v1 used HKCU Run; on this machine Windows silently ignored the
    // entry at logon, so we switched to a Startup-folder shortcut).
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "ClaudeUsageWidget";

    static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "ClaudeUsageWidget.lnk");

    public static bool IsEnabled() => File.Exists(ShortcutPath) || RunKeyExists();

    public static void Enable()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("WScript.Shell 不可用");
            dynamic shell = Activator.CreateInstance(shellType)!;
            var lnk = shell.CreateShortcut(ShortcutPath);
            lnk.TargetPath = exe;
            lnk.Arguments = "--autostart"; // tells the app to delay init until the profile is ready
            lnk.WorkingDirectory = Path.GetDirectoryName(exe);
            lnk.Description = "Claude Usage Widget";
            lnk.Save();
            Log.Write($"已建立啟動捷徑: {ShortcutPath} -> {exe}");
        }
        catch (Exception ex)
        {
            Log.Error("建立啟動捷徑失敗", ex);
        }
        RemoveRunKey(); // avoid double-launch via the legacy mechanism
    }

    public static void Disable()
    {
        try { File.Delete(ShortcutPath); } catch { }
        RemoveRunKey();
    }

    static bool RunKeyExists()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    static void RemoveRunKey()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
