using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace ClaudeUsageWidget;

public partial class MainWindow : Window
{
    readonly Settings _settings;
    List<UsageBucket> _lastBuckets = new();
    bool _hasError;

    public event Action? RefreshRequested;
    public event Action? ReloginRequested;
    public event Action? HideRequested;
    public event Action? ExitRequested;
    public event Action? SettingsRequested;
    public event Action? UpdateCheckRequested;

    public MainWindow(Settings settings)
    {
        _settings = settings;
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

        Loaded += (_, _) => AutoStartMenuItem.IsChecked = AutoStart.IsEnabled();
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
    void OnCheckUpdateClick(object sender, RoutedEventArgs e) => UpdateCheckRequested?.Invoke();

    void OnAutoStartToggle(object sender, RoutedEventArgs e)
    {
        if (AutoStartMenuItem.IsChecked) AutoStart.Enable();
        else AutoStart.Disable();
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

    public void ShowUsage(List<UsageBucket> buckets)
    {
        _lastBuckets = buckets;
        _hasError = false;
        StatusText.Text = DateTime.Now.ToString("HH:mm");
        RebuildRows();
        ApplyCollapsedState();
    }

    /// <summary>Re-renders countdown text from cached data (called by a UI timer between fetches).</summary>
    public void RefreshCountdowns()
    {
        if (_lastBuckets.Count > 0 && !_hasError && !_settings.Collapsed)
            RebuildRows();
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
        CompactText.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Visibility = !collapsed && _hasError ? Visibility.Visible : Visibility.Collapsed;
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
        var bg = ThemeManager.WindowBg;
        // Alpha floor of 2 keeps the window hit-testable (fully transparent pixels
        // would become click-through and the widget could no longer be dragged).
        var alpha = (byte)Math.Clamp(
            (int)Math.Round(255 * (100 - _settings.BgTransparency) / 100.0), 2, 255);
        RootBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, bg.R, bg.G, bg.B));

        TitleText.Text = L10n.T("widget_title");
        TitleText.Foreground = ThemeManager.Brush(ThemeManager.TitleText);
        StatusText.Foreground = ThemeManager.Brush(ThemeManager.StatusText);
        ErrorText.Foreground = ThemeManager.Brush(ThemeManager.ErrorText);
        ToggleGlyph.Foreground = ThemeManager.Brush(ThemeManager.StatusText);

        RefreshMenuItem.Header = L10n.T("menu_refresh");
        HideMenuItem.Header = L10n.T("menu_hide");
        SettingsMenuItem.Header = L10n.T("menu_settings");
        AutoStartMenuItem.Header = L10n.T("menu_autostart");
        CheckUpdateMenuItem.Header = L10n.T("menu_check_update");
        ReloginMenuItem.Header = L10n.T("menu_relogin");
        ExitMenuItem.Header = L10n.T("menu_exit");

        RebuildRows();
        ApplyCollapsedState();
    }

    void RebuildRows()
    {
        RowsPanel.Children.Clear();
        foreach (var bucket in _lastBuckets)
            RowsPanel.Children.Add(BuildRow(bucket));
    }

    static UIElement BuildRow(UsageBucket bucket)
    {
        var color = ThemeManager.ColorFor(bucket.Utilization);
        var panel = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };

        var header = new DockPanel();
        header.Children.Add(new TextBlock
        {
            Text = bucket.Label,
            Foreground = ThemeManager.Brush(ThemeManager.LabelText),
            FontSize = 11,
        });
        var pct = new TextBlock
        {
            Text = $"{Math.Round(bucket.Utilization)}%",
            Foreground = new SolidColorBrush(color),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        DockPanel.SetDock(pct, Dock.Right);
        header.Children.Add(pct);
        panel.Children.Add(header);

        // progress bar
        const double barWidth = 195;
        var track = new Grid { Height = 5, Width = barWidth, Margin = new Thickness(0, 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        track.Children.Add(new Rectangle
        {
            RadiusX = 2.5, RadiusY = 2.5,
            Fill = ThemeManager.Brush(ThemeManager.TrackBg),
        });
        var fillWidth = Math.Clamp(bucket.Utilization, 0, 100) / 100.0 * barWidth;
        track.Children.Add(new Rectangle
        {
            RadiusX = 2.5, RadiusY = 2.5,
            Width = Math.Max(fillWidth, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Fill = new SolidColorBrush(color),
        });
        panel.Children.Add(track);

        if (bucket.ResetsAt is DateTimeOffset resetsAt)
        {
            panel.Children.Add(new TextBlock
            {
                Text = UsageParser.FormatCountdown(resetsAt),
                Foreground = ThemeManager.Brush(ThemeManager.SubtleText),
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        return panel;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Closing the widget just hides it; lifetime is owned by the tray icon.
        e.Cancel = true;
        HideRequested?.Invoke();
    }
}
