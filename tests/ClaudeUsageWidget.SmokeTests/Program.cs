using ClaudeUsageWidget;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

L10n.Init(UiLanguage.En);

const string expiredRefreshTokenResponse = """
{
  "error": "invalid_grant",
  "error_description": "Refresh token expired"
}
""";
var expiredRefreshTokenError = OAuthTokenRequestException.FromResponse(
    System.Net.HttpStatusCode.BadRequest,
    expiredRefreshTokenResponse);
Require(expiredRefreshTokenError.IsInvalidGrant, "An expired OAuth refresh token should require sign-in again.");
Require(
    !expiredRefreshTokenError.Message.Contains("Refresh token expired") &&
    !expiredRefreshTokenError.Message.Contains("error_description"),
    "OAuth token endpoint response details should not be exposed in exception messages.");
Require(
    L10n.T("err_token_expired").Contains("Connect / sign in again") &&
    L10n.T("err_token_expired").Contains("tray icon"),
    "The expired Claude sign-in message should tell users where to sign in again.");
L10n.Init(UiLanguage.ZhHant);
Require(
    L10n.T("err_token_expired").Contains("連結 / 重新登入") &&
    L10n.T("err_token_expired").Contains("系統匣圖示"),
    "The Traditional Chinese expired Claude sign-in message should include re-login steps.");
L10n.Init(UiLanguage.En);

const string updateZip = "ClaudeUsageWidget-win-x64.zip";
const string expectedUpdateHash = "e68928a80d0cf0ba34c93c249794587ac833617bcdbbe2034bd61db23262e0e9";
Require(
    UpdateService.ParseExpectedHash($"{expectedUpdateHash}  {updateZip}\n", updateZip) == expectedUpdateHash,
    "The release checksum parser should select the expected zip asset.");
var missingChecksumWasRejected = false;
try
{
    UpdateService.ParseExpectedHash($"{expectedUpdateHash}  another-file.zip\n", updateZip);
}
catch (InvalidOperationException) { missingChecksumWasRejected = true; }
Require(missingChecksumWasRejected, "A missing update checksum should be rejected.");

var updaterTestRoot = Path.Combine(
    Path.GetTempPath(),
    "ClaudeUsageWidget.UpdaterTests",
    Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(updaterTestRoot);
try
{
    var downloadedFile = Path.Combine(updaterTestRoot, "download.zip");
    await File.WriteAllTextAsync(downloadedFile, "known test content");
    var hashMismatchWasRejected = false;
    try
    {
        await UpdateService.VerifySha256Async(downloadedFile, new string('0', 64));
    }
    catch (InvalidOperationException) { hashMismatchWasRejected = true; }
    Require(hashMismatchWasRejected, "A downloaded update with the wrong SHA-256 should be rejected.");

    var cancellationWasObserved = false;
    using (var input = new MemoryStream(new byte[128]))
    using (var output = new MemoryStream())
    using (var cancellation = new CancellationTokenSource())
    {
        cancellation.Cancel();
        try
        {
            await UpdateService.CopyDownloadAsync(
                input,
                output,
                input.Length,
                progress: null,
                cancellation.Token);
        }
        catch (OperationCanceledException) { cancellationWasObserved = true; }
    }
    Require(cancellationWasObserved, "Cancelling an update download should stop before writing.");

    var currentExe = Path.Combine(updaterTestRoot, "current.exe");
    File.WriteAllText(currentExe, "original executable");
    var replacementFailureWasObserved = false;
    try
    {
        UpdateService.ReplaceExecutable(
            currentExe,
            Path.Combine(updaterTestRoot, "missing-replacement.exe"));
    }
    catch (IOException) { replacementFailureWasObserved = true; }
    Require(replacementFailureWasObserved, "The replacement failure test should exercise rollback.");
    Require(File.Exists(currentExe), "The original executable should be restored after replacement failure.");
    Require(
        File.ReadAllText(currentExe) == "original executable",
        "Rollback should preserve the original executable contents.");
    Require(!File.Exists(currentExe + ".old"), "Rollback should not strand the original as an .old file.");

    var abandonedUpdate = Path.Combine(
        updaterTestRoot,
        "ClaudeUsageWidget-update-" + Guid.NewGuid().ToString("N"));
    var recentUpdate = Path.Combine(
        updaterTestRoot,
        "ClaudeUsageWidget-update-" + Guid.NewGuid().ToString("N"));
    var unrelatedDirectory = Path.Combine(updaterTestRoot, "unrelated-temp-data");
    Directory.CreateDirectory(abandonedUpdate);
    Directory.CreateDirectory(recentUpdate);
    Directory.CreateDirectory(unrelatedDirectory);
    File.WriteAllText(Path.Combine(abandonedUpdate, "payload.bin"), "old");
    Directory.SetLastWriteTimeUtc(abandonedUpdate, DateTime.UtcNow.AddDays(-10));

    UpdateService.CleanupStaleTemporaryDirectories(
        updaterTestRoot,
        DateTime.UtcNow,
        TimeSpan.FromDays(7));
    Require(!Directory.Exists(abandonedUpdate), "Abandoned updater data should be removed.");
    Require(Directory.Exists(recentUpdate), "Recent updater data should be preserved.");
    Require(Directory.Exists(unrelatedDirectory), "Unrelated temporary data should never be removed.");
    Require(
        !UpdateService.TryDeleteTemporaryDirectory(unrelatedDirectory, updaterTestRoot),
        "The updater cleanup guard should reject unrelated directory names.");
}
finally
{
    if (Directory.Exists(updaterTestRoot)) Directory.Delete(updaterTestRoot, recursive: true);
}

var privateError = new IOException(@"secret-token at C:\Users\Alice\private");
var diagnosticReport = DiagnosticsService.BuildReport(
    new Version(2, 0, 4),
    UsageProviderKind.ChatGpt,
    new[]
    {
        new ProviderDiagnostic(
            UsageProviderKind.Claude,
            DateTimeOffset.Parse("2030-01-01T00:00:00Z"),
            DiagnosticsService.ClassifyError(privateError)),
        new ProviderDiagnostic(UsageProviderKind.ChatGpt, null, "NotChecked"),
    },
    new CodexDiagnostic("Configured", "codex.exe", "1.2.3"));
Require(!diagnosticReport.Contains("secret-token"), "Diagnostics should not include exception messages.");
Require(!diagnosticReport.Contains(@"C:\Users\Alice"), "Diagnostics should not include full user paths.");
Require(!AutoStart.SafeFailureCode(privateError).Contains("secret-token"), "Auto-start failure codes should be redacted.");

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

Console.WriteLine("Smoke tests passed: sanitized OAuth expiry guidance, safe updater failure handling and cleanup, redacted diagnostics, window placement recovery, Claude parser, ChatGPT desktop Codex discovery, Codex JSON-RPC handshake, ChatGPT rate-limit parser.");
