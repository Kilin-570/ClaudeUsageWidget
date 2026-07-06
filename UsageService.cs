namespace ClaudeUsageWidget;

/// <summary>
/// Owns the stored tokens and exposes a single "get current usage" operation that
/// transparently refreshes the access token when needed.
/// </summary>
public class UsageService
{
    StoredTokens? _tokens;
    bool _loaded;
    readonly SemaphoreSlim _gate = new(1, 1);

    // Loaded lazily (not in the constructor) so that when the app is launched at logon,
    // the disk read happens after the startup delay, once the user profile is fully ready.
    StoredTokens? Tokens
    {
        get
        {
            if (!_loaded)
            {
                _tokens = TokenStore.Load();
                _loaded = true;
            }
            return _tokens;
        }
    }

    public bool HasTokens => Tokens is not null;

    public void SetTokens(StoredTokens tokens)
    {
        _tokens = tokens;
        _loaded = true;
        TokenStore.Save(tokens);
    }

    public void ClearTokens()
    {
        _tokens = null;
        _loaded = true;
        TokenStore.Delete();
    }

    /// <summary>Fetches usage; refreshes the token if expired/rejected. Throws
    /// UnauthorizedAccessException when a full re-login is required.</summary>
    public async Task<List<UsageBucket>> GetUsageAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var tokens = Tokens ?? throw new UnauthorizedAccessException(L10n.T("err_not_signed_in"));

            if (tokens.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(2))
                tokens = await RefreshOrThrowAsync(tokens);

            try
            {
                return await AnthropicOAuth.FetchUsageAsync(tokens.AccessToken);
            }
            catch (UnauthorizedAccessException)
            {
                // Access token rejected despite not being expired — try one refresh.
                tokens = await RefreshOrThrowAsync(tokens);
                return await AnthropicOAuth.FetchUsageAsync(tokens.AccessToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task<StoredTokens> RefreshOrThrowAsync(StoredTokens current)
    {
        if (string.IsNullOrEmpty(current.RefreshToken))
        {
            ClearTokens();
            throw new UnauthorizedAccessException(L10n.T("err_token_expired"));
        }
        try
        {
            var fresh = await AnthropicOAuth.RefreshAsync(current.RefreshToken);
            if (string.IsNullOrEmpty(fresh.RefreshToken))
                fresh.RefreshToken = current.RefreshToken;
            SetTokens(fresh);
            return fresh;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            // Network hiccup — not an auth failure; surface as a transient error instead
            // of demanding a re-login.
            throw new InvalidOperationException(L10n.F("err_network", ex.Message), ex);
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException(L10n.F("err_refresh_failed", ex.Message));
        }
    }
}
