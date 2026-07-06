using System.Text.Json;

namespace ClaudeUsageWidget;

/// <summary>One usage bucket returned by the oauth/usage endpoint (e.g. five_hour, seven_day).</summary>
public record UsageBucket(string Key, string Label, double Utilization, DateTimeOffset? ResetsAt);

public static class UsageParser
{
    static readonly Dictionary<string, string> KnownLabels = new()
    {
        ["five_hour"] = "Session",
        ["seven_day"] = "Weekly (all)",
        ["seven_day_opus"] = "Weekly (Opus)",
        ["seven_day_fable"] = "Weekly (Fable)",
        ["seven_day_sonnet"] = "Weekly (Sonnet)",
    };

    static int SortRank(string key) => key switch
    {
        "five_hour" => 0,
        "seven_day" => 1,
        _ => 2,
    };

    /// <summary>
    /// Parses the usage JSON. Primary source is the "limits" array (what the /usage screen
    /// renders: session / weekly_all / weekly_scoped per model). Falls back to the legacy
    /// top-level buckets (five_hour, seven_day, …) if "limits" is absent.
    /// </summary>
    public static List<UsageBucket> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return new List<UsageBucket>();

        if (doc.RootElement.TryGetProperty("limits", out var limits) &&
            limits.ValueKind == JsonValueKind.Array &&
            limits.GetArrayLength() > 0)
        {
            var fromLimits = ParseLimitsArray(limits);
            if (fromLimits.Count > 0) return fromLimits;
        }

        return ParseLegacyBuckets(doc.RootElement);
    }

    static List<UsageBucket> ParseLimitsArray(JsonElement limits)
    {
        var result = new List<UsageBucket>();
        foreach (var item in limits.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("percent", out var pct) || pct.ValueKind != JsonValueKind.Number)
                continue;

            var kind = item.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String
                ? k.GetString() ?? "" : "";

            string? scopeName = null;
            if (item.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.Object)
            {
                if (scope.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object &&
                    model.TryGetProperty("display_name", out var dn) && dn.ValueKind == JsonValueKind.String)
                    scopeName = dn.GetString();
                else if (scope.TryGetProperty("surface", out var surf) && surf.ValueKind == JsonValueKind.String)
                    scopeName = surf.GetString();
            }

            DateTimeOffset? resetsAt = null;
            if (item.TryGetProperty("resets_at", out var resets) &&
                resets.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(resets.GetString(), out var parsed))
                resetsAt = parsed;

            var label = kind switch
            {
                "session" => "Session",
                "weekly_all" => "Weekly (all)",
                "weekly_scoped" => $"Weekly ({scopeName ?? "scoped"})",
                _ => scopeName is null ? Prettify(kind) : $"{Prettify(kind)} ({scopeName})",
            };

            result.Add(new UsageBucket(kind, label, pct.GetDouble(), resetsAt));
        }
        return result; // keep the API's own ordering — it matches the /usage screen
    }

    static List<UsageBucket> ParseLegacyBuckets(JsonElement root)
    {
        var result = new List<UsageBucket>();
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;
            if (!prop.Value.TryGetProperty("utilization", out var util)) continue;
            if (util.ValueKind != JsonValueKind.Number) continue;

            DateTimeOffset? resetsAt = null;
            if (prop.Value.TryGetProperty("resets_at", out var resets) &&
                resets.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(resets.GetString(), out var parsed))
            {
                resetsAt = parsed;
            }

            var label = KnownLabels.TryGetValue(prop.Name, out var l) ? l : Prettify(prop.Name);
            result.Add(new UsageBucket(prop.Name, label, util.GetDouble(), resetsAt));
        }

        return result
            .OrderBy(b => SortRank(b.Key))
            .ThenBy(b => b.Key, StringComparer.Ordinal)
            .ToList();
    }

    static string Prettify(string key)
    {
        var words = key.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    public static string FormatCountdown(DateTimeOffset resetsAt)
    {
        var remaining = resetsAt - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero) return "即將重置";
        if (remaining.TotalHours >= 24)
            return $"{(int)remaining.TotalDays} 天 {remaining.Hours} 時後重置";
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours} 時 {remaining.Minutes} 分後重置";
        return $"{Math.Max(1, remaining.Minutes)} 分後重置";
    }
}
