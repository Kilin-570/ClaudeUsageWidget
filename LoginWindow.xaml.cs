using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace ClaudeUsageWidget;

public partial class LoginWindow : Window
{
    readonly AnthropicOAuth _oauth = new();
    CancellationTokenSource? _listenCts;

    /// <summary>Set when login succeeded.</summary>
    public StoredTokens? Result { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();

        Title = L10n.T("login_title");
        HeadingText.Text = L10n.T("login_heading");
        BodyText.Text = L10n.T("login_body");
        BrowserLoginButton.Content = L10n.T("login_browser_btn");
        ManualExpander.Header = L10n.T("login_manual_expander");
        ManualStepsText.Text = L10n.T("login_manual_steps");
        ManualOpenButton.Content = L10n.T("login_manual_open");
        ManualSubmitButton.Content = L10n.T("login_manual_submit");

        Closed += (_, _) => _listenCts?.Cancel();
    }

    async void OnBrowserLoginClick(object sender, RoutedEventArgs e)
    {
        BrowserLoginButton.IsEnabled = false;
        SetStatus(L10n.T("login_waiting"), ok: true);
        _listenCts?.Cancel();
        _listenCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            Result = await _oauth.LoginViaBrowserAsync(_listenCts.Token);
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            if (IsLoaded)
                SetStatus(L10n.T("login_timeout"), ok: false);
        }
        catch (Exception ex)
        {
            SetStatus(L10n.F("login_failed_with_hint", ex.Message), ok: false);
        }
        finally
        {
            if (IsLoaded) BrowserLoginButton.IsEnabled = true;
        }
    }

    void OnManualOpenClick(object sender, RoutedEventArgs e)
    {
        // Cancel the localhost listener — manual mode takes over with the same PKCE pair.
        _listenCts?.Cancel();
        try
        {
            _oauth.OpenManualLoginPage();
            SetStatus(L10n.T("login_manual_opened"), ok: true);
        }
        catch (Exception ex)
        {
            SetStatus(L10n.F("login_open_failed", ex.Message), ok: false);
        }
    }

    async void OnManualSubmitClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CodeBox.Text))
        {
            SetStatus(L10n.T("login_paste_first"), ok: false);
            return;
        }
        ManualSubmitButton.IsEnabled = false;
        try
        {
            Result = await _oauth.LoginViaPastedCodeAsync(CodeBox.Text);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            SetStatus(L10n.F("login_failed", ex.Message), ok: false);
        }
        finally
        {
            if (IsLoaded) ManualSubmitButton.IsEnabled = true;
        }
    }

    void SetStatus(string text, bool ok)
    {
        StatusText.Text = text;
        StatusText.Foreground = new SolidColorBrush(ok
            ? Color.FromRgb(0x8F, 0xD0, 0x8F)
            : Color.FromRgb(0xFF, 0x9E, 0x80));
        StatusText.Visibility = Visibility.Visible;
    }
}
