using System.Windows;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
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

        L10n.Init(_settings.Language switch
        {
            "en" => UiLanguage.En,
            "zh" => UiLanguage.ZhHant,
            _ => System.Globalization.CultureInfo.CurrentUICulture.Name
                     .StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                 ? UiLanguage.ZhHant
                 : UiLanguage.En,
        });
        ThemeManager.Init(_settings.Theme == "light");

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
        _widget.SettingsRequested += ShowSettings;
        _widget.UpdateCheckRequested += () => _ = CheckForUpdatesAsync(interactive: true);

        UpdateService.CleanupOldBinary();

        SetupTray();

        if (_settings.WidgetVisible) _widget.Show();

        _backoffSec = BaseIntervalSec;
        _fetchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(BaseIntervalSec) };
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
            _widget.ShowError(L10n.T("err_not_signed_in_hint"));
            PromptLogin(force: false);
        }

        _ = AutoCheckUpdatesAsync();
    }

    // ---------- self-update ----------

    UpdateInfo? _pendingUpdate;
    bool _updating;

    async Task AutoCheckUpdatesAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(30)); // don't compete with startup work
        try
        {
            _pendingUpdate = await UpdateService.CheckAsync();
            if (_pendingUpdate is not null)
            {
                Log.Write($"發現新版本 v{_pendingUpdate.Latest}");
                _tray.BalloonTipClicked -= OnUpdateBalloonClicked;
                _tray.BalloonTipClicked += OnUpdateBalloonClicked;
                _tray.ShowBalloonTip(8000, "Claude Usage Widget",
                    L10n.F("update_balloon", _pendingUpdate.Latest), WinForms.ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            Log.Error("自動檢查更新失敗", ex); // silent — manual check still available
        }
    }

    void OnUpdateBalloonClicked(object? sender, EventArgs e) => _ = CheckForUpdatesAsync(interactive: true);

    async Task CheckForUpdatesAsync(bool interactive)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            var info = _pendingUpdate ?? await UpdateService.CheckAsync();
            if (info is null)
            {
                if (interactive)
                    MessageBox.Show(L10n.F("update_none", UpdateService.Current),
                        L10n.T("update_title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var answer = MessageBox.Show(
                L10n.F("update_found", info.Latest, UpdateService.Current),
                L10n.T("update_title"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            _widget.ShowNotice(L10n.T("update_downloading"));
            await UpdateService.DownloadAndApplyAsync(info);
            ExitApp(); // the new version has been started
        }
        catch (Exception ex)
        {
            Log.Error("更新失敗", ex);
            if (interactive)
                MessageBox.Show(L10n.F("update_failed", ex.Message),
                    L10n.T("update_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _updating = false;
        }
    }

    // ---------- Tray ----------

    WinForms.ToolStripMenuItem _trayShowHide = null!;
    WinForms.ToolStripMenuItem _trayRefresh = null!;
    WinForms.ToolStripMenuItem _traySettings = null!;
    WinForms.ToolStripMenuItem _trayUpdate = null!;
    WinForms.ToolStripMenuItem _trayRelogin = null!;
    WinForms.ToolStripMenuItem _trayExit = null!;

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
        menu.Items.Add(_trayShowHide = new WinForms.ToolStripMenuItem("", null, (_, _) => ToggleWidget()));
        menu.Items.Add(_trayRefresh = new WinForms.ToolStripMenuItem("", null, (_, _) => _ = FetchAndRenderAsync()));
        menu.Items.Add(_traySettings = new WinForms.ToolStripMenuItem("", null, (_, _) => ShowSettings()));
        menu.Items.Add(_trayUpdate = new WinForms.ToolStripMenuItem("", null, (_, _) => _ = CheckForUpdatesAsync(interactive: true)));
        menu.Items.Add(_trayRelogin = new WinForms.ToolStripMenuItem("", null, (_, _) => PromptLogin(force: true)));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_trayExit = new WinForms.ToolStripMenuItem("", null, (_, _) => ExitApp()));
        _tray.ContextMenuStrip = menu;

        ApplyTrayLanguage();
        L10n.Changed += ApplyTrayLanguage;
    }

    void ApplyTrayLanguage()
    {
        _trayShowHide.Text = L10n.T("menu_showhide");
        _trayRefresh.Text = L10n.T("menu_refresh");
        _traySettings.Text = L10n.T("menu_settings");
        _trayUpdate.Text = L10n.T("menu_check_update");
        _trayRelogin.Text = L10n.T("menu_relogin");
        _trayExit.Text = L10n.T("menu_exit");
    }

    SettingsWindow? _settingsWindow;

    void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, () =>
        {
            _widget.ApplyAppearance();
            ApplyRefreshInterval();
        });
        _settingsWindow.Show();
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

    int BaseIntervalSec => Math.Max(30, _settings.RefreshIntervalSec);
    int _backoffSec;

    /// <summary>Called when the user changes the refresh interval in settings.</summary>
    void ApplyRefreshInterval()
    {
        _backoffSec = BaseIntervalSec;
        _fetchTimer.Interval = TimeSpan.FromSeconds(BaseIntervalSec);
    }

    async Task FetchAndRenderAsync()
    {
        if (!_service.HasTokens) return;
        _widget.ShowLoading(L10n.T("updating"));
        try
        {
            var buckets = await _service.GetUsageAsync();
            _widget.ShowUsage(buckets);
            UpdateTray(buckets);
            if (_backoffSec != BaseIntervalSec)
            {
                _backoffSec = BaseIntervalSec;
                _fetchTimer.Interval = TimeSpan.FromSeconds(BaseIntervalSec);
            }
        }
        catch (RateLimitedException ex)
        {
            // Transient 429: back off quietly and keep showing the last data.
            _backoffSec = Math.Min(600, _backoffSec * 2);
            var waitSec = ex.RetryAfter?.TotalSeconds is double ra && ra > 0
                ? Math.Clamp(ra, BaseIntervalSec, 600)
                : _backoffSec;
            _fetchTimer.Interval = TimeSpan.FromSeconds(waitSec);
            Log.Write($"usage API 限流 (429)，{waitSec:F0} 秒後重試");
            _widget.ShowNotice(L10n.F("retry_at", DateTime.Now.AddSeconds(waitSec).ToString("HH:mm")));
        }
        catch (UnauthorizedAccessException ex)
        {
            _widget.ShowError(ex.Message);
            _tray.Text = L10n.T("tray_need_login");
            PromptLogin(force: false);
        }
        catch (Exception ex)
        {
            Log.Error("更新失敗", ex);
            _widget.ShowError(L10n.F("err_update_prefix", ex.Message));
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
