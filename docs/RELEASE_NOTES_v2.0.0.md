# v2.0.0 — Claude + ChatGPT in one widget

## Highlights

- Added a Claude / ChatGPT provider switch in the widget and tray menu.
- Added ChatGPT/Codex 5-hour and weekly quota display through OpenAI's official Codex app-server.
- Kept ChatGPT tokens entirely under Codex management; the widget never reads `auth.json`.
- Added optional Codex executable selection for installations that are not on PATH.
- Preserved v1 Claude tokens, settings, auto-start behavior, updater, executable name, and release asset name.
- Added offline smoke tests for both usage parsers and the Codex JSON-RPC handshake.
- Added a Windows GitHub Actions build/test workflow and expanded privacy/security documentation.

## Scope note

The ChatGPT tab displays Codex quota windows returned for the signed-in ChatGPT account. It does not display every model-specific message limit from ordinary ChatGPT conversations. OpenAI API organization usage is also a separate surface and is not included in v2.0.0.

## Upgrade

Use **Check for updates** from v1.4.0 or later, or replace the existing `ClaudeUsageWidget.exe` manually. Existing Claude login and settings remain in `%APPDATA%\ClaudeUsageWidget`.
