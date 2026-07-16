using System.Text.Json;

while (Console.ReadLine() is { } line)
{
    using var document = JsonDocument.Parse(line);
    var root = document.RootElement;
    if (!root.TryGetProperty("id", out var id)) continue;
    var method = root.GetProperty("method").GetString();

    object result = method switch
    {
        "initialize" => new { userAgent = "mock", codexHome = "mock", platformFamily = "windows", platformOs = "windows" },
        "account/read" => new
        {
            account = new { type = "chatgpt", email = "private@example.invalid", planType = "plus" },
            requiresOpenaiAuth = true,
        },
        "account/rateLimits/read" => new
        {
            rateLimits = new
            {
                primary = new { usedPercent = 25.0, windowDurationMins = 300, resetsAt = 1_900_000_000L },
                secondary = new { usedPercent = 40.0, windowDurationMins = 10_080, resetsAt = 1_900_100_000L },
                rateLimitReachedType = (string?)null,
            },
            rateLimitResetCredits = (object?)null,
        },
        _ => new { },
    };

    Console.WriteLine(JsonSerializer.Serialize(new { id = id.GetInt64(), result }));
}
