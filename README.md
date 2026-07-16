# Claude + ChatGPT Usage Widget

A small, open-source Windows desktop widget for checking **Claude plan usage** and **ChatGPT/Codex quota windows** in one place. Switch providers from the widget or tray menu without opening two usage pages.

一個小巧的 Windows 桌面用量工具，可在同一個小工具中切換查看 **Claude 方案用量**與 **ChatGPT 帳號下的 Codex 額度**。

> [!IMPORTANT]
> The ChatGPT tab shows the quota windows returned by OpenAI Codex, such as the 5-hour and weekly limits. It does **not** claim to show every model-specific message limit from normal ChatGPT conversations. OpenAI's public Usage API is for API organization activity and is a separate product surface.
>
> ChatGPT 分頁顯示 OpenAI Codex 回傳的 5 小時、每週等額度；它**不代表**一般 ChatGPT 對話中所有模型的訊息上限。OpenAI 公開的 Usage API 是 API 組織用量，兩者是不同產品範圍。

| Dark theme | Light theme |
| :---: | :---: |
| ![Dark theme](docs/screenshot.png) | ![Light theme](docs/English_light.png) |

_The screenshots above are from the original Claude-only UI. Version 2 adds a compact Claude / ChatGPT provider switch above the usage rows._

## What it shows

### Claude

- Current session usage
- Weekly overall usage
- Weekly model-scoped rows returned by Claude
- Reset countdown for each quota window

### ChatGPT / Codex

- Primary quota window, normally the 5-hour limit
- Secondary quota window, normally the weekly limit
- Reset countdown for each window
- Data obtained through OpenAI's official, stable [`codex app-server` account API](https://github.com/openai/codex/blob/main/codex-rs/app-server/README.md#auth-endpoints)

The widget deliberately does not request or store an OpenAI API key. The public [OpenAI Usage API](https://platform.openai.com/docs/api-reference/usage) reports API organization activity and requires an organization admin key; that is not the same as a personal ChatGPT/Codex allowance.

## Features

- **Two providers, one widget** — switch between Claude and ChatGPT from the widget or tray menu
- **Floating and always on top** — draggable, resizable, and remembers its position
- **Compact mode** — collapse the widget to a one-line percentage summary
- **Tray icon** — live percentage ring for the active provider
- **Automatic refresh** — 60 s / 90 s / 2 min / 5 min; 90 s by default
- **Reset countdowns** — updated locally between network refreshes
- **Themes and language** — dark/light and Traditional Chinese/English
- **Start with Windows** — Startup-folder shortcut with delayed launch
- **Built-in updater** — checks this repository's GitHub Releases
- **No telemetry** — no analytics, advertising, or third-party relay server

## Install

### Download a release

1. Download `ClaudeUsageWidget-win-x64.zip` from [Releases](../../releases).
2. Extract it to a normal folder, for example `C:\Tools\ClaudeUsageWidget\`.
3. Run `ClaudeUsageWidget.exe`.

The executable keeps its original name so existing v1 users can update in place.

> Windows SmartScreen may warn because the release is not code-signed. You can inspect the source and build it yourself if you prefer.

### Claude setup

On first use, choose Claude and complete the browser sign-in. Claude tokens are encrypted with Windows DPAPI and stored under `%APPDATA%\ClaudeUsageWidget\tokens.dat` for the current Windows user only.

### ChatGPT / Codex setup

1. Install and sign in to the official ChatGPT desktop app (with Codex) or [OpenAI Codex CLI](https://github.com/openai/codex).
2. Select **ChatGPT** in the widget.
3. If prompted, right-click and choose **Connect / sign in again** once. The unified desktop UI login may be separate from the independently launched Codex CLI authorization; the widget asks Codex to open the official ChatGPT login flow.
4. The widget automatically detects both normal CLI installs and the Codex bundled with the unified ChatGPT desktop app. If detection still fails, open **Settings** and choose `codex.exe`, `codex.cmd`, or `codex.bat`.

The widget communicates with a local Codex child process over redirected stdin/stdout. It never opens, parses, copies, or uploads Codex's `auth.json`, and this separate authorization does not sign the ChatGPT desktop app out.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/Kilin-570/ClaudeUsageWidget.git
cd ClaudeUsageWidget
dotnet build ClaudeUsageWidget.csproj -c Release
dotnet publish ClaudeUsageWidget.csproj -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

Run the smoke tests:

```powershell
dotnet publish tests\CodexAppServerMock\CodexAppServerMock.csproj `
  -c Release -r win-x64 --self-contained false -o tests\mock-out
dotnet run --project tests\ClaudeUsageWidget.SmokeTests\ClaudeUsageWidget.SmokeTests.csproj `
  -c Release -- tests\mock-out\CodexAppServerMock.exe
```

The tests use a local mock app-server and contain no real account credentials or usage data.

To verify automatic discovery against the signed-in ChatGPT desktop app without printing account details or usage values:

```powershell
dotnet run --project tests\ClaudeUsageWidget.SmokeTests\ClaudeUsageWidget.SmokeTests.csproj `
  -c Release -- --live
```

## Privacy and security

- **Claude:** OAuth tokens are stored locally with Windows DPAPI, scoped to the current Windows account.
- **ChatGPT:** the widget stores no ChatGPT token and does not read Codex credential files. Codex owns the browser login, token storage, refresh, and OpenAI request.
- **Network:** the widget has no developer-operated backend and sends no telemetry. Claude usage is read from Anthropic; ChatGPT/Codex usage is requested through the local official Codex process; update checks go to GitHub Releases.
- **Logs:** Codex app-server stdout/stderr is not copied into widget logs, reducing the chance of local account metadata appearing in diagnostics.
- **Repository hygiene:** credential-shaped local files and signing keys are ignored. Release builds are scanned before publishing.

See [Security design](docs/SECURITY.md) and [Architecture](docs/ARCHITECTURE.md) for the complete boundaries and trade-offs.

## Updating

From v1.4.0 onward, right-click the widget and choose **Check for updates**. To update manually, exit the app and overwrite the old executable. Tokens and settings remain under `%APPDATA%\ClaudeUsageWidget`, so replacing the executable does not remove them.

## Uninstall

1. Exit from the tray menu.
2. Delete `ClaudeUsageWidget.exe`.
3. Delete `%APPDATA%\ClaudeUsageWidget\` to remove Claude tokens, settings, and local logs.
4. Delete the `ClaudeUsageWidget` shortcut from `shell:startup` if present.
5. ChatGPT credentials remain managed by Codex; use Codex's own logout command if you also want to sign out there.

## Known limitations

- Claude's usage endpoint is used by first-party clients but is not a documented public Anthropic API. It may change.
- The ChatGPT integration requires a Codex version that supports `account/rateLimits/read`.
- ChatGPT/Codex quota values come from OpenAI and can be unavailable temporarily.
- Releases are not code-signed yet.

## Disclaimer

This is an unofficial community project. It is not affiliated with or endorsed by Anthropic or OpenAI. Claude, ChatGPT, Codex, and the related marks belong to their respective owners.

## License

[MIT](LICENSE)
