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
        ["widget_title"] = ("AI 用量", "AI Usage"),
        ["provider_claude"] = ("Claude", "Claude"),
        ["provider_chatgpt"] = ("ChatGPT", "ChatGPT"),
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
        ["menu_provider"] = ("查看服務", "View provider"),
        ["menu_settings"] = ("設定…", "Settings…"),
        ["menu_autostart"] = ("開機自動啟動", "Start with Windows"),
        ["menu_relogin"] = ("連結 / 重新登入", "Connect / sign in again"),
        ["menu_exit"] = ("結束", "Exit"),
        ["menu_reset_position"] = ("重設視窗位置", "Reset window position"),
        // errors / status
        ["err_update_prefix"] = ("更新失敗:{0}", "Update failed: {0}"),
        ["err_not_signed_in_hint"] = ("尚未登入，右鍵選「連結 / 重新登入」。", "Not signed in — right-click and choose \"Connect / sign in again\"."),
        ["tray_need_login"] = ("AI Usage — 需要登入", "AI Usage — sign-in required"),
        ["err_not_signed_in"] = ("尚未登入", "Not signed in"),
        ["err_token_expired"] = ("Token 已過期,請重新登入", "Session expired — please sign in again"),
        ["err_refresh_failed"] = ("Token 刷新失敗,請重新登入({0})", "Token refresh failed — please sign in again ({0})"),
        ["err_network"] = ("網路暫時無法連線({0})", "Network temporarily unavailable ({0})"),
        ["err_schema_changed"] = ("無法解析用量資料,API 格式可能已變更。請「檢查更新」,若已是最新版請到 GitHub 回報。", "Couldn't parse usage data — the API format may have changed. Try \"Check for updates\", or report it on GitHub if you're up to date."),
        ["err_codex_start"] = ("無法啟動 Codex app-server。", "Couldn't start Codex app-server."),
        ["err_codex_missing"] = ("找不到或無法執行 Codex：{0}。請先安裝官方 Codex，或在設定中指定 codex.exe / codex.cmd。", "Codex couldn't be found or started: {0}. Install the official Codex CLI, or choose codex.exe / codex.cmd in Settings."),
        ["err_codex_path_invalid"] = ("設定的 Codex 路徑不存在：{0}", "The configured Codex path doesn't exist: {0}"),
        ["err_codex_stopped"] = ("Codex app-server 已停止，請稍後重試。", "Codex app-server stopped. Try again in a moment."),
        ["err_chatgpt_not_signed_in"] = ("小工具的 Codex CLI 尚未獲得 ChatGPT 授權（不影響桌面版登入）。請右鍵選「連結 / 重新登入」完成一次授權。", "The widget's Codex CLI is not authorized with ChatGPT yet (your desktop app remains signed in). Right-click and choose \"Connect / sign in again\" once."),
        ["err_chatgpt_no_limits"] = ("OpenAI 目前沒有回傳可顯示的 Codex 用量額度。", "OpenAI didn't return any displayable Codex quota windows."),
        ["err_chatgpt_login_start"] = ("Codex 沒有回傳登入網址，請更新 Codex 後再試一次。", "Codex didn't return a sign-in URL. Update Codex and try again."),
        ["err_chatgpt_login_timeout"] = ("等待 ChatGPT 登入逾時，請再試一次。", "Timed out waiting for ChatGPT sign-in. Try again."),
        ["chatgpt_login_waiting"] = ("瀏覽器已開啟，請完成 ChatGPT 登入…", "Browser opened — finish signing in to ChatGPT…"),
        ["chatgpt_login_replaces_api_key"] = ("Codex 目前使用 API key 模式。改用 ChatGPT 登入會替換 Codex 的主要登入方式。要繼續嗎？", "Codex is currently using API-key mode. Signing in with ChatGPT will replace Codex's primary login. Continue?"),
        ["chatgpt_limit_5h"] = ("5 小時額度", "5-hour limit"),
        ["chatgpt_limit_weekly"] = ("每週額度", "Weekly limit"),
        ["chatgpt_limit_monthly"] = ("每月額度", "Monthly limit"),
        ["chatgpt_limit_days"] = ("{0} 天額度", "{0}-day limit"),
        ["chatgpt_limit_hours"] = ("{0} 小時額度", "{0}-hour limit"),
        ["chatgpt_limit_minutes"] = ("{0} 分鐘額度", "{0}-minute limit"),
        ["chatgpt_limit_primary"] = ("主要額度", "Primary limit"),
        ["chatgpt_limit_secondary"] = ("次要額度", "Secondary limit"),
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
        ["settings_codex_path"] = ("Codex 執行檔（選填）", "Codex executable (optional)"),
        ["settings_codex_browse"] = ("瀏覽…", "Browse…"),
        ["settings_codex_hint"] = ("留空會自動搜尋 ChatGPT 桌面版與 PATH；ChatGPT 用量由官方 Codex app-server 提供。", "Leave blank to search the ChatGPT desktop app and PATH. ChatGPT usage is provided by the official Codex app-server."),
        // updater
        ["menu_check_update"] = ("檢查更新", "Check for updates"),
        ["update_title"] = ("軟體更新", "Software update"),
        ["update_none"] = ("目前已是最新版本 (v{0})。", "You're on the latest version (v{0})."),
        ["update_found"] = ("發現新版本 v{0}(目前為 v{1})。\n要現在下載並自動更新嗎?", "Version v{0} is available (you have v{1}).\nDownload and update now?"),
        ["update_downloading"] = ("下載更新中…", "Downloading update…"),
        ["update_download_progress"] = ("下載更新：{0}%（{1} / {2} MB）", "Downloading update: {0}% ({1} / {2} MB)"),
        ["update_download_unknown"] = ("正在下載更新…", "Downloading update…"),
        ["update_verifying"] = ("正在驗證更新檔…", "Verifying update…"),
        ["update_extracting"] = ("正在解壓縮更新檔…", "Extracting update…"),
        ["update_applying"] = ("正在安裝更新…", "Installing update…"),
        ["update_restarting"] = ("正在重新啟動…", "Restarting…"),
        ["update_cancel"] = ("取消", "Cancel"),
        ["update_cancelled"] = ("已取消更新。", "Update cancelled."),
        ["update_failed"] = ("更新失敗:{0}", "Update failed: {0}"),
        ["update_balloon"] = ("有新版本 v{0} 可用,點此更新", "New version v{0} available — click to update"),
        ["settings_interval"] = ("更新頻率", "Refresh interval"),
        ["interval_60"] = ("每 60 秒", "Every 60 s"),
        ["interval_90"] = ("每 90 秒(建議)", "Every 90 s (recommended)"),
        ["interval_120"] = ("每 2 分鐘", "Every 2 min"),
        ["interval_300"] = ("每 5 分鐘", "Every 5 min"),
    };
}
