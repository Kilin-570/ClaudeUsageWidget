using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeUsageWidget;

public sealed record ProviderDiagnostic(
    UsageProviderKind Provider,
    DateTimeOffset? LastSuccess,
    string Status);

public sealed record CodexDiagnostic(
    string Source,
    string ExecutableName,
    string Version);

/// <summary>
/// Builds an intentionally redacted support report. It never receives credentials,
/// usage values, account identifiers, log contents, or full filesystem paths.
/// </summary>
public static class DiagnosticsService
{
    public static string BuildReport(
        Version appVersion,
        UsageProviderKind activeProvider,
        IEnumerable<ProviderDiagnostic> providers,
        CodexDiagnostic codex)
    {
        var report = new StringBuilder();
        report.AppendLine("AI Usage Widget diagnostics (redacted)");
        report.AppendLine($"AppVersion: {NormalizeVersion(appVersion)}");
        report.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        report.AppendLine($"Architecture: {RuntimeInformation.OSArchitecture}");
        report.AppendLine($"ActiveProvider: {activeProvider.DisplayName()}");
        foreach (var item in providers.OrderBy(item => item.Provider))
        {
            var prefix = item.Provider.DisplayName();
            report.AppendLine($"{prefix}.LastSuccess: {FormatTimestamp(item.LastSuccess)}");
            report.AppendLine($"{prefix}.Status: {item.Status}");
        }
        report.AppendLine($"Codex.Source: {codex.Source}");
        report.AppendLine($"Codex.Executable: {codex.ExecutableName}");
        report.AppendLine($"Codex.Version: {codex.Version}");
        report.AppendLine("Privacy: no tokens, account data, usage values, log contents, or full paths included.");
        return report.ToString();
    }

    public static string ClassifyError(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "AuthenticationRequired",
        HttpRequestException => "NetworkUnavailable",
        CodexUnavailableException => "CodexUnavailable",
        OperationCanceledException => "Cancelled",
        _ when exception.Message.Contains("429", StringComparison.OrdinalIgnoreCase) => "RateLimited",
        _ => $"Error:{exception.GetType().Name}",
    };

    public static CodexDiagnostic InspectCodex(string? configuredPath)
    {
        try
        {
            var resolved = CodexLocator.Resolve(configuredPath);
            var source = !string.IsNullOrWhiteSpace(configuredPath)
                ? "Configured"
                : IsChatGptDesktopCodex(resolved) ? "ChatGPTDesktop" : "Automatic";
            var executableName = Path.GetFileName(resolved);
            if (string.IsNullOrWhiteSpace(executableName)) executableName = "unknown";

            var version = "unknown";
            if (Path.IsPathFullyQualified(resolved) && File.Exists(resolved))
            {
                var rawVersion = FileVersionInfo.GetVersionInfo(resolved).FileVersion;
                version = SanitizeVersion(rawVersion);
            }

            return new CodexDiagnostic(source, executableName, version);
        }
        catch (Exception ex)
        {
            return new CodexDiagnostic("Unavailable", "unknown", ClassifyError(ex));
        }
    }

    static bool IsChatGptDesktopCodex(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}OpenAI{Path.DirectorySeparatorChar}Codex{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

    static string SanitizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "unknown";
        var safe = new string(version.Where(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '+' or '_').ToArray());
        return safe.Length is > 0 and <= 80 ? safe : "unknown";
    }

    static string NormalizeVersion(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

    static string FormatTimestamp(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "Never";
}
