using System.Windows;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
using WinForms = System.Windows.Forms;

namespace ClaudeUsageWidget;

public partial class App : System.Windows.Application
{
    readonly UsageService _claudeService = new();
    readonly Dictionary<UsageProviderKind, List<UsageBucket>> _cache = new();
    readonly SemaphoreSlim _fetchGate = new(1, 1);

    ChatGptUsageService? _chatGptService;
    string? _chatGptServicePath;
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
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

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
            AutoStart.Enable();
        }

        _widget = new MainWindow(_settings);
        _widget.RefreshRequested += () => _ = FetchAndRenderAsync();
        _widget.ReloginRequested += () => _ = ConnectCurrentProviderAsync(force: true);
        _widget.ProviderChanged += provider => _ = ActivateProviderAsync(provider);
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

        if (_widget.ActiveProvider == UsageProviderKind.Claude && !_claudeService.HasTokens)
        {
            _widget.ShowError(L10n.T("err_not_signed_in_hint"));
            _ = ConnectCurrentProviderAsync(force: false);
        }
        else
        {
            _ = FetchAndRenderAsync();
        }

        _ = AutoCheckUpdatesAsync();
    }

    // ---------- self-update ----------

    UpdateInfo? _pendingUpdate;
    bool _updating;

    async Task AutoCheckUpdatesAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(30));
        try
        {
            _pendingUpdate = await UpdateService.CheckAsync();
            if (_pendingUpdate is not null)
            {
                Log.Write($"發現新版本 v{_pendingUpdate.Latest}");
                _tray.BalloonTipClicked -= OnUpdateBalloonClicked;
                _tray.BalloonTipClicked += OnUpdateBalloonClicked;
                _tray.ShowBalloonTip(8000, "AI Usage Widget",
                    L10n.F("update_balloon", _pendingUpdate.Latest), WinForms.ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            Log.Error("自動檢查更新失敗", ex);
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
            ExitApp();
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

    // ---------- tray ----------

    WinForms.ToolStripMenuItem _trayShowHide = null!;
    WinForms.ToolStripMenuItem _trayRefresh = null!;
    WinForms.ToolStripMenuItem _trayProvider = null!;
    WinForms.ToolStripMenuItem _trayClaude = null!;
    WinForms.ToolStripMenuItem _trayChatGpt = null!;
    WinForms.ToolStripMenuItem _traySettings = null!;
    WinForms.ToolStripMenuItem _trayUpdate = null!;
    WinForms.ToolStripMenuItem _trayRelogin = null!;
    WinForms.ToolStripMenuItem _trayExit = null!;

    void SetupTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Text = "AI Usage Widget",
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
        _trayProvider = new WinForms.ToolStripMenuItem();
        _trayProvider.DropDownItems.Add(_trayClaude = new WinForms.ToolStripMenuItem("Claude", null,
            (_, _) => _ = ActivateProviderAsync(UsageProviderKind.Claude)));
        _trayProvider.DropDownItems.Add(_trayChatGpt = new WinForms.ToolStripMenuItem("ChatGPT", null,
            (_, _) => _ = ActivateProviderAsync(UsageProviderKind.ChatGpt)));
        menu.Items.Add(_trayProvider);
        menu.Items.Add(_traySettings = new WinForms.ToolStripMenuItem("", null, (_, _) => ShowSettings()));
        menu.Items.Add(_trayUpdate = new WinForms.ToolStripMenuItem("", null, (_, _) => _ = CheckForUpdatesAsync(interactive: true)));
        menu.Items.Add(_trayRelogin = new WinForms.ToolStripMenuItem("", null, (_, _) => _ = ConnectCurrentProviderAsync(force: true)));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_trayExit = new WinForms.ToolStripMenuItem("", null, (_, _) => ExitApp()));
        _tray.ContextMenuStrip = menu;

        ApplyTrayLanguage();
        UpdateProviderChecks();
        L10n.Changed += ApplyTrayLanguage;
    }

    void ApplyTrayLanguage()
    {
        _trayShowHide.Text = L10n.T("menu_showhide");
        _trayRefresh.Text = L10n.T("menu_refresh");
        _trayProvider.Text = L10n.T("menu_provider");
        _trayClaude.Text = L10n.T("provider_claude");
        _trayChatGpt.Text = L10n.T("provider_chatgpt");
        _traySettings.Text = L10n.T("menu_settings");
        _trayUpdate.Text = L10n.T("menu_check_update");
        _trayRelogin.Text = L10n.T("menu_relogin");
        _trayExit.Text = L10n.T("menu_exit");
    }

    void UpdateProviderChecks()
    {
        if (_trayClaude is null) return;
        _trayClaude.Checked = _widget.ActiveProvider == UsageProviderKind.Claude;
        _trayChatGpt.Checked = _widget.ActiveProvider == UsageProviderKind.ChatGpt;
    }

    SettingsWindow? _settingsWindow;

    void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, ApplySettingsChanges);
        _settingsWindow.Show();
    }

    void ApplySettingsChanges()
    {
        _widget.ApplyAppearance();
        ApplyRefreshInterval();

        if (!string.Equals(_chatGptServicePath, _settings.CodexExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            _chatGptService?.Dispose();
            _chatGptService = null;
            _chatGptServicePath = null;
            _cache.Remove(UsageProviderKind.ChatGpt);
            if (_widget.ActiveProvider == UsageProviderKind.ChatGpt)
                _ = FetchAndRenderAsync();
        }
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
        _chatGptService?.Dispose();
        _chatGptService = null;
        _tray.Visible = false;
        _tray.Dispose();
        Shutdown();
    }

    // ---------- providers and data flow ----------

    int BaseIntervalSec => Math.Max(30, _settings.RefreshIntervalSec);
    int _backoffSec;

    void ApplyRefreshInterval()
    {
        _backoffSec = BaseIntervalSec;
        _fetchTimer.Interval = TimeSpan.FromSeconds(BaseIntervalSec);
    }

    ChatGptUsageService GetChatGptService()
    {
        if (_chatGptService is not null) return _chatGptService;
        _chatGptServicePath = _settings.CodexExecutablePath;
        _chatGptService = new ChatGptUsageService(() => _settings.CodexExecutablePath);
        return _chatGptService;
    }

    async Task ActivateProviderAsync(UsageProviderKind provider)
    {
        _widget.SetActiveProvider(provider);
        _settings.ActiveProvider = provider.StorageKey();
        _settings.Save();
        UpdateProviderChecks();
        _backoffSec = BaseIntervalSec;
        _fetchTimer.Interval = TimeSpan.FromSeconds(BaseIntervalSec);

        if (_cache.TryGetValue(provider, out var cached))
            _widget.ShowUsage(cached);
        else
            _widget.ShowLoading(L10n.T("updating"));
        await FetchAndRenderAsync();
    }

    async Task FetchAndRenderAsync()
    {
        if (!await _fetchGate.WaitAsync(0)) return;
        var provider = _widget.ActiveProvider;
        try
        {
            if (provider == UsageProviderKind.Claude && !_claudeService.HasTokens)
            {
                if (_widget.ActiveProvider == provider)
                    _widget.ShowError(L10n.T("err_not_signed_in_hint"));
                return;
            }

            if (_widget.ActiveProvider == provider) _widget.ShowLoading(L10n.T("updating"));
            var buckets = provider == UsageProviderKind.Claude
                ? await _claudeService.GetUsageAsync()
                : await GetChatGptService().GetUsageAsync();

            _cache[provider] = buckets;
            if (_widget.ActiveProvider == provider)
            {
                _widget.ShowUsage(buckets);
                UpdateTray(provider, buckets);
            }
            if (_backoffSec != BaseIntervalSec)
            {
                _backoffSec = BaseIntervalSec;
                _fetchTimer.Interval = TimeSpan.FromSeconds(BaseIntervalSec);
            }
        }
        catch (RateLimitedException ex)
        {
            _backoffSec = Math.Min(600, _backoffSec * 2);
            var waitSec = ex.RetryAfter?.TotalSeconds is double retryAfter && retryAfter > 0
                ? Math.Clamp(retryAfter, BaseIntervalSec, 600)
                : _backoffSec;
            _fetchTimer.Interval = TimeSpan.FromSeconds(waitSec);
            Log.Write($"usage API 限流 (429)，{waitSec:F0} 秒後重試");
            if (_widget.ActiveProvider == provider)
                _widget.ShowNotice(L10n.F("retry_at", DateTime.Now.AddSeconds(waitSec).ToString("HH:mm")));
        }
        catch (UnauthorizedAccessException ex)
        {
            if (_widget.ActiveProvider == provider) _widget.ShowError(ex.Message);
            _tray.Text = L10n.T("tray_need_login");
        }
        catch (Exception ex)
        {
            Log.Error($"{provider.DisplayName()} 用量更新失敗", ex);
            if (_widget.ActiveProvider == provider)
                _widget.ShowError(L10n.F("err_update_prefix", ex.Message));
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    int? _lastTrayPct;
    UsageProviderKind? _lastTrayProvider;

    void UpdateTray(UsageProviderKind provider, List<UsageBucket> buckets)
    {
        var primary = provider == UsageProviderKind.Claude
            ? buckets.FirstOrDefault(bucket => bucket.Key is "session" or "five_hour") ?? buckets.FirstOrDefault()
            : buckets.FirstOrDefault();
        var pct = primary is null ? (int?)null : (int)Math.Round(primary.Utilization);
        if (pct != _lastTrayPct || provider != _lastTrayProvider)
        {
            _lastTrayPct = pct;
            _lastTrayProvider = provider;
            var old = _tray.Icon;
            _tray.Icon = TrayIconRenderer.Render(primary?.Utilization);
            old?.Dispose();
        }

        var lines = new[] { provider.DisplayName() }
            .Concat(buckets.Select(bucket => $"{bucket.Label}: {Math.Round(bucket.Utilization)}%"));
        var tip = string.Join("\n", lines);
        var trimmed = tip.Length > 127 ? tip[..127] : tip;
        if (_tray.Text != trimmed) _tray.Text = trimmed;
    }

    async Task ConnectCurrentProviderAsync(bool force)
    {
        if (_loginWindowOpen) return;
        var provider = _widget.ActiveProvider;
        if (provider == UsageProviderKind.Claude)
        {
            if (!force && _claudeService.HasTokens) return;
            _loginWindowOpen = true;
            try
            {
                var login = new LoginWindow();
                var ok = login.ShowDialog() == true && login.Result is not null;
                if (ok)
                {
                    _claudeService.SetTokens(login.Result!);
                    _cache.Remove(UsageProviderKind.Claude);
                    await FetchAndRenderAsync();
                }
            }
            finally
            {
                _loginWindowOpen = false;
            }
            return;
        }

        _loginWindowOpen = true;
        try
        {
            var service = GetChatGptService();
            var accountType = await service.GetAccountTypeAsync();
            if (!force && accountType is "chatgpt" or "personalAccessToken")
            {
                await FetchAndRenderAsync();
                return;
            }
            if (accountType == "apiKey")
            {
                var answer = MessageBox.Show(
                    L10n.T("chatgpt_login_replaces_api_key"),
                    L10n.T("provider_chatgpt"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return;
            }

            _widget.ShowNotice(L10n.T("chatgpt_login_waiting"));
            await service.LoginViaBrowserAsync();
            _cache.Remove(UsageProviderKind.ChatGpt);
            await FetchAndRenderAsync();
        }
        catch (Exception ex)
        {
            Log.Error("ChatGPT 登入失敗", ex);
            _widget.ShowError(ex.Message);
        }
        finally
        {
            _loginWindowOpen = false;
        }
    }
}
