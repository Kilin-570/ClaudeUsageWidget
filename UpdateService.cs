using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace ClaudeUsageWidget;

public record UpdateInfo(Version Latest, string ZipUrl, string HtmlUrl);

/// <summary>Checks GitHub releases for a newer version and self-updates by swapping the exe.</summary>
public static class UpdateService
{
    const string Owner = "Kilin-570";
    const string Repo = "ClaudeUsageWidget";
    const string AssetName = "ClaudeUsageWidget-win-x64.zip";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ClaudeUsageWidget-updater");
        return c;
    }

    public static Version Current
    {
        get
        {
            var v = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    /// <summary>Returns info about a newer release, or null when already up to date.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        var json = await Http.GetStringAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
        latest = new Version(latest.Major, latest.Minor, Math.Max(latest.Build, 0));
        if (latest <= Current) return null;

        string? zipUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var n) && n.GetString() == AssetName &&
                    asset.TryGetProperty("browser_download_url", out var u))
                {
                    zipUrl = u.GetString();
                    break;
                }
            }
        }
        if (zipUrl is null) return null; // release without our asset — nothing to install

        var html = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        return new UpdateInfo(latest, zipUrl, html);
    }

    /// <summary>
    /// Downloads the release zip, swaps the running exe (rename-away trick) and starts
    /// the new version. The caller must exit the current process afterwards.
    /// </summary>
    public static async Task DownloadAndApplyAsync(UpdateInfo info)
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("cannot determine exe path");

        var tmpDir = Path.Combine(Path.GetTempPath(), "ClaudeUsageWidget-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var zipPath = Path.Combine(tmpDir, AssetName);

        using (var resp = await Http.GetAsync(info.ZipUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(zipPath);
            await resp.Content.CopyToAsync(fs);
        }

        var extractDir = Path.Combine(tmpDir, "extracted");
        ZipFile.ExtractToDirectory(zipPath, extractDir);
        var newExe = Directory.GetFiles(extractDir, "*.exe", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException("no exe found inside the update package");

        // A running exe cannot be overwritten, but it CAN be renamed on the same volume.
        var oldPath = exe + ".old";
        if (File.Exists(oldPath)) File.Delete(oldPath);
        File.Move(exe, oldPath);
        try
        {
            File.Move(newExe, exe);
        }
        catch
        {
            File.Move(oldPath, exe); // roll back so the app still starts next time
            throw;
        }

        Log.Write($"已更新 v{Current} -> v{info.Latest}，重新啟動");
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
    }

    /// <summary>Removes the leftover renamed binary from a previous update.</summary>
    public static void CleanupOldBinary()
    {
        try
        {
            var old = (Environment.ProcessPath ?? "") + ".old";
            if (File.Exists(old)) File.Delete(old);
        }
        catch { /* still locked by the exiting old process — next launch will get it */ }
    }
}
