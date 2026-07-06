namespace ClaudeUsageWidget;

public enum UiLanguage { ZhHant = 0, En = 1 }

/// <summary>Tiny string table for the two supported UI languages.</summary>
public static class L10n
{
    public static UiLanguage Lang { get; private set; } = UiLanguage.ZhHant;
    public static event Action? Changed;

    public static void Set(UiLanguage lang)
    {
        if (Lang == lang) return;
        Lang = lang;
        Changed?.Invoke();
    }

    public static void Init(UiLanguage lang) => Lang = lang;

    public static string T(string key) =>
        Table.TryGetValue(key, out var pair) ? (Lang == UiLanguage.En ? pair.en : pair.zh) : key;

    public static string F(string key, params object[] args) => string.Format(T(key), args);

    static readonly Dictionary<string, (string zh, string en)> Table = new()
    {
        // widget
        ["widget_title"] = ("Claude 用量", "Claude Usage"),
        ["updating"] = ("更新中…", "Updating…"),
        ["retry_at"] = ("{0} 重試", "retry {0}"),
        ["reset_soon"] = ("即將重置", "Resets soon"),
        ["reset_days"] = ("{0} 天 {1} 時後重置", "Resets in {0}d {1}h"),
        ["reset_hours"] = ("{0} 時 {1} 分後重置", "Resets in {0}h {1}m"),
        ["reset_mins"] = ("{0} 分後重置", "Resets in {0}m"),
        // menus
        ["menu_refresh"] = ("立即更新", "Refresh now"),
        ["menu_hide"] = ("隱藏小工具", "Hide widget"),
        ["menu_showhide"] = ("顯示 / 隱藏小工具", "Show / hide widget"),
        ["menu_settings"] = ("設定…", "Settings…"),
        ["menu_autostart"] = ("開機自動啟動", "Start with Windows"),
        ["menu_relogin"] = ("重新登入", "Sign in again"),
        ["menu_exit"] = ("結束", "Exit"),
        // errors / status
        ["err_update_prefix"] = ("更新失敗:{0}", "Update failed: {0}"),
        ["err_not_signed_in_hint"] = ("尚未登入,右鍵選「重新登入」。", "Not signed in — right-click and choose \"Sign in again\"."),
        ["tray_need_login"] = ("Claude Usage — 需要重新登入", "Claude Usage — sign-in required"),
        ["err_not_signed_in"] = ("尚未登入", "Not signed in"),
        ["err_token_expired"] = ("Token 已過期,請重新登入", "Session expired — please sign in again"),
        ["err_refresh_failed"] = ("Token 刷新失敗,請重新登入({0})", "Token refresh failed — please sign in again ({0})"),
        ["err_network"] = ("網路暫時無法連線({0})", "Network temporarily unavailable ({0})"),
        // login window
        ["login_title"] = ("登入 Claude 帳號", "Sign in to Claude"),
        ["login_heading"] = ("連結你的 Claude 帳號", "Connect your Claude account"),
        ["login_body"] = (
            "這個工具需要讀取你的 Claude 方案使用量。按下方按鈕會開啟瀏覽器,用你的 Claude 帳號登入並授權(和 Claude Code 的 /login 是同一套流程)。授權後 token 只會加密儲存在這台電腦上。",
            "This tool reads your Claude plan usage. The button below opens your browser to sign in and authorize with your Claude account (the same flow Claude Code's /login uses). Tokens are stored encrypted on this PC only."),
        ["login_browser_btn"] = ("開啟瀏覽器登入", "Sign in with browser"),
        ["login_waiting"] = ("已開啟瀏覽器,等待你完成授權…", "Browser opened — waiting for you to authorize…"),
        ["login_timeout"] = ("等待逾時或已取消。可以重試,或改用下方的手動模式。", "Timed out or cancelled. Try again, or use manual mode below."),
        ["login_failed_with_hint"] = ("登入失敗:{0}\n可以改用下方的手動模式。", "Sign-in failed: {0}\nYou can also try manual mode below."),
        ["login_failed"] = ("登入失敗:{0}", "Sign-in failed: {0}"),
        ["login_manual_expander"] = ("瀏覽器沒有自動完成?改用手動貼上", "Browser didn't finish automatically? Paste the code manually"),
        ["login_manual_steps"] = (
            "1. 按下面按鈕開啟登入頁(授權後頁面會顯示一段代碼)\n2. 複製整段代碼貼到下方,按「完成登入」",
            "1. Click the button below to open the sign-in page (a code is shown after authorizing)\n2. Copy the whole code, paste it below and click \"Complete sign-in\""),
        ["login_manual_open"] = ("開啟登入頁面(手動模式)", "Open sign-in page (manual mode)"),
        ["login_manual_submit"] = ("完成登入", "Complete sign-in"),
        ["login_manual_opened"] = ("已開啟手動登入頁,授權後把顯示的代碼貼到下方。", "Manual sign-in page opened — paste the code shown after authorizing."),
        ["login_paste_first"] = ("請先貼上代碼。", "Paste the code first."),
        ["login_open_failed"] = ("開啟失敗:{0}", "Failed to open: {0}"),
        ["oauth_state_mismatch"] = ("OAuth state 不符,已中止。", "OAuth state mismatch — aborted."),
        ["oauth_click_open_first"] = ("請先按「開啟登入頁面」再貼上代碼。", "Click \"Open sign-in page\" first, then paste the code."),
        ["browser_success"] = ("登入成功", "Signed in"),
        ["browser_success_body"] = ("可以關閉這個分頁,回到 Claude Usage Widget。", "You can close this tab and return to Claude Usage Widget."),
        ["browser_no_code"] = ("未收到授權碼", "No authorization code received"),
        ["browser_no_code_body"] = ("請回到工具重試。", "Please return to the app and try again."),
        // settings window
        ["settings_title"] = ("設定", "Settings"),
        ["settings_language"] = ("語言", "Language"),
        ["settings_theme"] = ("主題", "Theme"),
        ["theme_dark"] = ("深色", "Dark"),
        ["theme_light"] = ("淺色", "Light"),
        ["settings_opacity"] = ("背景透明度", "Background transparency"),
        ["settings_opacity_hint"] = ("0% 不透明,100% 完全透明", "0% solid — 100% fully transparent"),
        // updater
        ["menu_check_update"] = ("檢查更新", "Check for updates"),
        ["update_title"] = ("軟體更新", "Software update"),
        ["update_none"] = ("目前已是最新版本 (v{0})。", "You're on the latest version (v{0})."),
        ["update_found"] = ("發現新版本 v{0}(目前為 v{1})。\n要現在下載並自動更新嗎?", "Version v{0} is available (you have v{1}).\nDownload and update now?"),
        ["update_downloading"] = ("下載更新中…", "Downloading update…"),
        ["update_failed"] = ("更新失敗:{0}", "Update failed: {0}"),
        ["update_balloon"] = ("有新版本 v{0} 可用,點此更新", "New version v{0} available — click to update"),
        ["settings_interval"] = ("更新頻率", "Refresh interval"),
        ["interval_60"] = ("每 60 秒", "Every 60 s"),
        ["interval_90"] = ("每 90 秒(建議)", "Every 90 s (recommended)"),
        ["interval_120"] = ("每 2 分鐘", "Every 2 min"),
        ["interval_300"] = ("每 5 分鐘", "Every 5 min"),
    };
}
