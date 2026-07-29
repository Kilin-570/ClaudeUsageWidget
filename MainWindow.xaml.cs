using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;

namespace ClaudeUsageWidget;

public partial class MainWindow : Window
{
    readonly Settings _settings;
    List<UsageBucket> _lastBuckets = new();
    bool _hasError;
    bool _showingUpdateProgress;
    DateTimeOffset? _lastSuccessfulRefresh;
    UsageProviderKind _activeProvider;
    HwndSource? _windowSource;
    int _providerBackdropAnimationGeneration;

    const int WmDisplayChange = 0x007E;
    static readonly Duration ProviderBackdropFadeDuration =
        new(TimeSpan.FromMilliseconds(180));

    public event Action? RefreshRequested;
    public event Action? ReloginRequested;
    public event Action? HideRequested;
    public event Action? ExitRequested;
    public event Action? SettingsRequested;
    public event Action? DiagnosticsRequested;
    public event Action? UpdateCheckRequested;
    public event Action? CancelUpdateRequested;
    public event Action<UsageProviderKind>? ProviderChanged;

    public UsageProviderKind ActiveProvider => _activeProvider;

    public MainWindow(Settings settings)
    {
        _settings = settings;
        _activeProvider = UsageProviderKindExtensions.ParseProvider(_settings.ActiveProvider);
        InitializeComponent();

        if (_settings.WindowLeft is double left && _settings.WindowTop is double top)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            // Default: top-right corner of the primary work area.
            var area = SystemParameters.WorkArea;
            Left = area.Right - 250;
            Top = area.Top + 16;
        }

