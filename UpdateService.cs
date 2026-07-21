using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace ClaudeUsageWidget;

public record UpdateInfo(Version Latest, string ZipUrl, string ChecksumUrl, string HtmlUrl);

public enum UpdateStage
{
    Downloading,
    Verifying,
    Extracting,
    Applying,
    Restarting,
}

/// <summary>Updater state for the UI. TotalBytes is null when GitHub does not send a Content-Length.</summary>
public record UpdateProgress(UpdateStage Stage, long CompletedBytes = 0, long? TotalBytes = null);

/// <summary>Checks GitHub releases for a newer version and self-updates by swapping the exe.</summary>
public static class UpdateService
{
    const string Owner = "Kilin-570";
    const string Repo = "ClaudeUsageWidget";
    const string AssetName = "ClaudeUsageWidget-win-x64.zip";
    const string ChecksumAssetName = "SHA256SUMS.txt";
    const int BufferSize = 128 * 1024;

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
        string? checksumUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var n) && n.GetString() == AssetName &&
                    asset.TryGetProperty("browser_download_url", out var u))
                {
                    zipUrl = u.GetString();
                }
                if (asset.TryGetProperty("name", out var checksumName) && checksumName.GetString() == ChecksumAssetName &&
                    asset.TryGetProperty("browser_download_url", out var checksumDownload))
                {
                    checksumUrl = checksumDownload.GetString();
                }
            }
        }
        if (zipUrl is null || checksumUrl is null) return null; // only offer release assets we can verify

        var html = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        return new UpdateInfo(latest, zipUrl, checksumUrl, html);
    }

    /// <summary>
    /// Downloads the release zip, swaps the running exe (rename-away trick) and starts
    /// the new version. The caller must exit the current process afterwards.
    /// </summary>
    public static async Task DownloadAndApplyAsync(
        UpdateInfo info,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("cannot determine exe path");

        var tmpDir = Path.Combine(Path.GetTempPath(), "ClaudeUsageWidget-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var zipPath = Path.Combine(tmpDir, AssetName);

        await DownloadFileAsync(info.ZipUrl, zipPath, progress, cancellationToken);

        // Cancellation is intentionally limited to the download. Once verification starts,
        // stopping halfway through a file swap would be worse than finishing safely.
        progress?.Report(new UpdateProgress(UpdateStage.Verifying));
        var expectedHash = ParseExpectedHash(await Http.GetStringAsync(info.ChecksumUrl), AssetName);
        var actualHash = await ComputeSha256Async(zipPath, progress);
        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("downloaded update failed SHA-256 verification");

        var extractDir = Path.Combine(tmpDir, "extracted");
        progress?.Report(new UpdateProgress(UpdateStage.Extracting));
        ZipFile.ExtractToDirectory(zipPath, extractDir);
        var executables = Directory.GetFiles(extractDir, "ClaudeUsageWidget.exe", SearchOption.AllDirectories);
        if (executables.Length != 1)
            throw new InvalidOperationException("update package did not contain exactly one ClaudeUsageWidget.exe");
        var newExe = executables[0];

        // A running exe cannot be overwritten, but it CAN be renamed on the same volume.
        progress?.Report(new UpdateProgress(UpdateStage.Applying));
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
        progress?.Report(new UpdateProgress(UpdateStage.Restarting));
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
    }

    static async Task DownloadFileAsync(
        string url,
        string destination,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength;
        progress?.Report(new UpdateProgress(UpdateStage.Downloading, 0, total));

        await using var input = await resp.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destination);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var completed = 0L;
        var throttle = Stopwatch.StartNew();
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                completed += read;
                if (throttle.ElapsedMilliseconds >= 120)
                {
                    progress?.Report(new UpdateProgress(UpdateStage.Downloading, completed, total));
                    throttle.Restart();
                }
            }
            progress?.Report(new UpdateProgress(UpdateStage.Downloading, completed, total));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    static async Task<string> ComputeSha256Async(string path, IProgress<UpdateProgress>? progress)
    {
        var total = new FileInfo(path).Length;
        await using var input = File.OpenRead(path);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var completed = 0L;
        var throttle = Stopwatch.StartNew();
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferSize))) > 0)
            {
                sha.AppendData(buffer, 0, read);
                completed += read;
                if (throttle.ElapsedMilliseconds >= 120)
                {
                    progress?.Report(new UpdateProgress(UpdateStage.Verifying, completed, total));
                    throttle.Restart();
                }
            }
            progress?.Report(new UpdateProgress(UpdateStage.Verifying, total, total));
            return Convert.ToHexString(sha.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal static string ParseExpectedHash(string checksumFile, string assetName)
    {
        foreach (var rawLine in checksumFile.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = rawLine.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2 || !string.Equals(fields[^1].TrimStart('*'), assetName, StringComparison.Ordinal))
                continue;

            var hash = fields[0];
            if (hash.Length == 64 && hash.All(Uri.IsHexDigit)) return hash;
            throw new InvalidOperationException("release checksum has an invalid SHA-256 value");
        }

        throw new InvalidOperationException("release checksum did not contain the update asset");
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
