using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaudeUsageWidget;

public class StoredTokens
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>Persists OAuth tokens encrypted with DPAPI (current user) under %APPDATA%.</summary>
public static class TokenStore
{
    static string Dir => AppPaths.DataDir;
    static string FilePath => Path.Combine(Dir, "tokens.dat");

    public static StoredTokens? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var encrypted = File.ReadAllBytes(FilePath);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<StoredTokens>(Encoding.UTF8.GetString(plain));
        }
        catch
        {
            return null;
        }
    }

    public static void Save(StoredTokens tokens)
    {
        Directory.CreateDirectory(Dir);
        var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tokens));
        var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, encrypted);
    }

    public static void Delete()
    {
        try { File.Delete(FilePath); } catch { }
    }
}
