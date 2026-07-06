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
        Closed += (_, _) => _listenCts?.Cancel();
    }

    async void OnBrowserLoginClick(object sender, RoutedEventArgs e)
    {
        BrowserLoginButton.IsEnabled = false;
        SetStatus("已開啟瀏覽器，等待你完成授權…", ok: true);
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
                SetStatus("等待逾時或已取消。可以重試，或改用下方的手動模式。", ok: false);
        }
        catch (Exception ex)
        {
            SetStatus($"登入失敗：{ex.Message}\n可以改用下方的手動模式。", ok: false);
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
            SetStatus("已開啟手動登入頁，授權後把顯示的代碼貼到下方。", ok: true);
        }
        catch (Exception ex)
        {
            SetStatus($"開啟失敗：{ex.Message}", ok: false);
        }
    }

    async void OnManualSubmitClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CodeBox.Text))
        {
            SetStatus("請先貼上代碼。", ok: false);
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
            SetStatus($"登入失敗：{ex.Message}", ok: false);
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
