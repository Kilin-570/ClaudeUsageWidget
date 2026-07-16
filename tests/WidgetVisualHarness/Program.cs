using System.Windows;
using ClaudeUsageWidget;

namespace WidgetVisualHarness;

static class Program
{
    [STAThread]
    static void Main()
    {
        L10n.Init(UiLanguage.ZhHant);
        ThemeManager.Init(light: false);
        var settings = new Settings
        {
            ActiveProvider = "chatgpt",
            WidgetVisible = true,
            FirstRunDone = true,
            RefreshIntervalSec = 90,
            BgTransparency = 10,
            DoNotPersist = true,
        };

        var app = new System.Windows.Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        var window = new MainWindow(settings);
        // Make the preview targetable by Windows UI automation. Production keeps the
        // widget out of the taskbar; this harness changes only its own window instance.
        window.ShowInTaskbar = true;
        window.ProviderChanged += provider => window.ShowUsage(Sample(provider));
        window.SettingsRequested += () =>
        {
            var settingsWindow = new SettingsWindow(settings, window.ApplyAppearance)
            {
                Owner = window,
            };
            settingsWindow.Show();
        };
        window.ExitRequested += app.Shutdown;
        window.Loaded += (_, _) => window.ShowUsage(Sample(UsageProviderKind.ChatGpt));
        window.Show();
        app.Run();
    }

    static List<UsageBucket> Sample(UsageProviderKind provider) => provider switch
    {
        UsageProviderKind.Claude =>
        [
            new("session", "Session", 17, DateTimeOffset.Now.AddHours(3)),
            new("weekly_all", "Weekly (all)", 42, DateTimeOffset.Now.AddDays(4)),
            new("weekly_scoped", "Weekly (Opus)", 8, DateTimeOffset.Now.AddDays(4)),
        ],
        _ =>
        [
            new("chatgpt_0_300", "5 小時額度", 22, DateTimeOffset.Now.AddHours(2)),
            new("chatgpt_1_10080", "每週額度", 64, DateTimeOffset.Now.AddDays(5)),
        ],
    };
}
