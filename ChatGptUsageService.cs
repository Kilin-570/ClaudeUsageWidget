using System.Diagnostics;
using System.Text.Json;

namespace ClaudeUsageWidget;

public sealed class ChatGptSignInRequiredException(string message) : UnauthorizedAccessException(message);

/// <summary>
/// Reads ChatGPT/Codex quota windows through OpenAI's official Codex app-server.
/// Credentials remain entirely owned by Codex and are never exposed to this process.
/// </summary>
public sealed class ChatGptUsageService(Func<string?> configuredCodexPath) : IDisposable
{
    readonly CodexAppServerClient _client = new(configuredCodexPath);

    public async Task<string?> GetAccountTypeAsync(CancellationToken cancellationToken = default)
    {
        var result = await _client.RequestAsync(
            "account/read",
            new { refreshToken = false },
            cancellationToken);
        if (!result.TryGetProperty("account", out var account) ||
            account.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return account.TryGetProperty("type", out var type) ? type.GetString() : null;
    }

    public async Task<List<UsageBucket>> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        var accountType = await GetAccountTypeAsync(cancellationToken);
        if (accountType is not ("chatgpt" or "personalAccessToken"))
            throw new ChatGptSignInRequiredException(L10n.T("err_chatgpt_not_signed_in"));

        var result = await _client.RequestAsync("account/rateLimits/read", cancellationToken: cancellationToken);
        var buckets = ChatGptUsageParser.Parse(result);
        if (buckets.Count == 0)
            throw new InvalidOperationException(L10n.T("err_chatgpt_no_limits"));
        return buckets;
    }

    public async Task LoginViaBrowserAsync(CancellationToken cancellationToken = default)
    {
        var result = await _client.RequestAsync(
            "account/login/start",
            new { type = "chatgpt" },
            cancellationToken);
        var loginId = result.TryGetProperty("loginId", out var id) ? id.GetString() : null;
        var authUrl = result.TryGetProperty("authUrl", out var url) ? url.GetString() : null;
        if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrWhiteSpace(authUrl))
            throw new InvalidOperationException(L10n.T("err_chatgpt_login_start"));

        Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
                var accountType = await GetAccountTypeAsync(timeout.Token);
                if (accountType is "chatgpt" or "personalAccessToken") return;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _client.RequestAsync(
                    "account/login/cancel",
                    new { loginId },
                    CancellationToken.None);
            }
            catch { }
            throw new TimeoutException(L10n.T("err_chatgpt_login_timeout"));
        }
    }

    public void Dispose() => _client.Dispose();
}

public static class ChatGptUsageParser
{
    public static List<UsageBucket> Parse(JsonElement result)
    {
        var buckets = new List<UsageBucket>();
        if (result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("rateLimits", out var limits) ||
            limits.ValueKind != JsonValueKind.Object)
            return buckets;

        AddWindow(buckets, limits, "primary", 0);
        AddWindow(buckets, limits, "secondary", 1);
        return buckets;
    }

    static void AddWindow(List<UsageBucket> buckets, JsonElement limits, string property, int rank)
    {
        if (!limits.TryGetProperty(property, out var window) || window.ValueKind != JsonValueKind.Object)
            return;
        if (!window.TryGetProperty("usedPercent", out var percent) || percent.ValueKind != JsonValueKind.Number)
            return;

        var duration = window.TryGetProperty("windowDurationMins", out var durationValue) &&
                       durationValue.TryGetInt32(out var minutes)
            ? minutes
            : 0;
        DateTimeOffset? resetsAt = null;
        if (window.TryGetProperty("resetsAt", out var resetValue) && resetValue.TryGetInt64(out var unixSeconds))
        {
            try { resetsAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds); }
            catch (ArgumentOutOfRangeException) { }
        }

        buckets.Add(new UsageBucket(
            $"chatgpt_{rank}_{duration}",
            FormatWindow(duration, rank),
            percent.GetDouble(),
            resetsAt));
    }

    static string FormatWindow(int minutes, int rank) => minutes switch
    {
        300 => L10n.T("chatgpt_limit_5h"),
        10080 => L10n.T("chatgpt_limit_weekly"),
        43200 or 43800 or 44640 => L10n.T("chatgpt_limit_monthly"),
        >= 1440 when minutes % 1440 == 0 => L10n.F("chatgpt_limit_days", minutes / 1440),
        >= 60 when minutes % 60 == 0 => L10n.F("chatgpt_limit_hours", minutes / 60),
        > 0 => L10n.F("chatgpt_limit_minutes", minutes),
        _ => rank == 0 ? L10n.T("chatgpt_limit_primary") : L10n.T("chatgpt_limit_secondary"),
    };
}
