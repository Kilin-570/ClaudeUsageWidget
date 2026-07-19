using ClaudeUsageWidget;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

L10n.Init(UiLanguage.En);

var primaryScreen = new System.Windows.Rect(0, 0, 1920, 1080);
var strandedOnDisconnectedDisplay = new System.Windows.Rect(2200, 100, 320, 240);
Require(
    WindowPlacement.NeedsRecovery(strandedOnDisconnectedDisplay, primaryScreen),
    "A window stranded on a disconnected display should be recovered.");

var stillReachable = new System.Windows.Rect(1870, 100, 320, 240);
Require(
    !WindowPlacement.NeedsRecovery(stillReachable, primaryScreen),
    "A window with a reachable strip should not be moved unexpectedly.");

var dualScreenLayout = new System.Windows.Rect(-1280, 0, 3200, 1080);
var visibleOnLeftDisplay = new System.Windows.Rect(-1100, 100, 320, 240);
Require(
    !WindowPlacement.NeedsRecovery(visibleOnLeftDisplay, dualScreenLayout),
    "A window on an active left-side display should remain in place.");

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

var fakeLocalAppData = Path.Combine(Path.GetTempPath(), "ClaudeUsageWidget.SmokeTests", Guid.NewGuid().ToString("N"));
try
{
    var oldDirectory = Path.Combine(fakeLocalAppData, "OpenAI", "Codex", "bin", "old-version");
    var currentDirectory = Path.Combine(fakeLocalAppData, "OpenAI", "Codex", "bin", "current-version");
    Directory.CreateDirectory(oldDirectory);
    Directory.CreateDirectory(currentDirectory);
    var oldCodex = Path.Combine(oldDirectory, "codex.exe");
    var currentCodex = Path.Combine(currentDirectory, "codex.exe");
    File.WriteAllText(oldCodex, "old");
    File.WriteAllText(currentCodex, "current");
    File.SetLastWriteTimeUtc(oldCodex, DateTime.UtcNow.AddMinutes(-5));
    File.SetLastWriteTimeUtc(currentCodex, DateTime.UtcNow);

    var discovered = CodexLocator.FindChatGptDesktopCodex(fakeLocalAppData);
    Require(
        string.Equals(discovered, currentCodex, StringComparison.OrdinalIgnoreCase),
        "ChatGPT desktop Codex discovery should select the newest installed candidate.");
}
finally
{
    if (Directory.Exists(fakeLocalAppData)) Directory.Delete(fakeLocalAppData, recursive: true);
}

if (args.Length == 1 && string.Equals(args[0], "--live", StringComparison.OrdinalIgnoreCase))
{
    using var liveService = new ChatGptUsageService(() => null);
    var accountType = await liveService.GetAccountTypeAsync();
    if (accountType is null)
    {
        Console.WriteLine("Live ChatGPT desktop Codex probe passed (app-server started; separate Codex CLI sign-in required).");
        return;
    }

    var liveUsage = await liveService.GetUsageAsync();
    Require(liveUsage.Count > 0, "The live Codex app-server returned no displayable quota windows.");
    Console.WriteLine($"Live ChatGPT desktop Codex probe passed ({liveUsage.Count} quota windows; values withheld).");
    return;
}

var mockPath = args.FirstOrDefault()
    ?? throw new ArgumentException("Pass the CodexAppServerMock executable path as the first argument.");
using var service = new ChatGptUsageService(() => mockPath);
var chatGpt = await service.GetUsageAsync();
Require(chatGpt.Count == 2, "ChatGPT rate limits should produce two rows.");
Require(chatGpt[0].Label == "5-hour limit", "Primary ChatGPT window label is incorrect.");
Require(chatGpt[0].Utilization == 25.0, "Primary ChatGPT utilization is incorrect.");
Require(chatGpt[1].Label == "Weekly limit", "Secondary ChatGPT window label is incorrect.");
Require(chatGpt[1].Utilization == 40.0, "Secondary ChatGPT utilization is incorrect.");

Console.WriteLine("Smoke tests passed: window placement recovery, Claude parser, ChatGPT desktop Codex discovery, Codex JSON-RPC handshake, ChatGPT rate-limit parser.");

