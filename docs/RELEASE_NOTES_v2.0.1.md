# v2.0.1 — Unified ChatGPT desktop app compatibility

## Fixed

- Automatically discovers the Codex CLI bundled with the unified ChatGPT desktop app on Windows.
- Selects the newest available desktop-managed Codex candidate after ChatGPT updates.
- Keeps manual paths and `CODEX_PATH` as higher-priority overrides, with normal PATH installs as a fallback.
- Avoids protected `WindowsApps` Codex paths that may exist but cannot be started by an unpackaged widget.

## Privacy and security

- Discovery is limited to `%LOCALAPPDATA%\OpenAI\Codex\bin` and does not inspect ChatGPT or Codex credential files.
- ChatGPT authentication and usage requests still remain inside the official Codex child process.

## Upgrade

Right-click the widget and choose **Check for updates**, or replace the existing executable with the one from `ClaudeUsageWidget-win-x64.zip`. Settings and encrypted Claude tokens remain in `%APPDATA%\ClaudeUsageWidget`.

After upgrading, the widget may ask for a one-time Codex CLI authorization even when the unified ChatGPT desktop UI is already signed in. Use **Connect / sign in again** from the widget menu; the official Codex process owns that browser flow and its credentials.
