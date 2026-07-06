using System.Windows;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace ClaudeUsageWidget;

public partial class App : System.Windows.Application
{
    readonly UsageService _service = new();
    Settings _settings = null!;
    MainWindow _widget = null!;
    WinForms.NotifyIcon _tray = null!;
    DispatcherTimer _fetchTimer = null!;
    DispatcherTimer _countdownTimer = null!;
    bool _loginWindowOpen;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.Write($"=== 啟動 v{typeof(App).Assembly.GetName().Version} pid={Environment.ProcessId} args=[{string.Join(' ', e.Args)}] path={Environment.ProcessPath}");
        if (AppPaths.ResolutionNote.Length > 0)
            Log.Write($"資料路徑備援: {AppPaths.ResolutionNote} -> {AppPaths.DataDir}");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Write($"UnhandledException (terminating={args.IsTerminating}): {args.ExceptionObject}");
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("DispatcherUnhandledException", args.Exception);
            args.Handled = true; // keep the tray app alive on unexpected UI-thread errors
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        // When launched at logon the user profile/desktop may not be fully ready yet
        // (this previously made the saved token unreadable). Wait a bit before touching disk.
        if (e.Args.Contains("--autostart"))
        {
            Log.Write("開機自動啟動：延遲 15 秒等系統就緒");
            await Task.Delay(TimeSpan.FromSeconds(15));
        }

        try
        {
            StartupCore();
            Log.Write("啟動完成");
        }
        catch (Exception ex)
        {
            Log.Error("啟動失敗", ex);
            throw;
        }
    }

    void StartupCore()
    {
        _settings = Settings.Load();

        if (!_settings.FirstRunDone)
        {
            AutoStart.Enable();
            _settings.FirstRunDone = true;
            _settings.Save();
        }
        else if (AutoStart.IsEnabled())
        {
            // Re-create the shortcut each start: migrates legacy Run-key installs and
            // keeps the target path/arguments current after updates.
            AutoStart.Enable();
        }

        _widget = new MainWindow(_settings);
        _widget.RefreshRequested += () => _ = FetchAndRenderAsync();
        _widget.ReloginRequested += () => PromptLogin(force: true);
        _widget.HideRequested += HideWidget;
        _widget.ExitRequested += ExitApp;

        SetupTray();

        if (_settings.WidgetVisible) _widget.Show();

        _fetchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _fetchTimer.Tick += (_, _) => _ = FetchAndRenderAsync();
        _fetchTimer.Start();

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _countdownTimer.Tick += (_, _) => _widget.RefreshCountdowns();
        _countdownTimer.Start();

        if (_service.HasTokens)
        {
            _ = FetchAndRenderAsync();
        }
        else
        {
            _widget.ShowError("尚未登入，右鍵選「重新登入」或稍候登入視窗。");
            PromptLogin(force: false);
        }
    }

    // ---------- Tray ----------

    void SetupTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Text = "Claude Usage Widget",
            Visible = true,
            Icon = TrayIconRenderer.Render(null),
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == WinForms.MouseButtons.Left) ToggleWidget();
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("顯示 / 隱藏小工具", null, (_, _) => ToggleWidget());
        menu.Items.Add("立即更新", null, (_, _) => _ = FetchAndRenderAsync());
        menu.Items.Add("重新登入", null, (_, _) => PromptLogin(force: true));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("結束", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
    }

    void ToggleWidget()
    {
        if (_widget.IsVisible) HideWidget();
        else
        {
            _settings.WidgetVisible = true;
            _settings.Save();
            _widget.Show();
            _widget.Activate();
        }
    }

    void HideWidget()
    {
        _settings.WidgetVisible = false;
        _settings.Save();
        _widget.Hide();
    }

    void ExitApp()
    {
        Log.Write("使用者選擇結束");
        _tray.Visible = false;
        _tray.Dispose();
        Shutdown();
    }

    // ---------- Data flow ----------

    async Task FetchAndRenderAsync()
    {
        if (!_service.HasTokens) return;
        _widget.ShowLoading("更新中…");
        try
        {
            var buckets = await _service.GetUsageAsync();
            _widget.ShowUsage(buckets);
            UpdateTray(buckets);
        }
        catch (UnauthorizedAccessException ex)
        {
            _widget.ShowError(ex.Message);
            _tray.Text = "Claude Usage — 需要重新登入";
            PromptLogin(force: false);
        }
        catch (Exception ex)
        {
            Log.Error("更新失敗", ex);
            _widget.ShowError($"更新失敗：{ex.Message}");
        }
    }

    void UpdateTray(List<UsageBucket> buckets)
    {
        var session = buckets.FirstOrDefault(b => b.Key is "session" or "five_hour") ?? buckets.FirstOrDefault();

        var old = _tray.Icon;
        _tray.Icon = TrayIconRenderer.Render(session?.Utilization);
        old?.Dispose();

        // NotifyIcon tooltip is limited to 127 chars.
        var tip = string.Join("\n", buckets.Select(b => $"{b.Label}: {Math.Round(b.Utilization)}%"));
        _tray.Text = tip.Length > 127 ? tip[..127] : tip;
    }

    void PromptLogin(bool force)
    {
        if (_loginWindowOpen) return;
        if (!force && _service.HasTokens) return;

        _loginWindowOpen = true;
        try
        {
            var login = new LoginWindow();
            var ok = login.ShowDialog() == true && login.Result is not null;
            if (ok)
            {
                _service.SetTokens(login.Result!);
                _ = FetchAndRenderAsync();
            }
        }
        finally
        {
            _loginWindowOpen = false;
        }
    }
}
