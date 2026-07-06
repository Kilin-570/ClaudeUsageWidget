using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace ClaudeUsageWidget;

public partial class MainWindow : Window
{
    readonly Settings _settings;
    List<UsageBucket> _lastBuckets = new();

    public event Action? RefreshRequested;
    public event Action? ReloginRequested;
    public event Action? HideRequested;
    public event Action? ExitRequested;

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

        Loaded += (_, _) => AutoStartMenuItem.IsChecked = AutoStart.IsEnabled();
    }

    void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        DragMove();
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.Save();
    }

    void OnRefreshClick(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();
    void OnHideClick(object sender, RoutedEventArgs e) => HideRequested?.Invoke();
    void OnReloginClick(object sender, RoutedEventArgs e) => ReloginRequested?.Invoke();
    void OnExitClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();

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
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        StatusText.Text = "";
    }

    public void ShowUsage(List<UsageBucket> buckets)
    {
        _lastBuckets = buckets;
        ErrorText.Visibility = Visibility.Collapsed;
        StatusText.Text = DateTime.Now.ToString("HH:mm");
        RebuildRows();
    }

    /// <summary>Re-renders countdown text from cached data (called by a UI timer between fetches).</summary>
    public void RefreshCountdowns()
    {
        if (_lastBuckets.Count > 0 && ErrorText.Visibility == Visibility.Collapsed)
            RebuildRows();
    }

    void RebuildRows()
    {
        RowsPanel.Children.Clear();
        foreach (var bucket in _lastBuckets)
            RowsPanel.Children.Add(BuildRow(bucket));
    }

    static UIElement BuildRow(UsageBucket bucket)
    {
        var color = ColorFor(bucket.Utilization);
        var panel = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };

        var header = new DockPanel();
        header.Children.Add(new TextBlock
        {
            Text = bucket.Label,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xC2)),
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
            Fill = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x3E)),
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
                Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x82)),
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        return panel;
    }

    public static Color ColorFor(double utilization) => utilization switch
    {
        >= 90 => Color.FromRgb(0xF4, 0x51, 0x4E), // red
        >= 70 => Color.FromRgb(0xF5, 0xA9, 0x3B), // orange
        _ => Color.FromRgb(0x4C, 0x9F, 0xF0),     // blue
    };

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Closing the widget just hides it; lifetime is owned by the tray icon.
        e.Cancel = true;
        HideRequested?.Invoke();
    }
}
