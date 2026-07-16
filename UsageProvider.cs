namespace ClaudeUsageWidget;

public enum UsageProviderKind
{
    Claude,
    ChatGpt,
}

public static class UsageProviderKindExtensions
{
    public static string StorageKey(this UsageProviderKind provider) => provider switch
    {
        UsageProviderKind.ChatGpt => "chatgpt",
        _ => "claude",
    };

    public static UsageProviderKind ParseProvider(string? value) =>
        string.Equals(value, "chatgpt", StringComparison.OrdinalIgnoreCase)
            ? UsageProviderKind.ChatGpt
            : UsageProviderKind.Claude;

    public static string DisplayName(this UsageProviderKind provider) => provider switch
    {
        UsageProviderKind.ChatGpt => "ChatGPT",
        _ => "Claude",
    };
}
