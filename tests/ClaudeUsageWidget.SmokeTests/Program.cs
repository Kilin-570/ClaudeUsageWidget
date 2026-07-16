using ClaudeUsageWidget;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

L10n.Init(UiLanguage.En);

const string claudePayload = """
{
  "limits": [
    { "kind": "session", "percent": 12.5, "resets_at": "2030-01-01T00:00:00Z" },
    { "kind": "weekly_all", "percent": 34.0, "resets_at": "2030-01-02T00:00:00Z" },
    { "kind": "weekly_scoped", "percent": 56.0, "scope": { "model": { "display_name": "Opus" } } }
  ]
}
""";
var claude = UsageParser.Parse(claudePayload);
Require(claude.Count == 3, "Claude limits array should produce three rows.");
Require(claude[2].Label == "Weekly (Opus)", "Claude scoped model label wasn't preserved.");

var mockPath = args.FirstOrDefault()
    ?? throw new ArgumentException("Pass the CodexAppServerMock executable path as the first argument.");
using var service = new ChatGptUsageService(() => mockPath);
var chatGpt = await service.GetUsageAsync();
Require(chatGpt.Count == 2, "ChatGPT rate limits should produce two rows.");
Require(chatGpt[0].Label == "5-hour limit", "Primary ChatGPT window label is incorrect.");
Require(chatGpt[0].Utilization == 25.0, "Primary ChatGPT utilization is incorrect.");
Require(chatGpt[1].Label == "Weekly limit", "Secondary ChatGPT window label is incorrect.");
Require(chatGpt[1].Utilization == 40.0, "Secondary ChatGPT utilization is incorrect.");

Console.WriteLine("Smoke tests passed: Claude parser, Codex JSON-RPC handshake, ChatGPT rate-limit parser.");
