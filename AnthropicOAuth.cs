using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaudeUsageWidget;

/// <summary>Thrown on HTTP 429 from the usage endpoint — transient, retry later.</summary>
public class RateLimitedException(TimeSpan? retryAfter) : Exception("usage API 頻率受限 (429)")
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>
/// OAuth PKCE client against Anthropic's Claude-account OAuth (the same flow Claude Code's
/// /login uses), plus the usage endpoint that backs the /usage screen.
/// </summary>
public class AnthropicOAuth
{
    public const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    public const string Scopes = "org:create_api_key user:profile user:inference";
    const string AuthorizeUrl = "https://claude.ai/oauth/authorize";
    const string TokenUrl = "https://console.anthropic.com/v1/oauth/token";
    const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    const string ManualRedirectUri = "https://console.anthropic.com/oauth/code/callback";
    const int CallbackPort = 54545;
    static string LocalRedirectUri => $"http://localhost:{CallbackPort}/callback";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    string _verifier = "";
    string _state = "";

    // ---------- PKCE helpers ----------

    static string RandomUrlSafe(int bytes)
    {
        var buf = RandomNumberGenerator.GetBytes(bytes);
        return Base64Url(buf);
    }

    static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    void NewPkce()
    {
        _verifier = RandomUrlSafe(32);
        _state = RandomUrlSafe(32);
    }

    string Challenge() => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(_verifier)));

    string BuildAuthorizeUrl(string redirectUri) =>
        $"{AuthorizeUrl}?code=true&client_id={ClientId}&response_type=code" +
        $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
        $"&scope={Uri.EscapeDataString(Scopes)}" +
        $"&code_challenge={Challenge()}&code_challenge_method=S256" +
        $"&state={_state}";

    // ---------- Browser login with localhost callback ----------

    /// <summary>
    /// Starts a localhost listener, opens the browser, waits for the redirect and exchanges
    /// the code. Returns tokens on success; throws on failure/cancellation.
    /// </summary>
    public async Task<StoredTokens> LoginViaBrowserAsync(CancellationToken ct)
    {
        NewPkce();
        var listener = new TcpListener(IPAddress.Loopback, CallbackPort);
        listener.Start();
        try
        {
            OpenBrowser(BuildAuthorizeUrl(LocalRedirectUri));
            var (code, state) = await WaitForCallbackAsync(listener, ct);
            if (state != _state) throw new InvalidOperationException("OAuth state 不符，已中止。");
            return await ExchangeCodeAsync(code, _state, LocalRedirectUri);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Opens the manual-copy authorize page (code is displayed for copy-paste).</summary>
    public void OpenManualLoginPage()
    {
        if (string.IsNullOrEmpty(_verifier)) NewPkce();
        OpenBrowser(BuildAuthorizeUrl(ManualRedirectUri));
    }

    /// <summary>Completes login from a manually pasted "code#state" string.</summary>
    public async Task<StoredTokens> LoginViaPastedCodeAsync(string pasted)
    {
        if (string.IsNullOrEmpty(_verifier))
            throw new InvalidOperationException("請先按「開啟登入頁面」再貼上代碼。");
        var parts = pasted.Trim().Split('#');
        var code = parts[0].Trim();
        var state = parts.Length > 1 ? parts[1].Trim() : _state;
        return await ExchangeCodeAsync(code, state, ManualRedirectUri);
    }

    static void OpenBrowser(string url) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

    static async Task<(string code, string state)> WaitForCallbackAsync(TcpListener listener, CancellationToken ct)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            var stream = client.GetStream();
            var buffer = new byte[8192];
            var read = await stream.ReadAsync(buffer, ct);
            var request = Encoding.ASCII.GetString(buffer, 0, read);
            var firstLine = request.Split('\r', '\n')[0]; // e.g. GET /callback?code=..&state=.. HTTP/1.1

            var pathStart = firstLine.IndexOf(' ') + 1;
            var pathEnd = firstLine.LastIndexOf(' ');
            if (pathStart <= 0 || pathEnd <= pathStart) continue;
            var path = firstLine[pathStart..pathEnd];

            var qIndex = path.IndexOf('?');
            var query = qIndex >= 0
                ? System.Web.HttpUtility.ParseQueryString(path[(qIndex + 1)..])
                : System.Web.HttpUtility.ParseQueryString("");
            var code = query["code"];
            var state = query["state"];

            var ok = !string.IsNullOrEmpty(code);
            var html = ok
                ? "<html><body style='font-family:sans-serif;text-align:center;padding-top:80px'><h2>✅ 登入成功</h2><p>可以關閉這個分頁，回到 Claude Usage Widget。</p></body></html>"
                : "<html><body style='font-family:sans-serif;text-align:center;padding-top:80px'><h2>❌ 未收到授權碼</h2><p>請回到工具重試。</p></body></html>";
            var body = Encoding.UTF8.GetBytes(html);
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, ct);
            await stream.WriteAsync(body, ct);

            if (ok) return (code!, state ?? "");
        }
    }

    // ---------- Token endpoints ----------

    async Task<StoredTokens> ExchangeCodeAsync(string code, string state, string redirectUri)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["state"] = state,
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = _verifier,
        });
        return await PostTokenAsync(payload);
    }

    public static async Task<StoredTokens> RefreshAsync(string refreshToken)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId,
        });
        return await PostTokenAsync(payload);
    }

    static async Task<StoredTokens> PostTokenAsync(string jsonPayload)
    {
        using var resp = await Http.PostAsync(TokenUrl,
            new StringContent(jsonPayload, Encoding.UTF8, "application/json"));
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token 請求失敗 ({(int)resp.StatusCode}): {Truncate(body)}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var access = root.GetProperty("access_token").GetString() ?? "";
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() ?? "" : "";
        var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt64() : 3600;
        return new StoredTokens
        {
            AccessToken = access,
            RefreshToken = refresh,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
        };
    }

    // ---------- Usage ----------

    public static async Task<List<UsageBucket>> FetchUsageAsync(string accessToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
        req.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
        using var resp = await Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException($"usage API 授權失敗 ({(int)resp.StatusCode})");
        if ((int)resp.StatusCode == 429)
            throw new RateLimitedException(resp.Headers.RetryAfter?.Delta
                ?? (resp.Headers.RetryAfter?.Date is DateTimeOffset d ? d - DateTimeOffset.UtcNow : null));
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"usage API 失敗 ({(int)resp.StatusCode}): {Truncate(body)}");
        return UsageParser.Parse(body);
    }

    static string Truncate(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
