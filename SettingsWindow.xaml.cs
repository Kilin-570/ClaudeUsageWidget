using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace ClaudeUsageWidget;

public partial class SettingsWindow : Window
{
    readonly Settings _settings;
    readonly Action _onChanged;
    bool _initializing = true;

    public SettingsWindow(Settings settings, Action onChanged)
    {
        _settings = settings;
        _onChanged = onChanged;
        InitializeComponent();

        LanguageCombo.SelectedIndex = L10n.Lang == UiLanguage.En ? 1 : 0;
        ThemeCombo.SelectedIndex = ThemeManager.IsLight ? 1 : 0;
        IntervalCombo.SelectedIndex = IntervalSteps.Length - 1;
        for (var i = 0; i < IntervalSteps.Length; i++)
        {
            if (_settings.RefreshIntervalSec <= IntervalSteps[i])
            {
                IntervalCombo.SelectedIndex = i;
                break;
            }
        }
        OpacitySlider.Value = _settings.BgTransparency;

        ApplyAppearance();
        _initializing = false;
    }

    void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        _settings.Language = LanguageCombo.SelectedIndex == 1 ? "en" : "zh";
        _settings.Save();
        L10n.Set(LanguageCombo.SelectedIndex == 1 ? UiLanguage.En : UiLanguage.ZhHant);
        ApplyAppearance();
        _onChanged();
    }

    void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        _settings.Theme = ThemeCombo.SelectedIndex == 1 ? "light" : "dark";
        _settings.Save();
        ThemeManager.Set(ThemeCombo.SelectedIndex == 1);
        ApplyAppearance();
        _onChanged();
    }

    static readonly int[] IntervalSteps = { 60, 90, 120, 300 };

    void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || IntervalCombo.SelectedIndex < 0) return;
        _settings.RefreshIntervalSec = IntervalSteps[IntervalCombo.SelectedIndex];
        _settings.Save();
        _onChanged();
    }

    void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValue is null) return; // fires during InitializeComponent
        OpacityValue.Text = $"{(int)OpacitySlider.Value}%";
        if (_initializing) return;
        _settings.BgTransparency = (int)OpacitySlider.Value;
        _settings.Save();
        _onChanged();
    }

    void ApplyAppearance()
    {
        Title = L10n.T("settings_title");
        LanguageLabel.Text = L10n.T("settings_language");
        ThemeLabel.Text = L10n.T("settings_theme");
        ((ComboBoxItem)ThemeCombo.Items[0]).Content = L10n.T("theme_dark");
        ((ComboBoxItem)ThemeCombo.Items[1]).Content = L10n.T("theme_light");
        IntervalLabel.Text = L10n.T("settings_interval");
        ((ComboBoxItem)IntervalCombo.Items[0]).Content = L10n.T("interval_60");
        ((ComboBoxItem)IntervalCombo.Items[1]).Content = L10n.T("interval_90");
        ((ComboBoxItem)IntervalCombo.Items[2]).Content = L10n.T("interval_120");
        ((ComboBoxItem)IntervalCombo.Items[3]).Content = L10n.T("interval_300");
        OpacityLabel.Text = L10n.T("settings_opacity");
        OpacityValue.Text = $"{(int)OpacitySlider.Value}%";
        OpacityHint.Text = L10n.T("settings_opacity_hint");

        var bg = ThemeManager.IsLight ? Color.FromRgb(0xFA, 0xFA, 0xFC) : Color.FromRgb(0x1E, 0x1E, 0x28);
        Background = new SolidColorBrush(bg);
        var fg = ThemeManager.Brush(ThemeManager.TitleText);
        LanguageLabel.Foreground = fg;
        ThemeLabel.Foreground = fg;
        IntervalLabel.Foreground = fg;
        OpacityLabel.Foreground = fg;
        OpacityValue.Foreground = fg;
        OpacityHint.Foreground = ThemeManager.Brush(ThemeManager.SubtleText);
    }
}
