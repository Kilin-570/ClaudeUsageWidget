using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClaudeUsageWidget;

namespace WidgetVisualHarness;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var language = args.Contains(
            "--english",
            StringComparer.OrdinalIgnoreCase)
            ? UiLanguage.En
            : UiLanguage.ZhHant;
        L10n.Init(language);
        var snapshotIndex = Array.FindIndex(
            args,
            value => string.Equals(
                value,
                "--snapshot-dir",
                StringComparison.OrdinalIgnoreCase));
        if (snapshotIndex >= 0)
        {
            if (snapshotIndex + 1 >= args.Length)
                throw new ArgumentException("--snapshot-dir requires an output path.");
            RenderSnapshots(args[snapshotIndex + 1]);
            return;
        }

        var light = args.Contains("--light", StringComparer.OrdinalIgnoreCase);
        var initialProvider = args.Contains(
            "--claude",
            StringComparer.OrdinalIgnoreCase)
            ? UsageProviderKind.Claude
            : UsageProviderKind.ChatGpt;
        ThemeManager.Init(light);
        var settings = new Settings
        {
            ActiveProvider = initialProvider.StorageKey(),
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
        window.Title = $"AI Usage Preview — {(light ? "Light" : "Dark")}";
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
        window.Loaded += (_, _) => window.ShowUsage(Sample(initialProvider));
        window.Show();
        app.Run();
    }

    static void RenderSnapshots(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var app = new System.Windows.Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        foreach (var language in new[] { UiLanguage.ZhHant, UiLanguage.En })
        {
            L10n.Init(language);
            foreach (var light in new[] { false, true })
            {
                foreach (var provider in new[]
                         {
                             UsageProviderKind.Claude,
                             UsageProviderKind.ChatGpt,
                         })
                {
                    ThemeManager.Init(light);
                    var settings = new Settings
                    {
                        ActiveProvider = provider.StorageKey(),
                        WidgetVisible = true,
                        FirstRunDone = true,
                        RefreshIntervalSec = 90,
                        BgTransparency = 10,
                        DoNotPersist = true,
                    };
                    var window = new MainWindow(settings)
                    {
                        ShowInTaskbar = false,
                        Topmost = false,
                    };
                    window.Show();
                    window.ShowUsage(Sample(provider));
                    window.UpdateLayout();

                    var languageName = language == UiLanguage.En ? "en" : "zh-hant";
                    var themeName = light ? "light" : "dark";
                    var providerName = provider == UsageProviderKind.Claude
                        ? "claude"
                        : "chatgpt";
                    SaveSnapshot(
                        window,
                        Path.Combine(
                            outputDirectory,
                            $"{themeName}-{providerName}-{languageName}.png"));
                    window.Close();
                }
            }
        }

        app.Shutdown();
    }

    static void SaveSnapshot(Window window, string outputPath)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = Math.Max(
            1,
            (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(
            1,
            (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(
            width,
            height,
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    static List<UsageBucket> Sample(UsageProviderKind provider) => provider switch
    {
        UsageProviderKind.Claude =>
        [
            new(
                "session",
                "Session",
                17,
                DateTimeOffset.Now.AddHours(3)),
            new(
                "weekly_all",
                "Weekly (all)",
                42,
                DateTimeOffset.Now.AddDays(4)),
        ],
        _ =>
        [
            new(
                "chatgpt_1_10080",
                L10n.T("chatgpt_limit_weekly"),
                35,
                DateTimeOffset.Now.AddDays(7)),
        ],
    };
}