        ApplyScale(_settings.UiScale);
        ApplyAppearance();
        L10n.Changed += ApplyAppearance;
        ThemeManager.Changed += ApplyAppearance;

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && _rowsDirty) UpdateRows(); // catch up on data fetched while hidden
        };

        Loaded += (_, _) => AutoStartMenuItem.IsChecked = AutoStart.IsEnabled();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(WindowMessageHook);

        // SizeToContent has not finalized ActualWidth/ActualHeight until layout runs.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, EnsureVisibleOnCurrentDisplays);
    }

    IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDisplayChange)
        {
            // Defer until Windows has updated its virtual-screen metrics. This catches
            // virtual displays (such as spacedesk) being disconnected while the app runs.
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, EnsureVisibleOnCurrentDisplays);
        }

        return IntPtr.Zero;
    }

    public bool EnsureVisibleOnCurrentDisplays()
    {
        UpdateLayout();
        var size = CurrentWindowSize();
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var currentBounds = new Rect(Left, Top, size.Width, size.Height);

        if (!WindowPlacement.NeedsRecovery(currentBounds, virtualScreen))
            return false;

        MoveToPrimaryScreen(size, "window was outside the active display area");
        return true;
    }

    public void ResetPositionToPrimaryScreen() =>
        MoveToPrimaryScreen(CurrentWindowSize(), "position reset requested");

    Size CurrentWindowSize()
    {
        var width = double.IsFinite(ActualWidth) && ActualWidth > 0
            ? ActualWidth
            : 250 * _settings.UiScale;
        var height = double.IsFinite(ActualHeight) && ActualHeight > 0
            ? ActualHeight
            : 180 * _settings.UiScale;
        return new Size(width, height);
    }

    void MoveToPrimaryScreen(Size size, string reason)
    {
        var target = WindowPlacement.TopRightOf(SystemParameters.WorkArea, size);
        Left = target.X;
        Top = target.Y;
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.Save();
        Log.Write($"Window moved to primary display ({reason}): left={Left:F1}, top={Top:F1}");
    }

    protected override void OnClosed(EventArgs e)
    {
        _windowSource?.RemoveHook(WindowMessageHook);
        _windowSource = null;
        base.OnClosed(e);
    }

    // ---------- move & edge-resize ----------
    // Dragging an edge/corner rescales the whole widget proportionally (the window
    // auto-fits its content, so "resizing" means changing the UI scale).

    [Flags]
    enum ResizeEdge { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

    const double EdgeSize = 8;
    const double MinScale = 0.7, MaxScale = 2.5;

    bool _resizing;
    ResizeEdge _edge;
    Point _startPointer;          // screen DIPs
    double _startScale, _startWidth, _startHeight, _startRight, _startBottom;

    ResizeEdge HitTestEdge(Point p)
    {
        var edge = ResizeEdge.None;
        if (p.X < EdgeSize) edge |= ResizeEdge.Left;
        else if (p.X > ActualWidth - EdgeSize) edge |= ResizeEdge.Right;
        if (p.Y < EdgeSize) edge |= ResizeEdge.Top;
        else if (p.Y > ActualHeight - EdgeSize) edge |= ResizeEdge.Bottom;
        return edge;
    }

    static Cursor CursorFor(ResizeEdge edge)
    {
        var h = edge & (ResizeEdge.Left | ResizeEdge.Right);
        var v = edge & (ResizeEdge.Top | ResizeEdge.Bottom);
        if (h != 0 && v != 0)
        {
            var nwse = edge.HasFlag(ResizeEdge.Left) == edge.HasFlag(ResizeEdge.Top);
            return nwse ? Cursors.SizeNWSE : Cursors.SizeNESW;
        }
        if (h != 0) return Cursors.SizeWE;
        if (v != 0) return Cursors.SizeNS;
        return Cursors.Arrow;
    }

    Point PointerInScreenDips(MouseEventArgs e)
    {
        var screen = PointToScreen(e.GetPosition(this));
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        return transform?.Transform(screen) ?? screen;
    }

    void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;

        var edge = HitTestEdge(e.GetPosition(this));
        if (edge != ResizeEdge.None)
        {
            _resizing = true;
            _edge = edge;
            _startPointer = PointerInScreenDips(e);
            _startScale = _settings.UiScale;
            _startWidth = ActualWidth;
            _startHeight = ActualHeight;
            _startRight = Left + ActualWidth;
            _startBottom = Top + ActualHeight;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        DragMove();
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.Save();
    }

    void OnMouseMoveWindow(object sender, MouseEventArgs e)
    {
        if (_resizing)
        {
            var p = PointerInScreenDips(e);
            var dx = p.X - _startPointer.X;
            var dy = p.Y - _startPointer.Y;

            double? byWidth = null, byHeight = null;
            if (_edge.HasFlag(ResizeEdge.Right)) byWidth = (_startWidth + dx) / _startWidth;
            if (_edge.HasFlag(ResizeEdge.Left)) byWidth = (_startWidth - dx) / _startWidth;
            if (_edge.HasFlag(ResizeEdge.Bottom)) byHeight = (_startHeight + dy) / _startHeight;
            if (_edge.HasFlag(ResizeEdge.Top)) byHeight = (_startHeight - dy) / _startHeight;

            var factor = byWidth is double w && byHeight is double h ? (w + h) / 2
                : byWidth ?? byHeight ?? 1;
            var scale = Math.Clamp(_startScale * factor, MinScale, MaxScale);

            ApplyScale(scale);
            UpdateLayout(); // realize the new ActualWidth/Height before anchoring

            // Keep the edge opposite to the one being dragged anchored in place.
            if (_edge.HasFlag(ResizeEdge.Left)) Left = _startRight - ActualWidth;
            if (_edge.HasFlag(ResizeEdge.Top)) Top = _startBottom - ActualHeight;
            return;
        }

        Cursor = CursorFor(HitTestEdge(e.GetPosition(this)));
    }

    void OnMouseUpWindow(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false;
        ReleaseMouseCapture();
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.Save();
    }

    void OnMouseLeaveWindow(object sender, MouseEventArgs e)
    {
        if (!_resizing) Cursor = Cursors.Arrow;
    }

    void ApplyScale(double scale)
    {
        _settings.UiScale = Math.Clamp(scale, MinScale, MaxScale);
        RootBorder.LayoutTransform = new ScaleTransform(_settings.UiScale, _settings.UiScale);
    }

    void OnRefreshClick(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();
    void OnHideClick(object sender, RoutedEventArgs e) => HideRequested?.Invoke();
    void OnReloginClick(object sender, RoutedEventArgs e) => ReloginRequested?.Invoke();
    void OnExitClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();
    void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    void OnDiagnosticsClick(object sender, RoutedEventArgs e) => DiagnosticsRequested?.Invoke();
    void OnCheckUpdateClick(object sender, RoutedEventArgs e) => UpdateCheckRequested?.Invoke();
    void OnCancelUpdateClick(object sender, RoutedEventArgs e) => CancelUpdateRequested?.Invoke();
    void OnClaudeProviderClick(object sender, RoutedEventArgs e) => SelectProvider(UsageProviderKind.Claude);
    void OnChatGptProviderClick(object sender, RoutedEventArgs e) => SelectProvider(UsageProviderKind.ChatGpt);

    void SelectProvider(UsageProviderKind provider)
    {
        if (_activeProvider == provider) return;
        _activeProvider = provider;
        _settings.ActiveProvider = provider.StorageKey();
        _settings.Save();
        _lastBuckets = new List<UsageBucket>();
        _lastSuccessfulRefresh = null;
        _hasError = false;
        RowsPanel.Children.Clear();
        _rows.Clear();
        StatusText.Text = L10n.T("updating");
        ApplyProviderButtons();
        ApplyProviderBackdrop(animate: true);
        ApplyCollapsedState();
        ProviderChanged?.Invoke(provider);
    }

    public void SetActiveProvider(UsageProviderKind provider)
    {
        _activeProvider = provider;
        _settings.ActiveProvider = provider.StorageKey();
        ApplyProviderButtons();
        ApplyProviderBackdrop(animate: IsLoaded);
    }

    void OnAutoStartToggle(object sender, RoutedEventArgs e)
    {
        var result = AutoStartMenuItem.IsChecked
            ? AutoStart.TryEnable()
            : AutoStart.TryDisable();
        AutoStartMenuItem.IsChecked = AutoStart.IsEnabled();
        if (!result.Succeeded || result.Detail is not null)
        {
            System.Windows.MessageBox.Show(
                L10n.F("autostart_problem", result.Detail ?? "UnknownFailure"),
                L10n.T("widget_title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public void ShowLoading(string message)
    {
        StatusText.Text = message;
    }

    public void ShowError(string message)
    {
        _hasError = true;
        ErrorText.Text = message;
        StatusText.Text = "";
        ApplyCollapsedState();
    }

    /// <summary>Quiet corner note (e.g. transient rate limit) — keeps showing the last data.</summary>
    public void ShowNotice(string message)
    {
        _hasError = false;
        StatusText.Text = message;
        ApplyCollapsedState();
    }

    public void ShowUpdateProgress(string message, double? percent, bool canCancel)
    {
        _hasError = false;
        _showingUpdateProgress = true;
        UpdateProgressText.Text = message;
        CancelUpdateButton.Content = L10n.T("update_cancel");
        CancelUpdateButton.IsEnabled = canCancel;
        UpdateProgressBar.IsIndeterminate = percent is null;
        if (percent is double value) UpdateProgressBar.Value = Math.Clamp(value, 0, 100);
        ApplyCollapsedState();
    }

    public void HideUpdateProgress()
    {
        _showingUpdateProgress = false;
        UpdateProgressBar.IsIndeterminate = false;
        ApplyCollapsedState();
    }

    public void ShowUsage(List<UsageBucket> buckets, DateTimeOffset? refreshedAt = null)
    {
        _lastBuckets = buckets;
        _lastSuccessfulRefresh = refreshedAt ?? DateTimeOffset.Now;
        _hasError = false;
        StatusText.Text = _lastSuccessfulRefresh.Value.ToLocalTime().ToString("HH:mm");
        if (IsVisible) UpdateRows();
        else _rowsDirty = true; // hidden (tray-only): defer UI work until shown again
        ApplyCollapsedState();
    }

    /// <summary>Keeps the last successful values visible while clearly marking them as stale.</summary>
    public void ShowStaleData(DateTimeOffset lastSuccessfulRefresh)
    {
        _lastSuccessfulRefresh = lastSuccessfulRefresh;
        _hasError = false;
        StatusText.Text = L10n.F(
            "data_stale",
            lastSuccessfulRefresh.ToLocalTime().ToString("HH:mm"));
        ApplyCollapsedState();
    }

    /// <summary>Re-renders countdown text from cached data (called by a UI timer between fetches).</summary>
    public void RefreshCountdowns()
    {
        if (!IsVisible || _hasError || _settings.Collapsed) return;
        foreach (var row in _rows)
            if (row.ResetsAt is DateTimeOffset resetsAt)
                row.Countdown.Text = UsageParser.FormatCountdown(resetsAt);
    }

    void OnToggleCollapse(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // don't start a window drag
        _settings.Collapsed = !_settings.Collapsed;
        _settings.Save();
        ApplyCollapsedState();
    }

    /// <summary>Shows/hides the usage rows and the compact one-line summary.</summary>
    void ApplyCollapsedState()
    {
        var collapsed = _settings.Collapsed;
        ToggleGlyph.Text = collapsed ? "▸" : "▾";
        TitleRow.Margin = new Thickness(0, 0, 0, collapsed ? 0 : 6);
        RowsPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        ProviderPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        CompactText.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Visibility = !collapsed && _hasError ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgressPanel.Visibility = _showingUpdateProgress
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProviderBackdropHost.Visibility = collapsed
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (collapsed) RebuildCompact();
    }

    /// <summary>Compact summary shown while collapsed: "64% · 28% · 49%" colored per bucket.</summary>
    void RebuildCompact()
    {
        CompactText.Inlines.Clear();
        if (_hasError)
        {
            CompactText.Inlines.Add(new System.Windows.Documents.Run("⚠")
            {
                Foreground = ThemeManager.Brush(ThemeManager.ErrorText),
            });
            return;
        }
        var first = true;
        foreach (var bucket in _lastBuckets)
        {
            if (!first)
                CompactText.Inlines.Add(new System.Windows.Documents.Run(" · ")
                {
                    Foreground = ThemeManager.Brush(ThemeManager.SubtleText),
                });
            CompactText.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(bucket.Utilization)}%")
            {
                Foreground = ThemeManager.Brush(ThemeManager.ColorFor(bucket.Utilization)),
            });
            first = false;
        }
    }

    /// <summary>Applies theme colors, background transparency and UI language.</summary>
    public void ApplyAppearance()
    {
        // Alpha floor of 2 keeps the window hit-testable (fully transparent pixels
        // would become click-through and the widget could no longer be dragged).
        var alpha = (byte)Math.Clamp(
            (int)Math.Round(255 * (100 - _settings.BgTransparency) / 100.0), 2, 255);
        var surface = ThemeManager.IsLight
            ? Color.FromRgb(0xF3, 0xF3, 0xF7)
            : Color.FromRgb(0x1B, 0x1B, 0x24);
        RootBorder.Background = new SolidColorBrush(
            Color.FromArgb(alpha, surface.R, surface.G, surface.B));
        RootBorder.BorderBrush = ThemeManager.IsLight
            ? ThemeManager.Brush(Color.FromRgb(0xB8, 0xB9, 0xC3))
            : ThemeManager.Brush(Color.FromRgb(0x5A, 0x5B, 0x67));
        InnerHighlightBorder.BorderBrush = ThemeManager.IsLight
            ? ThemeManager.Brush(Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF))
            : ThemeManager.Brush(Color.FromArgb(0x32, 0xFF, 0xFF, 0xFF));
        RootShadow.Color = ThemeManager.IsLight
            ? Color.FromRgb(0x42, 0x45, 0x52)
            : Colors.Black;
        RootShadow.Opacity = ThemeManager.IsLight ? 0.24 : 0.44;

        TitleText.Text = L10n.T("widget_title");
        TitleText.Foreground = ThemeManager.Brush(ThemeManager.TitleText);
        StatusText.Foreground = ThemeManager.Brush(ThemeManager.StatusText);
        ErrorText.Foreground = ThemeManager.Brush(ThemeManager.ErrorText);
        ToggleGlyph.Foreground = ThemeManager.Brush(ThemeManager.StatusText);

        RefreshMenuItem.Header = L10n.T("menu_refresh");
        HideMenuItem.Header = L10n.T("menu_hide");
        SettingsMenuItem.Header = L10n.T("menu_settings");
        DiagnosticsMenuItem.Header = L10n.T("menu_copy_diagnostics");
        AutoStartMenuItem.Header = L10n.T("menu_autostart");
        CheckUpdateMenuItem.Header = L10n.T("menu_check_update");
        ReloginMenuItem.Header = L10n.T("menu_relogin");
        ExitMenuItem.Header = L10n.T("menu_exit");
        CancelUpdateButton.Content = L10n.T("update_cancel");

        ClaudeProviderButton.Content = L10n.T("provider_claude");
        ChatGptProviderButton.Content = L10n.T("provider_chatgpt");
        ApplyProviderButtons();
        ApplyProviderBackdrop(animate: false);

        RebuildRows();
        ApplyCollapsedState();
    }

    void ApplyProviderButtons()
    {
        if (ClaudeProviderButton is null || ChatGptProviderButton is null) return;
        var idleBg = ThemeManager.IsLight
            ? ThemeManager.Brush(Color.FromRgb(0xEC, 0xEC, 0xF1))
            : ThemeManager.Brush(Color.FromRgb(0x27, 0x27, 0x32));
        var claudeSelectedBg = ThemeManager.IsLight
            ? ThemeManager.Brush(Color.FromRgb(0xF5, 0xDF, 0xD6))
            : ThemeManager.Brush(Color.FromRgb(0x49, 0x2F, 0x29));
        var chatGptSelectedBg = ThemeManager.IsLight
            ? ThemeManager.Brush(Color.FromRgb(0xD8, 0xEF, 0xEA))
            : ThemeManager.Brush(Color.FromRgb(0x1F, 0x42, 0x3C));

        foreach (var (button, provider) in new[]
                 {
                     (ClaudeProviderButton, UsageProviderKind.Claude),
                     (ChatGptProviderButton, UsageProviderKind.ChatGpt),
                 })
        {
            var selected = provider == _activeProvider;
            button.Background = selected
                ? provider == UsageProviderKind.Claude
                    ? claudeSelectedBg
                    : chatGptSelectedBg
                : idleBg;
            button.Foreground = selected
                ? ThemeManager.Brush(ThemeManager.TitleText)
                : ThemeManager.Brush(ThemeManager.LabelText);
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    void ApplyProviderBackdrop(bool animate)
    {
        if (ClaudeBackdrop is null || ChatGptBackdrop is null) return;

        var claudeAccent = ThemeManager.IsLight
            ? Color.FromRgb(0x9E, 0x42, 0x27)
            : Color.FromRgb(0xF0, 0x8A, 0x68);
        var chatGptAccent = ThemeManager.IsLight
            ? Color.FromRgb(0x0A, 0x70, 0x60)
            : Color.FromRgb(0x48, 0xD6, 0xBE);

        ClaudeWatermark.Foreground = ThemeManager.Brush(claudeAccent);
        ChatGptWatermark.Foreground = ThemeManager.Brush(chatGptAccent);
        ClaudeWatermark.Opacity = ThemeManager.IsLight ? 0.14 : 0.095;
        ChatGptWatermark.Opacity = ThemeManager.IsLight ? 0.14 : 0.095;

        var providerAccent = _activeProvider == UsageProviderKind.Claude
            ? claudeAccent
            : chatGptAccent;
        ProviderAccentBar.Background = ThemeManager.Brush(providerAccent);
        ProviderAccentBar.Opacity = ThemeManager.IsLight ? 0.72 : 0.82;

        var claudeOpacity = _activeProvider == UsageProviderKind.Claude
            ? 1
            : 0;
        var chatGptOpacity = _activeProvider == UsageProviderKind.ChatGpt
            ? 1
            : 0;
        var shouldAnimate = animate
                            && IsVisible
                            && SystemParameters.ClientAreaAnimation;

        var generation = ++_providerBackdropAnimationGeneration;
        SetBackdropOpacity(ClaudeBackdrop, claudeOpacity, shouldAnimate, generation);
        SetBackdropOpacity(ChatGptBackdrop, chatGptOpacity, shouldAnimate, generation);
    }

    void SetBackdropOpacity(
        UIElement backdrop,
        double targetOpacity,
        bool animate,
        int generation)
    {
        var currentOpacity = backdrop.Opacity;
        backdrop.BeginAnimation(OpacityProperty, null);
        if (!animate)
        {
            backdrop.Opacity = targetOpacity;
            return;
        }

        var animation = new DoubleAnimation
        {
            From = currentOpacity,
            To = targetOpacity,
            Duration = ProviderBackdropFadeDuration,
            EasingFunction = new QuadraticEase
            {
                EasingMode = EasingMode.EaseOut,
            },
            FillBehavior = FillBehavior.Stop,
        };
        animation.Completed += (_, _) =>
        {
            if (generation != _providerBackdropAnimationGeneration) return;
            backdrop.BeginAnimation(OpacityProperty, null);
            backdrop.Opacity = targetOpacity;
        };
        backdrop.BeginAnimation(
            OpacityProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    // Rows are created once and updated in place on each refresh — rebuilding the whole
    // visual tree every 30-90s churned the GC and kept the render pipeline busy.
    sealed class UsageRow
    {
        public required string Key;
        public required StackPanel Panel;
        public required TextBlock Label;
        public required TextBlock Pct;
        public required Rectangle Fill;
        public required TextBlock Countdown;
        public DateTimeOffset? ResetsAt;
    }

    readonly List<UsageRow> _rows = new();
    bool _rowsDirty;
    const double BarWidth = 195;

    void RebuildRows()
    {
        RowsPanel.Children.Clear();
        _rows.Clear();
        foreach (var bucket in _lastBuckets)
        {
            var row = CreateRow(bucket);
            _rows.Add(row);
            RowsPanel.Children.Add(row.Panel);
            UpdateRow(row, bucket);
        }
    }

    void UpdateRows()
    {
        _rowsDirty = false;
        var structureMatches = _rows.Count == _lastBuckets.Count &&
            _rows.Zip(_lastBuckets).All(pair => pair.First.Key == pair.Second.Key);
        if (!structureMatches)
        {
            RebuildRows();
            return;
        }
        foreach (var (row, bucket) in _rows.Zip(_lastBuckets))
            UpdateRow(row, bucket);
    }

    static void UpdateRow(UsageRow row, UsageBucket bucket)
    {
        var brush = ThemeManager.Brush(ThemeManager.ColorFor(bucket.Utilization));
        row.ResetsAt = bucket.ResetsAt;
        row.Label.Text = bucket.Label;
        row.Pct.Text = $"{Math.Round(bucket.Utilization)}%";
        row.Pct.Foreground = brush;
        row.Fill.Fill = brush;
        row.Fill.Width = Math.Max(Math.Clamp(bucket.Utilization, 0, 100) / 100.0 * BarWidth, 2);
        if (bucket.ResetsAt is DateTimeOffset resetsAt)
        {
            row.Countdown.Text = UsageParser.FormatCountdown(resetsAt);
            row.Countdown.Visibility = Visibility.Visible;
        }
        else
        {
            row.Countdown.Visibility = Visibility.Collapsed;
        }
    }

    static UsageRow CreateRow(UsageBucket bucket)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };

        var label = new TextBlock
        {
            Foreground = ThemeManager.Brush(ThemeManager.LabelText),
            FontSize = 11,
        };
        var pct = new TextBlock
        {
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var header = new DockPanel();
        DockPanel.SetDock(pct, Dock.Right);
        header.Children.Add(pct);
        header.Children.Add(label);
        panel.Children.Add(header);

        var fill = new Rectangle
        {
            RadiusX = 2.5, RadiusY = 2.5,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var track = new Grid { Height = 5, Width = BarWidth, Margin = new Thickness(0, 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        track.Children.Add(new Rectangle
        {
            RadiusX = 2.5, RadiusY = 2.5,
            Fill = ThemeManager.Brush(ThemeManager.TrackBg),
        });
        track.Children.Add(fill);
        panel.Children.Add(track);

        var countdown = new TextBlock
        {
            Foreground = ThemeManager.Brush(ThemeManager.SubtleText),
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        panel.Children.Add(countdown);

        return new UsageRow
        {
            Key = bucket.Key,
            Panel = panel,
            Label = label,
            Pct = pct,
            Fill = fill,
            Countdown = countdown,
        };
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Closing the widget just hides it; lifetime is owned by the tray icon.
        e.Cancel = true;
        HideRequested?.Invoke();
    }
}
